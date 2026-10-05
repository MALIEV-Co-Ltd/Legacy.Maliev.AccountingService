using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;

namespace Legacy.Maliev.AccountingService.Application.Interfaces;

/// <summary>Invoice-only retained V2 coordination; legacy receipt delivery is independent.</summary>
public interface IInvoiceNotificationWorkflow
{
    bool Enabled { get; }
    Task ValidateOriginAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin, bool requirePending, CancellationToken cancellationToken);
    Task<bool> HasFenceAsync(int invoiceId, CancellationToken cancellationToken);
    Task ValidateReplayAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin, InvoiceCreationResult result, CancellationToken cancellationToken);
    Task PrepareFinancialAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin, InvoiceCreationResult result, CancellationToken cancellationToken);
    Task<InvoiceNotificationDeliveryResult> SendAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        string email, string customerName, Invoice invoice, byte[] pdf, CancellationToken cancellationToken);
    Task<InvoiceCreationResult> ReconcileAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin, CancellationToken cancellationToken);
}

/// <summary>Provider acceptance is not recipient delivery; unresolved observations remain explicit.</summary>
public sealed record InvoiceNotificationDeliveryResult(bool ProviderAccepted, string? ProviderMessageId);
