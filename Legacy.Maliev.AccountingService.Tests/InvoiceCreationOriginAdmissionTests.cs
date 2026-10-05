using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Real PostgreSQL retained origin and independent financial evidence; no provider authority is inferred.</summary>
public sealed class InvoiceCreationOriginAdmissionTests(InvoiceNotificationPhaseFencePostgresFixture fixture)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    private static readonly InvoiceNotificationOrigin Origin = new("https://auth.example.invalid", "employee:42", "service:legacy-intranet");
    private static readonly string Fingerprint = new('A', 64);

    [Fact]
    public async Task AdditiveMigration_OldRowsKeepNullIssuerAndFinancialEvidence()
    {
        await using var database = await fixture.NewDatabaseAsync("20261001133820_RetainInvoiceNotificationReceipt");
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        Assert.True((await store.AdmitAsync(operation, 84, Origin.EmployeeSubject, Origin.ServiceSubject,
            Fingerprint, CancellationToken.None)).IsNew);
        await database.Database.MigrateAsync();
        var row = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync();
        Assert.Null(row.OriginIssuer);
        Assert.Null(row.FinancialResultJson);
        Assert.Equal("Pending", row.State);
        Assert.False(database.Database.HasPendingModelChanges());
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.ValidateOriginAsync(operation, 84, Origin, false, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None));
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("employee")]
    [InlineData("service")]
    [InlineData("quotation")]
    [InlineData("fingerprint")]
    public async Task AdmissionReplay_RejectsEveryChangedRetainedContextField(string field)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        var first = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        Assert.True(first.IsNew);
        Assert.False(first.NeedsReconciliation);
        var changed = field switch
        {
            "issuer" => Origin with { Issuer = "https://other.example.invalid" },
            "employee" => Origin with { EmployeeSubject = "employee:other" },
            "service" => Origin with { ServiceSubject = "service:other" },
            _ => Origin,
        };
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.AdmitAsync(operation,
            field == "quotation" ? 85 : 84, changed, field == "fingerprint" ? new string('B', 64) : Fingerprint, CancellationToken.None));
        var row = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(Origin.Issuer, row.OriginIssuer);
        Assert.Equal("Pending", row.State);
        Assert.Null(row.ResultJson);
        Assert.Null(row.FinancialResultJson);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("NeedsReconciliation")]
    [InlineData("Unrecognized")]
    public async Task UncertainReplay_IsReadOnlyAndNeverFinancialEvidence(string state)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        _ = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        var row = await database.InvoiceCreationAdmissions.SingleAsync();
        row.State = state;
        await database.SaveChangesAsync();
        var before = row.UpdatedAt;
        var replay = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        Assert.False(replay.IsNew);
        Assert.True(replay.NeedsReconciliation);
        Assert.Null(replay.Completed);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.ReadFinancialResultAsync(operation, 84, Origin, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.AdmitAsync(operation, 84,
            Origin.EmployeeSubject, Origin.ServiceSubject, Fingerprint, CancellationToken.None));
        var retained = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(state, retained.State);
        Assert.Equal(before, retained.UpdatedAt);
        Assert.Null(retained.FinancialResultJson);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CompletedReplay_ValidatesAllFourDeclaredEmailStates(int emailState)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        _ = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        var expected = Result(17) with
        {
            EmailState = (InvoiceCreationEmailState)emailState,
            ProviderMessageId = emailState == 3 ? "synthetic-provider-receipt" : null
        };
        await store.CompleteAsync(operation, expected, CancellationToken.None);
        var replay = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        Assert.False(replay.IsNew);
        Assert.False(replay.NeedsReconciliation);
        Assert.Equal(expected, replay.Completed);
        // A completion result alone is never retained financial-completion evidence.
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.ReadFinancialResultAsync(operation, 84, Origin, CancellationToken.None));
    }

    [Theory]
    [InlineData(3, "null")]
    [InlineData(3, "blank")]
    [InlineData(3, "long")]
    [InlineData(3, "nul")]
    [InlineData(2, "present")]
    public async Task ProviderAcceptedReplay_RequiresBoundedProviderId_AndRetryRequiredHasNone(int emailState, string shape)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        _ = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        var providerId = shape switch
        {
            "null" => null,
            "blank" => " ",
            "long" => new string('p', 257),
            "nul" => "provider\0identifier",
            _ => "synthetic-provider-receipt",
        };
        var invalid = Result(17) with { EmailState = (InvoiceCreationEmailState)emailState, ProviderMessageId = providerId };
        await store.CompleteAsync(operation, invalid, CancellationToken.None);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None));
    }

    [Theory]
    [InlineData("enum")]
    [InlineData("invoice")]
    [InlineData("file")]
    [InlineData("json")]
    [InlineData("missing-state")]
    [InlineData("missing-email")]
    public async Task MalformedCompletedReplay_IsConflict(string field)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        _ = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        var result = field switch
        {
            "enum" => Result(17) with { EmailState = (InvoiceCreationEmailState)4 },
            "invoice" => Result(0),
            "file" => Result(17) with { StoredFile = new("", "") },
            _ => Result(17),
        };
        await store.CompleteAsync(operation, result, CancellationToken.None);
        if (field is "json" or "missing-state" or "missing-email")
        {
            var row = await database.InvoiceCreationAdmissions.SingleAsync();
            row.ResultJson = field switch
            {
                "missing-state" => row.ResultJson!.Replace("\"State\":0,", "", StringComparison.Ordinal),
                "missing-email" => row.ResultJson!.Replace("\"EmailState\":0,", "", StringComparison.Ordinal),
                _ => "{",
            };
            await database.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None));
    }

    [Fact]
    public async Task FinancialResult_IsWriteOnceAndRequiresPersistedInvoiceFile()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        _ = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.SaveFinancialResultAsync(operation, 84, Origin, Result(17), CancellationToken.None));
        var financial = await PersistFinancialAsync(database);
        await store.SaveFinancialResultAsync(operation, 84, Origin, financial, CancellationToken.None);
        var before = (await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).FinancialResultJson;
        await store.SaveFinancialResultAsync(operation, 84, Origin, financial, CancellationToken.None);
        Assert.Equal(financial, await store.ReadFinancialResultAsync(operation, 84, Origin, CancellationToken.None));
        var other = await PersistFinancialAsync(database);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.SaveFinancialResultAsync(operation, 84, Origin, other, CancellationToken.None));
        Assert.Equal(before, (await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).FinancialResultJson);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.ReadFinancialResultAsync(operation, 84,
            Origin with { Issuer = "https://other.example.invalid" }, CancellationToken.None));
    }

    [Theory]
    [InlineData("state")]
    [InlineData("email")]
    [InlineData("provider")]
    [InlineData("file")]
    public async Task FinancialEvidence_RejectsNotificationAndIncompleteFinancialResults(string field)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        _ = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        var financial = await PersistFinancialAsync(database);
        var changed = field switch
        {
            "state" => financial with { State = InvoiceCreationState.Reconciled },
            "email" => financial with { EmailState = (InvoiceCreationEmailState)3 },
            "provider" => financial with { ProviderMessageId = "synthetic-provider-id" },
            _ => financial with { StoredFile = new("missing-bucket", "missing.pdf") },
        };
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.SaveFinancialResultAsync(operation, 84, Origin, changed, CancellationToken.None));
        Assert.Null((await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).FinancialResultJson);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public async Task BoundedReconciliation_RequiresRetainedFinancialResultAndSameInvoiceFile(int emailState, bool uncertain)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        _ = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        var financial = await PersistFinancialAsync(database);
        var completed = financial with
        {
            EmailState = (InvoiceCreationEmailState)emailState,
            ProviderMessageId = emailState == 3 ? "synthetic-provider-receipt" : null
        };
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.CompleteReconciledAsync(operation, 84, Origin, completed, CancellationToken.None));
        await store.SaveFinancialResultAsync(operation, 84, Origin, financial, CancellationToken.None);
        if (uncertain) await store.MarkUncertainAsync(operation, CancellationToken.None);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.CompleteReconciledAsync(operation, 84, Origin,
            completed with { InvoiceId = financial.InvoiceId + 1 }, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.CompleteReconciledAsync(operation, 84, Origin,
            completed with { StoredFile = new("different-bucket", "different.pdf") }, CancellationToken.None));
        await store.CompleteReconciledAsync(operation, 84, Origin, completed, CancellationToken.None);
        Assert.Equal(financial, await store.ReadFinancialResultAsync(operation, 84, Origin, CancellationToken.None));
        var replay = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        Assert.Equal(completed, replay.Completed);
        Assert.False(replay.NeedsReconciliation);
        await store.ValidateOriginAsync(operation, 84, Origin, false, CancellationToken.None);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.ValidateOriginAsync(operation, 84, Origin, true, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentFinancialWriters_RetainOneExactResult()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        _ = await new InvoiceCreationAdmissionStore(database).AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        var first = await PersistFinancialAsync(database);
        var second = await PersistFinancialAsync(database);
        async Task<bool> Attempt(InvoiceCreationResult result)
        {
            await using var separate = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>()
                .UseNpgsql(database.Database.GetConnectionString()).Options);
            try
            {
                await new InvoiceCreationAdmissionStore(separate).SaveFinancialResultAsync(operation, 84, Origin, result, CancellationToken.None);
                return true;
            }
            catch (InvoiceCreationConflictException) { return false; }
        }
        var results = await Task.WhenAll(Attempt(first), Attempt(second));
        Assert.Single(results, success => success);
        var retained = await new InvoiceCreationAdmissionStore(database).ReadFinancialResultAsync(operation, 84, Origin, CancellationToken.None);
        Assert.Equal(results[0] ? first : second, retained);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("quotation")]
    [InlineData("issuer")]
    [InlineData("employee")]
    [InlineData("service")]
    public async Task FinancialAndReconciliationApis_RequireFullRetainedOrigin(string field)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        _ = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        var financial = await PersistFinancialAsync(database);
        await store.SaveFinancialResultAsync(operation, 84, Origin, financial, CancellationToken.None);
        var changedOrigin = field switch
        {
            "issuer" => Origin with { Issuer = "https://other.example.invalid" },
            "employee" => Origin with { EmployeeSubject = "employee:other" },
            "service" => Origin with { ServiceSubject = "service:other" },
            _ => Origin,
        };
        var changedOperation = field == "operation" ? Guid.NewGuid() : operation;
        var changedQuotation = field == "quotation" ? 85 : 84;
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.ValidateOriginAsync(
            changedOperation, changedQuotation, changedOrigin, false, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.SaveFinancialResultAsync(
            changedOperation, changedQuotation, changedOrigin, financial, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.ReadFinancialResultAsync(
            changedOperation, changedQuotation, changedOrigin, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.CompleteReconciledAsync(
            changedOperation, changedQuotation, changedOrigin,
            financial with { EmailState = (InvoiceCreationEmailState)3, ProviderMessageId = "synthetic-provider-receipt" }, CancellationToken.None));
        Assert.Equal("Pending", (await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).State);
        Assert.Equal(financial, await store.ReadFinancialResultAsync(operation, 84, Origin, CancellationToken.None));
    }

    [Fact]
    public async Task PendingOnlyFinancialWrite_AndOriginPhaseGuardsDoNotPromoteUncertainty()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        var store = new InvoiceCreationAdmissionStore(database);
        _ = await store.AdmitAsync(operation, 84, Origin, Fingerprint, CancellationToken.None);
        await store.ValidateOriginAsync(operation, 84, Origin, true, CancellationToken.None);
        var financial = await PersistFinancialAsync(database);
        await store.MarkUncertainAsync(operation, CancellationToken.None);
        await store.ValidateOriginAsync(operation, 84, Origin, false, CancellationToken.None);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.ValidateOriginAsync(operation, 84, Origin, true, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.SaveFinancialResultAsync(operation, 84, Origin, financial, CancellationToken.None));
        var row = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal("NeedsReconciliation", row.State);
        Assert.Null(row.FinancialResultJson);
        Assert.Null(row.ResultJson);
    }

    private static InvoiceCreationResult Result(int invoiceId) => new(invoiceId, InvoiceCreationState.Completed,
        InvoiceCreationEmailState.NotRequested, null, new("fixture-bucket", "fixture-invoice.pdf"));

    private static async Task<InvoiceCreationResult> PersistFinancialAsync(InvoiceDbContext database)
    {
        var invoice = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
            new Invoice { Number = "INV-" + Guid.NewGuid().ToString("N"), CustomerId = 42 }, [], CancellationToken.None);
        var result = Result(invoice.Id);
        var now = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Unspecified);
        database.Files.Add(new InvoiceFile
        {
            InvoiceId = invoice.Id,
            Bucket = result.StoredFile.Bucket,
            ObjectName = result.StoredFile.ObjectName,
            CreatedDate = now,
            ModifiedDate = now
        });
        await database.SaveChangesAsync();
        return result;
    }
}
