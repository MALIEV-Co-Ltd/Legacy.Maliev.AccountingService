using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Independently signed versioned proof negatives; no bearer is stored or logged.</summary>
public sealed class InvoiceCompletionCapabilityVerifierTests : IDisposable
{
    private readonly RSA rsa = RSA.Create(2048);
    private readonly DateTimeOffset now = new(2030, 7, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly InvoiceFinancialOwnership receipt = new(1, Guid.Parse("4f7870e2-d349-41bb-b4cf-567450f261e9"), 84, 901,
        "legacy-auth", "employee:42", "service:legacy-intranet", "2030-07-18T00:00:00.0000000Z", new string('A', 64));

    [Fact]
    public void ExactBoundProofValidatesWithoutDependingOnWallClockOrPersistingBearer() =>
        Assert.True(Verifier().Verify(Sign(Payload()), Caller(), receipt));

    [Theory]
    [InlineData("iss")]
    [InlineData("aud")]
    [InlineData("sub")]
    [InlineData("jti")]
    [InlineData("azp")]
    [InlineData("executor")]
    [InlineData("scope")]
    [InlineData("quotation_id")]
    [InlineData("operation_id")]
    [InlineData("invoice_id")]
    [InlineData("quotation_version")]
    [InlineData("financial_binding")]
    [InlineData("financial_binding_version")]
    public void EveryStringBindingIsRequiredAndExact(string field)
    {
        var payload = Payload();
        payload[field] = "wrong";
        Assert.False(Verifier().Verify(Sign(payload), Caller(), receipt));
        payload.Remove(field);
        Assert.False(Verifier().Verify(Sign(payload), Caller(), receipt));
    }

    [Theory]
    [InlineData("iat")]
    [InlineData("nbf")]
    [InlineData("exp")]
    public void NumericTimeFieldsRequireActualJsonIntegers(string field)
    {
        var payload = Payload();
        payload[field] = payload[field].ToString()!;
        Assert.False(Verifier().Verify(Sign(payload), Caller(), receipt));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("overlong")]
    [InlineData("future")]
    [InlineData("nbf-before-iat")]
    [InlineData("nbf-after-iat")]
    public void FiniteLifetimeHasNoExpiryGraceOrFutureAuthority(string field)
    {
        var payload = Payload();
        var seconds = now.ToUnixTimeSeconds();
        switch (field)
        {
            case "expired": payload["iat"] = seconds - 120; payload["nbf"] = seconds - 120; payload["exp"] = seconds; break;
            case "overlong": payload["exp"] = seconds + 121; break;
            case "future": payload["iat"] = seconds + 1; payload["nbf"] = seconds + 1; break;
            case "nbf-before-iat": payload["nbf"] = seconds - 1; break;
            case "nbf-after-iat": payload["nbf"] = seconds + 1; break;
        }
        Assert.False(Verifier().Verify(Sign(payload), Caller(), receipt));
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("accounting")]
    [InlineData("duplicate-sub")]
    [InlineData("duplicate-kind")]
    [InlineData("sid")]
    [InlineData("executor")]
    public void OrdinaryCallerCannotAcquireEmployeeAuthorityFromSignedProof(string field)
    {
        var claims = new List<Claim> { new("sub", "service:legacy-intranet"), new("identity_kind", "service") };
        switch (field)
        {
            case "customer": claims[1] = new("identity_kind", "customer"); break;
            case "accounting": claims[0] = new("sub", "service:legacy-accounting"); break;
            case "duplicate-sub": claims.Add(new("sub", "service:legacy-intranet")); break;
            case "duplicate-kind": claims.Add(new("identity_kind", "service")); break;
            case "sid": claims.Add(new("sid", Guid.NewGuid().ToString("D"))); break;
            case "executor": claims.Add(new("executor", "service:legacy-accounting")); break;
        }
        Assert.False(Verifier().Verify(Sign(Payload()), new(new ClaimsIdentity(claims, "test")), receipt));
    }

    [Fact]
    public void ExistingUnboundCapabilityAndWrongTypedBindingCannotAuthorizeFinancialCompletion()
    {
        var payload = Payload();
        payload.Remove("invoice_id"); payload.Remove("quotation_version"); payload.Remove("financial_binding"); payload.Remove("financial_binding_version");
        Assert.False(Verifier().Verify(Sign(payload), Caller(), receipt));
        payload = Payload(); payload["invoice_id"] = 901;
        Assert.False(Verifier().Verify(Sign(payload), Caller(), receipt));
        payload = Payload(); payload["aud"] = new[] { InvoiceCompletionCapabilityVerifier.Audience };
        Assert.False(Verifier().Verify(Sign(payload), Caller(), receipt));
    }

    [Fact]
    public void DuplicateUnknownAndForgedPayloadsAreRejectedAfterSignatureValidation()
    {
        var json = JsonSerializer.Serialize(Payload());
        var duplicate = json[..^1] + ",\"invoice_id\":\"901\"}";
        Assert.False(Verifier().Verify(SignJson(duplicate, rsa), Caller(), receipt));
        var payload = Payload(); payload["roles"] = "Employee";
        Assert.False(Verifier().Verify(Sign(payload), Caller(), receipt));
        using var foreign = RSA.Create(2048);
        Assert.False(Verifier().Verify(SignJson(json, foreign), Caller(), receipt));
    }

    private Dictionary<string, object> Payload() => new()
    {
        ["iss"] = receipt.OriginIssuer, ["aud"] = InvoiceCompletionCapabilityVerifier.Audience,
        ["sub"] = receipt.EmployeeSubject, ["jti"] = Guid.NewGuid().ToString("D"),
        ["iat"] = now.ToUnixTimeSeconds(), ["nbf"] = now.ToUnixTimeSeconds(), ["exp"] = now.ToUnixTimeSeconds() + 120,
        ["azp"] = receipt.RequesterSubject, ["executor"] = "service:legacy-accounting", ["scope"] = InvoiceCompletionCapabilityVerifier.Scope,
        ["quotation_id"] = "84", ["operation_id"] = receipt.OperationId.ToString("D"), ["invoice_id"] = "901",
        ["quotation_version"] = receipt.OriginalQuotationVersion, ["financial_binding"] = receipt.FinancialBinding,
        ["financial_binding_version"] = InvoiceCompletionCapabilityVerifier.BindingVersion,
    };
    private string Sign(Dictionary<string, object> payload) => SignJson(JsonSerializer.Serialize(payload), rsa);
    private static string SignJson(string json, RSA key)
    {
        var header = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        var payload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
        var input = header + "." + payload;
        return input + "." + Base64UrlEncoder.Encode(key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
    private InvoiceCompletionCapabilityVerifier Verifier() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Issuer"] = receipt.OriginIssuer,
        ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
    }).Build(), new FakeTimeProvider(now));
    private static ClaimsPrincipal Caller() => new(new ClaimsIdentity([new Claim("sub", "service:legacy-intranet"), new Claim("identity_kind", "service")], "test"));
    public void Dispose() => rsa.Dispose();
}
