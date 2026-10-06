using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Models;
using Microsoft.IdentityModel.Tokens;

namespace Legacy.Maliev.AccountingService.Api.Authorization;

/// <summary>Independent bounded Auth proof check against actual retained financial ownership; no credential is persisted.</summary>
public sealed class InvoiceCompletionCapabilityVerifier(IConfiguration configuration, TimeProvider clock)
{
    public const string HeaderName = "X-Maliev-Quotation-Invoice-Capability";
    public const string Audience = "legacy-quotation:invoice-complete";
    public const string Scope = "legacy.quotation.invoice-complete";
    public const string BindingVersion = "invoice-creation-financial-v1";
    private static readonly string[] Fields = ["iss", "aud", "sub", "jti", "iat", "nbf", "exp", "azp", "executor", "scope",
        "quotation_id", "operation_id", "invoice_id", "quotation_version", "financial_binding", "financial_binding_version"];

    public static bool IsRequester(ClaimsPrincipal caller) =>
        caller.FindAll("sub").Select(value => value.Value).ToArray() is ["service:legacy-intranet"] &&
        caller.FindAll("identity_kind").Select(value => value.Value).ToArray() is ["service"] &&
        !caller.HasClaim(value => value.Type is "executor" or "sid" or ClaimTypes.Role);

    /// <summary>Returns only a Boolean authority decision; raw JWT remains transient at the caller.</summary>
    public bool Verify(string? bearer, ClaimsPrincipal caller, InvoiceFinancialOwnership receipt)
    {
        if (bearer is null || bearer.Length is < 1 or > 16384 || bearer.Any(char.IsWhiteSpace) || receipt.ContractVersion != 1 ||
            receipt.QuotationId <= 0 || receipt.InvoiceId <= 0 || receipt.OperationId == Guid.Empty ||
            receipt.RequesterSubject != "service:legacy-intranet" || receipt.EmployeeSubject.StartsWith("service:", StringComparison.Ordinal) ||
            !IsRequester(caller)) return false;
        var issuer = configuration["Jwt:Issuer"];
        var encodedKey = configuration["Jwt:PublicKey"];
        if (issuer != receipt.OriginIssuer || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(encodedKey)) return false;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(encodedKey)));
            var key = new RsaSecurityKey(rsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } };
            new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 16384 }.ValidateToken(bearer,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true, IssuerSigningKey = key, RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidateIssuer = true, ValidIssuer = issuer,
                    ValidateAudience = true, ValidAudience = Audience,
                    // Numeric payload checks below use the injected clock with no post-expiry grace.
                    ValidateLifetime = false,
                }, out var validated);
            if (validated is not JwtSecurityToken token || token.Header.Alg != SecurityAlgorithms.RsaSha256) return false;
            using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(token.RawPayload));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Length != Fields.Length || Fields.Any(name => properties.Count(value => value.Name == name) != 1)) return false;
            string? Text(string name) => document.RootElement.GetProperty(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
            bool Integer(string name, out long number)
            {
                var value = document.RootElement.GetProperty(name);
                number = 0;
                return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number) &&
                    value.GetRawText() == number.ToString(CultureInfo.InvariantCulture);
            }
            if (Text("iss") != issuer || Text("aud") != Audience || Text("sub") != receipt.EmployeeSubject ||
                Text("azp") != receipt.RequesterSubject || Text("executor") != "service:legacy-accounting" || Text("scope") != Scope ||
                Text("quotation_id") != receipt.QuotationId.ToString(CultureInfo.InvariantCulture) ||
                Text("invoice_id") != receipt.InvoiceId.ToString(CultureInfo.InvariantCulture) ||
                Text("operation_id") != receipt.OperationId.ToString("D") || Text("quotation_version") != receipt.OriginalQuotationVersion ||
                Text("financial_binding") != receipt.FinancialBinding || Text("financial_binding_version") != BindingVersion ||
                !Guid.TryParseExact(Text("jti"), "D", out var jti) || jti == Guid.Empty || Text("jti") != jti.ToString("D") ||
                !Integer("iat", out var issued) || !Integer("nbf", out var notBefore) || !Integer("exp", out var expires)) return false;
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            return issued > 0 && issued <= now && notBefore == issued && expires > now && expires - issued is >= 1 and <= 120;
        }
        catch (Exception exception) when (exception is SecurityTokenException or CryptographicException or FormatException or
            ArgumentException or JsonException or InvalidOperationException)
        { return false; }
    }
}
