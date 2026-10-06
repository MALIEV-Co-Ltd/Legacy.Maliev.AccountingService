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
using InvoiceItem = Legacy.Maliev.AccountingService.Domain.Invoice.InvoiceOrderItem;
using ReceiptItem = Legacy.Maliev.AccountingService.Domain.Receipt.ReceiptOrderItem;

namespace Legacy.Maliev.AccountingService.Tests;

// Fixed source controller fields and database arithmetic; no reflection-derived field oracle.
public sealed class InvoiceReceiptOrderItemSourceHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
    public async Task Create_SourceScalarsAndNullableComputedSubtotalPersist(bool invoice, int variant)
    {
        await SeedAsync();
        using var client = await ClientAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        var redisBefore = await fixture.ReceiptRedisSnapshotAsync();
        var parents = await ParentsAsync(invoice);
        (int? quantity, decimal? price, decimal? subtotal) = variant switch
        {
            0 => ((int?)2, (decimal?)12.34m, (decimal?)24.68m),
            1 => (null, (decimal?)12.34m, null),
            2 => ((int?)2, null, null),
            3 => ((int?)0, (decimal?)12.34m, (decimal?)0m),
            4 => ((int?)-2, (decimal?)12.34m, (decimal?)-24.68m),
            _ => ((int?)null, (decimal?)null, (decimal?)null),
        };
        var body = Body(invoice, 91, quantity, price);
        var (id, wire) = await CreateAsync(client, invoice, body);
        Assert.True(id > 0);
        Assert.NotEqual(999999, id);
        Assert.NotEqual(body["CreatedDate"]!.GetValue<string>(), wire["CreatedDate"]!.GetValue<string>());
        Assert.NotEqual(body["ModifiedDate"]!.GetValue<string>(), wire["ModifiedDate"]!.GetValue<string>());
        AssertScalars(body, wire, invoice, subtotal);
        AssertPersistedScalars(body, await PersistedAsync(invoice, id), invoice, subtotal);
        Assert.Equal(parents, await ParentsAsync(invoice));
        await AssertOtherBoundariesAsync(before, invoice, redisBefore,
            SourceMutationProof.RedisKeys(client, $"{(invoice ? "invoiceorderitem" : "receiptorderitem")}:{id}", invoice ? "invoice-items" : "receipt-items"), created: true);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Update_ReparentsOrClearsNullableFieldsAndInvalidatesPrimedRead(bool invoice, bool detach)
    {
        await SeedAsync();
        using var client = await ClientAsync();
        var (id, original) = await CreateAsync(client, invoice, Body(invoice, 91, 2, 12.34m));
        await using (var scope = await fixture.ReceiptScopeAsync())
        {
            var cache = scope.ServiceProvider.GetRequiredService<IAccountingCache>();
            if (invoice) Assert.NotNull(await cache.GetAsync<InvoiceItem>($"invoiceorderitem:{id}", CancellationToken.None));
            else Assert.NotNull(await cache.GetAsync<ReceiptItem>($"receiptorderitem:{id}", CancellationToken.None));
        }
        var before = await fixture.ReceiptSnapshotAsync();
        var redisBefore = await fixture.ReceiptRedisSnapshotAsync();
        var parents = await ParentsAsync(invoice);
        var body = Body(invoice, detach ? null : 92, detach ? 3 : null, detach ? null : 7.89m);
        body["Description"] = " updated ใบงาน ";
        using var updated = await client.PutAsJsonAsync($"{Route(invoice)}/{id}", body);
        await fixture.AssertStatusAsync(updated, HttpStatusCode.NoContent);
        var wire = (await client.GetFromJsonAsync<JsonObject>($"{Route(invoice)}/{id}"))!;
        Assert.Equal(id, wire["Id"]!.GetValue<int>());
        Assert.Equal(original["CreatedDate"]!.GetValue<string>(), wire["CreatedDate"]!.GetValue<string>());
        AssertScalars(body, wire, invoice, null);
        AssertPersistedScalars(body, await PersistedAsync(invoice, id), invoice, null);
        using var oldList = await client.GetAsync($"{MasterRoute(invoice)}/91/orderitems");
        await fixture.AssertStatusAsync(oldList, HttpStatusCode.NotFound);
        if (!detach)
        {
            var rows = (await client.GetFromJsonAsync<JsonArray>($"{MasterRoute(invoice)}/92/orderitems"))!;
            Assert.Equal(id, Assert.Single(rows)!["Id"]!.GetValue<int>());
        }
        Assert.Equal(parents, await ParentsAsync(invoice));
        await SourceMutationProof.PayloadAsync(fixture, $"{(invoice ? "invoiceorderitem" : "receiptorderitem")}:{id}", wire,
            invoice ? "invoiceorderitem" : "receiptorderitem");
        await AssertOtherBoundariesAsync(before, invoice, redisBefore,
            SourceMutationProof.RedisKeys(client, $"{(invoice ? "invoiceorderitem" : "receiptorderitem")}:{id}"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SequentialReplay_SameKeyKeepsOriginalRowAndComputedAmount(bool invoice)
    {
        await SeedAsync();
        using var client = await ClientAsync();
        var body = Body(invoice, 91, 2, 12.34m);
        using var first = await client.PostAsJsonAsync(Route(invoice), body);
        await fixture.AssertStatusAsync(first, HttpStatusCode.Created);
        var original = (await first.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = original["Id"]!.GetValue<int>();
        Assert.NotNull(first.Headers.Location);
        var getBefore = (await client.GetFromJsonAsync<JsonObject>(first.Headers.Location))!;
        var persistedBefore = await PersistedAsync(invoice, id);
        var before = await fixture.ReceiptSnapshotAsync();
        using var replay = await client.PostAsJsonAsync(Route(invoice), Body(invoice, 92, -3, 7.89m));
        await fixture.AssertStatusAsync(replay, HttpStatusCode.Created);
        var wire = (await replay.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(id, wire["Id"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(original, wire));
        AssertScalars(body, wire, invoice, 24.68m);
        AssertPersistedScalars(body, await PersistedAsync(invoice, id), invoice, 24.68m);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.True(JsonNode.DeepEquals(getBefore, await client.GetFromJsonAsync<JsonObject>(first.Headers.Location)));
        Assert.True(JsonNode.DeepEquals(persistedBefore, await PersistedAsync(invoice, id)));
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_RemovesRowAndPrimedReadWithoutChangingParent(bool invoice)
    {
        await SeedAsync();
        using var client = await ClientAsync();
        var (id, _) = await CreateAsync(client, invoice, Body(invoice, 91, 2, 12.34m));
        var parents = await ParentsAsync(invoice);
        var before = await fixture.ReceiptSnapshotAsync();
        var redisBefore = await fixture.ReceiptRedisSnapshotAsync();
        using var deleted = await client.DeleteAsync($"{Route(invoice)}/{id}");
        await fixture.AssertStatusAsync(deleted, HttpStatusCode.NoContent);
        using var read = await client.GetAsync($"{Route(invoice)}/{id}");
        await fixture.AssertStatusAsync(read, HttpStatusCode.NotFound);
        using var repeated = await client.DeleteAsync($"{Route(invoice)}/{id}");
        await fixture.AssertStatusAsync(repeated, HttpStatusCode.NotFound);
        if (invoice)
        {
            await using var database = fixture.InvoiceDatabase();
            Assert.False(await database.Items.AnyAsync(row => row.Id == id));
        }
        else
        {
            await using var database = fixture.ReceiptDatabase();
            Assert.False(await database.Items.AnyAsync(row => row.Id == id));
        }
        Assert.Equal(parents, await ParentsAsync(invoice));
        await AssertOtherBoundariesAsync(before, invoice, redisBefore,
            SourceMutationProof.RedisKeys(client, $"{(invoice ? "invoiceorderitem" : "receiptorderitem")}:{id}", rowPresent: false));
    }

    [Theory]
    [InlineData(true, "POST")]
    [InlineData(true, "PUT")]
    [InlineData(true, "DELETE")]
    [InlineData(false, "POST")]
    [InlineData(false, "PUT")]
    [InlineData(false, "DELETE")]
    public async Task LiveDenied_CannotChangeExistingItemOrFinancialState(bool invoice, string method)
    {
        await SeedAsync();
        using var allowed = await ClientAsync();
        var (id, _) = await CreateAsync(allowed, invoice, Body(invoice, 91, 2, 12.34m));
        using var client = await fixture.ReceiptClientAsync(Permissions, allowLive: false);
        var before = await fixture.ReceiptSnapshotAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "POST" ? Route(invoice) : $"{Route(invoice)}/{id}");
        if (method != "DELETE") request.Content = JsonContent.Create(Body(invoice, 92, -3, 7.89m));
        using var response = await client.SendAsync(request);
        await fixture.AssertStatusAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Anonymous_CreateCannotPersistChild(bool invoice)
    {
        await SeedAsync();
        using var client = await fixture.ReceiptClientAsync(null);
        var before = await fixture.ReceiptSnapshotAsync();
        using var response = await client.PostAsJsonAsync(Route(invoice), Body(invoice, 91, 2, 12.34m));
        await fixture.AssertStatusAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(true, "GET")]
    [InlineData(true, "PUT")]
    [InlineData(true, "DELETE")]
    [InlineData(false, "GET")]
    [InlineData(false, "PUT")]
    [InlineData(false, "DELETE")]
    public async Task MissingRow_Returns404WithoutFinancialMutation(bool invoice, string method)
    {
        await SeedAsync();
        using var client = await ClientAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{Route(invoice)}/999999");
        if (method == "PUT") request.Content = JsonContent.Create(Body(invoice, 91, 2, 12.34m));
        using var response = await client.SendAsync(request);
        await fixture.AssertStatusAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    private static readonly string[] Permissions = [AccountingPermissions.Create, AccountingPermissions.Read, AccountingPermissions.Update, AccountingPermissions.Delete];
    private async Task<HttpClient> ClientAsync()
    {
        var client = await fixture.ReceiptClientAsync(Permissions);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return client;
    }

    private async Task SeedAsync()
    {
        await fixture.ResetAsync();
        await using var invoices = fixture.InvoiceDatabase();
        await invoices.Files.ExecuteDeleteAsync();
        await invoices.Items.ExecuteDeleteAsync();
        await invoices.Invoices.ExecuteDeleteAsync();
        invoices.Invoices.AddRange(new InvoiceRecord { Id = 91, Number = "SYNTHETIC-ITEM-91", Currency = "THB", Total = 107m },
            new InvoiceRecord { Id = 92, Number = "SYNTHETIC-ITEM-92", Currency = "THB", Total = 214m });
        await invoices.SaveChangesAsync();
        await using var receipts = fixture.ReceiptDatabase();
        await receipts.Files.ExecuteDeleteAsync();
        await receipts.Items.ExecuteDeleteAsync();
        await receipts.Receipts.ExecuteDeleteAsync();
        receipts.Receipts.AddRange(new ReceiptRecord { Id = 91, InvoiceNumber = "SYNTHETIC-ITEM-91", Currency = "THB", PaymentDate = DateTime.UtcNow, Total = 107m },
            new ReceiptRecord { Id = 92, InvoiceNumber = "SYNTHETIC-ITEM-92", Currency = "THB", PaymentDate = DateTime.UtcNow, Total = 214m });
        await receipts.SaveChangesAsync();
    }

    private static JsonObject Body(bool invoice, int? parent, int? quantity, decimal? price) => new()
    {
        ["Id"] = 999999,
        [invoice ? "InvoiceId" : "ReceiptId"] = parent,
        ["Description"] = " source ใบงาน ",
        ["Quantity"] = quantity,
        ["UnitPrice"] = price,
        ["Subtotal"] = 999.89m,
        ["CreatedDate"] = "1999-01-01T00:00:00",
        ["ModifiedDate"] = "1999-01-02T00:00:00",
    };

    private async Task<(int Id, JsonObject Wire)> CreateAsync(HttpClient client, bool invoice, JsonObject body)
    {
        using var response = await client.PostAsJsonAsync(Route(invoice), body);
        await fixture.AssertStatusAsync(response, HttpStatusCode.Created);
        Assert.NotNull(response.Headers.Location);
        var wire = (await client.GetFromJsonAsync<JsonObject>(response.Headers.Location))!;
        var post = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        await SourceMutationProof.CreationPayloadsAsync(fixture, client, invoice ? "invoiceorderitem" : "receiptorderitem",
            invoice ? "invoice-items" : "receipt-items", wire["Id"]!.GetValue<int>(), wire, post);
        return (wire["Id"]!.GetValue<int>(), wire);
    }

    private static void AssertScalars(JsonObject body, JsonObject wire, bool invoice, decimal? subtotal)
    {
        foreach (var field in new[] { invoice ? "InvoiceId" : "ReceiptId", "Description", "Quantity", "UnitPrice" })
        {
            if (body[field] is null)
            {
                Assert.False(wire.ContainsKey(field), field);
                continue;
            }
            Assert.True(wire.ContainsKey(field), field);
            Assert.True(JsonNode.DeepEquals(body[field], wire[field]), field);
        }
        if (subtotal is null) Assert.False(wire.ContainsKey("Subtotal"));
        else
        {
            Assert.True(wire.ContainsKey("Subtotal"));
            Assert.Equal(subtotal, wire["Subtotal"]!.GetValue<decimal>());
        }
    }

    private static void AssertPersistedScalars(JsonObject body, JsonObject wire, bool invoice, decimal? subtotal)
    {
        foreach (var field in new[] { invoice ? "InvoiceId" : "ReceiptId", "Description", "Quantity", "UnitPrice" })
        {
            Assert.True(wire.ContainsKey(field), field);
            Assert.True(JsonNode.DeepEquals(body[field], wire[field]), field);
        }
        Assert.True(wire.ContainsKey("Subtotal"));
        Assert.Equal(subtotal, wire["Subtotal"]?.GetValue<decimal>());
    }

    private async Task<JsonObject> PersistedAsync(bool invoice, int id)
    {
        if (invoice)
        {
            await using var database = fixture.InvoiceDatabase();
            return JsonSerializer.SerializeToNode(await database.Items.AsNoTracking().SingleAsync(row => row.Id == id))!.AsObject();
        }
        await using var receipts = fixture.ReceiptDatabase();
        return JsonSerializer.SerializeToNode(await receipts.Items.AsNoTracking().SingleAsync(row => row.Id == id))!.AsObject();
    }

    private async Task<string> ParentsAsync(bool invoice)
    {
        if (invoice)
        {
            await using var database = fixture.InvoiceDatabase();
            return JsonSerializer.Serialize(await database.Invoices.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync());
        }
        await using var receipts = fixture.ReceiptDatabase();
        return JsonSerializer.Serialize(await receipts.Receipts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync());
    }

    private async Task AssertOtherBoundariesAsync(AccountingBoundaryHttpFixture.ReceiptBoundaryState before, bool invoice,
        IReadOnlyDictionary<string, string> redisBefore, IReadOnlyDictionary<string, bool> expectedKeys, bool created = false)
    {
        var after = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(before.Payment, after.Payment);
        await fixture.AssertReceiptRedisChangesAsync(redisBefore, expectedKeys, expectedPreviouslyPresent: !created);
        if (invoice) Assert.Equal(before.Receipt, after.Receipt);
        else Assert.Equal(before.Invoice, after.Invoice);
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    private static string MasterRoute(bool invoice) => invoice ? "/invoices" : "/receipts";
    private static string Route(bool invoice) => $"{MasterRoute(invoice)}/orderitems";
}
