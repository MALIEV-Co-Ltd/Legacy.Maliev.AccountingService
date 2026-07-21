using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceCreationDownstreamClientTests
{
    [Fact]
    public async Task SourceClient_UsesExactServiceRoutesAndResolvesCountryNames()
    {
        var routes = new Dictionary<string, string>
        {
            ["InvoiceCreationQuotation|/quotations/84"] = """{"id":84,"customerId":42,"employeeId":7,"invoiceId":null,"period":14,"expirationDate":"2030-08-01T00:00:00Z","subtotal":1000.25,"vat":70.02,"total":1070.27,"withholdingTax":30,"currencyId":1,"comment":"note","fob":"Bangkok","shippedVia":"Courier","terms":"Net 7"}""",
            ["InvoiceCreationQuotation|/quotations/84/orderitems"] = """[{"id":1,"quotationId":84,"orderId":51,"description":"Part","quantity":2,"unitPrice":500.125,"subtotal":1000.25}]""",
            ["InvoiceCreationCustomer|/customers/42"] = """{"id":42,"fullName":"Customer One","telephone":"02","mobile":"08","fax":null,"email":"customer@example.com","company":{"name":"Company","taxNumber":"TAX","registrar":"REG"},"billingAddress":{"building":"Tower","addressLine1":"Road","addressLine2":null,"city":"Bangkok","state":"Bangkok","postalCode":"10110","countryId":764},"shippingAddress":{"building":"Tower","addressLine1":"Road","addressLine2":null,"city":"Bangkok","state":"Bangkok","postalCode":"10110","countryId":764}}""",
            ["InvoiceCreationEmployee|/employees/7"] = """{"id":7,"fullName":"Employee One"}""",
            ["InvoiceCreationCatalog|/currencies/1"] = """{"id":1,"shortName":"THB","longName":"Thai baht"}""",
            ["InvoiceCreationCatalog|/countries/764"] = """{"id":764,"name":"Thailand"}""",
        };
        var factory = new RoutingFactory(routes);

        var result = await new InvoiceCreationSourceClient(factory).GetAsync(84, CancellationToken.None);

        Assert.Equal("Thailand", result.Customer.BillingAddress?.Country);
        Assert.Equal("Thailand", result.Customer.ShippingAddress?.Country);
        Assert.Equal(6, factory.Requests.Count);
        Assert.Contains("InvoiceCreationQuotation|/quotations/84/orderitems", factory.Requests);
    }

    [Fact]
    public async Task DocumentClient_UsesQuestPdfInvoiceRouteAndLegacyWireNames()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, [1, 2, 3], "application/pdf");
        var invoice = new Invoice { Id = 9, Number = "INV-9", CustomerId = 42, Currency = "THB", Comment = "remark", Vat = 7m };

        var bytes = await new InvoiceCreationDocumentClient(Http(handler, "http://documents/"))
            .RenderAsync(invoice, [new InvoiceOrderItem { Description = "Part", Quantity = 2, UnitPrice = 5m, Subtotal = 10m }], CancellationToken.None);

        Assert.Equal("/pdfs/invoice", handler.Path);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("remark", json.RootElement.GetProperty("remark").GetString());
        Assert.Equal(7m, json.RootElement.GetProperty("vat").GetDecimal());
        Assert.Equal("Part", json.RootElement.GetProperty("orderItems")[0].GetProperty("description").GetString());
        Assert.Equal([1, 2, 3], bytes);
    }

    [Fact]
    public async Task QuotationCompletion_PreservesQuotationFieldsAndTransitionsDecision()
    {
        var handler = new SequenceHandler();
        await new InvoiceQuotationCompletionClient(Http(handler, "http://quotations/"))
            .CompleteAsync(84, 901, Guid.Parse("4f7870e2-d349-41bb-b4cf-567450f261e9"), CancellationToken.None);

        Assert.Equal(["GET /quotations/84", "PUT /quotations/84", "PUT /quotations/84/decision"], handler.Requests);
        using var update = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(901, update.RootElement.GetProperty("invoiceId").GetInt32());
        Assert.True(update.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal(14, update.RootElement.GetProperty("period").GetInt32());
    }

    private static HttpClient Http(HttpMessageHandler handler, string baseAddress) => new(handler) { BaseAddress = new(baseAddress) };

    private sealed class RoutingFactory(Dictionary<string, string> routes) : IHttpClientFactory
    {
        public List<string> Requests { get; } = [];
        public HttpClient CreateClient(string name) => Http(new RouteHandler(name, routes, Requests), "http://service/");
    }
    private sealed class RouteHandler(string name, Dictionary<string, string> routes, List<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = $"{name}|{request.RequestUri!.PathAndQuery}"; requests.Add(key);
            return Task.FromResult(routes.TryGetValue(key, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
    private sealed class CaptureHandler(HttpStatusCode status, byte[] response, string contentType) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath; Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new ByteArrayContent(response) { Headers = { ContentType = new(contentType) } } };
        }
    }
    private sealed class SequenceHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Content is not null) Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"customerId":42,"employeeId":7,"invoiceId":null,"period":14,"expirationDate":"2030-08-01T00:00:00Z","subtotal":1000.25,"vat":70.02,"total":1070.27,"withholdingTax":30,"currencyId":1,"comment":"note","fob":"Bangkok","shippedVia":"Courier","terms":"Net 7","accepted":null,"modifiedDate":"2030-07-18T00:00:00Z"}""", Encoding.UTF8, "application/json") };
            return new(HttpStatusCode.OK);
        }
    }
}
