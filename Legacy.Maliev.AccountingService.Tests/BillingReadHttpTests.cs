using System.Net;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Tests.Fixtures;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class BillingReadHttpTests(AccountingBoundaryHttpFixture fixture)
{
    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("missing", HttpStatusCode.Forbidden)]
    [InlineData("revoked", HttpStatusCode.Forbidden)]
    [InlineData("granted", HttpStatusCode.ServiceUnavailable)]
    public async Task NormalJwtLivePermissionAndDefaultDisabledModuleRefuseBeforeFinancialMutation(string scenario, HttpStatusCode expected)
    {
        await fixture.ResetAsync();
        using var client = scenario == "anonymous" ? await fixture.ReceiptClientAsync(null)
            : await fixture.ReceiptActorClientAsync("employee:42", scenario == "missing" ? [] : [AccountingPermissions.Read],
                scenario != "revoked", "employee");
        var before = await fixture.ReceiptSnapshotAsync();
        var outbound = fixture.ReceiptOutboundCalls;
        using var response = await client.GetAsync($"/v1/customers/7/billing/accounts/{Guid.NewGuid():D}");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(outbound, fixture.ReceiptOutboundCalls);
        if (scenario == "granted") Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
