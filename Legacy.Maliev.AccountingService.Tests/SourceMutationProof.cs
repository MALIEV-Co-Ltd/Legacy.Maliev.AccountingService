using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Tests.Fixtures;

namespace Legacy.Maliev.AccountingService.Tests;

internal static class SourceMutationProof
{
    public static IReadOnlyDictionary<string, bool> RedisKeys(HttpClient client, string rowKey, string? createScope = null, bool rowPresent = true)
    {
        var keys = new Dictionary<string, bool>(StringComparer.Ordinal) { [rowKey] = rowPresent };
        if (createScope is not null && client.DefaultRequestHeaders.TryGetValues("Idempotency-Key", out var values))
        {
            var key = Assert.Single(values);
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            keys.Add($"idempotency:{createScope}:{digest}", true);
        }
        return keys;
    }

    public static Task PayloadAsync(AccountingBoundaryHttpFixture fixture, string logicalKey, JsonObject expected, string kind, bool memo = false)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        byte[] Bytes<T>() => JsonSerializer.SerializeToUtf8Bytes(JsonSerializer.Deserialize<T>(expected.ToJsonString(), json), json);
        var payload = kind switch
        {
            "invoice" => Bytes<Domain.Invoice.Invoice>(),
            "receipt" => Bytes<Domain.Receipt.Receipt>(),
            "invoiceorderitem" => Bytes<Domain.Invoice.InvoiceOrderItem>(),
            "receiptorderitem" => Bytes<Domain.Receipt.ReceiptOrderItem>(),
            "invoicefile" => Bytes<Domain.Invoice.InvoiceFile>(),
            "receiptfile" => Bytes<Domain.Receipt.ReceiptFile>(),
            _ => throw new InvalidOperationException("Unexpected source proof entity."),
        };
        return fixture.AssertReceiptRedisPayloadAsync(logicalKey, payload, memo ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(2));
    }

    public static async Task CreationPayloadsAsync(AccountingBoundaryHttpFixture fixture, HttpClient client, string kind,
        string scope, int id, JsonObject get, JsonObject post)
    {
        await PayloadAsync(fixture, $"{kind}:{id}", get, kind);
        foreach (var key in RedisKeys(client, $"{kind}:{id}", scope).Keys.Where(key => key.StartsWith("idempotency:", StringComparison.Ordinal)))
            await PayloadAsync(fixture, key, post, kind, memo: true);
    }
}
