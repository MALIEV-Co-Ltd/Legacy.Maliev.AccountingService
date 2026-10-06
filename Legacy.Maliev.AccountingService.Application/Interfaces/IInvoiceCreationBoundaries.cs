using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;

namespace Legacy.Maliev.AccountingService.Application.Interfaces;

public interface IInvoiceCreationWorkflow
{
    Task<InvoiceCreationResult> CompleteEmployeeAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        CreateInvoiceFromQuotationRequest request, string freshCapability, CancellationToken cancellationToken) =>
        throw new InvoiceCreationUnavailableException("Employee invoice completion is unavailable.");
    Task<InvoiceQuotationOperationReceipt> ResumeEmployeeDecisionAsync(int quotationId, Guid operationId,
        InvoiceNotificationOrigin origin, string freshCapability, CancellationToken cancellationToken) =>
        throw new InvoiceCreationUnavailableException("Employee decision resume is unavailable.");
    Task<InvoiceFinancialOwnership> ReadPreparedFinancialAsync(int quotationId, Guid operationId,
        InvoiceNotificationOrigin origin, CancellationToken cancellationToken) =>
        throw new InvoiceCreationUnavailableException("Employee financial readback is unavailable.");
    Task<InvoiceFinancialOwnership> PrepareFinancialAsync(int quotationId, CreateInvoiceFromQuotationRequest request,
        Guid operationId, InvoiceNotificationOrigin origin, CancellationToken cancellationToken) =>
        throw new InvoiceCreationUnavailableException("Employee financial preparation is unavailable.");
    Task<InvoiceCreationPreview> PreviewAsync(int quotationId, CancellationToken cancellationToken);
    Task<InvoiceCreationResult> CreateAsync(int quotationId, CreateInvoiceFromQuotationRequest request, Guid operationId, CancellationToken cancellationToken);
    Task<InvoiceCreationResult> CreateAsync(int quotationId, CreateInvoiceFromQuotationRequest request, Guid operationId,
        InvoiceNotificationOrigin origin, CancellationToken cancellationToken) => CreateAsync(quotationId, request, operationId, cancellationToken);
    Task<InvoiceCreationResult> ReconcileAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        CancellationToken cancellationToken) => throw new InvoiceCreationUnavailableException("Invoice notification reconciliation is unavailable.");
    Task<InvoiceCreationResult> ReplayCompletedAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        InvoiceCreationResult result, CancellationToken cancellationToken) => Task.FromResult(result);
}

public interface IInvoiceCreationSource { Task<InvoiceCreationSourceSnapshot> GetAsync(int quotationId, CancellationToken cancellationToken); }
public interface IInvoiceCreationStore
{
    Task<Invoice> CreateAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items, InvoiceFinancialCommitContext authority,
        CancellationToken cancellationToken) => throw new InvoiceCreationUnavailableException("Atomic employee financial persistence is unavailable.");
    Task<Invoice?> FindByNumberAsync(string invoiceNumber, CancellationToken cancellationToken);
    Task<Invoice> CreateAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items, CancellationToken cancellationToken);
    Task LinkFileAsync(int invoiceId, string bucket, string objectName, CancellationToken cancellationToken);
}
public interface IInvoiceFinancialOwnershipReader
{
    Task<bool> HasFenceAsync(int invoiceId, CancellationToken cancellationToken) => Task.FromResult(false);
    Task MarkDecisionUncertainAsync(Guid operationId, int quotationId, InvoiceNotificationOrigin origin,
        CancellationToken cancellationToken) => throw new InvoiceCreationUnavailableException("Employee decision fence is unavailable.");
    Task<InvoiceCommittedFinancialSnapshot> ReadCommittedAsync(Guid operationId, CancellationToken cancellationToken) =>
        throw new InvoiceCreationUnavailableException("Committed financial snapshot is unavailable.");
    Task<InvoiceFinancialOwnership?> ReadForOriginAsync(Guid operationId, int quotationId, InvoiceNotificationOrigin origin,
        CancellationToken cancellationToken);
    Task ValidatePendingAsync(Guid operationId, int quotationId, InvoiceNotificationOrigin origin, CancellationToken cancellationToken);
}
public interface IInvoiceQuotationCompletionClient
{
    Task CompleteAsync(int quotationId, int invoiceId, Guid operationId, CancellationToken cancellationToken);
    Task CompleteAsync(int quotationId, int invoiceId, Guid operationId, DateTime? originalModifiedDate, CancellationToken cancellationToken);
}
public interface IInvoiceCreationDocumentClient { Task<byte[]> RenderAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items, CancellationToken cancellationToken); }
public interface IInvoiceEmployeeQuotationCompletionClient
{
    Task<InvoiceQuotationOperationReceipt> CompleteAsync(InvoiceFinancialOwnership ownership, string freshCapability,
        CancellationToken cancellationToken);
}
public interface IInvoiceCreationFileClient
{
    Task<byte[]> DownloadAsync(string bucket, string objectName, int maximumBytes, CancellationToken cancellationToken) =>
        throw new InvoiceCreationUnavailableException("Retained invoice document readback is unavailable.");
    Task<bool> ExistsAsync(string bucket, string objectName, CancellationToken cancellationToken);
    Task<InvoiceCreationStoredFile> UploadAsync(string bucket, string path, string fileName, byte[] content, Guid operationId, CancellationToken cancellationToken);
}
public interface IInvoiceCreationNotificationClient { Task<string?> SendAsync(string email, string customerName, Invoice invoice, byte[] pdf, Guid operationId, CancellationToken cancellationToken); }
public interface IInvoiceCreationJournal
{
    Task<InvoiceCreationResult?> GetAsync(string scope, Guid operationId, CancellationToken cancellationToken);
    Task SetAsync(string scope, Guid operationId, InvoiceCreationResult result, CancellationToken cancellationToken);
}
public interface IInvoiceCreationLock { ValueTask<IAsyncDisposable> AcquireAsync(int quotationId, CancellationToken cancellationToken); }

public interface IInvoiceEmployeeCompletionStore
{
    Task<InvoiceEmployeeCompletionPhase?> ReadAsync(InvoiceFinancialOwnership ownership, CancellationToken cancellationToken);
    Task<bool> BeginDocumentAsync(InvoiceFinancialOwnership ownership, InvoiceQuotationOperationReceipt decision, CancellationToken cancellationToken);
    Task<InvoiceEmployeeCompletionPhase> RetainDocumentAsync(InvoiceFinancialOwnership ownership, string pdfSha256,
        InvoiceCreationStoredFile stored, CancellationToken cancellationToken);
    Task<bool> BeginNotificationAsync(InvoiceFinancialOwnership ownership, CancellationToken cancellationToken);
    Task RetainCompletedAsync(InvoiceFinancialOwnership ownership, InvoiceCreationResult result, CancellationToken cancellationToken);
}
