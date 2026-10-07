using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Tests.Fixtures;

namespace Legacy.Maliev.AccountingService.Tests;

// Independent literal source inventory from immutable snapshot135e526d, not runtime EF rules.
// Native execution remains pending the coordinated model/migration/fixture bundle.
[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class MasterStringSourceHttpTests(AccountingBoundaryHttpFixture fixture)
{
    public static IEnumerable<object[]> LengthCases()
    {
        var rules = new (string Entity, string Field, int Maximum)[]
        {
            ("Invoice", "BillingAddressBuilding", 256),
            ("Invoice", "BillingAddressCity", 256),
            ("Invoice", "BillingAddressCompany", 256),
            ("Invoice", "BillingAddressCountry", 256),
            ("Invoice", "BillingAddressLine1", 256),
            ("Invoice", "BillingAddressLine2", 256),
            ("Invoice", "BillingAddressPostalCode", 256),
            ("Invoice", "BillingAddressRecipient", 256),
            ("Invoice", "BillingAddressState", 256),
            ("Invoice", "CommercialRegistration", 256),
            ("Invoice", "Currency", 50),
            ("Invoice", "Fob", 256),
            ("Invoice", "Number", 100),
            ("Invoice", "PurchaseOrderNumber", 256),
            ("Invoice", "Requisitioner", 256),
            ("Invoice", "SalesPerson", 256),
            ("Invoice", "ShippedVia", 256),
            ("Invoice", "ShippingAddressBuilding", 256),
            ("Invoice", "ShippingAddressCity", 256),
            ("Invoice", "ShippingAddressCompany", 256),
            ("Invoice", "ShippingAddressCountry", 256),
            ("Invoice", "ShippingAddressLine1", 256),
            ("Invoice", "ShippingAddressLine2", 256),
            ("Invoice", "ShippingAddressPostalCode", 256),
            ("Invoice", "ShippingAddressRecipient", 256),
            ("Invoice", "ShippingAddressRecipientTelephone", 256),
            ("Invoice", "ShippingAddressState", 256),
            ("Invoice", "TaxIdentification", 100),
            ("Invoice", "Terms", 256),
            ("Receipt", "BillingAddressBuilding", 256),
            ("Receipt", "BillingAddressCity", 256),
            ("Receipt", "BillingAddressCompany", 256),
            ("Receipt", "BillingAddressCountry", 256),
            ("Receipt", "BillingAddressPostalCode", 256),
            ("Receipt", "BillingAddressRecipient", 256),
            ("Receipt", "BillingAddressState", 256),
            ("Receipt", "CommercialRegistration", 256),
            ("Receipt", "Currency", 256),
            ("Receipt", "InvoiceNumber", 256),
            ("Receipt", "TaxIdentification", 256),
            ("Account", "AccountNumber", 50),
            ("Account", "Bank", 100),
            ("Account", "Branch", 100),
            ("Account", "Swift", 50),
            ("PaymentDirection", "Name", 50),
            ("PaymentMethod", "Name", 50),
            ("PaymentType", "Name", 50),
        };
        foreach (var rule in rules)
        {
            foreach (var update in Writes())
            {
                yield return [rule.Entity, rule.Field, rule.Maximum, update, false];
                yield return [rule.Entity, rule.Field, rule.Maximum, update, true];
            }
        }
    }

    public static IEnumerable<object[]> NullableCases()
    {
        var rules = new (string Entity, string Field)[]
        {
            ("Invoice", "BillingAddressBuilding"),
            ("Invoice", "BillingAddressCity"),
            ("Invoice", "BillingAddressCompany"),
            ("Invoice", "BillingAddressCountry"),
            ("Invoice", "BillingAddressLine1"),
            ("Invoice", "BillingAddressLine2"),
            ("Invoice", "BillingAddressPostalCode"),
            ("Invoice", "BillingAddressRecipient"),
            ("Invoice", "BillingAddressState"),
            ("Invoice", "CommercialRegistration"),
            ("Invoice", "Currency"),
            ("Invoice", "Fob"),
            ("Invoice", "PurchaseOrderNumber"),
            ("Invoice", "Requisitioner"),
            ("Invoice", "SalesPerson"),
            ("Invoice", "ShippedVia"),
            ("Invoice", "ShippingAddressBuilding"),
            ("Invoice", "ShippingAddressCity"),
            ("Invoice", "ShippingAddressCompany"),
            ("Invoice", "ShippingAddressCountry"),
            ("Invoice", "ShippingAddressLine1"),
            ("Invoice", "ShippingAddressLine2"),
            ("Invoice", "ShippingAddressPostalCode"),
            ("Invoice", "ShippingAddressRecipient"),
            ("Invoice", "ShippingAddressRecipientTelephone"),
            ("Invoice", "ShippingAddressState"),
            ("Invoice", "TaxIdentification"),
            ("Invoice", "Terms"),
            ("Receipt", "BillingAddressBuilding"),
            ("Receipt", "BillingAddressCity"),
            ("Receipt", "BillingAddressCompany"),
            ("Receipt", "BillingAddressCountry"),
            ("Receipt", "BillingAddressPostalCode"),
            ("Receipt", "BillingAddressRecipient"),
            ("Receipt", "BillingAddressState"),
            ("Receipt", "CommercialRegistration"),
            ("Receipt", "InvoiceNumber"),
            ("Receipt", "TaxIdentification"),
            ("Account", "AccountNumber"),
            ("Account", "Bank"),
            ("Account", "Branch"),
            ("Account", "Swift"),
        };
        foreach (var rule in rules)
        {
            foreach (var update in Writes())
            {
                yield return [rule.Entity, rule.Field, update];
            }
        }
    }

    public static IEnumerable<object[]> RequiredCases()
    {
        var rules = new (string Entity, string Field)[]
        {
            ("Invoice", "Number"),
            ("Receipt", "Currency"),
            ("Payment", "Description"),
            ("PaymentDirection", "Description"),
            ("PaymentDirection", "Name"),
            ("PaymentMethod", "Description"),
            ("PaymentMethod", "Name"),
            ("PaymentType", "Description"),
            ("PaymentType", "Name"),
        };
        foreach (var rule in rules)
        {
            foreach (var update in Writes())
            {
                yield return [rule.Entity, rule.Field, update];
            }
        }
    }

    public static IEnumerable<object[]> AuthorizationCases()
    {
        foreach (var entity in new[] { "Invoice", "Receipt", "Account", "Payment", "PaymentDirection", "PaymentMethod", "PaymentType" })
        {
            foreach (var update in Writes())
            {
                yield return [entity, update, false];
                yield return [entity, update, true];
            }
        }
    }

    [Theory]
    [MemberData(nameof(LengthCases))]
    public async Task ExactUtf16BoundaryWritesRoundTripAndOverflowRejectsWithoutMutation(string entity, string field, int maximum, bool update, bool supplementary)
    {
        await fixture.ResetAsync();
        using var client = Writer();
        int? id = update ? await SeedAsync(client, entity) : null;
        var original = id.HasValue ? await ReadAsync(client, entity, id.Value) : null;
        var value = supplementary
            ? string.Concat(Enumerable.Repeat("😀", maximum / 2)) + (maximum % 2 == 1 ? "ก" : string.Empty)
            : new string('ก', maximum);
        Assert.Equal(maximum, value.Length);
        var body = Body(entity);
        body[field] = value;
        using var accepted = await WriteAsync(client, entity, body, id);
        await fixture.AssertStatusAsync(accepted, update ? HttpStatusCode.NoContent : HttpStatusCode.Created);
        var written = await ReadAsync(client, entity, id ?? LocationId(accepted));
        Assert.Equal(value, written[field]!.GetValue<string>());
        if (update)
        {
            Assert.Equal(original!["CreatedDate"]!.GetValue<string>(), written["CreatedDate"]!.GetValue<string>());
        }
        var before = await fixture.ReceiptSnapshotAsync();
        var beforeCache = await fixture.SummaryCacheSnapshotAsync();
        body[field] = value + "ก";
        Assert.Equal(maximum + 1, body[field]!.GetValue<string>().Length);
        using var rejected = await WriteAsync(client, entity, body, id);
        await fixture.AssertStatusAsync(rejected, HttpStatusCode.BadRequest);
        await AssertRedactedProblemAsync(rejected);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(beforeCache, await fixture.SummaryCacheSnapshotAsync());
        Assert.Equal(value, (await ReadAsync(client, entity, id ?? LocationId(accepted)))[field]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(NullableCases))]
    public async Task SourceNullableFieldCanClearThroughPostOrReplacementPut(string entity, string field, bool update)
    {
        await fixture.ResetAsync();
        using var client = Writer();
        int? id = update ? await SeedAsync(client, entity, field) : null;
        var body = Body(entity);
        body[field] = null;
        using var accepted = await WriteAsync(client, entity, body, id);
        await fixture.AssertStatusAsync(accepted, update ? HttpStatusCode.NoContent : HttpStatusCode.Created);
        var written = await ReadAsync(client, entity, id ?? LocationId(accepted));
        Assert.False(written.ContainsKey(field));
        if (update)
        {
            // SeedAsync warms the persisted read cache. This read requires PUT cache eviction.
            Assert.Equal(id, written["Id"]!.GetValue<int>());
        }
        Assert.Empty(fixture.FailureMetadata);
    }

    [Theory]
    [MemberData(nameof(RequiredCases))]
    public async Task SourceRequiredMeansNullOnlyAndRetainsEmptyAndWhitespace(string entity, string field, bool update)
    {
        await fixture.ResetAsync();
        using var client = Writer();
        int? id = update ? await SeedAsync(client, entity) : null;
        var body = Body(entity);
        body[field] = null;
        var before = await fixture.ReceiptSnapshotAsync();
        var beforeCache = await fixture.SummaryCacheSnapshotAsync();
        using var rejected = await WriteAsync(client, entity, body, id);
        await fixture.AssertStatusAsync(rejected, HttpStatusCode.BadRequest);
        await AssertRedactedProblemAsync(rejected);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(beforeCache, await fixture.SummaryCacheSnapshotAsync());
        foreach (var value in new[] { string.Empty, "  " })
        {
            body[field] = value;
            using var accepted = await WriteAsync(client, entity, body, id);
            await fixture.AssertStatusAsync(accepted, update ? HttpStatusCode.NoContent : HttpStatusCode.Created);
            var written = await ReadAsync(client, entity, id ?? LocationId(accepted));
            Assert.Equal(value, written[field]!.GetValue<string>());
        }
    }

    [Theory]
    [MemberData(nameof(AuthorizationCases))]
    public async Task InvalidSourceStringsNeverBypassAuthenticationOrLivePermission(string entity, bool update, bool authenticated)
    {
        await fixture.ResetAsync();
        using var seed = Writer();
        int? id = update ? await SeedAsync(seed, entity) : null;
        using var client = fixture.Client(authenticated ? [AccountingPermissions.Create, AccountingPermissions.Update] : null, allowLive: false);
        var body = Body(entity);
        Invalidate(entity, body);
        var before = await fixture.ReceiptSnapshotAsync();
        using var rejected = await WriteAsync(client, entity, body, id);
        Assert.Equal(authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Empty(fixture.FailureMetadata);
    }

    [Theory]
    [InlineData("Invoice")]
    [InlineData("Receipt")]
    [InlineData("Account")]
    [InlineData("Payment")]
    [InlineData("PaymentDirection")]
    [InlineData("PaymentMethod")]
    [InlineData("PaymentType")]
    public async Task MissingPutRetains404BeforeInvalidStringValidation(string entity)
    {
        await fixture.ResetAsync();
        using var client = Writer();
        var body = Body(entity);
        Invalidate(entity, body);
        var before = await fixture.ReceiptSnapshotAsync();
        using var rejected = await WriteAsync(client, entity, body, 999999);
        await fixture.AssertStatusAsync(rejected, HttpStatusCode.NotFound);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Empty(fixture.FailureMetadata);
    }

    private static IEnumerable<bool> Writes() => [false, true];

    private HttpClient Writer() => fixture.Client([AccountingPermissions.Create, AccountingPermissions.Read, AccountingPermissions.Update]);

    private static string Route(string entity) => entity switch
    {
        "Invoice" => "/invoices",
        "Receipt" => "/receipts",
        "Account" => "/payments/accounts",
        "Payment" => "/payments",
        "PaymentDirection" => "/payments/directions",
        "PaymentMethod" => "/payments/methods",
        "PaymentType" => "/payments/types",
        _ => throw new ArgumentOutOfRangeException(nameof(entity)),
    };

    private static JsonObject Body(string entity) => entity switch
    {
        "Invoice" => new() { ["Number"] = "SOURCE-STRING", ["CustomerId"] = 42, ["Currency"] = "THB" },
        "Receipt" => new() { ["InvoiceNumber"] = "SOURCE-STRING", ["PaymentDate"] = "2026-10-07T00:00:00Z", ["Currency"] = "THB" },
        "Account" => new() { ["Bank"] = "Synthetic bank", ["AccountNumber"] = "Synthetic account" },
        "Payment" => new() { ["PaymentDirectionId"] = 100000, ["PaymentMethodId"] = 100000, ["PaymentTypeId"] = 100000, ["Amount"] = 1m, ["CurrencyId"] = 7, ["Description"] = "Synthetic source strings" },
        "PaymentDirection" or "PaymentMethod" or "PaymentType" => new() { ["Name"] = "Synthetic source", ["Description"] = "Synthetic source strings" },
        _ => throw new ArgumentOutOfRangeException(nameof(entity)),
    };

    private static void Invalidate(string entity, JsonObject body) => body[entity switch
    {
        "Invoice" => "Number",
        "Receipt" => "Currency",
        "Account" => "Bank",
        "Payment" => "Description",
        _ => "Name",
    }] = entity == "Account" ? JsonValue.Create(new string('ก', 101)) : null;

    private static async Task<HttpResponseMessage> WriteAsync(HttpClient client, string entity, JsonObject body, int? id)
    {
        using var request = new HttpRequestMessage(id.HasValue ? HttpMethod.Put : HttpMethod.Post, Route(entity) + (id.HasValue ? $"/{id}" : string.Empty))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return await client.SendAsync(request);
    }

    private static int LocationId(HttpResponseMessage response) => int.Parse(response.Headers.Location!.ToString().TrimEnd('/').Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonObject> ReadAsync(HttpClient client, string entity, int id) => (await client.GetFromJsonAsync<JsonObject>($"{Route(entity)}/{id}"))!;

    private async Task<int> SeedAsync(HttpClient client, string entity, string? populatedField = null)
    {
        var body = Body(entity);
        if (populatedField is not null)
        {
            body[populatedField] = "Synthetic populated value";
        }
        using var response = await WriteAsync(client, entity, body, null);
        await fixture.AssertStatusAsync(response, HttpStatusCode.Created);
        int id = LocationId(response);
        var wire = await ReadAsync(client, entity, id);
        if (populatedField is not null)
        {
            Assert.Equal("Synthetic populated value", wire[populatedField]!.GetValue<string>());
        }
        return id;
    }

    private static async Task AssertRedactedProblemAsync(HttpResponseMessage response)
    {
        var problem = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(["details", "error", "statusCode", "traceId"], problem.Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Equal(400, problem["statusCode"]!.GetValue<int>());
        Assert.Equal("The request is invalid.", problem["error"]!.GetValue<string>());
        Assert.Null(problem["details"]);
        var body = problem.ToJsonString();
        Assert.DoesNotContain("Synthetic", body, StringComparison.Ordinal);
        Assert.DoesNotContain("THB", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ก", body, StringComparison.Ordinal);
        Assert.DoesNotContain("😀", body, StringComparison.Ordinal);
        Assert.False(problem.ContainsKey("exception"));
    }
}
