using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Billing;
using Legacy.Maliev.AccountingService.Api.Controllers.Billing;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class BillingRegisteredReadHttpTests(BillingLedgerFixture fixture) : IClassFixture<BillingLedgerFixture>
{
    [Fact]
    public async Task RegisteredReadAndPreviewUseRealLedgerAndKeepIssuedHistoryUnchanged()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var ledger = new BillingLedger(database, TimeProvider.System);
        var snapshot = new BillingSnapshot(17, 7, "legal-tax", "source-version", new string('a', 64),
            new(1000, 0, 1000, "THB"), [new(1, new(1000, 0, 1000, "THB"), "synthetic")], 2);
        var opened = await ledger.OpenAsync(snapshot, new(42, Guid.NewGuid(), 0), default);
        var recipient = new BillingRecipient(7, "legal-tax", "Head office", null, "Address");
        await ledger.ExecuteAsync(opened.AccountId, new(42, Guid.NewGuid(), 1),
            new IssueBillingStage(new(BillingStageKind.Deposit, 200, null, new(2026, 10, 30), recipient, [])), default);
        var before = JsonSerializer.Serialize(await ledger.GetAsync(opened.AccountId, default));
        using var rsa = RSA.Create(2048);
        await using var factory = new Factory(database.Database.GetConnectionString()!, rsa)
        { GrantedResource = $"/customers/7/billing/accounts/{opened.AccountId:D}" };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var now = DateTime.UtcNow;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken("https://billing-read.invalid", "billing-read", [new Claim("sub", "employee:42"),
                new Claim("identity_kind", "employee"), new Claim("permissions", "legacy.accounting.read")], now.AddSeconds(-5), now.AddMinutes(5),
                new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256))));
        var path = $"/v1/customers/7/billing/accounts/{opened.AccountId:D}";
        using var read = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
        var account = JsonSerializer.Deserialize<BillingAccountView>(await read.Content.ReadAsStringAsync());
        Assert.Equal(200, account!.Billed.Gross);
        Assert.Equal(800, account.Unbilled);
        using var preview = await client.PostAsJsonAsync(path + "/preview", new BillingPreviewRequest(BillingStageKind.Remaining,
            null, null, null, recipient, account.Revision));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var result = JsonSerializer.Deserialize<BillingStagePreview>(await preview.Content.ReadAsStringAsync());
        Assert.Equal(800, result!.Amount.Gross);
        Assert.Equal(before, JsonSerializer.Serialize(await ledger.GetAsync(opened.AccountId, default)));
        var other = await ledger.OpenAsync(snapshot with { QuotationId = 18 }, new(42, Guid.NewGuid(), 0), default);
        using var forbidden = await client.GetAsync($"/v1/customers/7/billing/accounts/{other.AccountId:D}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        factory.GrantedResource = $"/customers/8/billing/accounts/{opened.AccountId:D}";
        using var wrongCustomer = await client.GetAsync(path.Replace("/customers/7/", "/customers/8/", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, wrongCustomer.StatusCode);
        Assert.True(factory.LiveChecks >= 3);
    }

    private sealed class Factory(string connection, RSA rsa) : WebApplicationFactory<Program>
    {
        public int LiveChecks;
        public string? GrantedResource;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:Issuer", "https://billing-read.invalid");
            builder.UseSetting("Jwt:Audience", "billing-read");
            builder.UseSetting("Jwt:PublicKey", Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())));
            builder.UseSetting("Cache:RedisEnabled", "false");
            builder.UseSetting("Cache:AllowInMemoryFallback", "true");
            builder.UseSetting("Billing:ReadEnabled", "true");
            builder.UseSetting("Features:ResourceScopedAuthEnabled", "true");
            foreach (var context in new[] { "InvoiceDbContext", "PaymentDbContext", "ReceiptDbContext" })
                builder.UseSetting("ConnectionStrings:" + context, connection);
            builder.ConfigureTestServices(services =>
            {
                var iam = new Mock<IIamServiceClient>(MockBehavior.Strict);
                iam.Setup(value => value.CheckPermissionLiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .Returns((string subject, string permission, string? resource, CancellationToken _) =>
                    {
                        Interlocked.Increment(ref LiveChecks);
                        return Task.FromResult(subject == "employee:42" && permission == "legacy.accounting.read" && resource == GrantedResource);
                    });
                services.RemoveAll<IIamServiceClient>();
                services.AddSingleton(iam.Object);
                services.AddStagedBillingReadModel();
            });
        }
    }
}
