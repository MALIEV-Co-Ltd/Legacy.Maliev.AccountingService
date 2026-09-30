using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceDelegationChainHttpTests(InvoiceDelegationChainFixture fixture) : IClassFixture<InvoiceDelegationChainFixture>
{
    [Fact]
    public async Task ActualAuthWire_ProductionAccountingAcceptsAndDurablyReplaysRenewedJti()
    {
        var operation = Guid.NewGuid();
        var first = await fixture.Exchange(operation);
        Assert.Equal(new[] { "accessToken", "expiresIn", "tokenType" }, first.EnumerateObject().Select(value => value.Name).Order().ToArray());
        Assert.Equal("Bearer", first.GetProperty("tokenType").GetString());
        Assert.Equal(120, first.GetProperty("expiresIn").GetInt32());
        var jwt = Read(first);
        Assert.Equal(SecurityAlgorithms.RsaSha256, jwt.Header.Alg);
        Assert.Equal(InvoiceDelegationChainFixture.Issuer, jwt.Issuer);
        Assert.Equal([InvoiceCreationDelegationVerifier.Audience], jwt.Audiences);
        Assert.Equal("employee:42", One(jwt, "sub"));
        Assert.Equal(InvoiceDelegationChainFixture.Service, One(jwt, "azp"));
        Assert.Equal(InvoiceDelegationChainFixture.Permission, One(jwt, "scope"));
        Assert.Equal("84", One(jwt, "quotation_id"));
        Assert.Equal(operation.ToString("D"), One(jwt, "operation_id"));
        Assert.Equal(Guid.Parse(One(jwt, "jti")).ToString("D"), One(jwt, "jti"));
        Assert.NotEqual(Guid.Empty, Guid.Parse(One(jwt, "jti")));
        Assert.Equal(120, long.Parse(One(jwt, "exp")) - long.Parse(One(jwt, "iat")));
        Assert.Equal(One(jwt, "iat"), One(jwt, "nbf"));
        foreach (var name in new[] { "iat", "nbf", "exp" })
            Assert.Equal(JsonValueKind.Number, JsonDocument.Parse(Base64UrlEncoder.Decode(jwt.EncodedPayload)).RootElement.GetProperty(name).ValueKind);
        var before = fixture.Effects;
        using var created = await Send(operation, Token(first));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("Production", fixture.Accounting.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        var validation = fixture.Accounting.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        Assert.True(validation.ValidateIssuer);
        Assert.True(validation.ValidateAudience);
        Assert.True(validation.ValidateLifetime);
        Assert.True(validation.ValidateIssuerSigningKey);
        Assert.Null(validation.SignatureValidator);
        Assert.Equal([SecurityAlgorithms.RsaSha256], validation.ValidAlgorithms);
        var renewed = await fixture.Exchange(operation);
        Assert.NotEqual(One(jwt, "jti"), One(Read(renewed), "jti"));
        using var replay = await Send(operation, Token(renewed));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(before + 1, fixture.Effects);
        await using var database = fixture.Database();
        var admission = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == operation);
        Assert.Equal("Completed", admission.State);
        Assert.Equal("employee:42", admission.EmployeeSubject);
        Assert.Equal(InvoiceDelegationChainFixture.Service, admission.ServiceSubject);
        Assert.Equal(84, admission.QuotationId);
        Assert.Equal(64, admission.IntentFingerprint.Length);
        Assert.Contains((InvoiceDelegationChainFixture.Service, InvoiceDelegationChainFixture.Permission), fixture.LiveChecks);
    }

    [Fact]
    public async Task LiveIamDenial_OverridesValidPermissionJwtBeforeAdmissionOrEffects()
    {
        var operation = Guid.NewGuid();
        var issued = await fixture.Exchange(operation);
        var before = fixture.Effects;
        var checks = fixture.LiveChecks.Count;
        fixture.AllowLive = false;
        try
        {
            using var response = await Send(operation, Token(issued));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(before, fixture.Effects);
            Assert.True(fixture.LiveChecks.Count > checks);
            await AssertAbsent(operation);
        }
        finally { fixture.AllowLive = true; }
    }

    [Theory]
    [InlineData("quotation")]
    [InlineData("operation")]
    [InlineData("noncanonical-key")]
    [InlineData("wrong-service")]
    [InlineData("forged")]
    [InlineData("wrong-algorithm")]
    [InlineData("future-nbf")]
    [InlineData("ambiguous-sub")]
    [InlineData("ambiguous-scope")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    [InlineData("overlong-lifetime")]
    public async Task InvalidDelegation_ProductionHttpRejectsBeforeAdmissionAndEffects(string mutation)
    {
        var operation = Guid.NewGuid();
        var issued = await fixture.Exchange(operation);
        var token = Token(issued);
        var quotation = mutation == "quotation" ? 85 : 84;
        var key = mutation == "operation" ? Guid.NewGuid().ToString("D") : operation.ToString("D");
        if (mutation == "noncanonical-key") key = operation.ToString("N");
        var service = mutation == "wrong-service" ? "service:other" : InvoiceDelegationChainFixture.Service;
        if (mutation is "forged" or "wrong-algorithm" or "future-nbf" or "ambiguous-sub" or "ambiguous-scope" or "wrong-audience" or "expired" or "overlong-lifetime")
        {
            var jwt = Read(issued);
            var claims = jwt.Claims.Where(value => value.Type is not "iss" and not "aud" and not "exp" and not "nbf").ToList();
            if (mutation == "ambiguous-sub") claims.Add(new Claim("sub", "employee:99"));
            if (mutation == "ambiguous-scope") claims.Add(new Claim("scope", "other"));
            var nbf = mutation == "future-nbf" ? DateTime.UtcNow.AddMinutes(2) : jwt.ValidFrom;
            if (mutation == "expired")
            {
                nbf = DateTime.UtcNow.AddMinutes(-5);
                claims.RemoveAll(value => value.Type == "iat");
                claims.Add(new Claim("iat", new DateTimeOffset(nbf).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
            }
            using var forged = RSA.Create(2048);
            token = fixture.Sign(claims, mutation == "wrong-audience" ? "other" : InvoiceCreationDelegationVerifier.Audience,
                nbf, nbf.AddSeconds(mutation == "overlong-lifetime" ? 121 : 120),
                mutation == "wrong-algorithm" ? SecurityAlgorithms.RsaSha384 : SecurityAlgorithms.RsaSha256,
                mutation == "forged" ? forged : null);
        }
        var before = fixture.Effects;
        using var response = await Send(operation, token, quotation, key, service);
        Assert.Equal(mutation == "wrong-service" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, fixture.Effects);
        await AssertAbsent(operation);
        await AssertAbsent(Guid.Parse(key));
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("quotation")]
    [InlineData("body")]
    public async Task DurableIntentConflict_WithValidRenewedIssuerTokenNeverRepeatsEffects(string change)
    {
        var operation = Guid.NewGuid();
        using var first = await Send(operation, Token(await fixture.Exchange(operation)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var before = fixture.Effects;
        var quotation = change == "quotation" ? 85 : 84;
        var issued = await fixture.Exchange(operation, change == "actor" ? "employee:99" : "employee:42", quotation);
        using var response = await Send(operation, Token(issued), quotation, intent: InvoiceDelegationChainFixture.Intent(change == "body" ? "changed" : "INV-chain"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, fixture.Effects);
        await using var database = fixture.Database();
        var row = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == operation);
        Assert.Equal("employee:42", row.EmployeeSubject);
        Assert.Equal(84, row.QuotationId);
        Assert.Equal("Completed", row.State);
    }

    private async Task<HttpResponseMessage> Send(Guid operation, string token, int quotation = 84, string? key = null,
        string service = InvoiceDelegationChainFixture.Service, CreateInvoiceFromQuotationRequest? intent = null)
    {
        using var client = fixture.Accounting.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/invoices/from-quotation/{quotation}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Ordinary(service, "service", InvoiceDelegationChainFixture.Permission));
        request.Headers.Add("Idempotency-Key", key ?? operation.ToString("D"));
        request.Headers.Add(InvoiceCreationDelegationVerifier.HeaderName, "Bearer " + token);
        request.Content = JsonContent.Create(intent ?? InvoiceDelegationChainFixture.Intent());
        return await client.SendAsync(request);
    }

    private async Task AssertAbsent(Guid operation)
    {
        await using var database = fixture.Database();
        Assert.False(await database.InvoiceCreationAdmissions.AnyAsync(value => value.OperationId == operation));
    }

    private static string Token(JsonElement response) => response.GetProperty("accessToken").GetString()!;
    private static JwtSecurityToken Read(JsonElement response) => new JwtSecurityTokenHandler().ReadJwtToken(Token(response));
    private static string One(JwtSecurityToken jwt, string name) => Assert.Single(jwt.Claims, value => value.Type == name).Value;
}
