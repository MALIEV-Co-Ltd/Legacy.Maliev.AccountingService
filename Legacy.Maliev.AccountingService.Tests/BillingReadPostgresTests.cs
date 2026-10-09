using Legacy.Maliev.AccountingService.Api.Controllers.Billing;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class BillingReadPostgresTests(BillingLedgerFixture fixture) : IClassFixture<BillingLedgerFixture>
{
    [Theory]
    [InlineData("deposit", 200, 200)]
    [InlineData("percentage", 25, 250)]
    [InlineData("remaining", 0, 1000)]
    public async Task PreviewUsesPersistedCapWithoutIssuingStageOrAdvancingRevision(string kind, decimal input, decimal expected)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var ledger = new BillingLedger(database, TimeProvider.System);
        var snapshot = new BillingSnapshot(17, 7, "legal-tax", "source-version", new string('a', 64),
            new(1000, 0, 1000, "THB"), [new(1, new(1000, 0, 1000, "THB"), "synthetic")], 2);
        var opened = await ledger.OpenAsync(snapshot, new(42, Guid.NewGuid(), 0), default);
        var before = await ledger.GetAsync(opened.AccountId, default);
        var controller = Controller(ledger);
        var request = new BillingPreviewRequest(kind == "remaining" ? BillingStageKind.Remaining : BillingStageKind.Deposit,
            kind == "deposit" ? input : null, kind == "percentage" ? input : null, new(2026, 10, 30),
            new(7, "legal-tax", "Head office", "00000", "Confirmed address"), before!.Revision);
        var result = await controller.PreviewAsync(7, opened.AccountId, request, default);
        var preview = Assert.IsType<BillingStagePreview>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(expected, preview.Amount.Gross);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await ledger.GetAsync(opened.AccountId, default)));
        Assert.DoesNotContain(preview.Portions, line => line.Amount.Gross <= 0);
    }

    [Theory]
    [InlineData("customer", 404)]
    [InlineData("revision", 409)]
    [InlineData("debtor", 400)]
    [InlineData("both", 400)]
    [InlineData("cap", 400)]
    public async Task WrongCustomerOrStaleOrInvalidPreviewCannotChangeLedger(string scenario, int status)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var ledger = new BillingLedger(database, TimeProvider.System);
        var snapshot = new BillingSnapshot(17, 7, "legal-tax", "source-version", new string('a', 64),
            new(1000, 0, 1000, "THB"), [new(1, new(1000, 0, 1000, "THB"), "synthetic")], 2);
        var opened = await ledger.OpenAsync(snapshot, new(42, Guid.NewGuid(), 0), default);
        var before = await ledger.GetAsync(opened.AccountId, default);
        var request = new BillingPreviewRequest(BillingStageKind.Deposit, scenario == "cap" ? 1001 : 200,
            scenario == "both" ? 20 : null, null, new(7, scenario == "debtor" ? "other" : "legal-tax", "Office", null, "Address"),
            before!.Revision + (scenario == "revision" ? 1 : 0));
        var result = await Controller(ledger).PreviewAsync(scenario == "customer" ? 8 : 7, opened.AccountId, request, default);
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result.Result).StatusCode);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await ledger.GetAsync(opened.AccountId, default)));
    }

    private static BillingAccountsController Controller(BillingLedger ledger) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Billing:ReadEnabled"] = "true", ["Features:ResourceScopedAuthEnabled"] = "true" }).Build(), ledger)
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task EnabledReadModuleWithoutResourceScopeStillRefusesFinancialRead(string? scoped)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var ledger = new BillingLedger(database, TimeProvider.System);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Billing:ReadEnabled"] = "true", ["Features:ResourceScopedAuthEnabled"] = scoped }).Build();
        var controller = new BillingAccountsController(configuration, ledger)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = await controller.ReadAsync(7, Guid.NewGuid(), default);
        Assert.Equal(503, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
    }
}
