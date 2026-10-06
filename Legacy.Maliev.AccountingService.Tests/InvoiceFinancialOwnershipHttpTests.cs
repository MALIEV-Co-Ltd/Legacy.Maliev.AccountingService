using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Normal Accounting HTTP/JWT/live-permission pipeline with synthetic IAM grants and owned PostgreSQL.</summary>
[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class InvoiceFinancialOwnershipHttpTests(AccountingBoundaryHttpFixture fixture)
{
    private const string Permission = InvoiceFinancialOwnershipController.ReadPermission;

    [Theory]
    [InlineData("service:legacy-auth")]
    [InlineData("service:legacy-quotation")]
    public async Task ExactReaderReturnsNineFieldOwnershipWithoutMutatingAnyBoundary(string subject)
    {
        await fixture.ResetAsync();
        var receipt = await SeedAsync();
        using var client = await fixture.ReceiptActorClientAsync(subject, [Permission]);
        var before = await fixture.ReceiptSnapshotAsync();
        var outbound = fixture.ReceiptOutboundCalls;
        using var response = await client.GetAsync(Route(receipt.OperationId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(new[] { "ContractVersion", "EmployeeSubject", "FinancialBinding", "InvoiceId", "OperationId", "OriginIssuer", "OriginalQuotationVersion", "QuotationId", "RequesterSubject" },
            document.RootElement.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.Equal(receipt, JsonSerializer.Deserialize<InvoiceFinancialOwnership>(body));
        Assert.Contains((subject, Permission), fixture.LiveChecks);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(outbound, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("missing-grant")]
    [InlineData("live-revoked")]
    [InlineData("employee")]
    [InlineData("accounting")]
    [InlineData("intranet")]
    [InlineData("other-service")]
    [InlineData("missing-kind")]
    [InlineData("employee-kind")]
    [InlineData("duplicate-sub")]
    [InlineData("duplicate-kind")]
    [InlineData("role")]
    [InlineData("sid")]
    [InlineData("executor")]
    [InlineData("wildcard")]
    [InlineData("wrong-grant")]
    public async Task GranularGrantNeverSubstitutesForExactReaderIdentity(string scenario)
    {
        await fixture.ResetAsync();
        var receipt = await SeedAsync();
        var subject = scenario switch
        {
            "employee" => "employee:42",
            "accounting" => "service:legacy-accounting",
            "intranet" => "service:legacy-intranet",
            "other-service" => "service:untrusted",
            _ => "service:legacy-auth"
        };
        var kind = scenario switch { "missing-kind" => null, "employee-kind" or "employee" => "employee", _ => "service" };
        string[] permissions = scenario switch
        {
            "missing-grant" => [],
            "wrong-grant" => ["legacy-accounting.read"],
            "wildcard" => [Permission, "legacy.accounting.*"],
            _ => [Permission]
        };
        Claim[] extra = scenario switch
        {
            "duplicate-sub" => [new("sub", "service:legacy-quotation")],
            "duplicate-kind" => [new("identity_kind", "employee")],
            "role" => [new(ClaimTypes.Role, "Admin")],
            "sid" => [new("sid", Guid.NewGuid().ToString("D"))],
            "executor" => [new("executor", "service:legacy-accounting")],
            _ => []
        };
        using var client = scenario == "anonymous" ? await fixture.ReceiptClientAsync(null)
            : await fixture.ReceiptActorClientAsync(subject, permissions, scenario != "live-revoked", kind, extra);
        var before = await fixture.ReceiptSnapshotAsync();
        var outbound = fixture.ReceiptOutboundCalls;
        using var response = await client.GetAsync(Route(receipt.OperationId));
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(receipt.FinancialBinding, body, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.EmployeeSubject, body, StringComparison.Ordinal);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(outbound, fixture.ReceiptOutboundCalls);
    }

    private async Task<InvoiceFinancialOwnership> SeedAsync()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var scope = await fixture.ReceiptScopeAsync();
        var operation = Guid.NewGuid();
        var origin = new InvoiceNotificationOrigin("https://auth.example.invalid", "employee:42", "service:legacy-intranet");
        _ = await scope.ServiceProvider.GetRequiredService<InvoiceCreationAdmissionStore>().AdmitAsync(operation, 84, origin,
            new string('C', 64), budget.Token);
        _ = await scope.ServiceProvider.GetRequiredService<IInvoiceCreationStore>().CreateAsync(
            new Invoice { Number = "SYNTHETIC-HTTP-" + operation.ToString("N"), CustomerId = 42, Total = 12m },
            [new() { Description = "owned", Quantity = 3, UnitPrice = 4m }],
            new(operation, 84, origin, new DateTime(2026, 10, 6, 1, 2, 3, DateTimeKind.Unspecified)), budget.Token);
        return await scope.ServiceProvider.GetRequiredService<InvoiceFinancialOwnershipStore>().ReadAsync(operation, budget.Token);
    }

    private static string Route(Guid operation) => $"/internal/invoice-creation/operations/{operation:D}/financial-ownership";
}
