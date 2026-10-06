using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Normal Accounting HTTP/JWT/live-IAM and actual retained rows; controlled signed proof is not real Auth mint acceptance.</summary>
[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class InvoiceEmployeeCompletionAuthorityHttpTests(AccountingBoundaryHttpFixture fixture)
{
    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("unbound")]
    [InlineData("wrong-binding")]
    [InlineData("wrong-invoice")]
    [InlineData("prefixed")]
    [InlineData("wrong-caller")]
    [InlineData("live-revoked")]
    [InlineData("changed-intent")]
    [InlineData("unknown-operation")]
    public async Task UnverifiedCompletionCannotWriteAnyPhaseOrReachQuotation(string scenario)
    {
        await fixture.ResetAsync();
        var request = Request();
        await using var scope = await fixture.ReceiptScopeAsync();
        var operation = Guid.NewGuid();
        var origin = new InvoiceNotificationOrigin(fixture.AuthorityIssuer, "employee:42", "service:legacy-intranet");
        _ = await scope.ServiceProvider.GetRequiredService<InvoiceCreationAdmissionStore>().AdmitAsync(operation, 84, origin,
            InvoiceCreationAdmissionStore.Fingerprint(request), CancellationToken.None);
        _ = await scope.ServiceProvider.GetRequiredService<IInvoiceCreationStore>().CreateAsync(
            new Invoice { Number = "AUTHORITY-" + operation.ToString("N"), CustomerId = 42, Total = 12m },
            [new() { Description = "owned", Quantity = 3, UnitPrice = 4m }],
            new(operation, 84, origin, new DateTime(2026, 10, 6, 1, 2, 3, DateTimeKind.Unspecified)), CancellationToken.None);
        var ownership = await scope.ServiceProvider.GetRequiredService<InvoiceFinancialOwnershipStore>().ReadAsync(operation, CancellationToken.None);
        var proofOwnership = scenario switch
        {
            "wrong-binding" => ownership with { FinancialBinding = new string('B', 64) },
            "wrong-invoice" => ownership with { InvoiceId = ownership.InvoiceId + 1 },
            _ => ownership
        };
        var proof = fixture.ReceiptCompletionCapability(proofOwnership, scenario == "expired" ? -180 : 0, scenario == "unbound");
        if (scenario == "prefixed") proof = "Bearer " + proof;
        using var client = await fixture.ReceiptActorClientAsync(scenario == "wrong-caller" ? "service:legacy-accounting" : "service:legacy-intranet",
            [AccountingPermissions.Create], scenario != "live-revoked");
        using var message = new HttpRequestMessage(HttpMethod.Post, "/invoices/from-quotation/84/complete")
        { Content = JsonContent.Create(scenario == "changed-intent" ? request with { SendEmail = true } : request) };
        message.Headers.Add("Idempotency-Key", (scenario == "unknown-operation" ? Guid.NewGuid() : operation).ToString("D"));
        if (scenario != "missing") message.Headers.Add(InvoiceCompletionCapabilityVerifier.HeaderName, proof);
        var before = await fixture.ReceiptSnapshotAsync();
        var outbound = fixture.ReceiptOutboundCalls;
        using var response = await client.SendAsync(message);
        var expected = scenario switch
        {
            "live-revoked" => HttpStatusCode.Forbidden,
            "changed-intent" or "unknown-operation" => HttpStatusCode.Conflict,
            _ => HttpStatusCode.Unauthorized
        };
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(outbound, fixture.ReceiptOutboundCalls);
        Assert.DoesNotContain(proof, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
    private static CreateInvoiceFromQuotationRequest Request() => new("synthetic-original", null, null, null, null, null, null,
        new(null, null, null, null, null, null, null, null, null), new(null, null, null, null, null, null, null, null, null), null, null, false, false);
}
