using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Application.Interfaces;

/// <summary>Committed local phase authority; remote observations never mint execution capability.</summary>
public interface IInvoiceNotificationPhaseStore
{
    Task<InvoiceNotificationCorrelation?> FindAsync(int invoiceId, string purpose, int quotationId,
        Guid workflowOperationId, InvoiceNotificationOrigin origin, string senderIssuer,
        string senderServiceSubject, CancellationToken cancellationToken);
    Task<InvoiceNotificationAdmissionPermit> IssueAdmissionAsync(InvoiceNotificationCorrelationIdentity identity,
        string payloadDigest, long expectedVersion, CancellationToken cancellationToken);
    Task<InvoiceNotificationExecutionPermit> IssueExecutionAsync(InvoiceNotificationCorrelationIdentity identity,
        string payloadDigest, long expectedVersion, CancellationToken cancellationToken);
    Task<InvoiceNotificationCorrelation> RetainReceiptAsync(InvoiceNotificationCorrelationIdentity identity,
        string payloadDigest, long expectedVersion, InvoiceNotificationReceiptObservation receipt,
        CancellationToken cancellationToken);
    Task<InvoiceNotificationCorrelation> ObserveAsync(InvoiceNotificationCorrelationIdentity identity,
        long expectedVersion, InvoiceNotificationReceiptObservation receipt, CancellationToken cancellationToken);
    Task ValidateAcceptedResultAsync(InvoiceNotificationCorrelationIdentity identity,
        string providerMessageId, CancellationToken cancellationToken);
}
