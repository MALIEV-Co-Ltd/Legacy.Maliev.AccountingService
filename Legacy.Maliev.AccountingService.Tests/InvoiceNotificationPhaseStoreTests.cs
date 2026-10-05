using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Real PostgreSQL phase authority; no notification transport or provider calls.</summary>
public sealed class InvoiceNotificationPhaseStoreTests(InvoiceNotificationPhaseFencePostgresFixture fixture)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task CommittedAdmission_IsSingleUse_AndReadReplayCannotIssueAgain()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var identity = await PrepareAsync(database);
        var store = Store(database);
        var permit = await store.IssueAdmissionAsync(identity, Digest, 1, CancellationToken.None);
        Assert.Equal("AdmissionIssued", permit.Correlation.Phase);
        Assert.Equal(2, permit.Correlation.Version);
        Assert.Equal(Digest, permit.PayloadDigest);
        Assert.Empty(typeof(InvoiceNotificationAdmissionPermit).GetConstructors());
        Assert.Empty(typeof(InvoiceNotificationExecutionPermit).GetConstructors());
        Assert.True(permit.TryConsume());
        Assert.False(permit.TryConsume());
        var replay = await store.AdmitAsync(identity, Digest, CancellationToken.None);
        Assert.Equal("AdmissionIssued", replay.Phase);
        Assert.Equal(2, replay.Version);
        var read = await store.ReadAsync(identity.IntentId, CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(replay.Identity, read.Identity);
        Assert.Equal(replay.Phase, read.Phase);
        Assert.Equal(replay.Version, read.Version);
        Assert.Equal(replay.PayloadBinding, read.PayloadBinding);
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() =>
            store.IssueAdmissionAsync(identity, Digest, replay.Version, CancellationToken.None));
        Assert.Single(await database.InvoiceNotificationCorrelations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ConcurrentAdmissionCas_GrantsExactlyOneCapability()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var identity = await PrepareAsync(database);
        async Task<InvoiceNotificationAdmissionPermit?> Attempt()
        {
            try { return await Store(database).IssueAdmissionAsync(identity, Digest, 1, CancellationToken.None); }
            catch (InvoiceNotificationCorrelationConflictException) { return null; }
        }
        var results = await Task.WhenAll(Attempt(), Attempt());
        var winner = Assert.Single(results, value => value is not null);
        Assert.NotNull(winner);
        Assert.True(winner.TryConsume());
        var row = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        Assert.Equal("AdmissionIssued", row.Phase);
        Assert.Equal(2, row.Version);
    }

    [Fact]
    public async Task ConcurrentFirstWriters_InvoicePurposeRetainsOnlyOneIntent()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var invoice = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
            new Invoice { Number = "INV-first-writer", CustomerId = 42 }, [], CancellationToken.None);
        var workflow = Guid.NewGuid();
        var first = new InvoiceNotificationCorrelationIdentity(Guid.NewGuid(), invoice.Id, "invoice-issued", 84, workflow,
            new("https://auth.example.invalid", "employee:42", "service:legacy-intranet"),
            "https://auth.example.invalid", "service:legacy-accounting", "notification-payload-v1",
            "accounting-invoice-notification-hmac-v1");
        _ = await new InvoiceCreationAdmissionStore(database).AdmitAsync(workflow, 84, first.Origin,
            new string('A', 64), CancellationToken.None);
        var second = first with { IntentId = Guid.NewGuid() };
        async Task<InvoiceNotificationCorrelation?> Attempt(InvoiceNotificationCorrelationIdentity identity)
        {
            try { return await Store(database).AdmitAsync(identity, Digest, CancellationToken.None); }
            catch (InvoiceNotificationCorrelationConflictException) { return null; }
        }
        var results = await Task.WhenAll(Attempt(first), Attempt(second));
        var winner = Assert.Single(results, value => value is not null);
        Assert.NotNull(winner);
        var row = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        Assert.Equal(winner.Identity.IntentId, row.IntentId);
        Assert.Equal("Prepared", row.Phase);
        Assert.Null(row.AdmissionIssuedAt);
    }

    [Fact]
    public async Task ReceiptContinuity_ExecutionOnce_ProviderIdentifierRetainedOnlyAsBinding()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var identity = await PrepareAsync(database);
        var store = Store(database);
        _ = await store.IssueAdmissionAsync(identity, Digest, 1, CancellationToken.None);
        var admitted = Receipt(identity, "admitted", 1);
        var snapshot = await store.RetainReceiptAsync(identity, Digest, 2, admitted, CancellationToken.None);
        Assert.Equal("Admitted", snapshot.Phase);
        Assert.Equal(3, snapshot.Version);
        var execution = await store.IssueExecutionAsync(identity, Digest, 3, CancellationToken.None);
        Assert.Equal(Digest, execution.PayloadDigest);
        Assert.True(execution.TryConsume());
        Assert.False(execution.TryConsume());
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() =>
            store.IssueExecutionAsync(identity, Digest, 4, CancellationToken.None));
        var accepted = Receipt(identity, "providerAccepted", 3) with { ProviderMessageId = "synthetic-provider-id" };
        var retained = await store.ObserveAsync(identity, 4, accepted, CancellationToken.None);
        Assert.Equal("ProviderAccepted", retained.Phase);
        Assert.Equal(5, retained.Version);
        var duplicate = await store.ObserveAsync(identity, 5, accepted, CancellationToken.None);
        Assert.Equal(5, duplicate.Version);
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() =>
            store.ObserveAsync(identity, 5, accepted with { ProviderMessageId = "different-provider-id" }, CancellationToken.None));
        var row = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        Assert.Equal("providerAccepted", row.RemoteState);
        Assert.Equal(32, row.RemoteReceiptBinding!.Length);
        Assert.Equal(3, row.RemoteVersion);
        Assert.NotNull(row.ExecutionIssuedAt);
    }

    [Fact]
    public async Task AcceptedPublicResult_ValidatesRetainedCommitmentWithoutChangingPhaseOrIssuingPermit()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var identity = await PrepareAsync(database);
        var store = Store(database);
        _ = await store.IssueAdmissionAsync(identity, Digest, 1, CancellationToken.None);
        _ = await store.ObserveAsync(identity, 2, Receipt(identity, "admitted", 1), CancellationToken.None);
        _ = await store.IssueExecutionAsync(identity, Digest, 3, CancellationToken.None);
        _ = await store.ObserveAsync(identity, 4, Receipt(identity, "providerAccepted", 3) with
        { ProviderMessageId = "synthetic-provider-id" }, CancellationToken.None);
        var before = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        await store.ValidateAcceptedResultAsync(identity, "synthetic-provider-id", CancellationToken.None);
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() =>
            store.ValidateAcceptedResultAsync(identity, "changed-provider-id", CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationUnavailableException>(() =>
            new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new Keys(false))
                .ValidateAcceptedResultAsync(identity, "synthetic-provider-id", CancellationToken.None));
        var after = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.Phase, after.Phase);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.RemoteReceiptBinding, after.RemoteReceiptBinding);
        Assert.Equal(before.AdmissionIssuedAt, after.AdmissionIssuedAt);
        Assert.Equal(before.ExecutionIssuedAt, after.ExecutionIssuedAt);
    }

    [Fact]
    public async Task RestartLookup_IsFullContextGuarded_AndNeedsNoRebuiltPayload()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var identity = await PrepareAsync(database);
        var store = Store(database);
        var retained = await store.FindAsync(identity.InvoiceId, identity.Purpose, identity.QuotationId,
            identity.WorkflowOperationId, identity.Origin, identity.SenderIssuer, identity.SenderServiceSubject, CancellationToken.None);
        Assert.NotNull(retained);
        Assert.Equal(identity, retained.Identity);
        Assert.Equal("Prepared", retained.Phase);
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() => store.FindAsync(
            identity.InvoiceId, identity.Purpose, identity.QuotationId, Guid.NewGuid(), identity.Origin,
            identity.SenderIssuer, identity.SenderServiceSubject, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() => store.FindAsync(
            identity.InvoiceId, identity.Purpose, identity.QuotationId, identity.WorkflowOperationId, identity.Origin,
            identity.SenderIssuer, "service:other", CancellationToken.None));
        Assert.Null(await store.FindAsync(identity.InvoiceId + 1, identity.Purpose, identity.QuotationId,
            identity.WorkflowOperationId, identity.Origin, identity.SenderIssuer, identity.SenderServiceSubject, CancellationToken.None));
    }

    [Fact]
    public async Task WrongDigestIdentityAndRetainedKey_NeverGrantCapability()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var identity = await PrepareAsync(database);
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() => Store(database).IssueAdmissionAsync(
            identity, new string('b', 64), 1, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() => Store(database).IssueAdmissionAsync(
            identity with { QuotationId = 85 }, Digest, 1, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationUnavailableException>(() =>
            new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new Keys(false)).IssueAdmissionAsync(
                identity, Digest, 1, CancellationToken.None));
        var row = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        Assert.Equal("Prepared", row.Phase);
        Assert.Equal(1, row.Version);
        Assert.Null(row.AdmissionIssuedAt);
    }

    [Fact]
    public async Task ReceiptReplayAndUnknownOutcome_NeverReissueExecution()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var identity = await PrepareAsync(database);
        var store = Store(database);
        _ = await store.IssueAdmissionAsync(identity, Digest, 1, CancellationToken.None);
        var admitted = Receipt(identity, "admitted", 1);
        _ = await store.ObserveAsync(identity, 2, admitted, CancellationToken.None);
        _ = await store.IssueExecutionAsync(identity, Digest, 3, CancellationToken.None);
        var duplicate = await store.ObserveAsync(identity, 4, admitted, CancellationToken.None);
        Assert.Equal("ExecutionIssued", duplicate.Phase);
        Assert.Equal(4, duplicate.Version);
        var unknown = await store.ObserveAsync(identity, 4, Receipt(identity, "outcomeUnknown", 3), CancellationToken.None);
        Assert.Equal("OutcomeUnknown", unknown.Phase);
        Assert.Equal(5, unknown.Version);
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() =>
            store.IssueExecutionAsync(identity, Digest, 5, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() =>
            store.ObserveAsync(identity, 5, admitted, CancellationToken.None));
    }

    [Fact]
    public async Task TamperedRetainedAdmissionBinding_AndStaleVersionCannotIssueExecution()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var identity = await PrepareAsync(database);
        var store = Store(database);
        _ = await store.IssueAdmissionAsync(identity, Digest, 1, CancellationToken.None);
        _ = await store.ObserveAsync(identity, 2, Receipt(identity, "admitted", 1), CancellationToken.None);
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() =>
            store.IssueExecutionAsync(identity, Digest, 2, CancellationToken.None));
        var row = await database.InvoiceNotificationCorrelations.SingleAsync();
        row.RemoteReceiptBinding = new byte[32];
        await database.SaveChangesAsync();
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationUnavailableException>(() =>
            store.IssueExecutionAsync(identity, Digest, 3, CancellationToken.None));
        var retained = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        Assert.Equal("Admitted", retained.Phase);
        Assert.Equal(3, retained.Version);
        Assert.Null(retained.ExecutionIssuedAt);
    }

    [Theory]
    [InlineData("commit-after")]
    [InlineData("transaction-dispose")]
    [InlineData("context-dispose")]
    public async Task UnknownCommitOrDisposal_WithholdsPermitAndRetainsFence(string stage)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var identity = await PrepareAsync(database);
        var fault = new CommitFault(stage);
        var options = new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql(database.Database.GetConnectionString())
            .AddInterceptors(fault);
        options.LogTo((eventId, _) => eventId == RelationalEventId.TransactionDisposed || eventId == CoreEventId.ContextDisposed,
            eventData =>
            {
                if (fault.Armed && (stage == "transaction-dispose" && eventData.EventId == RelationalEventId.TransactionDisposed ||
                    stage == "context-dispose" && eventData.EventId == CoreEventId.ContextDisposed))
                { fault.Reached = true; throw new IOException("Synthetic disposal acknowledgment fault."); }
            });
        await using var configured = new InvoiceDbContext(options.Options);
        InvoiceNotificationAdmissionPermit? permit = null;
        try
        {
            await Assert.ThrowsAsync<InvoiceNotificationCorrelationUnavailableException>(async () =>
                permit = await Store(configured).IssueAdmissionAsync(identity, Digest, 1, CancellationToken.None));
        }
        finally { fault.Armed = false; }
        Assert.Null(permit);
        Assert.True(fault.Reached);
        var row = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        Assert.Equal("AdmissionIssued", row.Phase);
        Assert.Equal(2, row.Version);
    }

    private static InvoiceNotificationCorrelationStore Store(InvoiceDbContext database) => new(database, TimeProvider.System, new Keys());

    private static async Task<InvoiceNotificationCorrelationIdentity> PrepareAsync(InvoiceDbContext database)
    {
        var invoice = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
            new Invoice { Number = "INV-phase", CustomerId = 42, Total = 107m }, [], CancellationToken.None);
        var workflow = Guid.NewGuid();
        var identity = new InvoiceNotificationCorrelationIdentity(Guid.NewGuid(), invoice.Id, "invoice-issued", 84, workflow,
            new("https://auth.example.invalid", "employee:42", "service:legacy-intranet"),
            "https://auth.example.invalid", "service:legacy-accounting", "notification-payload-v1",
            "accounting-invoice-notification-hmac-v1");
        _ = await new InvoiceCreationAdmissionStore(database).AdmitAsync(workflow, 84, identity.Origin,
            new string('A', 64), CancellationToken.None);
        _ = await Store(database).AdmitAsync(identity, Digest, CancellationToken.None);
        return identity;
    }

    private static InvoiceNotificationReceiptObservation Receipt(InvoiceNotificationCorrelationIdentity identity, string state, long version)
    {
        var time = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        return new(identity.IntentId.ToString("D"), identity.Purpose, "invoice", identity.InvoiceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.WorkflowOperationId.ToString("D"), state, version, time, time.AddSeconds(version), null);
    }

    private sealed class Keys(bool available = true) : IInvoiceNotificationBindingKeyring
    {
        public string ActiveKeyId => "fixture-phase";
        public ReadOnlyMemory<byte>? Find(string keyId) => available && keyId == ActiveKeyId
            ? Enumerable.Range(0, 32).Select(value => (byte)value).ToArray() : null;
    }

    private sealed class CommitFault(string stage) : DbTransactionInterceptor
    {
        public bool Armed = true;
        public bool Reached;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Armed && stage == "commit-after")
            { Reached = true; throw new IOException("Synthetic commit acknowledgment fault."); }
            return Task.CompletedTask;
        }
    }
}
