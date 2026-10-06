using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceEmployeeQuotationCompletionClientTests
{
    private static readonly InvoiceFinancialOwnership Ownership = new(1, Guid.Parse("dc904058-4d1b-4eab-b8b2-a505700b0419"), 84, 901,
        "https://auth.example.invalid", "employee:42", "service:legacy-intranet", "2026-10-06T01:02:03.0000000Z", new string('A', 64));
    private const string Proof = "synthetic.transient.capability";

    [Theory]
    [InlineData("Bearer synthetic.transient.capability")]
    [InlineData(" synthetic.transient.capability")]
    [InlineData("synthetic.transient.capability ")]
    [InlineData("synthetic.transient")]
    [InlineData("synthetic..capability")]
    public async Task InternalCapabilityMustBeRawCompactWithoutPrefixOrWhitespace(string proof)
    {
        var handler = new Handler([]);
        using var http = Http(handler);
        await Assert.ThrowsAsync<InvoiceCreationUnavailableException>(() => new InvoiceEmployeeQuotationCompletionClient(http).CompleteAsync(Ownership, proof, CancellationToken.None));
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("membership")]
    [InlineData("retreat")]
    public async Task PartialReadbackCannotChangeFrozenOrderVersionOrMembership(string changedField)
    {
        var prior = Receipt("OrdersPartial", 1, 2);
        var changed = changedField switch
        {
            "version" => Receipt() with { DecisionOrderVersion = "2026-10-06T01:02:06.0000000Z" },
            "membership" => Receipt("Completed", 3, 3),
            "retreat" => Receipt("OrdersPartial", 0, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(changedField))
        };
        var handler = new Handler([Response(prior), new(HttpStatusCode.OK), Response(changed)]);
        using var http = Http(handler);
        await Assert.ThrowsAsync<InvoiceCreationUnavailableException>(() => new InvoiceEmployeeQuotationCompletionClient(http).CompleteAsync(Ownership, Proof, CancellationToken.None));
        Assert.Equal(3, handler.Calls.Count);
    }

    [Fact]
    public async Task ExactCompletedReplayReadsOwningReceiptOnly()
    {
        var handler = new Handler([Response(Receipt())]);
        using var http = Http(handler);
        var result = await new InvoiceEmployeeQuotationCompletionClient(http).CompleteAsync(Ownership, Proof, CancellationToken.None);
        Assert.Equal("Completed", result.State);
        Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Get, handler.Calls[0].Method);
        AssertHeaders(handler);
    }

    [Fact]
    public async Task MissingReceiptSendsOneExactEmployeeDecisionThenRequiresOwningReadback()
    {
        var handler = new Handler([new(HttpStatusCode.NotFound), new(HttpStatusCode.OK), Response(Receipt())]);
        using var http = Http(handler);
        _ = await new InvoiceEmployeeQuotationCompletionClient(http).CompleteAsync(Ownership, Proof, CancellationToken.None);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Get }, handler.Calls.Select(value => value.Method));
        using var body = JsonDocument.Parse(handler.Calls[1].Body!);
        Assert.Equal(new[] { "Accepted", "EmployeeInitiated", "InvoiceId" }, body.RootElement.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.True(body.RootElement.GetProperty("EmployeeInitiated").GetBoolean());
        Assert.True(body.RootElement.GetProperty("Accepted").GetBoolean());
        Assert.Equal(Ownership.InvoiceId, body.RootElement.GetProperty("InvoiceId").GetInt32());
        AssertHeaders(handler);
    }

    [Fact]
    public async Task LostPutAcknowledgmentUsesExactReceiptWithoutRepeatingDecision()
    {
        var handler = new Handler([new(HttpStatusCode.NotFound), null, Response(Receipt())]);
        using var http = Http(handler);
        Assert.Equal("Completed", (await new InvoiceEmployeeQuotationCompletionClient(http).CompleteAsync(Ownership, Proof, CancellationToken.None)).State);
        Assert.Single(handler.Calls.Where(value => value.Method == HttpMethod.Put));
        Assert.Equal(3, handler.Calls.Count);
    }

    [Fact]
    public async Task BareDecisionSuccessWithoutOwningReceiptCannotComplete()
    {
        var handler = new Handler([new(HttpStatusCode.NotFound), new(HttpStatusCode.OK), new(HttpStatusCode.NotFound)]);
        using var http = Http(handler);
        await Assert.ThrowsAsync<InvoiceCreationUnavailableException>(() =>
            new InvoiceEmployeeQuotationCompletionClient(http).CompleteAsync(Ownership, Proof, CancellationToken.None));
        Assert.Single(handler.Calls.Where(value => value.Method == HttpMethod.Put));
        Assert.Equal(3, handler.Calls.Count);
    }

    [Theory]
    [InlineData("QuotationCommitted")]
    [InlineData("OrdersPartial")]
    [InlineData("OrdersConflict")]
    public async Task GenuinePartialReceiptNeverBecomesCompleted(string state)
    {
        var partial = Receipt(state, 1, 2);
        var handler = new Handler([Response(partial), new(HttpStatusCode.Conflict), Response(partial)]);
        using var http = Http(handler);
        var actual = await new InvoiceEmployeeQuotationCompletionClient(http).CompleteAsync(Ownership, Proof, CancellationToken.None);
        Assert.Equal(state, actual.State);
        Assert.NotEqual("Completed", actual.State);
        Assert.Equal(3, handler.Calls.Count);
    }

    [Theory]
    [InlineData("OperationId")]
    [InlineData("QuotationId")]
    [InlineData("InvoiceId")]
    [InlineData("OriginIssuer")]
    [InlineData("EmployeeSubject")]
    [InlineData("RequesterSubject")]
    [InlineData("ExecutorSubject")]
    [InlineData("OriginalQuotationVersion")]
    [InlineData("FinancialBinding")]
    [InlineData("FinancialBindingVersion")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("numeric-type")]
    [InlineData("date-kind")]
    [InlineData("counts")]
    [InlineData("state")]
    public async Task MalformedOrForeignReceiptFailsClosedBeforeAnyDecision(string scenario)
    {
        var json = JsonSerializer.SerializeToNode(Receipt())!.AsObject();
        switch (scenario)
        {
            case "extra": json["Token"] = "never-authority"; break;
            case "duplicate": break;
            case "missing": json.Remove("OriginIssuer"); break;
            case "numeric-type": json["ContractVersion"] = "1"; break;
            case "date-kind": json["DecisionOrderVersion"] = "2026-10-06T01:02:03.0000000+00:00"; break;
            case "counts": json["CompletedOrders"] = 0; break;
            case "state": json["State"] = "InvoiceCompleted"; break;
            case "QuotationId": json[scenario] = 85; break;
            case "InvoiceId": json[scenario] = 902; break;
            default: json[scenario] = "foreign"; break;
        }
        var body = json.ToJsonString();
        if (scenario == "duplicate") body = body.Insert(1, "\"ContractVersion\":1,");
        var handler = new Handler([new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }]);
        using var http = Http(handler);
        await Assert.ThrowsAsync<InvoiceCreationUnavailableException>(() => new InvoiceEmployeeQuotationCompletionClient(http).CompleteAsync(Ownership, Proof, CancellationToken.None));
        Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Get, handler.Calls[0].Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task UnverifiedInitialReadNeverAuthorizesDecision(HttpStatusCode status)
    {
        var handler = new Handler([new(status)]);
        using var http = Http(handler);
        var error = await Record.ExceptionAsync(() => new InvoiceEmployeeQuotationCompletionClient(http).CompleteAsync(Ownership, Proof, CancellationToken.None));
        Assert.NotNull(error);
        Assert.True(error is InvoiceCreationUnavailableException or InvoiceCreationConflictException);
        Assert.Single(handler.Calls);
    }

    private static InvoiceQuotationOperationReceipt Receipt(string state = "Completed", int completed = 2, int total = 2) =>
        new(1, Ownership.OperationId.ToString("D"), Ownership.QuotationId, Ownership.InvoiceId, Ownership.OriginIssuer,
            Ownership.EmployeeSubject, Ownership.RequesterSubject, "service:legacy-accounting", Ownership.OriginalQuotationVersion,
            Ownership.FinancialBinding, "invoice-creation-financial-v1", state, "2026-10-06T01:02:04.0000000Z", completed, total,
            "2026-10-06T01:02:05.0000000Z");
    private static HttpResponseMessage Response(InvoiceQuotationOperationReceipt receipt) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(receipt), Encoding.UTF8, "application/json") };
    private static HttpClient Http(Handler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new("http://quotation.invalid/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-accounting-workload");
        return http;
    }
    private static void AssertHeaders(Handler handler)
    {
        foreach (var call in handler.Calls)
        {
            Assert.Equal("Bearer " + Proof, call.Proof);
            Assert.Equal(Ownership.OriginalQuotationVersion, call.Version);
            Assert.Equal(Ownership.OperationId.ToString("D"), call.Key);
            Assert.Equal("Bearer synthetic-accounting-workload", call.Authorization);
            Assert.Equal(call.Method == HttpMethod.Put ? "/quotations/84/decision"
                : $"/quotations/84/invoice-completion/operations/{Ownership.OperationId:D}?invoiceId=901", call.Path);
        }
    }
    private sealed record Call(HttpMethod Method, string Path, string? Body, string Proof, string Version, string Key, string? Authorization);
    private sealed class Handler(IEnumerable<HttpResponseMessage?> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage?> remaining = new(responses);
        public List<Call> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(new(request.Method, request.RequestUri!.PathAndQuery,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.GetValues(InvoiceEmployeeQuotationCompletionClient.CapabilityHeader).Single(),
                request.Headers.GetValues("X-Expected-Modified-Date").Single(), request.Headers.GetValues("Idempotency-Key").Single(),
                request.Headers.Authorization?.ToString()));
            return remaining.Dequeue() ?? throw new HttpRequestException("Synthetic lost decision acknowledgment.");
        }
    }
}
