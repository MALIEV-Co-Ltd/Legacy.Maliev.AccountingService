using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Data;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceSourceAttributionWireTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task PascalCaseQuotationWire_PreservesEachOptionalAttributionKeyInAuthoritativeSnapshot(bool requestPresent, bool journeyPresent)
    {
        var journey = Guid.Parse("28e3a736-882c-4b4e-b254-b85432d994ee");
        var quotation = new
        {
            Id = 84,
            CustomerId = 42,
            EmployeeId = 7,
            InvoiceId = (int?)null,
            Period = 14,
            ExpirationDate = "2030-08-01T00:00:00Z",
            Subtotal = 100m,
            Vat = 7m,
            Total = 107m,
            CurrencyId = 1,
            SourceRequestId = requestPresent ? 701 : (int?)null,
            SourceJourneyId = journeyPresent ? journey : (Guid?)null,
        };
        // Source producer uses PascalCase. Real client deserialization/mapping is exercised,
        // with deterministic dependency responses; this is not joined downstream runtime proof.
        var routes = new Dictionary<string, string>
        {
            ["InvoiceCreationQuotation|/quotations/84"] = JsonSerializer.Serialize(quotation),
            ["InvoiceCreationQuotation|/quotations/84/orderitems"] = """[{"Id":1,"QuotationId":84,"Description":"Synthetic part","Quantity":1,"UnitPrice":100,"Subtotal":100}]""",
            ["InvoiceCreationCustomer|/customers/42"] = """{"Id":42,"FullName":"Synthetic Thai customer","Email":"fixture@example.invalid"}""",
            ["InvoiceCreationEmployee|/employees/7"] = """{"Id":7,"FullName":"Synthetic Thai employee"}""",
            ["InvoiceCreationCatalog|/currencies/1"] = """{"Id":1,"ShortName":"THB","LongName":"Thai baht"}""",
        };
        var factory = new Routes(routes);
        var snapshot = await new InvoiceCreationSourceClient(factory).GetAsync(84, CancellationToken.None);

        Assert.Equal(requestPresent ? 701 : (int?)null, snapshot.Quotation.SourceRequestId);
        Assert.Equal(journeyPresent ? journey : (Guid?)null, snapshot.Quotation.SourceJourneyId);
        Assert.Equal(84, snapshot.Quotation.Id);
        Assert.Equal(42, snapshot.Customer.Id);
        Assert.Equal(7, snapshot.Employee.Id);
        Assert.Equal("THB", snapshot.Currency.ShortName);
        Assert.Equal(107m, snapshot.Quotation.Total);
        Assert.Equal(5, factory.Seen.Count);
        Assert.Equal(routes.Keys.Order(StringComparer.Ordinal), factory.Seen.Order(StringComparer.Ordinal));
    }

    private sealed class Routes(Dictionary<string, string> responses) : IHttpClientFactory
    {
        public List<string> Seen { get; } = [];
        public HttpClient CreateClient(string name) => new(new Handler(name, responses, Seen)) { BaseAddress = new("http://source-fixture.invalid") };
    }

    private sealed class Handler(string name, Dictionary<string, string> responses, List<string> seen) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var route = $"{name}|{request.RequestUri!.PathAndQuery}";
            seen.Add(route);
            return Task.FromResult(responses.TryGetValue(route, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
