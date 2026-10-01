using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Application.Interfaces;

/// <summary>Internal retained-key access; never shares Notification's producer secret.</summary>
public interface IInvoiceNotificationBindingKeyring
{
    string ActiveKeyId { get; }
    ReadOnlyMemory<byte>? Find(string keyId);
}

/// <summary>Durable invoice-purpose admission only; no provider or retry capability.</summary>
public interface IInvoiceNotificationCorrelationStore
{
    Task<InvoiceNotificationCorrelation> AdmitAsync(InvoiceNotificationCorrelationIdentity identity,
        string payloadDigest, CancellationToken cancellationToken);
    Task<InvoiceNotificationCorrelation?> ReadAsync(Guid intentId, CancellationToken cancellationToken);
}
