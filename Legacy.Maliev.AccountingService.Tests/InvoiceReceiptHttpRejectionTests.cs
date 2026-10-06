using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Tests.Fixtures;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class InvoiceReceiptHttpRejectionTests(AccountingBoundaryHttpFixture fixture)
{
    [Theory]
    [InlineData("create", "missing")]
    [InlineData("create", "malformed")]
    [InlineData("create", "empty")]
    [InlineData("delete", "missing")]
    [InlineData("delete", "malformed")]
    [InlineData("delete", "empty")]
    [InlineData("email", "missing")]
    [InlineData("email", "malformed")]
    [InlineData("email", "empty")]
    public Task Receipt_InvalidOperationIdentityRejectsWithoutStateOrOutboundChanges(string operation, string identity) =>
        AssertRejectedAsync(operation, identity switch
        {
            "missing" => null,
            "malformed" => "not-a-uuid",
            "empty" => Guid.Empty.ToString("D"),
            _ => throw new ArgumentOutOfRangeException(nameof(identity)),
        }, 7, anonymous: false, allowLive: true, HttpStatusCode.BadRequest, "Stable operation identity required");

    [Theory]
    [InlineData("create", 0)]
    [InlineData("create", -1)]
    [InlineData("email", 0)]
    [InlineData("email", -1)]
    public Task Receipt_InvalidEmployeeRejectsWithoutStateOrOutboundChanges(string operation, int employeeId) =>
        AssertRejectedAsync(operation, Guid.NewGuid().ToString("D"), employeeId,
            anonymous: false, allowLive: true, HttpStatusCode.BadRequest, "Trusted employee identity required");

    [Theory]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("email")]
    public Task Receipt_MissingInvoiceWithValidIdentityReturnsNotFoundWithoutStateOrOutboundChanges(string operation) =>
        AssertRejectedAsync(operation, Guid.NewGuid().ToString("D"), 7,
            anonymous: false, allowLive: true, HttpStatusCode.NotFound, "Receipt workflow state not found");

    [Theory]
    [InlineData("create", true)]
    [InlineData("delete", true)]
    [InlineData("email", true)]
    [InlineData("create", false)]
    [InlineData("delete", false)]
    [InlineData("email", false)]
    public Task Receipt_AnonymousOrLiveDeniedRejectsDespiteValidHeadersWithoutStateOrOutboundChanges(
        string operation, bool anonymous) =>
        AssertRejectedAsync(operation, Guid.NewGuid().ToString("D"), 7, anonymous, allowLive: false,
            anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, expectedTitle: null);

    private async Task AssertRejectedAsync(string operation, string? operationId, int employeeId,
        bool anonymous, bool allowLive, HttpStatusCode expectedStatus, string? expectedTitle)
    {
        await fixture.ResetAsync();
        var permission = operation switch
        {
            "create" => AccountingPermissions.Create,
            "delete" => AccountingPermissions.Delete,
            "email" => AccountingPermissions.Update,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        using var client = await fixture.ReceiptClientAsync(anonymous ? null : [permission], allowLive);
        // This request must not add any outbound attempt to the shared host lifetime count.
        var outbound = fixture.ReceiptOutboundCalls;
        var before = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(6, before.Payment.Tables);
        Assert.Equal(5, before.Invoice.Tables);
        Assert.Equal(3, before.Receipt.Tables);
        // Shared Redis may retain earlier cache/memo keys; complete before/after equality below
        // requires zero changes to all retained keys and every hash field.
        Assert.Empty(fixture.LiveChecks);
        Assert.Equal(outbound, fixture.ReceiptOutboundCalls);

        using var request = new HttpRequestMessage(operation == "delete" ? HttpMethod.Delete : HttpMethod.Post,
            $"/invoices/{int.MaxValue}/receipt" + (operation == "email" ? "/email" : ""));
        if (operation == "create") request.Content = JsonContent.Create(new { Comment = "Synthetic receipt rejection", SendEmail = true });
        if (operation != "delete")
            request.Headers.Add("X-Legacy-Employee-Id", employeeId.ToString(CultureInfo.InvariantCulture));
        if (operationId is not null) request.Headers.Add("Idempotency-Key", operationId);
        using var response = await client.SendAsync(request);
        await fixture.AssertStatusAsync(response, expectedStatus);
        if (expectedTitle is not null)
        {
            using var wire = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var title = wire.RootElement.EnumerateObject().Single(property =>
                property.Name.Equals("title", StringComparison.OrdinalIgnoreCase)).Value.GetString();
            var status = wire.RootElement.EnumerateObject().Single(property =>
                property.Name.Equals("status", StringComparison.OrdinalIgnoreCase)).Value.GetInt32();
            Assert.True(StringComparer.Ordinal.Equals(expectedTitle, title), "Receipt rejection problem title must match its boundary.");
            Assert.Equal((int)expectedStatus, status);
        }

        if (anonymous) Assert.Empty(fixture.LiveChecks);
        else Assert.Equal(permission, Assert.Single(fixture.LiveChecks).Permission);
        var after = await fixture.ReceiptSnapshotAsync();
        Assert.True(before.Payment == after.Payment, "Receipt rejection changed complete Payment context row state.");
        Assert.True(before.Invoice == after.Invoice, "Receipt rejection changed complete Invoice context row state.");
        Assert.True(before.Receipt == after.Receipt, "Receipt rejection changed complete Receipt context row state.");
        Assert.True(before.Journal == after.Journal, "Receipt rejection changed Redis journal key or value state.");
        Assert.Equal(outbound, fixture.ReceiptOutboundCalls);
    }
}
