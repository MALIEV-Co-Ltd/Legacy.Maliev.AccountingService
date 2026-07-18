using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Microsoft.Extensions.Caching.Distributed;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>
/// Fail-closed Redis replay journal for completed receipt operations. Unlike ordinary read caching,
/// an unavailable journal aborts the workflow because silently continuing could duplicate email.
/// </summary>
public sealed class ReceiptOperationJournal(IDistributedCache cache) : IReceiptOperationJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<ReceiptWorkflowResult?> GetAsync(
        string scope,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await cache.GetAsync(Key(scope, operationId), cancellationToken);
            return value is null ? null : JsonSerializer.Deserialize<ReceiptWorkflowResult>(value, JsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReceiptWorkflowUnavailableException("Receipt operation replay protection is unavailable.", exception);
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(
        string scope,
        Guid operationId,
        ReceiptWorkflowResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(
                Key(scope, operationId),
                JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReceiptWorkflowUnavailableException("Receipt operation replay protection is unavailable.", exception);
        }
    }

    private static string Key(string scope, Guid operationId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operationId.ToString("D"))));
        return $"receipt-operation:{scope}:{hash}";
    }
}
