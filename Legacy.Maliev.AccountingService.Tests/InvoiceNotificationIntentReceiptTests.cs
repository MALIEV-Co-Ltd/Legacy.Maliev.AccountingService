using System.Net;
using System.Text;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceNotificationIntentReceiptTests
{
    private static readonly InvoiceNotificationCorrelationIdentity Identity = new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"), 42, "invoice-issued", 7,
        Guid.Parse("20000000-0000-0000-0000-000000000002"),
        new("https://origin.example.test", "employee:fixture", "service:legacy-intranet"),
        "https://auth.example.test", "service:legacy-accounting", "notification-payload-v1", "accounting-invoice-notification-hmac-v1");
    private const string Admitted = "{\"intentId\":\"10000000-0000-0000-0000-000000000001\",\"purpose\":\"invoice-issued\",\"resourceType\":\"invoice\",\"resourceId\":\"42\",\"workflowOperationId\":\"20000000-0000-0000-0000-000000000002\",\"state\":\"admitted\",\"version\":1,\"admittedAt\":\"2026-10-01T00:00:00Z\",\"updatedAt\":\"2026-10-01T00:00:00Z\"}";

    [Fact]
    public void CamelCaseOffsetReceipt_ValidatesExactIdentity()
    {
        var receipt = Parse(Admitted);
        Assert.Equal("admitted", receipt.State);
        Assert.Null(receipt.ProviderMessageId);
        Assert.Equal(1, receipt.Version);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("pascal")]
    [InlineData("identity")]
    [InlineData("missing")]
    [InlineData("offset")]
    [InlineData("submicrosecond")]
    [InlineData("null-provider")]
    public void MalformedReceipt_IsUncertaintyNeverSendAuthority(string mutation)
    {
        var json = mutation switch
        {
            "duplicate" => Admitted.Replace("\"version\":1", "\"version\":1,\"version\":1", StringComparison.Ordinal),
            "unknown" => Admitted.Replace("\"version\":1", "\"version\":1,\"unexpected\":true", StringComparison.Ordinal),
            "pascal" => Admitted.Replace("intentId", "IntentId", StringComparison.Ordinal),
            "identity" => Admitted.Replace("\"42\"", "\"43\"", StringComparison.Ordinal),
            "missing" => Admitted.Replace("\"version\":1,", "", StringComparison.Ordinal),
            "offset" => Admitted.Replace("00:00:00Z", "00:00:00", StringComparison.Ordinal),
            "submicrosecond" => Admitted.Replace("00:00:00Z", "00:00:00.0000001Z", StringComparison.Ordinal),
            _ => Admitted.Replace("\"version\":1", "\"version\":1,\"providerMessageId\":null", StringComparison.Ordinal),
        };
        Assert.Throws<InvoiceNotificationCorrelationUnavailableException>(() => Parse(json));
    }

    [Theory]
    [InlineData(200, "execute")]
    [InlineData(202, "admit")]
    [InlineData(403, "read")]
    [InlineData(404, "read")]
    [InlineData(503, "read")]
    public void StatusAndOperation_DoNotTurnAdmittedIntoAcceptance(int status, string operation) =>
        Assert.Throws<InvoiceNotificationCorrelationUnavailableException>(() =>
            InvoiceNotificationIntentClient.ParseReceipt(Encoding.UTF8.GetBytes(Admitted), Identity, (HttpStatusCode)status, operation));

    private static InvoiceNotificationReceiptObservation Parse(string json) =>
        InvoiceNotificationIntentClient.ParseReceipt(Encoding.UTF8.GetBytes(json), Identity, HttpStatusCode.OK, "read");
}
