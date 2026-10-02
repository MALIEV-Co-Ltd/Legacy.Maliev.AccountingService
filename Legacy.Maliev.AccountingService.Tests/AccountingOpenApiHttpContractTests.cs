using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using PaymentRecord = Legacy.Maliev.AccountingService.Domain.Payment.Payment;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class AccountingOpenApiHttpContractTests(AccountingBoundaryHttpFixture fixture)
{
    private static JsonObject Path(JsonObject document, string expected)
    {
        var paths = document["paths"]!.AsObject();
        return Assert.Single(paths, path => string.Equals(path.Key, expected, StringComparison.OrdinalIgnoreCase)).Value!.AsObject();
    }

    private static JsonObject Resolve(JsonObject document, JsonObject schema)
    {
        while (schema["$ref"] is JsonValue reference)
        {
            var path = reference.GetValue<string>();
            Assert.StartsWith("#/components/schemas/", path);
            schema = document["components"]!["schemas"]![path["#/components/schemas/".Length..]]!.AsObject();
        }

        return schema;
    }

    private async Task<JsonObject> DocumentAsync()
    {
        using var client = fixture.Client(developmentEnvironment: true);
        using var response = await client.GetAsync("/accounting/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(),
            new JsonNodeOptions { PropertyNameCaseInsensitive = false })!.AsObject();
    }

    [Fact]
    public async Task Production_DocumentationRouteIs404WhilePaymentStillRequiresAuthentication()
    {
        await fixture.ResetAsync();
        using var client = fixture.Client();
        using var docs = await client.GetAsync("/accounting/openapi/v1.json");
        using var payment = await client.GetAsync("/payments");
        Assert.Equal(HttpStatusCode.NotFound, docs.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, payment.StatusCode);
    }

    [Fact]
    public async Task Development_DocumentDescribesEveryPaymentOperationAndActualQueryAndHeaders()
    {
        await fixture.ResetAsync();
        var document = await DocumentAsync();
        Assert.StartsWith("3.", document["openapi"]!.GetValue<string>());
        Assert.Equal("Legacy MALIEV Accounting Service API", document["info"]!["title"]!.GetValue<string>());
        Assert.Contains("does not execute payments", document["info"]!["description"]!.GetValue<string>());
        var expected = new Dictionary<string, string[]>
        {
            ["/payments"] = ["get", "post"],
            ["/payments/{paymentId}"] = ["get", "put", "delete"],
            ["/payments/files"] = ["post"],
            ["/payments/files/{paymentFileId}"] = ["get", "put", "delete"],
            ["/payments/{paymentId}/files"] = ["get"],
            ["/payments/summaries/monthly"] = ["get"],
            ["/payments/summaries/weekly"] = ["get"],
            ["/payments/summaries/monthly/income/job"] = ["get"],
            ["/payments/summaries/yearly/income"] = ["get"],
            ["/payments/summaries/yearly/expense"] = ["get"],
            ["/payments/summaries/yearly"] = ["get"],
        };
        foreach (var (resource, parameter) in new[]
        {
            ("Accounts", "accountId"), ("Directions", "paymentDirectionId"),
            ("Methods", "paymentMethodId"), ("Types", "paymentTypeId"),
        })
        {
            expected[$"/payments/{resource}"] = ["get", "post"];
            expected[$"/payments/{resource}/{{{parameter}}}"] = ["get", "put", "delete"];
        }

        foreach (var (path, methods) in expected)
        {
            var actual = Path(document, path);
            var verbs = actual.Select(property => property.Key)
                .Where(key => key is "get" or "post" or "put" or "delete" or "patch").Order().ToArray();
            Assert.Equal(methods.Order().ToArray(), verbs);
            foreach (var method in methods)
                Assert.NotEmpty(actual[method]!["responses"]!.AsObject());
        }

        var actualPaymentPaths = document["paths"]!.AsObject().Count(path => path.Key.StartsWith("/payments", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected.Count, actualPaymentPaths);
        var query = Path(document, "/payments")["get"]!["parameters"]!.AsArray();
        foreach (var name in new[] { "sort", "search", "index", "size" })
        {
            var parameter = Assert.Single(query, item => item!["name"]!.GetValue<string>() == name);
            Assert.Equal("query", parameter!["in"]!.GetValue<string>());
            Assert.NotNull(parameter["schema"]);
        }

        var createHeaders = Path(document, "/payments")["post"]!["parameters"]!.AsArray();
        var idempotency = Assert.Single(createHeaders, item => item!["name"]!.GetValue<string>() == "Idempotency-Key");
        Assert.Equal("header", idempotency!["in"]!.GetValue<string>());
        var updateHeaders = Path(document, "/payments/{paymentId}")["put"]!["parameters"]!.AsArray();
        var concurrency = Assert.Single(updateHeaders, item => item!["name"]!.GetValue<string>() == "If-Unmodified-Since");
        Assert.Equal("header", concurrency!["in"]!.GetValue<string>());
        // No explicit OpenAPI maximum/default is registered for index/size: no invented metadata assertion.
    }

    [Fact]
    public async Task Development_DocumentSchemasMatchPascalCaseMoneyScalarLinksAndRuntimeSecurity()
    {
        await fixture.ResetAsync();
        var document = await DocumentAsync();
        var post = Path(document, "/payments")["post"]!.AsObject();
        var schema = Resolve(document, post["requestBody"]!["content"]!["application/json"]!["schema"]!.AsObject());
        var properties = schema["properties"]!.AsObject();
        await using (var database = fixture.Database())
        {
            database.Payments.Add(new PaymentRecord
            {
                PaymentDirectionId = 100000,
                PaymentMethodId = 100000,
                PaymentTypeId = 100000,
                Description = "Synthetic schema wire control",
                Amount = 1234.56m,
                Recipient = "Synthetic",
                TransactionNumber = "SYNTHETIC",
                PaymentDate = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            });
            await database.SaveChangesAsync();
            using var reader = fixture.Client([AccountingPermissions.Read]);
            using var response = await reader.GetAsync($"/payments/{database.Payments.Local.Single().Id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var wire = JsonNode.Parse(await response.Content.ReadAsStringAsync(),
                new JsonNodeOptions { PropertyNameCaseInsensitive = false })!.AsObject();
            Assert.Equal(1234.56m, wire["Amount"]!.GetValue<decimal>());
            Assert.False(wire.ContainsKey("amount"));
            Assert.True(!properties.ContainsKey("amount"),
                $"HTTP wire fields=[{string.Join(',', wire.Select(field => field.Key))}]; "
                + $"OpenAPI schema fields=[{string.Join(',', properties.Select(field => field.Key))}]");
        }

        foreach (var name in new[] { "Id", "Amount", "Description", "PaymentDirectionId", "PaymentMethodId", "PaymentTypeId", "PaymentDate", "CreatedDate", "ModifiedDate" })
            Assert.True(properties.ContainsKey(name), $"Missing declared wire property {name}.");
        Assert.Contains("number", properties["Amount"]!["type"]!.ToJsonString());
        foreach (var name in new[] { "PaymentDirection", "PaymentMethod", "PaymentType", "PaymentFile" })
            Assert.False(properties.ContainsKey(name), $"Navigation {name} must not be advertised as serialized data.");
        var file = Resolve(document, Path(document, "/payments/files")["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!.AsObject());
        var fileProperties = file["properties"]!.AsObject();
        Assert.True(fileProperties.ContainsKey("PaymentId"));
        Assert.True(fileProperties.ContainsKey("Bucket"));
        Assert.True(fileProperties.ContainsKey("ObjectName"));
        Assert.False(fileProperties.ContainsKey("Payment"));
        // Inspect actual security schemes only if registered; the current Defaults source adds no bearer transformer.
        if (document["components"]?["securitySchemes"] is JsonObject schemes)
        {
            foreach (var scheme in schemes)
                Assert.NotNull(scheme.Value?["type"]);
        }

        using var anonymous = fixture.Client(developmentEnvironment: true);
        using var unauthenticated = await anonymous.GetAsync("/payments");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var denied = fixture.Client([AccountingPermissions.Read], allowLive: false, developmentEnvironment: true);
        using var unauthorized = await denied.GetAsync("/payments");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Read);
    }
}
