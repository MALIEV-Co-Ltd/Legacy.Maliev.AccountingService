using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class ReceiptInvoiceCacheHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    [Fact]
    public async Task ReconcileReceipt_CommittedLinkReplacesPrimedInvoiceReadAndExposesOwnedFiles()
    {
        await SeedAsync(null, true);
        using var client = await ClientAsync();
        Assert.Null((await ReadInvoiceAsync(client)).ReceiptId);
        await AssertCachedAsync(null);
        using var response = await CreateAsync(client, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var database = fixture.InvoiceDatabase();
        Assert.Equal(91, (await database.Invoices.AsNoTracking().SingleAsync()).ReceiptId);
        Assert.Equal(91, (await ReadInvoiceAsync(client)).ReceiptId);
        await AssertCachedAsync(91);
        using var files = await client.GetAsync("/receipts/91/files");
        Assert.Equal(HttpStatusCode.OK, files.StatusCode);
        Assert.Equal(91, Assert.Single((await files.Content.ReadFromJsonAsync<ReceiptFile[]>())!).ReceiptId);
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Fact]
    public async Task RemoveReceipt_CommittedUnlinkReplacesPrimedInvoiceRead()
    {
        await SeedAsync(91, false);
        await using var scope = await fixture.ReceiptScopeAsync();
        AssertConfiguredRetryStrategy(scope.ServiceProvider.GetRequiredService<ReceiptDbContext>());
        using var client = await ClientAsync();
        Assert.Equal(91, (await ReadInvoiceAsync(client)).ReceiptId);
        await AssertCachedAsync(91);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/invoices/42/receipt");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var database = fixture.InvoiceDatabase();
        Assert.Null((await database.Invoices.AsNoTracking().SingleAsync()).ReceiptId);
        Assert.Null((await ReadInvoiceAsync(client)).ReceiptId);
        await AssertCachedAsync(null);
        await using var receipts = fixture.ReceiptDatabase();
        Assert.Empty(await receipts.Receipts.ToArrayAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Fact]
    public async Task DeleteReceiptFailure_RollsBackOwnedRowsAndPreservesPrimedInvoiceCache()
    {
        await SeedAsync(91, true);
        await using (var seeded = fixture.ReceiptDatabase())
        {
            seeded.Items.Add(new ReceiptOrderItem { Id = 912, ReceiptId = 91, Description = "Synthetic rollback item", Quantity = 1, UnitPrice = 100m });
            await seeded.SaveChangesAsync();
        }
        using var client = await ClientAsync();
        Assert.Equal(91, (await ReadInvoiceAsync(client)).ReceiptId);
        var before = await fixture.ReceiptSnapshotAsync();
        await using var scope = await fixture.ReceiptScopeAsync();
        var configured = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        AssertConfiguredRetryStrategy(configured);
        var options = (DbContextOptions<ReceiptDbContext>)configured.GetService<IDbContextOptions>();
        var failure = new FailAfterReceiptFilesDelete();
        await using var receipts = new ReceiptDbContext(new DbContextOptionsBuilder<ReceiptDbContext>(options)
            .AddInterceptors(failure).Options);
        AssertConfiguredRetryStrategy(receipts);
        await using var invoices = fixture.InvoiceDatabase();
        var store = new ReceiptWorkflowStore(invoices, receipts, TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<IAccountingCache>());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.DeleteReceiptAsync(91, cancelled.Token));
        Assert.Equal(0, failure.FileRowsDeleted);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteReceiptAsync(91, CancellationToken.None));
        Assert.Equal("Synthetic failure after owned file deletion", exception.Message);
        Assert.Equal(1, failure.FileRowsDeleted);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        await AssertCachedAsync(91);
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Fact]
    public async Task CompletedCreateReplay_KeepsCommittedLinkWithoutDuplicateFinancialOrJournalEffects()
    {
        await SeedAsync(null, true);
        using var client = await ClientAsync();
        Assert.Null((await ReadInvoiceAsync(client)).ReceiptId);
        var operation = Guid.NewGuid();
        using var first = await CreateAsync(client, operation);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(91, (await ReadInvoiceAsync(client)).ReceiptId);
        var before = await fixture.ReceiptSnapshotAsync();
        using var replay = await CreateAsync(client, operation);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        await AssertCachedAsync(91);
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Fact]
    public async Task ConflictingLink_LeavesPrimedInvoiceCacheAndEveryOwnedTableUnchanged()
    {
        await SeedAsync(92, false);
        using var client = await ClientAsync();
        Assert.Equal(92, (await ReadInvoiceAsync(client)).ReceiptId);
        var before = await fixture.ReceiptSnapshotAsync();
        await using var scope = await fixture.ReceiptScopeAsync();
        var store = scope.ServiceProvider.GetRequiredService<IReceiptWorkflowStore>();
        await Assert.ThrowsAsync<ReceiptWorkflowDependencyException>(() => store.LinkInvoiceAsync(42, 91, CancellationToken.None));
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        await AssertCachedAsync(92);
    }

    [Fact]
    public async Task CancellationBeforeLink_LeavesPrimedInvoiceCacheAndEveryOwnedTableUnchanged()
    {
        await SeedAsync(null, false);
        using var client = await ClientAsync();
        Assert.Null((await ReadInvoiceAsync(client)).ReceiptId);
        var before = await fixture.ReceiptSnapshotAsync();
        await using var scope = await fixture.ReceiptScopeAsync();
        var store = scope.ServiceProvider.GetRequiredService<IReceiptWorkflowStore>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LinkInvoiceAsync(42, 91, cancelled.Token));
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        await AssertCachedAsync(null);
    }

    [Fact]
    public async Task UnlinkDifferentReceipt_DoesNotEvictOrMutateRightfulLink()
    {
        await SeedAsync(92, false);
        using var client = await ClientAsync();
        Assert.Equal(92, (await ReadInvoiceAsync(client)).ReceiptId);
        var before = await fixture.ReceiptSnapshotAsync();
        await using var scope = await fixture.ReceiptScopeAsync();
        await scope.ServiceProvider.GetRequiredService<IReceiptWorkflowStore>().UnlinkInvoiceAsync(42, 91, CancellationToken.None);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        await AssertCachedAsync(92);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationAfterCommittedSql_CannotCancelOwnedCacheInvalidation(bool link)
    {
        await SeedAsync(link ? null : 91, false);
        using var client = await ClientAsync();
        Assert.Equal(link ? null : 91, (await ReadInvoiceAsync(client)).ReceiptId);
        await AssertCachedAsync(link ? null : 91);
        await using var scope = await fixture.ReceiptScopeAsync();
        await using var original = fixture.InvoiceDatabase();
        var options = (DbContextOptions<InvoiceDbContext>)original.GetService<IDbContextOptions>();
        using var cancelled = new CancellationTokenSource();
        await using var invoice = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>(options)
            .AddInterceptors(new CancelAfterInvoiceUpdate(cancelled)).Options);
        await using var receipt = fixture.ReceiptDatabase();
        var store = new ReceiptWorkflowStore(invoice, receipt, TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<IAccountingCache>());
        if (link) await store.LinkInvoiceAsync(42, 91, cancelled.Token);
        else await store.UnlinkInvoiceAsync(42, 91, cancelled.Token);
        Assert.True(cancelled.IsCancellationRequested);
        await using var persisted = fixture.InvoiceDatabase();
        Assert.Equal(link ? 91 : null, (await persisted.Invoices.AsNoTracking().SingleAsync()).ReceiptId);
        Assert.Equal(link ? 91 : null, (await ReadInvoiceAsync(client)).ReceiptId);
        await AssertCachedAsync(link ? 91 : null);
    }

    private Task<HttpClient> ClientAsync() => fixture.ReceiptClientAsync([
        AccountingPermissions.Read, AccountingPermissions.Create, AccountingPermissions.Delete, AccountingPermissions.FilesRead]);

    private static void AssertConfiguredRetryStrategy(ReceiptDbContext context)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        Assert.True(strategy.RetriesOnFailure);
        Assert.IsType<Npgsql.EntityFrameworkCore.PostgreSQL.NpgsqlRetryingExecutionStrategy>(strategy);
    }

    private static async Task<Invoice> ReadInvoiceAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/invoices/42");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Invoice>())!;
    }

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid operation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/invoices/42/receipt")
        {
            Content = JsonContent.Create(new { Comment = "Synthetic cache reconciliation", SendEmail = false }),
        };
        request.Headers.Add("Idempotency-Key", operation.ToString("D"));
        request.Headers.Add("X-Legacy-Employee-Id", "7");
        return await client.SendAsync(request);
    }

    private async Task AssertCachedAsync(int? receiptId)
    {
        await using var scope = await fixture.ReceiptScopeAsync();
        Assert.True(scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().IsConnected);
        var cache = scope.ServiceProvider.GetRequiredService<IAccountingCache>();
        Assert.IsType<DistributedAccountingCache>(cache);
        var actual = await cache.GetAsync<Invoice>("invoice:42", CancellationToken.None);
        Assert.NotNull(actual);
        Assert.Equal(42, actual.Id);
        Assert.Equal(receiptId, actual.ReceiptId);
    }

    private async Task SeedAsync(int? linked, bool files)
    {
        await fixture.ResetAsync();
        await using var invoice = fixture.InvoiceDatabase();
        await invoice.Files.ExecuteDeleteAsync();
        await invoice.Items.ExecuteDeleteAsync();
        await invoice.Invoices.ExecuteDeleteAsync();
        invoice.Invoices.Add(new Invoice { Id = 42, Number = "SYNTHETIC-42", ReceiptId = linked, Currency = "THB", Subtotal = 100m, Vat = 7m, Total = 107m });
        await invoice.SaveChangesAsync();
        await using var receipt = fixture.ReceiptDatabase();
        await receipt.Files.ExecuteDeleteAsync();
        await receipt.Items.ExecuteDeleteAsync();
        await receipt.Receipts.ExecuteDeleteAsync();
        receipt.Receipts.Add(new Receipt { Id = 91, InvoiceNumber = "SYNTHETIC-42", Currency = "THB", PaymentDate = DateTime.UtcNow, Subtotal = 100m, Vat = 7m, Total = 107m });
        if (files) receipt.Files.Add(new ReceiptFile { Id = 911, ReceiptId = 91, Bucket = "maliev.com", ObjectName = "receipts/91/receipt_91.pdf" });
        await receipt.SaveChangesAsync();
        // Clear only this fixture's known master-read key between cases; no journal purge.
        await using var scope = await fixture.ReceiptScopeAsync();
        await scope.ServiceProvider.GetRequiredService<IAccountingCache>().RemoveAsync("invoice:42", CancellationToken.None);
    }

    private sealed class CancelAfterInvoiceUpdate(CancellationTokenSource caller) : DbCommandInterceptor
    {
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE \"Invoice\"", StringComparison.Ordinal)) caller.Cancel();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailAfterReceiptFilesDelete : DbCommandInterceptor
    {
        public int FileRowsDeleted { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.Ordinal)
                && command.CommandText.Contains("\"OrderItem\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Synthetic failure after owned file deletion");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.Ordinal)
                && command.CommandText.Contains("\"ReceiptFile\"", StringComparison.Ordinal)) FileRowsDeleted += result;
            return ValueTask.FromResult(result);
        }
    }
}
