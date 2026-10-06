using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

// Fixed source135e copied-field inventory; tests do not derive their field oracle from target reflection.
public sealed class InvoiceReceiptMasterScalarSourceHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    private static readonly string[] CommonStrings =
    [
        "BillingAddressBuilding", "BillingAddressCity", "BillingAddressCompany", "BillingAddressCountry",
        "BillingAddressLine1", "BillingAddressLine2", "BillingAddressPostalCode", "BillingAddressRecipient",
        "BillingAddressState", "Comment", "Currency", "CommercialRegistration", "TaxIdentification",
    ];
    private static readonly string[] InvoiceStrings =
    [
        "Number", "PurchaseOrderNumber", "Fob", "Requisitioner", "SalesPerson", "ShippedVia", "Terms", "InternalComment",
        "ShippingAddressBuilding", "ShippingAddressCity", "ShippingAddressCompany", "ShippingAddressCountry",
        "ShippingAddressLine1", "ShippingAddressLine2", "ShippingAddressPostalCode", "ShippingAddressRecipient",
        "ShippingAddressRecipientTelephone", "ShippingAddressState",
    ];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_AllSourceScalarsPersistWithServerIdentityAndFinancialAdaptation(bool invoice)
    {
        await fixture.ResetAsync();
        using var client = await ClientAsync();
        var body = Body(invoice, false);
        var before = await fixture.ReceiptSnapshotAsync();
        var (id, wire) = await CreateAsync(client, invoice, body);
        Assert.True(id > 0);
        Assert.NotEqual(999999, id);
        AssertScalars(body, wire, invoice);
        AssertScalars(body, await PersistedAsync(invoice, id), invoice);
        Assert.NotEqual(body["CreatedDate"]!.GetValue<string>(), wire["CreatedDate"]!.GetValue<string>());
        Assert.NotEqual(body["ModifiedDate"]!.GetValue<string>(), wire["ModifiedDate"]!.GetValue<string>());
        await AssertOtherBoundariesAsync(before, invoice);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_AllSourceScalarsReplacePrimedReadPreservingCreationAndAttribution(bool invoice)
    {
        await fixture.ResetAsync();
        using var client = await ClientAsync();
        var (id, original) = await CreateAsync(client, invoice, Body(invoice, false));
        var before = await fixture.ReceiptSnapshotAsync();
        var body = Body(invoice, true);
        using var updated = await client.PutAsJsonAsync($"{Route(invoice)}/{id}", body);
        await fixture.AssertStatusAsync(updated, HttpStatusCode.NoContent);
        var wire = (await client.GetFromJsonAsync<JsonObject>($"{Route(invoice)}/{id}"))!;
        if (invoice)
        {
            body["SourceRequestId"] = original["SourceRequestId"]!.DeepClone();
            body["SourceJourneyId"] = original["SourceJourneyId"]!.DeepClone();
        }
        Assert.Equal(id, wire["Id"]!.GetValue<int>());
        Assert.Equal(original["CreatedDate"]!.GetValue<string>(), wire["CreatedDate"]!.GetValue<string>());
        AssertScalars(body, wire, invoice);
        AssertScalars(body, await PersistedAsync(invoice, id), invoice);
        await AssertOtherBoundariesAsync(before, invoice);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateReplay_SameKeyReturnsOriginalScalarsWithoutSecondFinancialWrite(bool invoice)
    {
        await fixture.ResetAsync();
        using var client = await ClientAsync();
        var body = Body(invoice, false);
        var (id, original) = await CreateAsync(client, invoice, body);
        var before = await fixture.ReceiptSnapshotAsync();
        using var replay = await client.PostAsJsonAsync(Route(invoice), Body(invoice, true));
        await fixture.AssertStatusAsync(replay, HttpStatusCode.Created);
        var wire = (await replay.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(id, wire["Id"]!.GetValue<int>());
        AssertScalars(body, wire, invoice);
        Assert.Equal(original["CreatedDate"]!.GetValue<string>(), wire["CreatedDate"]!.GetValue<string>());
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        AssertScalars(body, await PersistedAsync(invoice, id), invoice);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaleUpdate_RejectsWithoutFinancialMutationOrReplacingPrimedRead(bool invoice)
    {
        await fixture.ResetAsync();
        using var client = await ClientAsync();
        var (id, original) = await CreateAsync(client, invoice, Body(invoice, false));
        var before = await fixture.ReceiptSnapshotAsync();
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Unmodified-Since", "1999-01-01T00:00:00Z");
        using var updated = await client.PutAsJsonAsync($"{Route(invoice)}/{id}", Body(invoice, true));
        await fixture.AssertStatusAsync(updated, HttpStatusCode.Conflict);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        var wire = (await client.GetFromJsonAsync<JsonObject>($"{Route(invoice)}/{id}"))!;
        Assert.True(JsonNode.DeepEquals(original, wire));
        AssertScalars(Body(invoice, false), await PersistedAsync(invoice, id), invoice);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_ChildlessMasterRemovesPersistedRowAndPrimedReadThenReturns404(bool invoice)
    {
        await fixture.ResetAsync();
        using var client = await ClientAsync();
        var (id, _) = await CreateAsync(client, invoice, Body(invoice, false));
        var before = await fixture.ReceiptSnapshotAsync();
        using var deleted = await client.DeleteAsync($"{Route(invoice)}/{id}");
        await fixture.AssertStatusAsync(deleted, HttpStatusCode.NoContent);
        using var read = await client.GetAsync($"{Route(invoice)}/{id}");
        await fixture.AssertStatusAsync(read, HttpStatusCode.NotFound);
        using var repeated = await client.DeleteAsync($"{Route(invoice)}/{id}");
        await fixture.AssertStatusAsync(repeated, HttpStatusCode.NotFound);
        if (invoice)
        {
            await using var database = fixture.InvoiceDatabase();
            Assert.False(await database.Invoices.AnyAsync(value => value.Id == id));
        }
        else
        {
            await using var database = fixture.ReceiptDatabase();
            Assert.False(await database.Receipts.AnyAsync(value => value.Id == id));
        }
        await AssertOtherBoundariesAsync(before, invoice);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task DeniedWrite_CannotChangeCompleteFinancialSnapshot(bool invoice, bool update, bool authenticated)
    {
        await fixture.ResetAsync();
        using var client = await fixture.ReceiptClientAsync(authenticated ? [AccountingPermissions.Create, AccountingPermissions.Update] : null, allowLive: false);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var before = await fixture.ReceiptSnapshotAsync();
        using var response = update
            ? await client.PutAsJsonAsync($"{Route(invoice)}/999999", Body(invoice, true))
            : await client.PostAsJsonAsync(Route(invoice), Body(invoice, false));
        await fixture.AssertStatusAsync(response, authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
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
    public async Task MissingMaster_Returns404WithoutFinancialMutation(bool invoice, string verb)
    {
        await fixture.ResetAsync();
        using var client = await ClientAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        using var request = new HttpRequestMessage(new HttpMethod(verb), $"{Route(invoice)}/999999");
        if (verb == "PUT") request.Content = JsonContent.Create(Body(invoice, true));
        using var response = await client.SendAsync(request);
        await fixture.AssertStatusAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = await fixture.ReceiptClientAsync([AccountingPermissions.Create, AccountingPermissions.Read, AccountingPermissions.Update, AccountingPermissions.Delete]);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return client;
    }

    private static string Route(bool invoice) => invoice ? "/invoices" : "/receipts";

    private async Task<(int Id, JsonObject Wire)> CreateAsync(HttpClient client, bool invoice, JsonObject body)
    {
        using var response = await client.PostAsJsonAsync(Route(invoice), body);
        await fixture.AssertStatusAsync(response, HttpStatusCode.Created);
        Assert.NotNull(response.Headers.Location);
        var wire = (await client.GetFromJsonAsync<JsonObject>(response.Headers.Location))!;
        return (wire["Id"]!.GetValue<int>(), wire);
    }

    private async Task<JsonObject> PersistedAsync(bool invoice, int id)
    {
        if (invoice)
        {
            await using var database = fixture.InvoiceDatabase();
            return JsonSerializer.SerializeToNode(await database.Invoices.AsNoTracking().SingleAsync(value => value.Id == id))!.AsObject();
        }
        await using var receipts = fixture.ReceiptDatabase();
        return JsonSerializer.SerializeToNode(await receipts.Receipts.AsNoTracking().SingleAsync(value => value.Id == id))!.AsObject();
    }

    private async Task AssertOtherBoundariesAsync(AccountingBoundaryHttpFixture.ReceiptBoundaryState before, bool invoice)
    {
        var after = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(before.Payment, after.Payment);
        Assert.Equal(before.Journal, after.Journal);
        Assert.Equal(invoice ? before.Receipt : before.Invoice, invoice ? after.Receipt : after.Invoice);
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    private static void AssertScalars(JsonObject expected, JsonObject actual, bool invoice)
    {
        foreach (var (field, value) in expected)
        {
            if (field is "Id" or "CreatedDate" or "ModifiedDate") continue;
            if (!invoice && field == "AmountPaid")
                Assert.Equal(expected["Total"]!.GetValue<decimal>() - expected["WithholdingTax"]!.GetValue<decimal>(), actual[field]!.GetValue<decimal>());
            else if (field == "PaymentDate")
                Assert.Equal(DateTime.Parse(value!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DateTime.Parse(actual[field]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
            else
                Assert.True(JsonNode.DeepEquals(value, actual[field]), $"Source scalar did not persist: {field}");
        }
    }

    private static JsonObject Body(bool invoice, bool update)
    {
        var body = new JsonObject();
        var fields = CommonStrings.Concat(invoice ? InvoiceStrings : ["InvoiceNumber"]).ToArray();
        for (var index = 0; index < fields.Length; index++) body[fields[index]] = $"{(update ? 'B' : 'A')}-{index:D2}-ไทย";
        body["Currency"] = update ? "USD" : "THB";
        body["Id"] = 999999;
        body["CustomerId"] = update ? 314 : 42;
        body["Subtotal"] = update ? 98.76m : 100.34m;
        body["Total"] = update ? 105.67m : 107.89m;
        body["Vat"] = update ? 6.91m : 7.55m;
        body["WithholdingTax"] = update ? 2.15m : 3.21m;
        body["PaymentDate"] = update ? "2026-11-03T12:34:30Z" : "2026-10-03T00:00:00Z";
        body["CreatedDate"] = "1999-01-01T00:00:00";
        body["ModifiedDate"] = "1999-01-01T00:00:00";
        if (invoice)
        {
            body["Outstanding"] = update ? 103.52m : 90.68m;
            body["IsPaid"] = !update;
            body["ReceiptId"] = update ? 818 : 717;
            body["SourceRequestId"] = update ? 999 : 401;
            body["SourceJourneyId"] = update ? "8470c0b4-dc8f-4a5a-af0a-65d8c4460fdc" : "0b990cab-d295-4359-bae3-ad7876d3ed38";
        }
        else body["AmountPaid"] = 999m;
        return body;
    }
}
