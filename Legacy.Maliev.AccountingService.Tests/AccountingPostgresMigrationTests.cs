using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class AccountingPostgresMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer paymentPostgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly PostgreSqlContainer invoicePostgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly PostgreSqlContainer receiptPostgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public Task InitializeAsync() => Task.WhenAll(paymentPostgres.StartAsync(), invoicePostgres.StartAsync(), receiptPostgres.StartAsync());

    public async Task DisposeAsync()
    {
        await paymentPostgres.DisposeAsync();
        await invoicePostgres.DisposeAsync();
        await receiptPostgres.DisposeAsync();
    }

    [Fact]
    public async Task InitialMigrations_CreateThreeIsolatedLegacyDatabasesAndComputedValues()
    {
        await using var paymentContext = PaymentContext();
        await using var invoiceContext = InvoiceContext();
        await using var receiptContext = ReceiptContext();
        await Task.WhenAll(
            paymentContext.Database.MigrateAsync(),
            invoiceContext.Database.MigrateAsync(),
            receiptContext.Database.MigrateAsync());

        var repository = Repository(paymentContext, invoiceContext, receiptContext);
        var income = await repository.CreateAsync(new PaymentDirection { Name = "Income" }, CancellationToken.None);
        var expense = await repository.CreateAsync(new PaymentDirection { Name = "Expense" }, CancellationToken.None);
        var method = await repository.CreateAsync(new PaymentMethod { Name = "Bank" }, CancellationToken.None);
        var type = await repository.CreateAsync(new PaymentType { Name = "Job" }, CancellationToken.None);
        await repository.CreateAsync(new Payment
        {
            PaymentDirectionId = income.Id,
            PaymentMethodId = method.Id,
            PaymentTypeId = type.Id,
            Amount = 1200.50m,
            PaymentDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
        }, CancellationToken.None);
        await repository.CreateAsync(new Payment
        {
            PaymentDirectionId = expense.Id,
            PaymentMethodId = method.Id,
            PaymentTypeId = type.Id,
            Amount = 200m,
            PaymentDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
        }, CancellationToken.None);
        await repository.CreateAsync(new Payment
        {
            PaymentDirectionId = income.Id,
            PaymentMethodId = method.Id,
            PaymentTypeId = type.Id,
            Amount = 1000m,
            PaymentDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
        }, CancellationToken.None);

        var invoice = await repository.CreateAsync(new Invoice { Number = "INV-1", CustomerId = 42 }, CancellationToken.None);
        var invoiceLine = await repository.CreateAsync(new InvoiceOrderItem
        {
            InvoiceId = invoice.Id,
            Description = "Legacy print",
            Quantity = 3,
            UnitPrice = 12.25m,
        }, CancellationToken.None);
        await invoiceContext.Entry(invoiceLine).ReloadAsync();

        var receipt = await repository.CreateAsync(new Receipt
        {
            InvoiceNumber = "INV-1",
            PaymentDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            Total = 107m,
            WithholdingTax = 3m,
        }, CancellationToken.None);
        await receiptContext.Entry(receipt).ReloadAsync();

        Assert.Equal(36.75m, invoiceLine.Subtotal);
        Assert.Equal(104m, receipt.AmountPaid);
        Assert.Equal(2, (await repository.GetSummaryAsync("month", income: true, CancellationToken.None)).Count);
        var financialSummary = await repository.GetFinancialSummaryAsync("month", jobIncomeOnly: false, CancellationToken.None);
        var detail = Assert.Single(financialSummary!.Details);
        Assert.Equal(1000.50m, detail.CurrentAmount);
        Assert.Equal(1000m, detail.PreviousAmount);
        Assert.Equal(0.50m, detail.DeltaAmount);
        Assert.Equal(2, (await repository.GetYearlyDetailAsync(true, 2026, null, CancellationToken.None))!.Count);
        Assert.Equal(6, await TableCount(paymentContext));
        Assert.Equal(3, await TableCount(invoiceContext));
        Assert.Equal(3, await TableCount(receiptContext));
    }

    [Fact]
    public async Task InvoiceQuery_AppliesPaidSearchAndSortBeforePagination()
    {
        await using var paymentContext = PaymentContext();
        await using var invoiceContext = InvoiceContext();
        await using var receiptContext = ReceiptContext();
        await Task.WhenAll(
            paymentContext.Database.MigrateAsync(),
            invoiceContext.Database.MigrateAsync(),
            receiptContext.Database.MigrateAsync());
        var repository = Repository(paymentContext, invoiceContext, receiptContext);
        var marker = $"PAGE-{Guid.NewGuid():N}";
        var created = new[]
        {
            new Invoice { Number = $"{marker}-A", CustomerId = 42, IsPaid = true, ReceiptId = 910001, CreatedDate = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc) },
            new Invoice { Number = $"{marker}-B", CustomerId = 42, IsPaid = false, ReceiptId = 910002, CreatedDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) },
            new Invoice { Number = $"{marker}-C", CustomerId = 42, IsPaid = true, ReceiptId = 910003, CreatedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Invoice { Number = $"{marker}-D", CustomerId = 42, IsPaid = true, ReceiptId = 910004, CreatedDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) },
        };
        invoiceContext.Invoices.AddRange(created);
        await invoiceContext.SaveChangesAsync();
        invoiceContext.ChangeTracker.Clear();

        var first = await repository.GetInvoicesAsync(
            42,
            InvoiceSortType.InvoiceCreatedDate_Ascending,
            marker,
            true,
            1,
            2,
            CancellationToken.None);
        var second = await repository.GetInvoicesAsync(
            42,
            InvoiceSortType.InvoiceCreatedDate_Ascending,
            marker,
            true,
            2,
            2,
            CancellationToken.None);
        var byReceipt = await repository.GetInvoicesAsync(
            null,
            null,
            "910002",
            null,
            1,
            10,
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(3, first.TotalRecords);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal([created[2].Id, created[3].Id], first.Items.Select(invoice => invoice.Id));
        Assert.NotNull(second);
        Assert.Equal([created[0].Id], second.Items.Select(invoice => invoice.Id));
        Assert.Equal(created[1].Id, Assert.Single(byReceipt!.Items).Id);
    }

    [Fact]
    public async Task ReceiptWorkflowStore_ReconcilesAcrossExistingInvoiceAndReceiptSchemasWithoutMigration()
    {
        await using var invoiceContext = InvoiceContext();
        await using var receiptContext = ReceiptContext();
        await Task.WhenAll(invoiceContext.Database.MigrateAsync(), receiptContext.Database.MigrateAsync());
        var marker = $"WF-{Guid.NewGuid():N}";
        var invoice = new Invoice
        {
            Number = marker,
            CustomerId = 42,
            Currency = "THB",
            IsPaid = true,
            PaymentDate = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc),
            Subtotal = 100m,
            Vat = 7m,
            Total = 107m,
            Outstanding = 107m,
        };
        invoiceContext.Add(invoice);
        await invoiceContext.SaveChangesAsync();
        invoiceContext.Add(new InvoiceOrderItem { InvoiceId = invoice.Id, Description = "Print", Quantity = 1, UnitPrice = 100m });
        await invoiceContext.SaveChangesAsync();
        var store = new ReceiptWorkflowStore(invoiceContext, receiptContext, TimeProvider.System);

        var before = await store.GetAsync(invoice.Id, CancellationToken.None);
        var receipt = await store.CreateReceiptAsync(before.Invoice, before.InvoiceItems, "paid", CancellationToken.None);
        await store.LinkInvoiceAsync(invoice.Id, receipt.Id, CancellationToken.None);
        await store.LinkFileAsync(receipt.Id, "maliev.com", $"receipts/{receipt.Id}/receipt_{receipt.Id}.pdf", CancellationToken.None);
        await store.LinkFileAsync(receipt.Id, "maliev.com", $"receipts/{receipt.Id}/receipt_{receipt.Id}.pdf", CancellationToken.None);

        var reconciled = await store.GetAsync(invoice.Id, CancellationToken.None);
        Assert.Equal(receipt.Id, reconciled.Invoice.ReceiptId);
        Assert.Single(reconciled.ReceiptItems);
        Assert.Single(reconciled.ReceiptFiles);

        await store.DeleteReceiptAsync(receipt.Id, CancellationToken.None);
        await store.UnlinkInvoiceAsync(invoice.Id, receipt.Id, CancellationToken.None);
        var removed = await store.GetAsync(invoice.Id, CancellationToken.None);
        Assert.Null(removed.Invoice.ReceiptId);
        Assert.Null(removed.Receipt);
    }

    [Fact]
    public async Task ReceiptOperationLock_SerializesTheSameInvoiceAcrossDatabaseConnections()
    {
        await using var firstContext = InvoiceContext();
        await using var secondContext = InvoiceContext();
        await firstContext.Database.MigrateAsync();
        var firstLock = new PostgresReceiptOperationLock(firstContext);
        var secondLock = new PostgresReceiptOperationLock(secondContext);

        var firstLease = await firstLock.AcquireAsync(42, CancellationToken.None);
        using var blockedAttempt = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAsync<ReceiptWorkflowDependencyException>(async () =>
            await secondLock.AcquireAsync(42, blockedAttempt.Token));

        await firstLease.DisposeAsync();
        await using var secondLease = await secondLock.AcquireAsync(42, CancellationToken.None);
    }

    [Fact]
    public async Task InvoiceCreationStore_AtomicallyCreatesItemsAndReusesExistingFileLinkWithoutSchemaChange()
    {
        await using var context = InvoiceContext();
        await context.Database.MigrateAsync();
        var store = new InvoiceCreationStore(context, TimeProvider.System);
        var marker = $"CREATE-{Guid.NewGuid():N}";

        var invoice = await store.CreateAsync(
            new Invoice { Number = marker, CustomerId = 42, Currency = "THB", Total = 107m },
            [new InvoiceOrderItem { Description = "Thai fixture", Quantity = 2, UnitPrice = 50m }],
            CancellationToken.None);
        await store.LinkFileAsync(invoice.Id, "maliev.com", $"invoices/{invoice.Id}/invoice_{marker}.pdf", CancellationToken.None);
        await store.LinkFileAsync(invoice.Id, "maliev.com", $"invoices/{invoice.Id}/invoice_{marker}.pdf", CancellationToken.None);
        context.ChangeTracker.Clear();

        var found = await store.FindByNumberAsync(marker, CancellationToken.None);
        Assert.Equal(invoice.Id, found?.Id);
        Assert.Single(await context.Items.Where(value => value.InvoiceId == invoice.Id).ToListAsync());
        Assert.Single(await context.Files.Where(value => value.InvoiceId == invoice.Id).ToListAsync());
        Assert.Equal(3, await TableCount(context));
    }

    private static async Task<int> TableCount(DbContext context) =>
        await context.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'").SingleAsync();

    private static AccountingRepository Repository(PaymentDbContext payment, InvoiceDbContext invoice, ReceiptDbContext receipt)
    {
        var cache = new Mock<IAccountingCache>();
        return new AccountingRepository(payment, invoice, receipt, cache.Object, TimeProvider.System);
    }

    private PaymentDbContext PaymentContext() => new(new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql(paymentPostgres.GetConnectionString()).Options);
    private InvoiceDbContext InvoiceContext() => new(new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql(invoicePostgres.GetConnectionString()).Options);
    private ReceiptDbContext ReceiptContext() => new(new DbContextOptionsBuilder<ReceiptDbContext>().UseNpgsql(receiptPostgres.GetConnectionString()).Options);
}
