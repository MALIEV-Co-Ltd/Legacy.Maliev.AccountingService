using System.Net;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Tests.Fixtures;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class AccountingOpenApiXmlHttpContractTests(AccountingBoundaryHttpFixture fixture)
{
    private async Task<JsonObject> DocumentAsync()
    {
        using var client = fixture.Client(developmentEnvironment: true);
        using var response = await client.GetAsync("/accounting/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(),
            new JsonNodeOptions { PropertyNameCaseInsensitive = false })!.AsObject();
    }

    private static JsonObject Operation(JsonObject document, string path, string method)
    {
        var actual = Assert.Single(document["paths"]!.AsObject(),
            entry => string.Equals(entry.Key, path, StringComparison.OrdinalIgnoreCase));
        return actual.Value![method]!.AsObject();
    }

    private static JsonObject RequestSchema(JsonObject document, string path)
    {
        var schema = Operation(document, path, "post")["requestBody"]!["content"]!["application/json"]!["schema"]!.AsObject();
        while (schema["$ref"] is JsonValue reference)
        {
            var value = reference.GetValue<string>();
            Assert.StartsWith("#/components/schemas/", value);
            schema = document["components"]!["schemas"]![value["#/components/schemas/".Length..]]!.AsObject();
        }

        return schema;
    }

    [Theory]
    [InlineData("/invoices/{invoiceId}/receipt", "post", "Creates or reconciles a receipt, its PDF, storage link, and optional email.")]
    [InlineData("/invoices/{invoiceId}/receipt", "delete", "Removes receipt storage and accounting state idempotently.")]
    [InlineData("/invoices/{invoiceId}/receipt/email", "post", "Explicitly resends an existing receipt with an operator-approved operation UUID.")]
    public async Task Development_ReceiptWorkflowOperationsServeExistingXmlSummary(string path, string method, string summary)
    {
        var document = await DocumentAsync();
        Assert.Equal(summary, Operation(document, path, method)["summary"]?.GetValue<string>());
    }

    [Fact]
    public async Task Development_PaymentSchemaServesExistingMoneyAndScalarXmlDescriptions()
    {
        var schema = RequestSchema(await DocumentAsync(), "/payments");
        Assert.Equal("A payment.", schema["description"]?.GetValue<string>());
        var properties = schema["properties"]!.AsObject();
        Assert.Equal("Gets or sets the amount.\nThe amount.", properties["Amount"]?["description"]?.GetValue<string>());
        Assert.Equal("Gets or sets the payment direction identifier.\nThe payment direction identifier.", properties["PaymentDirectionId"]?["description"]?.GetValue<string>());
        Assert.False(properties.ContainsKey("PaymentDirection"));
        Assert.False(properties.ContainsKey("PaymentFile"));
    }

    [Fact]
    public async Task Development_PaymentFileSchemaServesExistingStorageMetadataXmlDescriptions()
    {
        var schema = RequestSchema(await DocumentAsync(), "/payments/files");
        Assert.Equal("Payment file.", schema["description"]?.GetValue<string>());
        var properties = schema["properties"]!.AsObject();
        Assert.Equal("Gets or sets the bucket.\nThe bucket.", properties["Bucket"]?["description"]?.GetValue<string>());
        Assert.Equal("Gets or sets the name of the object.\nThe name of the object.", properties["ObjectName"]?["description"]?.GetValue<string>());
        Assert.False(properties.ContainsKey("Payment"));
    }
}
