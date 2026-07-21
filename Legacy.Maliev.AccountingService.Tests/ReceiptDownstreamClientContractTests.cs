using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class ReceiptDownstreamClientContractTests
{
    [Fact]
    public async Task DocumentClient_UsesReceiptRouteAndExactLegacyDocumentShape()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        });
        var client = new ReceiptDocumentClient(Http(handler, "http://documents/"));

        var result = await client.RenderAsync(
            Receipt(),
            [new ReceiptOrderItem { Description = "Print", Quantity = 2, UnitPrice = 50m, Subtotal = 100m }],
            [7, 8],
            CancellationToken.None);

        Assert.Equal([1, 2, 3], result);
        Assert.Equal("/Pdfs/receipt", handler.Path);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal(91, json.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal("INV-42", json.RootElement.GetProperty("InvoiceNumber").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("OrderItems")[0].GetProperty("Quantity").GetInt32());
        Assert.Equal("Bwg=", json.RootElement.GetProperty("Signature").GetString());
    }

    [Fact]
    public async Task FileClient_UploadsOneDeterministicPdfThroughLegacyScannedBoundary()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                "{\"Object\":[{\"Bucket\":\"maliev.com\",\"ObjectName\":\"receipts/91/receipt_91.pdf\",\"Uri\":\"https://example.test/file\"}]}",
                Encoding.UTF8,
                "application/json"),
        });
        var factory = new Mock<IHttpClientFactory>();
        var client = new ReceiptFileClient(Http(handler, "http://files/"), factory.Object);
        var operationId = Guid.Parse("9e60b70d-21af-473e-8749-fab4993e4f4f");

        var result = await client.UploadAsync(
            "maliev.com",
            "receipts/91",
            "receipt_91.pdf",
            [1, 2, 3],
            operationId,
            CancellationToken.None);

        Assert.Equal("maliev.com", result.Bucket);
        Assert.Equal("receipts/91/receipt_91.pdf", result.ObjectName);
        Assert.Equal("/Uploads?bucket=maliev.com&path=receipts%2F91", handler.Path);
        Assert.Contains("name=files", handler.Body!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("filename=receipt_91.pdf", handler.Body!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(operationId.ToString("D"), handler.IdempotencyKey);
    }

    [Fact]
    public async Task NotificationClient_ForwardsStableUuidAndPdfAttachment()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"providerMessageId\":\"message-id\"}", Encoding.UTF8, "application/json"),
        });
        var client = new ReceiptNotificationClient(Http(handler, "http://notifications/"));
        var operationId = Guid.Parse("9e60b70d-21af-473e-8749-fab4993e4f4f");

        var result = await client.SendAsync(
            "customer@example.com",
            "Customer <One>",
            Receipt(),
            [1, 2, 3],
            operationId,
            CancellationToken.None);

        Assert.Equal("message-id", result);
        Assert.Equal("/notifications/v1/email/Info", handler.Path);
        Assert.Equal(operationId.ToString("D"), handler.IdempotencyKey);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("receipt_91.pdf", json.RootElement.GetProperty("attachments")[0].GetProperty("fileName").GetString());
        Assert.Equal("AQID", json.RootElement.GetProperty("attachments")[0].GetProperty("content").GetString());
        Assert.Contains("Customer &lt;One&gt;", json.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task CustomerClient_UsesOwnedProfileRouteAndReturnsTrustedContact()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"email\":\"customer@example.com\",\"fullName\":\"Customer One\"}"));
        var client = new ReceiptCustomerClient(Http(handler, "http://customers/"));

        var result = await client.GetAsync(7, CancellationToken.None);

        Assert.Equal("/customers/7", handler.Path);
        Assert.Equal("customer@example.com", result.Email);
        Assert.Equal("Customer One", result.Name);
    }

    [Fact]
    public async Task SignatureClient_UsesEmployeeSignatureRouteAndCleanFileBoundary()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"bucket\":\"maliev.com\",\"objectName\":\"employees/12/signature.png\"}"));
        var files = new Mock<IReceiptFileClient>(MockBehavior.Strict);
        files.Setup(value => value.DownloadAsync("maliev.com", "employees/12/signature.png", 2 * 1024 * 1024, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 7, 8 });
        var client = new ReceiptSignatureClient(Http(handler, "http://employees/"), files.Object);

        var result = await client.GetAsync(12, CancellationToken.None);

        Assert.Equal("/employees/Signatures/12", handler.Path);
        Assert.Equal([7, 8], result);
    }

    [Fact]
    public async Task FileClient_DownloadsOnlyFromBoundedGoogleStorageSignedUrlWithoutServiceAuth()
    {
        var fileService = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            "\"https://storage.googleapis.com/maliev.com/employees/12/signature.png?signature=test\""));
        var storage = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([7, 8]),
        });
        var client = new ReceiptFileClient(
            Http(fileService, "http://files/"),
            new StubHttpClientFactory(Http(storage, "https://storage.googleapis.com/")));

        var result = await client.DownloadAsync(
            "maliev.com",
            "employees/12/signature.png",
            2 * 1024 * 1024,
            CancellationToken.None);

        Assert.Equal([7, 8], result);
        Assert.Equal("/uploads/SignedUrl?bucket=maliev.com&objectName=employees%2F12%2Fsignature.png", fileService.Path);
        Assert.Equal("/maliev.com/employees/12/signature.png?signature=test", storage.Path);
        Assert.Null(storage.Authorization);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string value) => new(status)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private static HttpClient Http(HttpMessageHandler handler, string origin) => new(handler)
    {
        BaseAddress = new Uri(origin),
    };

    private static Receipt Receipt() => new()
    {
        Id = 91,
        InvoiceNumber = "INV-42",
        CustomerId = 7,
        Currency = "THB",
        PaymentDate = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc),
        Subtotal = 100m,
        Vat = 7m,
        Total = 107m,
        AmountPaid = 107m,
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Path = request.RequestUri?.PathAndQuery;
            this.IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
            this.Authorization = request.Headers.Authorization?.ToString();
            this.Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }


    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(ReceiptFileClient.ObjectDownloadClientName, name);
            return client;
        }
    }
}
