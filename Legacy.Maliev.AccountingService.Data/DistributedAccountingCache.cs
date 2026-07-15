using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Redis adapter for authorized accounting reads and create-response idempotency.</summary>
public sealed class DistributedAccountingCache(
    IDistributedCache cache,
    ILogger<DistributedAccountingCache> logger) : IAccountingCache, IIdempotencyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var bytes = await cache.GetAsync(key, cancellationToken);
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Accounting cache read failed; using PostgreSQL");
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan lifetime, CancellationToken cancellationToken) where T : class
    {
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Accounting cache write failed; continuing without cache");
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Accounting cache invalidation failed");
        }
    }

    async Task<T?> IIdempotencyStore.GetAsync<T>(string scope, string key, CancellationToken cancellationToken) where T : class =>
        await GetAsync<T>(IdempotencyKey(scope, key), cancellationToken);

    Task IIdempotencyStore.SetAsync<T>(string scope, string key, T response, CancellationToken cancellationToken) where T : class =>
        SetAsync(IdempotencyKey(scope, key), response, TimeSpan.FromHours(24), cancellationToken);

    private static string IdempotencyKey(string scope, string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return $"idempotency:{scope}:{hash}";
    }
}
