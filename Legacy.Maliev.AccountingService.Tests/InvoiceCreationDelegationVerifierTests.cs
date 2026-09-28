using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceCreationDelegationVerifierTests : IDisposable
{
    private readonly RSA rsa = RSA.Create(2048);
    private readonly Guid operationId = Guid.NewGuid();
    private readonly DateTimeOffset now = DateTimeOffset.UtcNow;

    [Fact]
    public void Valid_AuthContract_BindsEmployeeServiceRouteAndOperation()
    {
        Assert.Equal("employee:42", Verifier().Verify(Header(Token()), Service(), 84, operationId));
        Assert.Null(Verifier().Verify(null, Service(), 84, operationId));
    }

    [Fact]
    public void WrongAudienceScopeActorRouteOrOperation_IsRejected()
    {
        Assert.Null(Verifier().Verify(Header(Token(audience: "legacy-accounting")), Service(), 84, operationId));
        Assert.Null(Verifier().Verify(Header(Token(scope: "legacy.accounting.read")), Service(), 84, operationId));
        Assert.Null(Verifier().Verify(Header(Token()), Service("service:other"), 84, operationId));
        Assert.Null(Verifier().Verify(Header(Token()), Service(identityKind: "employee"), 84, operationId));
        Assert.Null(Verifier().Verify(Header(Token()), Service(), 85, operationId));
        Assert.Null(Verifier().Verify(Header(Token()), Service(), 84, Guid.NewGuid()));
        Assert.Null(Verifier().Verify(Header(Token(azp: "service:other")), Service(), 84, operationId));
    }

    [Fact]
    public void ExpiredOverlongOrForgedDelegation_IsRejected()
    {
        Assert.Null(Verifier().Verify(Header(Token(issuedAt: now.AddMinutes(-5), expires: now.AddMinutes(-3))), Service(), 84, operationId));
        Assert.Null(Verifier().Verify(Header(Token(expires: now.AddMinutes(3))), Service(), 84, operationId));
        using var other = RSA.Create(2048);
        Assert.Null(Verifier().Verify(Header(Token(signingKey: other)), Service(), 84, operationId));
        Assert.Null(Verifier().Verify("Bearer not-a-jwt", Service(), 84, operationId));
    }

    [Fact]
    public void RenewedDelegationForSameOperation_ValidatesWithoutBindingJti()
    {
        var first = Token();
        var second = Token();
        Assert.NotEqual(first, second);
        Assert.Equal("employee:42", Verifier().Verify(Header(first), Service(), 84, operationId));
        Assert.Equal("employee:42", Verifier().Verify(Header(second), Service(), 84, operationId));
    }

    private InvoiceCreationDelegationVerifier Verifier() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "legacy-auth",
            ["Jwt:PublicKey"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
        }).Build(), TimeProvider.System);

    private string Token(string audience = InvoiceCreationDelegationVerifier.Audience,
        string scope = InvoiceCreationDelegationVerifier.Scope,
        string azp = InvoiceCreationDelegationVerifier.IntranetSubject,
        DateTimeOffset? issuedAt = null, DateTimeOffset? expires = null, RSA? signingKey = null)
    {
        var issued = issuedAt ?? now;
        var token = new JwtSecurityToken("legacy-auth", audience,
        [
            new(JwtRegisteredClaimNames.Sub, "employee:42"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new(JwtRegisteredClaimNames.Iat, issued.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("azp", azp), new("scope", scope), new("quotation_id", "84"),
            new("operation_id", operationId.ToString("D")),
        ], issued.UtcDateTime, (expires ?? issued.AddSeconds(120)).UtcDateTime,
        new SigningCredentials(new RsaSecurityKey(signingKey ?? rsa), SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string Header(string token) => "Bearer " + token;
    private static ClaimsPrincipal Service(string subject = InvoiceCreationDelegationVerifier.IntranetSubject, string identityKind = "service") =>
        new(new ClaimsIdentity([new Claim("sub", subject), new Claim("identity_kind", identityKind)], "test"));

    public void Dispose() => rsa.Dispose();
}
