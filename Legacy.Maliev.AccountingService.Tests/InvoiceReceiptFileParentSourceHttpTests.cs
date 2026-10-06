using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InvoiceRecord = Legacy.Maliev.AccountingService.Domain.Invoice.Invoice;
using ReceiptRecord = Legacy.Maliev.AccountingService.Domain.Receipt.Receipt;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceReceiptFileParentSourceHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    private const int ParentId = 91;
    private const string Bucket = " bucket-demo ";
    private const string ObjectName = " folder/ใบเสร็จ sample.pdf ";

    [Theory]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 999999)]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 999999)]
    public async Task MissingParent_Source404BeforeMetadataPersistence(bool invoice, int id)
    {
        await SeedAsync(invoice);
        using var client = await ClientAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        using var response = await client.PostAsync(Path(invoice, id), null);
        await fixture.AssertStatusAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task DeletedParent_FreshCheckRejectsStaleCacheAndPriorMemo(bool invoice, bool primeParentRead)
    {
        await SeedAsync(invoice);
        using var client = await ClientAsync(includeParentRead: primeParentRead);
        if (primeParentRead)
        {
            using var parent = await client.GetAsync($"{Route(invoice)}/{ParentId}");
            await fixture.AssertStatusAsync(parent, HttpStatusCode.OK);
            await using var scope = await fixture.ReceiptScopeAsync();
            var cache = scope.ServiceProvider.GetRequiredService<IAccountingCache>();
            if (invoice) Assert.NotNull(await cache.GetAsync<InvoiceRecord>($"invoice:{ParentId}", CancellationToken.None));
            else Assert.NotNull(await cache.GetAsync<ReceiptRecord>($"receipt:{ParentId}", CancellationToken.None));
        }
        using var created = await client.PostAsync(Path(invoice, ParentId), null);
        await fixture.AssertStatusAsync(created, HttpStatusCode.Created);
        // Bypass API invalidation deliberately: this tests a persisted deletion with a stale read/memo.
        if (invoice)
        {
            await using var database = fixture.InvoiceDatabase();
            await database.Files.ExecuteDeleteAsync();
            await database.Invoices.Where(row => row.Id == ParentId).ExecuteDeleteAsync();
        }
        else
        {
            await using var database = fixture.ReceiptDatabase();
            await database.Files.ExecuteDeleteAsync();
            await database.Receipts.Where(row => row.Id == ParentId).ExecuteDeleteAsync();
        }
        var before = await fixture.ReceiptSnapshotAsync();
        using var repeated = await client.PostAsync(Path(invoice, ParentId), null);
        await fixture.AssertStatusAsync(repeated, HttpStatusCode.NotFound);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task ExistingParent_FileWriteGrantCreatesLiteralMetadataWithoutParentReadGrant(bool invoice, bool key)
    {
        await SeedAsync(invoice);
        using var client = await ClientAsync(key: key);
        var before = await fixture.ReceiptSnapshotAsync();
        var parentBefore = await ParentSnapshotAsync(invoice);
        using var created = await client.PostAsync(Path(invoice, ParentId), null);
        await fixture.AssertStatusAsync(created, HttpStatusCode.Created);
        Assert.NotNull(created.Headers.Location);
        var wire = (await client.GetFromJsonAsync<JsonObject>(created.Headers.Location))!;
        Assert.True(wire["Id"]!.GetValue<int>() > 0);
        Assert.Equal(ParentId, wire[invoice ? "InvoiceId" : "ReceiptId"]!.GetValue<int>());
        Assert.Equal(Bucket, wire["Bucket"]!.GetValue<string>());
        Assert.Equal(ObjectName, wire["ObjectName"]!.GetValue<string>());
        if (invoice)
        {
            await using var database = fixture.InvoiceDatabase();
            var row = Assert.Single(await database.Files.AsNoTracking().ToArrayAsync());
            Assert.Equal(Bucket, row.Bucket);
            Assert.Equal(ObjectName, row.ObjectName);
            Assert.Equal(ParentId, row.InvoiceId);
        }
        else
        {
            await using var database = fixture.ReceiptDatabase();
            var row = Assert.Single(await database.Files.AsNoTracking().ToArrayAsync());
            Assert.Equal(Bucket, row.Bucket);
            Assert.Equal(ObjectName, row.ObjectName);
            Assert.Equal(ParentId, row.ReceiptId);
        }
        var after = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(before.Payment, after.Payment);
        Assert.Equal(before.Journal, after.Journal);
        Assert.Equal(invoice ? before.Receipt : before.Invoice, invoice ? after.Receipt : after.Invoice);
        Assert.Equal(parentBefore, await ParentSnapshotAsync(invoice));
        Assert.DoesNotContain(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Read);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.FilesWrite);
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task MissingMetadata_Source400BeforeParentOrFinancialWrite(bool invoice, bool bucketMissing)
    {
        await SeedAsync(invoice);
        using var client = await ClientAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        using var response = await client.PostAsync(Path(invoice, 999999, bucketMissing ? "" : Bucket, bucketMissing ? ObjectName : ""), null);
        await fixture.AssertStatusAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task AnonymousOrLiveDenied_CannotProbeParentOrPersistMetadata(bool invoice, bool authenticated)
    {
        await SeedAsync(invoice);
        using var client = await fixture.ReceiptClientAsync(authenticated ? [AccountingPermissions.FilesWrite] : null, allowLive: false);
        var before = await fixture.ReceiptSnapshotAsync();
        using var response = await client.PostAsync(Path(invoice, ParentId), null);
        await fixture.AssertStatusAsync(response, authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingParent_SequentialReplayPreservesOneMetadataRow(bool invoice)
    {
        await SeedAsync(invoice);
        using var client = await ClientAsync();
        using var created = await client.PostAsync(Path(invoice, ParentId), null);
        await fixture.AssertStatusAsync(created, HttpStatusCode.Created);
        var original = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var before = await fixture.ReceiptSnapshotAsync();
        using var repeated = await client.PostAsync(Path(invoice, ParentId), null);
        await fixture.AssertStatusAsync(repeated, HttpStatusCode.Created);
        var replay = (await repeated.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(original["Id"]!.GetValue<int>(), replay["Id"]!.GetValue<int>());
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    private async Task<HttpClient> ClientAsync(bool includeParentRead = false, bool key = true)
    {
        var permissions = new List<string> { AccountingPermissions.FilesWrite, AccountingPermissions.FilesRead };
        if (includeParentRead) permissions.Add(AccountingPermissions.Read);
        var client = await fixture.ReceiptClientAsync(permissions.ToArray());
        if (key) client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return client;
    }

    private async Task SeedAsync(bool invoice)
    {
        await fixture.ResetAsync();
        await using var invoices = fixture.InvoiceDatabase();
        await invoices.Files.ExecuteDeleteAsync();
        await invoices.Items.ExecuteDeleteAsync();
        await invoices.Invoices.ExecuteDeleteAsync();
        await using var receipts = fixture.ReceiptDatabase();
        await receipts.Files.ExecuteDeleteAsync();
        await receipts.Items.ExecuteDeleteAsync();
        await receipts.Receipts.ExecuteDeleteAsync();
        if (invoice)
        {
            invoices.Invoices.Add(new InvoiceRecord { Id = ParentId, Number = "SYNTHETIC-FILE-PARENT", CustomerId = 42, Currency = "THB", Total = 107m });
            await invoices.SaveChangesAsync();
        }
        else
        {
            receipts.Receipts.Add(new ReceiptRecord { Id = ParentId, InvoiceNumber = "SYNTHETIC-FILE-PARENT", CustomerId = 42, Currency = "THB", PaymentDate = DateTime.UtcNow, Total = 107m });
            await receipts.SaveChangesAsync();
        }
        await using var scope = await fixture.ReceiptScopeAsync();
        var cache = scope.ServiceProvider.GetRequiredService<IAccountingCache>();
        await cache.RemoveAsync($"invoice:{ParentId}", CancellationToken.None);
        await cache.RemoveAsync($"receipt:{ParentId}", CancellationToken.None);
    }

    private static string Route(bool invoice) => invoice ? "/invoices" : "/receipts";
    private async Task<string> ParentSnapshotAsync(bool invoice)
    {
        if (invoice)
        {
            await using var database = fixture.InvoiceDatabase();
            return JsonSerializer.Serialize(await database.Invoices.AsNoTracking().SingleAsync(row => row.Id == ParentId));
        }
        await using var receipts = fixture.ReceiptDatabase();
        return JsonSerializer.Serialize(await receipts.Receipts.AsNoTracking().SingleAsync(row => row.Id == ParentId));
    }

    private static string Path(bool invoice, int id, string bucket = Bucket, string objectName = ObjectName) =>
        $"{Route(invoice)}/{id}/files?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}";
}
