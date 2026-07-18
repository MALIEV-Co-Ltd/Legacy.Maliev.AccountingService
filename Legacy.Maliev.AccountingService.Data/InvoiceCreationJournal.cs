using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Microsoft.Extensions.Caching.Distributed;

namespace Legacy.Maliev.AccountingService.Data;

public sealed class InvoiceCreationJournal(IDistributedCache cache) : IInvoiceCreationJournal
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<InvoiceCreationResult?> GetAsync(string scope, Guid operationId, CancellationToken cancellationToken)
    {
        try { var bytes = await cache.GetAsync(Key(scope, operationId), cancellationToken); return bytes is null ? null : JsonSerializer.Deserialize<InvoiceCreationResult>(bytes, Json); }
        catch (Exception exception) when (exception is not OperationCanceledException) { throw new InvoiceCreationUnavailableException("Invoice operation replay protection is unavailable.", exception); }
    }
    public async Task SetAsync(string scope, Guid operationId, InvoiceCreationResult result, CancellationToken cancellationToken)
    {
        try { await cache.SetAsync(Key(scope, operationId), JsonSerializer.SerializeToUtf8Bytes(result, Json), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) }, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { throw new InvoiceCreationUnavailableException("Invoice operation replay protection is unavailable.", exception); }
    }
    private static string Key(string scope, Guid operationId) => $"invoice-workflow:{scope}:{operationId:D}";
}
