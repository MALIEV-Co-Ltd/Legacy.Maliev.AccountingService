using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Api.Authorization;

/// <summary>Validates an AuthService invoice-create delegation independently of the ordinary service bearer.</summary>
public sealed class InvoiceCreationDelegationVerifier(IConfiguration configuration, TimeProvider clock)
{
    public const string HeaderName = "X-Maliev-Employee-Delegation";
    public const string Audience = "legacy-accounting:invoice-create";
    public const string Scope = "legacy.accounting.create";
    public const string IntranetSubject = "service:legacy-intranet";

    public string? Verify(string? header, ClaimsPrincipal service, int quotationId, Guid operationId) =>
        VerifyOrigin(header, service, quotationId, operationId)?.EmployeeSubject;

    public InvoiceNotificationOrigin? VerifyOrigin(string? header, ClaimsPrincipal service, int quotationId, Guid operationId)
    {
        if (header is null) return null;
        if (header.Length is < 8 or > 16391 || !header.StartsWith("Bearer ", StringComparison.Ordinal) ||
            header[7..].Contains(' ')) return null;
        var issuer = configuration["Jwt:Issuer"];
        var encodedKey = configuration["Jwt:PublicKey"];
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(encodedKey)) return null;
        var serviceSubjects = service.Claims.Where(value => value.Type is "sub" or ClaimTypes.NameIdentifier).Select(value => value.Value).Distinct().ToArray();
        if (serviceSubjects.Length != 1 || serviceSubjects[0] != IntranetSubject ||
            !service.HasClaim("identity_kind", "service")) return null;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedKey)));
            var signingKey = new RsaSecurityKey(rsa)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            };
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 16384 };
            handler.ValidateToken(header[7..], parameters, out var validated);
            if (validated is not JwtSecurityToken token || token.Header.Alg != SecurityAlgorithms.RsaSha256 ||
                token.Audiences.Count() != 1 || token.Issuer != issuer) return null;
            static string? One(JwtSecurityToken token, string name)
            {
                var values = token.Claims.Where(value => value.Type == name).Select(value => value.Value).ToArray();
                return values.Length == 1 ? values[0] : null;
            }
            var employee = One(token, JwtRegisteredClaimNames.Sub);
            if (string.IsNullOrWhiteSpace(employee) || employee.Length > 256 || employee.StartsWith("service:", StringComparison.Ordinal) ||
                One(token, "azp") != IntranetSubject || One(token, "scope") != Scope ||
                One(token, "quotation_id") != quotationId.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                One(token, "operation_id") != operationId.ToString("D") ||
                !Guid.TryParseExact(One(token, JwtRegisteredClaimNames.Jti), "D", out var jti) || jti == Guid.Empty ||
                One(token, JwtRegisteredClaimNames.Jti) != jti.ToString("D") ||
                !long.TryParse(One(token, JwtRegisteredClaimNames.Iat), out var issuedAt) ||
                !long.TryParse(One(token, JwtRegisteredClaimNames.Nbf), out var notBefore) ||
                !long.TryParse(One(token, JwtRegisteredClaimNames.Exp), out var expires)) return null;
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            if (issuedAt > now + 30 || notBefore > now + 30 || expires <= now - 30 ||
                notBefore < issuedAt - 1 || expires - issuedAt is < 1 or > 120) return null;
            return new(token.Issuer, employee, IntranetSubject);
        }
        catch (Exception exception) when (exception is SecurityTokenException or CryptographicException or FormatException or ArgumentException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
