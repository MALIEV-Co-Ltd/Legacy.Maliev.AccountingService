using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;

namespace Legacy.Maliev.AccountingService.Application.Interfaces;

/// <summary>Owns receipt creation, removal, reconciliation, and explicit email delivery.</summary>
public interface IReceiptWorkflow
{
    /// <summary>Creates or reconciles a receipt and optionally delivers it once.</summary>
    Task<ReceiptWorkflowResult> CreateAsync(int invoiceId, CreateReceiptRequest request, Guid operationId, CancellationToken cancellationToken);
    /// <summary>Removes receipt storage and accounting state idempotently.</summary>
    Task<ReceiptWorkflowResult> RemoveAsync(int invoiceId, Guid operationId, CancellationToken cancellationToken);
    /// <summary>Explicitly sends an existing receipt with caller-stable duplicate suppression.</summary>
    Task<ReceiptWorkflowResult> SendEmailAsync(int invoiceId, SendReceiptEmailRequest request, Guid operationId, CancellationToken cancellationToken);
}

/// <summary>Persistence boundary for the receipt workflow's existing legacy tables.</summary>
public interface IReceiptWorkflowStore
{
    /// <summary>Loads invoice, receipt, line, and file state needed for reconciliation.</summary>
    Task<ReceiptWorkflowSnapshot> GetAsync(int invoiceId, CancellationToken cancellationToken);
    /// <summary>Creates the receipt snapshot and its line items atomically in the receipt database.</summary>
    Task<Receipt> CreateReceiptAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items, string? comment, CancellationToken cancellationToken);
    /// <summary>Links the receipt when the invoice is unlinked or already linked to the same receipt.</summary>
    Task LinkInvoiceAsync(int invoiceId, int receiptId, CancellationToken cancellationToken);
    /// <summary>Records clean-object metadata once.</summary>
    Task LinkFileAsync(int receiptId, string bucket, string objectName, CancellationToken cancellationToken);
    /// <summary>Deletes receipt lines, file links, and the receipt in one receipt-database transaction.</summary>
    Task DeleteReceiptAsync(int receiptId, CancellationToken cancellationToken);
    /// <summary>Clears only the expected receipt link, including a stale link after partial deletion.</summary>
    Task UnlinkInvoiceAsync(int invoiceId, int receiptId, CancellationToken cancellationToken);
}

/// <summary>QuestPDF receipt rendering boundary.</summary>
public interface IReceiptDocumentClient
{
    /// <summary>Renders the persisted receipt snapshot.</summary>
    Task<byte[]> RenderAsync(Receipt receipt, IReadOnlyList<ReceiptOrderItem> items, byte[]? signature, CancellationToken cancellationToken);
}

/// <summary>Malware-scanned receipt object boundary.</summary>
public interface IReceiptFileClient
{
    /// <summary>Checks whether deterministic clean-object metadata already exists.</summary>
    Task<bool> ExistsAsync(string bucket, string objectName, CancellationToken cancellationToken);
    /// <summary>Uploads one PDF through quarantine and malware scanning.</summary>
    Task<ReceiptStoredFile> UploadAsync(string bucket, string path, string fileName, byte[] content, Guid operationId, CancellationToken cancellationToken);
    /// <summary>Deletes an object or reconciles an already-missing object as success.</summary>
    Task DeleteAsync(string bucket, string objectName, CancellationToken cancellationToken);
    /// <summary>Downloads a clean object through a bounded, non-authenticated signed URL.</summary>
    Task<byte[]> DownloadAsync(string bucket, string objectName, int maximumBytes, CancellationToken cancellationToken);
}

/// <summary>Resolves receipt-delivery contact from CustomerService.</summary>
public interface IReceiptCustomerClient
{
    /// <summary>Gets the authoritative contact for an invoice-owned customer.</summary>
    Task<ReceiptCustomerContact> GetAsync(int customerId, CancellationToken cancellationToken);
}

/// <summary>Resolves the authenticated employee's current signature from owned metadata.</summary>
public interface IReceiptSignatureClient
{
    /// <summary>Gets a bounded clean signature, or null when the employee has no signature.</summary>
    Task<byte[]?> GetAsync(int employeeId, CancellationToken cancellationToken);
}

/// <summary>Transactional receipt email boundary.</summary>
public interface IReceiptNotificationClient
{
    /// <summary>Sends one receipt using the operation UUID as the provider idempotency key.</summary>
    Task<string?> SendAsync(string customerEmail, string customerName, Receipt receipt, byte[] pdf, Guid operationId, CancellationToken cancellationToken);
}

/// <summary>Cross-replica serialization boundary keyed by invoice.</summary>
public interface IReceiptOperationLock
{
    /// <summary>Acquires an invoice-scoped lease.</summary>
    ValueTask<IAsyncDisposable> AcquireAsync(int invoiceId, CancellationToken cancellationToken);
}

/// <summary>Fail-closed replay journal for completed receipt operations.</summary>
public interface IReceiptOperationJournal
{
    /// <summary>Gets a completed operation response.</summary>
    Task<ReceiptWorkflowResult?> GetAsync(string scope, Guid operationId, CancellationToken cancellationToken);
    /// <summary>Stores a completed operation response.</summary>
    Task SetAsync(string scope, Guid operationId, ReceiptWorkflowResult result, CancellationToken cancellationToken);
}
