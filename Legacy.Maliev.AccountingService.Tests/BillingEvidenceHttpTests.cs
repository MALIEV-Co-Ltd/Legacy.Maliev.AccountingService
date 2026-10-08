using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class BillingEvidenceHttpTests
{
    private static readonly EvidenceReference Reference = new(Guid.NewGuid(), Guid.NewGuid());
    private static EvidenceReceipt Receipt() => new(Reference.DocumentId, Reference.VersionId, 21, "Release", new string('a', 64),
        "Verified", "employee:42", new DateTimeOffset(2026, 10, 8, 1, 2, 3, TimeSpan.Zero), 2, 84, [5]);

    [Fact]
    public async Task ReadsExactImmutableVersionAndVerifiedAssociations()
    {
        using var handler = new ReceiptHandler(Receipt());
        using var client = new HttpClient(handler) { BaseAddress = new("https://files.example.invalid/") };
        var receipts = await new BillingEvidenceClient(client).VerifyAsync(21, 84, [5], [Reference], CancellationToken.None);
        var expected = Receipt();
        var actual = Assert.Single(receipts);
        Assert.Equal(expected, actual with { OrderIds = expected.OrderIds });
        Assert.Equal<int>(expected.OrderIds, actual.OrderIds);
        Assert.Equal($"/customers/21/documents/{Reference.DocumentId:D}/versions/{Reference.VersionId:D}/receipt", handler.Path);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("quotation")]
    [InlineData("order")]
    [InlineData("version")]
    [InlineData("kind")]
    [InlineData("status")]
    [InlineData("actor")]
    [InlineData("time")]
    [InlineData("revision")]
    [InlineData("digest")]
    [InlineData("offset")]
    [InlineData("duplicate-order")]
    [InlineData("no-association")]
    public async Task RejectsUnverifiedOrUnrelatedEvidence(string change)
    {
        var original = Receipt();
        var receipt = change switch
        {
            "customer" => original with { CustomerId = 99 },
            "quotation" => original with { QuotationId = 999 },
            "order" => original with { OrderIds = [999] },
            "version" => original with { VersionId = Guid.NewGuid() },
            "kind" => original with { Kind = "Nda" },
            "status" => original with { VerificationStatus = "Rejected" },
            "actor" => original with { VerifiedBySubject = " " },
            "time" => original with { VerifiedAtUtc = null },
            "revision" => original with { Revision = 0 },
            "offset" => original with { VerifiedAtUtc = original.VerifiedAtUtc!.Value.ToOffset(TimeSpan.FromHours(7)) },
            "duplicate-order" => original with { OrderIds = [5, 5] },
            "no-association" => original with { QuotationId = null, OrderIds = [] },
            _ => original with { ContentSha256 = new string('z', 64) }
        };
        using var handler = new ReceiptHandler(receipt);
        using var client = new HttpClient(handler) { BaseAddress = new("https://files.example.invalid/") };
        await Assert.ThrowsAsync<BillingDependencyException>(() => new BillingEvidenceClient(client).VerifyAsync(21, 84, [5], [Reference], CancellationToken.None));
    }

    [Theory]
    [InlineData("camel-case")]
    [InlineData("ordinal-kind")]
    [InlineData("ordinal-status")]
    [InlineData("malformed")]
    [InlineData("oversized")]
    public async Task RejectsUntrustedWireFormats(string change)
    {
        var json = JsonSerializer.Serialize(Receipt());
        json = change switch
        {
            "camel-case" => JsonSerializer.Serialize(Receipt(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            "ordinal-kind" => json.Replace("\"Kind\":\"Release\"", "\"Kind\":2", StringComparison.Ordinal),
            "ordinal-status" => json.Replace("\"VerificationStatus\":\"Verified\"", "\"VerificationStatus\":1", StringComparison.Ordinal),
            "malformed" => "{",
            _ => new string(' ', 65537) + json
        };
        using var client = new HttpClient(new RawHandler(json)) { BaseAddress = new("https://files.example.invalid/") };
        await Assert.ThrowsAsync<BillingDependencyException>(() => new BillingEvidenceClient(client).VerifyAsync(21, 84, [5], [Reference], CancellationToken.None));
    }

    [Fact]
    public async Task PreviewReceiptCannotAuthorizeLaterUnavailableRead()
    {
        using var handler = new ReceiptHandler(Receipt());
        using var client = new HttpClient(handler) { BaseAddress = new("https://files.example.invalid/") };
        var reader = new BillingEvidenceClient(client);
        Assert.Single(await reader.VerifyAsync(21, 84, [5], [Reference], CancellationToken.None));
        handler.Status = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<BillingDependencyException>(() => reader.VerifyAsync(21, 84, [5], [Reference], CancellationToken.None));
    }

    [Fact]
    public async Task ExactConfirmedOrderAssociationIsSufficientWithoutQuotationAssociation()
    {
        using var handler = new ReceiptHandler(Receipt() with { QuotationId = null });
        using var client = new HttpClient(handler) { BaseAddress = new("https://files.example.invalid/") };
        Assert.Single(await new BillingEvidenceClient(client).VerifyAsync(21, 84, [5], [Reference], CancellationToken.None));
    }

    private sealed class RawHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task UnavailableOrDeniedEvidenceNeverFallsBackToAnEarlierReceipt(int status)
    {
        using var handler = new ReceiptHandler(Receipt(), (HttpStatusCode)status);
        using var client = new HttpClient(handler) { BaseAddress = new("https://files.example.invalid/") };
        await Assert.ThrowsAsync<BillingDependencyException>(() => new BillingEvidenceClient(client).VerifyAsync(21, 84, [5], [Reference], CancellationToken.None));
    }

    private sealed class ReceiptHandler(EvidenceReceipt receipt, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public HttpStatusCode Status { get; set; } = status;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = JsonContent.Create(receipt, options: new JsonSerializerOptions()) });
        }
    }
}
