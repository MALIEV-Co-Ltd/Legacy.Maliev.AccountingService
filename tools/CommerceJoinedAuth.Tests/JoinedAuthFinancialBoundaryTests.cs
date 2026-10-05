using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.QuotationService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.JoinedAuth.Tests;

[Collection("joined-auth")]
public sealed class JoinedAuthFinancialBoundaryTests(AccountingQuotationBaselineFixture fixture)
{
    [Fact]
    public async Task RealAuthDelegation_PreservesInvoiceAndReconciliationFenceAtProtectedQuotation409()
    {
        fixture.ResetObservations();
        var row = await fixture.Quotation.SeedAsync();
        await using (var quotations = fixture.Quotation.Context())
        {
            quotations.OrderItems.Add(new QuotationOrderItem { QuotationId = row.Id, Description = "Synthetic delegated part", Quantity = 1, UnitPrice = 100m });
            await quotations.SaveChangesAsync();
        }
        var quotationBefore = await fixture.QuotationScalarSnapshotAsync(row.Id);
        var operation = Guid.NewGuid();
        var issued = await fixture.Auth.ExchangeAsync(row.Id, operation);
        Assert.Equal(HttpStatusCode.OK, issued.Status);
        Assert.NotNull(issued.Token);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);
        Assert.Equal("employee:42", jwt.Subject);
        Assert.Equal("legacy-accounting:invoice-create", Assert.Single(jwt.Audiences));
        Assert.Equal(operation.ToString("D"), Assert.Single(jwt.Claims, value => value.Type == "operation_id").Value);
        Assert.Equal(row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(jwt.Claims, value => value.Type == "quotation_id").Value);
        var number = $"AUTH-JOINED-{Guid.NewGuid():N}";
        using var first = await SendAsync(row.Id, operation, issued.Token!, number);
        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.True(fixture.AccountingLiveChecks > 0);
        Assert.Contains(fixture.Trace, call => call == $"PUT /quotations/{row.Id} =>409");
        Assert.DoesNotContain(fixture.Trace, call => call.Contains("/decision", StringComparison.Ordinal));
        await using var invoices = fixture.InvoiceDatabase();
        var invoice = await invoices.Invoices.AsNoTracking().SingleAsync(value => value.Number == number);
        Assert.True(invoice.Id > 0);
        Assert.Equal(107m, invoice.Total);
        Assert.Single(await invoices.Items.Where(value => value.InvoiceId == invoice.Id).ToArrayAsync());
        var admission = await invoices.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == operation);
        Assert.Equal("NeedsReconciliation", admission.State);
        Assert.Equal("employee:42", admission.EmployeeSubject);
        Assert.Equal("service:legacy-intranet", admission.ServiceSubject);
        Assert.Equal(row.Id, admission.QuotationId);
        Assert.Equal(64, admission.IntentFingerprint.Length);
        var requestsBeforeReplay = fixture.Trace.Count;
        var renewed = await fixture.Auth.ExchangeAsync(row.Id, operation);
        Assert.Equal(HttpStatusCode.OK, renewed.Status);
        Assert.NotNull(renewed.Token);
        var renewedJwt = new JwtSecurityTokenHandler().ReadJwtToken(renewed.Token);
        Assert.NotEqual(jwt.Id, renewedJwt.Id);
        using var replay = await SendAsync(row.Id, operation, renewed.Token!, number);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(requestsBeforeReplay, fixture.Trace.Count);
        Assert.Single(await invoices.Invoices.AsNoTracking().Where(value => value.Number == number).ToArrayAsync());
        var replayAdmission = await invoices.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == operation);
        Assert.Equal("NeedsReconciliation", replayAdmission.State);
        Assert.Equal(admission.EmployeeSubject, replayAdmission.EmployeeSubject);
        Assert.Equal(admission.ServiceSubject, replayAdmission.ServiceSubject);
        Assert.Equal(admission.QuotationId, replayAdmission.QuotationId);
        Assert.Equal(admission.IntentFingerprint, replayAdmission.IntentFingerprint);
        Assert.Null(replayAdmission.ResultJson);
        Assert.Equal(quotationBefore, await fixture.QuotationScalarSnapshotAsync(row.Id));
        await using var after = fixture.Quotation.Context();
        Assert.Empty(await after.AcceptedOutcomes.Where(value => value.QuotationId == row.Id).ToArrayAsync());
        Assert.Empty(await after.GoogleAnalyticsOutbox.Where(value => value.QuotationId == row.Id).ToArrayAsync());
        fixture.AssertSuccessfulQuotationIam(row.Id);
        AssertAuthAndProviderProof();
    }

    [Theory]
    [InlineData("quotation")]
    [InlineData("operation")]
    public async Task AuthIssuedDelegation_CannotAuthorizeAnotherFinancialCommand(string mismatch)
    {
        fixture.ResetObservations();
        var row = await fixture.Quotation.SeedAsync();
        var operation = Guid.NewGuid();
        var issued = await fixture.Auth.ExchangeAsync(row.Id, operation);
        Assert.Equal(HttpStatusCode.OK, issued.Status);
        var number = $"AUTH-REJECT-{Guid.NewGuid():N}";
        var sentOperation = mismatch == "operation" ? Guid.NewGuid() : operation;
        using var response = await SendAsync(mismatch == "quotation" ? row.Id + 1 : row.Id, sentOperation, issued.Token!, number);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(fixture.AccountingLiveChecks > 0);
        await using var invoices = fixture.InvoiceDatabase();
        Assert.False(await invoices.Invoices.AnyAsync(value => value.Number == number));
        Assert.False(await invoices.InvoiceCreationAdmissions.AnyAsync(value => value.OperationId == operation || value.OperationId == sentOperation));
        Assert.Empty(fixture.Trace);
        AssertAuthAndProviderProof();
    }

    [Fact]
    public async Task RevokedRealEmployeeSession_CannotIssueDelegationOrReachFinancialCommand()
    {
        fixture.ResetObservations();
        var row = await fixture.Quotation.SeedAsync();
        var sessionId = Guid.Parse(Assert.Single(new JwtSecurityTokenHandler().ReadJwtToken(fixture.Auth.EmployeeToken).Claims, value => value.Type == "sid").Value);
        await using var state = fixture.Auth.State();
        var session = await state.RefreshSessions.SingleAsync(value => value.Id == sessionId);
        var previous = session.RevokedAt;
        try
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
            await state.SaveChangesAsync();
            var operation = Guid.NewGuid();
            var issued = await fixture.Auth.ExchangeAsync(row.Id, operation);
            Assert.Equal(HttpStatusCode.Unauthorized, issued.Status);
            Assert.Null(issued.Token);
            await using var invoices = fixture.InvoiceDatabase();
            Assert.False(await invoices.InvoiceCreationAdmissions.AnyAsync(value => value.OperationId == operation));
            Assert.Empty(fixture.Trace);
            AssertAuthAndProviderProof();
        }
        finally { session.RevokedAt = previous; await state.SaveChangesAsync(); }
    }

    private async Task<HttpResponseMessage> SendAsync(int quotation, Guid operation, string delegation, string number)
    {
        using var client = fixture.Accounting.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/invoices/from-quotation/{quotation}");
        request.Headers.Authorization = new("Bearer", fixture.Auth.IntranetToken);
        request.Headers.Add("Idempotency-Key", operation.ToString("D"));
        request.Headers.Add("X-Maliev-Employee-Delegation", "Bearer " + delegation);
        request.Content = JsonContent.Create(new
        {
            InvoiceNumber = number, Comment = "Synthetic joined Auth financial boundary", SendEmail = false,
            BillingAddress = new { Recipient = "Synthetic Thai customer", Line1 = "Synthetic address", Country = "Thailand" },
            ShippingAddress = new { Recipient = "Synthetic Thai customer", Line1 = "Synthetic address", Country = "Thailand" },
        });
        return await client.SendAsync(request);
    }

    private void AssertAuthAndProviderProof()
    {
        Assert.Contains(fixture.Auth.Responses, response => response.Path == "/auth/v1/service/login" && response.Status == 200);
        Assert.Contains(fixture.Auth.Responses, response => response.Path == "/auth/v1/login" && response.Status == 200);
        Assert.True(fixture.Auth.SuccessfulIamResponses > 0);
        Assert.Equal(0, fixture.Auth.IamTransportFailures);
        Assert.Equal(0, fixture.Auth.UnmatchedTransportCalls);
        Assert.Equal(0, fixture.ExternalProviderCalls);
        Assert.Equal(0, fixture.QuotationBoundary.UnmatchedTransportCalls);
    }
}
