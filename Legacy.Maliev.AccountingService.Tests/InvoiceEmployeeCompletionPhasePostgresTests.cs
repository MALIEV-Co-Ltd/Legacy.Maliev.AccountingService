using System.Security.Cryptography;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Real PostgreSQL phase CAS/retention drafts; no external acknowledgment or IAM acceptance is simulated.</summary>
public sealed class InvoiceEmployeeCompletionPhasePostgresTests(InvoiceNotificationPhaseFencePostgresFixture fixture)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    [Fact]
    public async Task CompletedDocumentRetainsSeparateFinancialResultAndExactReplayWithoutUpdatingRows()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var ownership = await SeedAsync(database);
        var store = new InvoiceEmployeeCompletionStore(database);
        Assert.True(await store.BeginDocumentAsync(ownership, Decision(ownership), CancellationToken.None));
        var ready = await ReadyAsync(database, store, ownership);
        var result = new InvoiceCreationResult(ownership.InvoiceId, InvoiceCreationState.Completed, InvoiceCreationEmailState.NotRequested, null, ready.StoredFile!);
        await store.RetainCompletedAsync(ownership, result, CancellationToken.None);
        var before = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal("Completed", before.State);
        Assert.NotNull(before.FinancialOwnershipJson);
        Assert.NotNull(before.FinancialResultJson);
        Assert.NotNull(before.EmployeeCompletionJson);
        Assert.Equal(result, (await store.ReadAsync(ownership, CancellationToken.None))!.Result);
        await store.RetainCompletedAsync(ownership, result, CancellationToken.None);
        var after = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.EmployeeCompletionJson, after.EmployeeCompletionJson);
        Assert.Equal(before.ResultJson, after.ResultJson);
        Assert.Single(await database.Invoices.AsNoTracking().ToListAsync());
        Assert.Single(await database.Items.AsNoTracking().ToListAsync());
        Assert.Single(await database.Files.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task LostDocumentAcknowledgmentCannotAcquireAnotherAttemptOrResetUncertainAdmission()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var ownership = await SeedAsync(database);
        var store = new InvoiceEmployeeCompletionStore(database);
        Assert.True(await store.BeginDocumentAsync(ownership, Decision(ownership), CancellationToken.None));
        await new InvoiceCreationAdmissionStore(database).MarkUncertainAsync(ownership.OperationId, CancellationToken.None);
        Assert.False(await store.BeginDocumentAsync(ownership, Decision(ownership), CancellationToken.None));
        Assert.Equal("DocumentExecuting", (await store.ReadAsync(ownership, CancellationToken.None))!.State);
        Assert.Equal("NeedsReconciliation", (await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).State);
        Assert.Empty(await database.Files.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnlyAcknowledgedFirstNotificationMarkerMayEnterPendingAfterCompletedDocument()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var ownership = await SeedAsync(database);
        var store = new InvoiceEmployeeCompletionStore(database);
        Assert.True(await store.BeginDocumentAsync(ownership, Decision(ownership), CancellationToken.None));
        _ = await ReadyAsync(database, store, ownership);
        var admissions = new InvoiceCreationAdmissionStore(database);
        await admissions.MarkUncertainAsync(ownership.OperationId, CancellationToken.None);
        Assert.True(await store.BeginNotificationAsync(ownership, CancellationToken.None));
        Assert.Equal("Pending", (await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).State);
        await admissions.MarkUncertainAsync(ownership.OperationId, CancellationToken.None);
        Assert.False(await store.BeginNotificationAsync(ownership, CancellationToken.None));
        Assert.Equal("NeedsReconciliation", (await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).State);
        Assert.Equal("NotificationExecuting", (await store.ReadAsync(ownership, CancellationToken.None))!.State);
    }

    [Fact]
    public async Task DeletedDocumentMetadataCannotBeAcceptedFromRetainedFilename()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var ownership = await SeedAsync(database);
        var store = new InvoiceEmployeeCompletionStore(database);
        Assert.True(await store.BeginDocumentAsync(ownership, Decision(ownership), CancellationToken.None));
        _ = await ReadyAsync(database, store, ownership);
        await database.Files.ExecuteDeleteAsync();
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => store.ReadAsync(ownership, CancellationToken.None));
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("operation")]
    [InlineData("invoice")]
    [InlineData("binding")]
    [InlineData("counts")]
    public async Task NonOwningOrIncompleteQuotationReceiptCannotCreateDocumentAuthority(string field)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var ownership = await SeedAsync(database);
        var valid = Decision(ownership);
        var invalid = field switch
        {
            "partial" => valid with { State = "OrdersPartial", CompletedOrders = 1 },
            "operation" => valid with { OperationId = Guid.NewGuid().ToString("D") },
            "invoice" => valid with { InvoiceId = ownership.InvoiceId + 1 },
            "binding" => valid with { FinancialBinding = new string('B', 64) },
            "counts" => valid with { CompletedOrders = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => new InvoiceEmployeeCompletionStore(database).BeginDocumentAsync(ownership, invalid, CancellationToken.None));
        Assert.Null((await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).EmployeeCompletionJson);
        Assert.Empty(await database.Files.AsNoTracking().ToListAsync());
    }

    private static async Task<InvoiceFinancialOwnership> SeedAsync(InvoiceDbContext database)
    {
        var operation = Guid.NewGuid();
        var origin = new InvoiceNotificationOrigin("https://auth.example.invalid", "employee:42", "service:legacy-intranet");
        _ = await new InvoiceCreationAdmissionStore(database).AdmitAsync(operation, 84, origin, new string('A', 64), CancellationToken.None);
        _ = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(new Invoice { Number = "PHASE-" + operation.ToString("N"), CustomerId = 42, Total = 12m },
            [new() { Description = "owned", Quantity = 3, UnitPrice = 4m }], new(operation, 84, origin, new DateTime(2026, 10, 6, 1, 2, 3, DateTimeKind.Unspecified)), CancellationToken.None);
        return await new InvoiceFinancialOwnershipStore(database).ReadAsync(operation, CancellationToken.None);
    }
    private static InvoiceQuotationOperationReceipt Decision(InvoiceFinancialOwnership ownership) => new(1, ownership.OperationId.ToString("D"), 84,
        ownership.InvoiceId, ownership.OriginIssuer, ownership.EmployeeSubject, ownership.RequesterSubject, "service:legacy-accounting",
        ownership.OriginalQuotationVersion, ownership.FinancialBinding, "invoice-creation-financial-v1", "Completed", "2026-10-06T01:02:04.0000000Z", 2, 2,
        "2026-10-06T01:02:05.0000000Z");
    private static async Task<InvoiceEmployeeCompletionPhase> ReadyAsync(InvoiceDbContext database, InvoiceEmployeeCompletionStore store, InvoiceFinancialOwnership ownership)
    {
        var file = new InvoiceCreationStoredFile("maliev.com", $"invoices/{ownership.InvoiceId}/invoice_owned.pdf");
        await new InvoiceCreationStore(database, TimeProvider.System).LinkFileAsync(ownership.InvoiceId, file.Bucket, file.ObjectName, CancellationToken.None);
        return await store.RetainDocumentAsync(ownership, Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), file, CancellationToken.None);
    }
}
