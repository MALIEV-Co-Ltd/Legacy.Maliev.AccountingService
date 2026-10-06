using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Actual PostgreSQL atomic receipt and financial drift regressions; no remote authority is simulated here.</summary>
public sealed class InvoiceFinancialOwnershipPostgresTests(InvoiceNotificationPhaseFencePostgresFixture fixture)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    private static readonly InvoiceNotificationOrigin Origin = new("https://auth.example.invalid", "employee:42", "service:legacy-intranet");
    private static readonly DateTime OriginalVersion = new(2026, 10, 6, 1, 2, 3, DateTimeKind.Unspecified);

    [Fact]
    public async Task CommitRetainsActualComputedItemsWithoutNotificationCompletion()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = await AdmitAsync(database);
        var invoice = await CommitAsync(database, operation);
        var receipt = await new InvoiceFinancialOwnershipStore(database).ReadAsync(operation, CancellationToken.None);
        Assert.Equal(1, receipt.ContractVersion);
        Assert.Equal(operation, receipt.OperationId);
        Assert.Equal(84, receipt.QuotationId);
        Assert.Equal(invoice.Id, receipt.InvoiceId);
        Assert.Equal(Origin.Issuer, receipt.OriginIssuer);
        Assert.Equal(Origin.EmployeeSubject, receipt.EmployeeSubject);
        Assert.Equal(Origin.ServiceSubject, receipt.RequesterSubject);
        Assert.Equal("2026-10-06T01:02:03.0000000Z", receipt.OriginalQuotationVersion);
        Assert.Matches("^[0-9A-F]{64}$", receipt.FinancialBinding);
        Assert.Equal(12m, (await database.Items.AsNoTracking().SingleAsync()).Subtotal);
        var committed = await new InvoiceFinancialOwnershipStore(database).ReadCommittedAsync(operation, CancellationToken.None);
        Assert.Equal(receipt, committed.Ownership);
        Assert.Equal(invoice.Id, committed.Invoice.Id);
        Assert.Equal(invoice.Number, committed.Invoice.Number);
        Assert.Equal(invoice.Total, committed.Invoice.Total);
        Assert.Equal(12m, Assert.Single(committed.Items).Subtotal);
        Assert.Equal(invoice.Id, committed.Items[0].InvoiceId);
        Assert.True(await new InvoiceFinancialOwnershipStore(database).HasFenceAsync(invoice.Id, CancellationToken.None));
        Assert.False(await new InvoiceFinancialOwnershipStore(database).HasFenceAsync(invoice.Id + 100000, CancellationToken.None));
        Assert.Empty(committed.Invoice.InvoiceFiles);
        Assert.Empty(database.ChangeTracker.Entries<Invoice>());
        var admission = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal("Pending", admission.State);
        Assert.Null(admission.ResultJson);
        Assert.Null(admission.FinancialResultJson);
        Assert.NotNull(admission.FinancialOwnershipJson);
        Assert.Empty(await database.Files.AsNoTracking().ToListAsync());
        Assert.Empty(await database.InvoiceNotificationCorrelations.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("employee")]
    [InlineData("requester")]
    [InlineData("quotation")]
    [InlineData("operation")]
    [InlineData("local-version")]
    [InlineData("zero-version")]
    public async Task InvalidRetainedAuthorityLeavesNoInvoiceItemsOrOwnership(string field)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = await AdmitAsync(database);
        var origin = field switch
        {
            "issuer" => Origin with { Issuer = "https://foreign.example.invalid" },
            "employee" => Origin with { EmployeeSubject = "employee:other" },
            "requester" => Origin with { ServiceSubject = "service:other" },
            _ => Origin,
        };
        var authority = new InvoiceFinancialCommitContext(field == "operation" ? Guid.NewGuid() : operation,
            field == "quotation" ? 85 : 84, origin, field switch
            {
                "local-version" => DateTime.SpecifyKind(OriginalVersion, DateTimeKind.Local),
                "zero-version" => DateTime.MinValue,
                _ => OriginalVersion,
            });
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => CommitAsync(database, operation, authority));
        Assert.Empty(await database.Invoices.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Items.AsNoTracking().ToListAsync());
        Assert.Null((await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).FinancialOwnershipJson);
    }

    [Theory]
    [InlineData("invoice")]
    [InlineData("item")]
    [InlineData("new-item")]
    [InlineData("deleted-item")]
    [InlineData("fingerprint")]
    [InlineData("issuer")]
    [InlineData("receipt")]
    public async Task ReadbackRejectsFinancialOrAdmissionDriftWithoutRewritingReceipt(string field)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = await AdmitAsync(database);
        var invoice = await CommitAsync(database, operation);
        var admission = await database.InvoiceCreationAdmissions.SingleAsync();
        var receiptBefore = admission.FinancialOwnershipJson;
        switch (field)
        {
            case "invoice": (await database.Invoices.SingleAsync()).Total = 99m; break;
            case "item": (await database.Items.SingleAsync()).Quantity = 4; break;
            case "new-item": database.Items.Add(new() { InvoiceId = invoice.Id, Description = "foreign", Quantity = 1, UnitPrice = 1m }); break;
            case "deleted-item": database.Items.Remove(await database.Items.SingleAsync()); break;
            case "fingerprint": admission.IntentFingerprint = new string('B', 64); break;
            case "issuer": admission.OriginIssuer = "https://foreign.example.invalid"; break;
            case "receipt": admission.FinancialOwnershipJson = "{}"; break;
        }
        await database.SaveChangesAsync();
        var beforeRead = admission.FinancialOwnershipJson;
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => new InvoiceFinancialOwnershipStore(database).ReadAsync(operation, CancellationToken.None));
        Assert.Equal(beforeRead, (await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).FinancialOwnershipJson);
        if (field != "receipt") Assert.Equal(receiptBefore, beforeRead);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("NeedsReconciliation")]
    [InlineData("Completed")]
    public async Task CommittedReceiptReadbackIsStableAcrossAdmissionOutcomeAndNeverRecreates(string state)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = await AdmitAsync(database);
        _ = await CommitAsync(database, operation);
        var ownership = new InvoiceFinancialOwnershipStore(database);
        var expected = await ownership.ReadAsync(operation, CancellationToken.None);
        var admission = await database.InvoiceCreationAdmissions.SingleAsync();
        admission.State = state;
        await database.SaveChangesAsync();
        Assert.Equal(expected, await ownership.ReadAsync(operation, CancellationToken.None));
        Assert.Single(await database.Invoices.AsNoTracking().ToListAsync());
        Assert.True(await new InvoiceFinancialOwnershipStore(database).HasFenceAsync((await database.Invoices.AsNoTracking().SingleAsync()).Id, CancellationToken.None));
        Assert.Single(await database.Items.AsNoTracking().ToListAsync());
        Assert.Equal(state, (await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task SecondFinancialCommitOnSameOperationRollsBackNewInvoiceAndItems()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = await AdmitAsync(database);
        _ = await CommitAsync(database, operation);
        var expected = await new InvoiceFinancialOwnershipStore(database).ReadAsync(operation, CancellationToken.None);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => CommitAsync(database, operation));
        Assert.Equal(expected, await new InvoiceFinancialOwnershipStore(database).ReadAsync(operation, CancellationToken.None));
        Assert.Single(await database.Invoices.AsNoTracking().ToListAsync());
        Assert.Single(await database.Items.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task LegacyNullIssuerRowsAndUnboundSameNumberInvoiceCannotBecomeFinancialAuthority()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var operation = Guid.NewGuid();
        _ = await new InvoiceCreationAdmissionStore(database).AdmitAsync(operation, 84, Origin.EmployeeSubject,
            Origin.ServiceSubject, new string('A', 64), CancellationToken.None);
        _ = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
            new Invoice { Number = "unbound", CustomerId = 42, Total = 12m },
            [new() { Description = "actual", Quantity = 3, UnitPrice = 4m }], CancellationToken.None);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => new InvoiceFinancialOwnershipStore(database).ReadAsync(operation, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => CommitAsync(database, operation));
        Assert.Single(await database.Invoices.AsNoTracking().ToListAsync());
        Assert.False(await new InvoiceFinancialOwnershipStore(database).HasFenceAsync((await database.Invoices.AsNoTracking().SingleAsync()).Id, CancellationToken.None));
        Assert.Null((await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).FinancialOwnershipJson);
    }

    private static async Task<Guid> AdmitAsync(InvoiceDbContext database)
    {
        var operation = Guid.NewGuid();
        _ = await new InvoiceCreationAdmissionStore(database).AdmitAsync(operation, 84, Origin, new string('A', 64), CancellationToken.None);
        return operation;
    }

    private static Task<Invoice> CommitAsync(InvoiceDbContext database, Guid operation, InvoiceFinancialCommitContext? authority = null) =>
        new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
            new Invoice { Number = "INV-" + Guid.NewGuid().ToString("N"), CustomerId = 42, Total = 12m },
            [new() { Description = "actual", Quantity = 3, UnitPrice = 4m, Subtotal = 999m }],
            authority ?? new(operation, 84, Origin, OriginalVersion), CancellationToken.None);
}
