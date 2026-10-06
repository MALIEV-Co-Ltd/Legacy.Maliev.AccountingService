using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Legacy.Maliev.AccountingService.Tests;

// Unpublished regression draft: real normal-Program store/options/cache, not live-provider proof.
public sealed class ReceiptCreationRetryTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshCreation_NormalRetryStrategyPersistsOneReceiptAndComputedLines(bool hasPaymentDate)
    {
        DateTime? paymentDate = hasPaymentDate ? new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc) : null;
        await SeedAsync(false, paymentDate);
        await PrimeInvoiceAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        await using var scope = await ScopeAsync();
        var store = scope.ServiceProvider.GetRequiredService<IReceiptWorkflowStore>();
        var input = await store.GetAsync(42, CancellationToken.None);
        Assert.Equal(paymentDate, input.Invoice.PaymentDate);
        var startedAtUtc = DateTime.UtcNow;
        var created = await store.CreateReceiptAsync(input.Invoice, input.InvoiceItems, "Synthetic creation", CancellationToken.None);
        Assert.True(created.Id > 0);
        Assert.Equal("SYNTHETIC-CREATE-42", created.InvoiceNumber);
        Assert.Equal("THB", created.Currency);
        Assert.Equal(200m, created.Subtotal);
        Assert.Equal(14m, created.Vat);
        Assert.Equal(214m, created.Total);
        Assert.Equal(212m, created.AmountPaid);
        Assert.Equal(input.Invoice.CustomerId, created.CustomerId);
        Assert.Equal(input.Invoice.BillingAddressCity, created.BillingAddressCity);
        Assert.Equal(input.Invoice.TaxIdentification, created.TaxIdentification);
        Assert.Equal("Synthetic creation", created.Comment);
        Assert.NotNull(created.CreatedDate);
        Assert.NotNull(created.ModifiedDate);
        Assert.Equal(DateTimeKind.Unspecified, created.CreatedDate.GetValueOrDefault().Kind);
        Assert.Equal(DateTimeKind.Unspecified, created.ModifiedDate.GetValueOrDefault().Kind);
        Assert.Equal(DateTimeKind.Utc, created.PaymentDate.Kind);
        if (paymentDate is { } supplied)
            Assert.Equal(supplied, created.PaymentDate);
        else
            Assert.InRange(created.PaymentDate, startedAtUtc, DateTime.UtcNow);
        await using var receipts = fixture.ReceiptDatabase();
        var persisted = Assert.Single(await receipts.Receipts.AsNoTracking().ToArrayAsync());
        Assert.Equal(created.Id, persisted.Id);
        Assert.Equal(DateTimeKind.Utc, persisted.PaymentDate.Kind);
        Assert.Equal(created.PaymentDate, persisted.PaymentDate);
        var item = Assert.Single(await receipts.Items.AsNoTracking().ToArrayAsync());
        Assert.Equal(created.Id, item.ReceiptId);
        Assert.Equal(200m, item.Subtotal);
        Assert.Equal(2, item.Quantity);
        var after = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(before.Invoice, after.Invoice);
        Assert.Equal(before.Payment, after.Payment);
        Assert.Equal(before.Journal, after.Journal);
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Fact]
    public async Task ExistingReceipt_ReconciliationDoesNotInsertOrChangeAnyOwnedState()
    {
        await SeedAsync(true);
        await PrimeInvoiceAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        await using var scope = await ScopeAsync();
        var store = scope.ServiceProvider.GetRequiredService<IReceiptWorkflowStore>();
        var input = await store.GetAsync(42, CancellationToken.None);
        var receipt = await store.CreateReceiptAsync(input.Invoice, input.InvoiceItems, "Must not replace persisted comment", CancellationToken.None);
        Assert.Equal(91, receipt.Id);
        Assert.Equal("Persisted synthetic receipt", receipt.Comment);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Fact]
    public async Task PreCancellation_DoesNotInsertReceiptOrChangeOwnedCacheAndTables()
    {
        await SeedAsync(false);
        await PrimeInvoiceAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        await using var scope = await ScopeAsync();
        var store = scope.ServiceProvider.GetRequiredService<IReceiptWorkflowStore>();
        var input = await store.GetAsync(42, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.CreateReceiptAsync(input.Invoice, input.InvoiceItems, null, cancelled.Token));
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Fact]
    public async Task LineInsertionFailure_RollsBackReceiptAndLeavesCallerContextClean()
    {
        await AssertPreCommitFailureAsync(cancel: false);
    }

    [Fact]
    public async Task CancellationAfterReceiptInsertion_RollsBackWithoutReplayingInsert()
    {
        await AssertPreCommitFailureAsync(cancel: true);
    }

    [Fact]
    public async Task LostCommitAcknowledgment_FailsClosedAndLaterReconcilesSinglePersistedReceipt()
    {
        await SeedAsync(false);
        await PrimeInvoiceAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        await using var scope = await ScopeAsync();
        var configured = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var options = (DbContextOptions<ReceiptDbContext>)configured.GetService<IDbContextOptions>();
        var inserts = new CreationInsertControl(null);
        var commit = new LoseCommitAcknowledgment();
        await using var receipts = new ReceiptDbContext(new DbContextOptionsBuilder<ReceiptDbContext>(options)
            .AddInterceptors(inserts, commit).Options);
        AssertRetryStrategy(receipts);
        var store = new ReceiptWorkflowStore(scope.ServiceProvider.GetRequiredService<InvoiceDbContext>(), receipts,
            TimeProvider.System, scope.ServiceProvider.GetRequiredService<IAccountingCache>());
        var input = await store.GetAsync(42, CancellationToken.None);
        await Assert.ThrowsAsync<ReceiptWorkflowUnavailableException>(() =>
            store.CreateReceiptAsync(input.Invoice, input.InvoiceItems, null, CancellationToken.None));
        Assert.Equal(1, inserts.ReceiptInsertAttempts);
        Assert.Equal(1, inserts.LineInsertAttempts);
        Assert.Equal(1, commit.CommitAcknowledgmentsLost);
        await using var persisted = fixture.ReceiptDatabase();
        var accepted = Assert.Single(await persisted.Receipts.AsNoTracking().ToArrayAsync());
        Assert.Equal(accepted.Id, Assert.Single(await persisted.Items.AsNoTracking().ToArrayAsync()).ReceiptId);
        var after = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(before.Invoice, after.Invoice);
        Assert.Equal(before.Payment, after.Payment);
        Assert.Equal(before.Journal, after.Journal);
        await using var recovery = await ScopeAsync();
        var retry = recovery.ServiceProvider.GetRequiredService<IReceiptWorkflowStore>();
        var reconciled = await retry.CreateReceiptAsync(input.Invoice, input.InvoiceItems, null, CancellationToken.None);
        Assert.Equal(accepted.Id, reconciled.Id);
        Assert.Equal(after, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InternalWorkflow_SyntheticProvidersCompleteOnceAndNormalHttpReadExposesLink(bool sendEmail)
    {
        await SeedAsync(false);
        await PrimeInvoiceAsync();
        await using var scope = await ScopeAsync();
        var operation = Guid.NewGuid();
        var providers = new SyntheticReceiptProviders(operation);
        var workflow = new ReceiptWorkflowService(scope.ServiceProvider.GetRequiredService<IReceiptWorkflowStore>(),
            providers, providers, providers, providers, providers,
            scope.ServiceProvider.GetRequiredService<IReceiptOperationJournal>(),
            scope.ServiceProvider.GetRequiredService<IReceiptOperationLock>());
        var request = new CreateReceiptRequest("Synthetic owned workflow", sendEmail, 7);
        var result = await workflow.CreateAsync(42, request, operation, CancellationToken.None);
        Assert.Equal(ReceiptWorkflowState.Completed, result.State);
        Assert.Equal(sendEmail ? ReceiptEmailState.Delivered : ReceiptEmailState.NotRequested, result.EmailState);
        Assert.True(result.ReceiptId > 0);
        using var client = await fixture.ReceiptClientAsync([AccountingPermissions.Read]);
        using var response = await client.GetAsync("/invoices/42");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(result.ReceiptId, (await response.Content.ReadFromJsonAsync<Invoice>())!.ReceiptId);
        await using var persisted = fixture.ReceiptDatabase();
        Assert.Equal(result.ReceiptId, Assert.Single(await persisted.Receipts.AsNoTracking().ToArrayAsync()).Id);
        Assert.Equal(result.ReceiptId, Assert.Single(await persisted.Items.AsNoTracking().ToArrayAsync()).ReceiptId);
        var file = Assert.Single(await persisted.Files.AsNoTracking().ToArrayAsync());
        Assert.Equal("maliev.com", file.Bucket);
        Assert.Equal($"receipts/{result.ReceiptId}/receipt_{result.ReceiptId}.pdf", file.ObjectName);
        Assert.Equal(1, providers.Signatures);
        Assert.Equal(1, providers.Documents);
        Assert.Equal(1, providers.Exists);
        Assert.Equal(1, providers.Uploads);
        Assert.Equal(sendEmail ? 1 : 0, providers.Customers);
        Assert.Equal(sendEmail ? 1 : 0, providers.Notifications);
        var before = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(result, await workflow.CreateAsync(42, request, operation, CancellationToken.None));
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(1, providers.Uploads);
        Assert.Equal(sendEmail ? 1 : 0, providers.Notifications);
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    private async Task AssertPreCommitFailureAsync(bool cancel)
    {
        await SeedAsync(false);
        await PrimeInvoiceAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        await using var scope = await ScopeAsync();
        var configured = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var options = (DbContextOptions<ReceiptDbContext>)configured.GetService<IDbContextOptions>();
        using var caller = new CancellationTokenSource();
        var control = new CreationInsertControl(cancel ? caller : null) { RejectLineInsert = true };
        await using var receipts = new ReceiptDbContext(new DbContextOptionsBuilder<ReceiptDbContext>(options)
            .AddInterceptors(control).Options);
        AssertRetryStrategy(receipts);
        var store = new ReceiptWorkflowStore(scope.ServiceProvider.GetRequiredService<InvoiceDbContext>(), receipts,
            TimeProvider.System, scope.ServiceProvider.GetRequiredService<IAccountingCache>());
        var input = await store.GetAsync(42, CancellationToken.None);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateReceiptAsync(input.Invoice, input.InvoiceItems, null, caller.Token));
        else
            await Assert.ThrowsAsync<DbUpdateException>(() => store.CreateReceiptAsync(input.Invoice, input.InvoiceItems, null, caller.Token));
        Assert.Equal(1, control.ReceiptInsertAttempts);
        Assert.Equal(1, control.LineInsertAttempts);
        Assert.DoesNotContain(receipts.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var scope = await fixture.ReceiptScopeAsync();
        AssertRetryStrategy(scope.ServiceProvider.GetRequiredService<ReceiptDbContext>());
        return scope;
    }

    private static void AssertRetryStrategy(ReceiptDbContext context)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        Assert.True(strategy.RetriesOnFailure);
        Assert.IsType<Npgsql.EntityFrameworkCore.PostgreSQL.NpgsqlRetryingExecutionStrategy>(strategy);
    }

    private async Task PrimeInvoiceAsync()
    {
        using var client = await fixture.ReceiptClientAsync([AccountingPermissions.Read]);
        using var response = await client.GetAsync("/invoices/42");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<Invoice>())!.ReceiptId);
        await using var scope = await fixture.ReceiptScopeAsync();
        var cache = scope.ServiceProvider.GetRequiredService<IAccountingCache>();
        Assert.IsType<DistributedAccountingCache>(cache);
        Assert.NotNull(await cache.GetAsync<Invoice>("invoice:42", CancellationToken.None));
    }

    private async Task SeedAsync(bool existing, DateTime? paymentDate = null)
    {
        await fixture.ResetAsync();
        await using var invoices = fixture.InvoiceDatabase();
        await invoices.Files.ExecuteDeleteAsync();
        await invoices.Items.ExecuteDeleteAsync();
        await invoices.Invoices.ExecuteDeleteAsync();
        invoices.Invoices.Add(new Invoice
        {
            Id = 42,
            Number = "SYNTHETIC-CREATE-42",
            CustomerId = 7,
            Currency = "THB",
            Subtotal = 200m,
            Vat = 14m,
            Total = 214m,
            WithholdingTax = 2m,
            BillingAddressCity = "Synthetic Bangkok",
            TaxIdentification = "SYNTHETIC-TAX",
            PaymentDate = paymentDate,
        });
        invoices.Items.Add(new InvoiceOrderItem { Id = 421, InvoiceId = 42, Description = "Synthetic line", Quantity = 2, UnitPrice = 100m });
        await invoices.SaveChangesAsync();
        await using var receipts = fixture.ReceiptDatabase();
        await receipts.Files.ExecuteDeleteAsync();
        await receipts.Items.ExecuteDeleteAsync();
        await receipts.Receipts.ExecuteDeleteAsync();
        if (existing)
        {
            receipts.Receipts.Add(new Receipt { Id = 91, InvoiceNumber = "SYNTHETIC-CREATE-42", Comment = "Persisted synthetic receipt", Currency = "THB" });
            await receipts.SaveChangesAsync();
        }
        await using var scope = await fixture.ReceiptScopeAsync();
        await scope.ServiceProvider.GetRequiredService<IAccountingCache>().RemoveAsync("invoice:42", CancellationToken.None);
    }

    private sealed class CreationInsertControl(CancellationTokenSource? caller) : DbCommandInterceptor
    {
        public bool RejectLineInsert { get; init; }
        public int ReceiptInsertAttempts { get; private set; }
        public int LineInsertAttempts { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"Receipt\"", StringComparison.Ordinal)) ReceiptInsertAttempts++;
            if (command.CommandText.Contains("INSERT INTO \"OrderItem\"", StringComparison.Ordinal))
            {
                LineInsertAttempts++;
                if (RejectLineInsert)
                {
                    if (caller is not null)
                    {
                        caller.Cancel();
                        throw new OperationCanceledException(caller.Token);
                    }
                    throw new InvalidOperationException("Synthetic line insert failure");
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class LoseCommitAcknowledgment : DbTransactionInterceptor
    {
        public int CommitAcknowledgmentsLost { get; private set; }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            CommitAcknowledgmentsLost++;
            throw new NpgsqlException("Synthetic lost receipt commit acknowledgment", new TimeoutException());
        }
    }

    private sealed class SyntheticReceiptProviders(Guid expectedOperation) : IReceiptDocumentClient,
        IReceiptFileClient, IReceiptCustomerClient, IReceiptSignatureClient, IReceiptNotificationClient
    {
        private static readonly byte[] Pdf = [37, 80, 68, 70];
        public int Signatures { get; private set; }
        public int Documents { get; private set; }
        public int Exists { get; private set; }
        public int Uploads { get; private set; }
        public int Customers { get; private set; }
        public int Notifications { get; private set; }

        Task<byte[]?> IReceiptSignatureClient.GetAsync(int employeeId, CancellationToken cancellationToken)
        {
            Assert.Equal(7, employeeId);
            Signatures++;
            return Task.FromResult<byte[]?>(null);
        }

        public Task<byte[]> RenderAsync(Receipt receipt, IReadOnlyList<ReceiptOrderItem> items, byte[]? signature,
            CancellationToken cancellationToken)
        {
            Assert.True(receipt.Id > 0);
            Assert.Equal(212m, receipt.AmountPaid);
            Assert.Equal(200m, Assert.Single(items).Subtotal);
            Documents++;
            return Task.FromResult(Pdf.ToArray());
        }

        public Task<bool> ExistsAsync(string bucket, string objectName, CancellationToken cancellationToken)
        {
            Assert.Equal("maliev.com", bucket);
            Assert.StartsWith("receipts/", objectName, StringComparison.Ordinal);
            Exists++;
            return Task.FromResult(false);
        }

        public Task<ReceiptStoredFile> UploadAsync(string bucket, string path, string fileName, byte[] content,
            Guid operationId, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedOperation, operationId);
            Assert.Equal(Pdf, content);
            Uploads++;
            return Task.FromResult(new ReceiptStoredFile(bucket, $"{path}/{fileName}"));
        }

        Task<ReceiptCustomerContact> IReceiptCustomerClient.GetAsync(int customerId, CancellationToken cancellationToken)
        {
            Assert.Equal(7, customerId);
            Customers++;
            return Task.FromResult(new ReceiptCustomerContact("synthetic@invalid.example", "Synthetic customer"));
        }

        public Task<string?> SendAsync(string customerEmail, string customerName, Receipt receipt, byte[] pdf,
            Guid operationId, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedOperation, operationId);
            Assert.Equal("synthetic@invalid.example", customerEmail);
            Assert.Equal(Pdf, pdf);
            Notifications++;
            return Task.FromResult<string?>("synthetic-provider-acknowledgment");
        }

        public Task DeleteAsync(string bucket, string objectName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic creation proof must not delete objects.");

        public Task<byte[]> DownloadAsync(string bucket, string objectName, int maximumBytes, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic creation proof must not download objects.");
    }
}
