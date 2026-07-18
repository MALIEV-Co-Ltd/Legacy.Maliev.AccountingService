using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;

namespace Legacy.Maliev.AccountingService.Application.Interfaces;

public interface IInvoiceCreationWorkflow
{
    Task<InvoiceCreationPreview> PreviewAsync(int quotationId, CancellationToken cancellationToken);
    Task<InvoiceCreationResult> CreateAsync(int quotationId, CreateInvoiceFromQuotationRequest request, Guid operationId, CancellationToken cancellationToken);
}

public interface IInvoiceCreationSource { Task<InvoiceCreationSourceSnapshot> GetAsync(int quotationId, CancellationToken cancellationToken); }
public interface IInvoiceCreationStore
{
    Task<Invoice?> FindByNumberAsync(string invoiceNumber, CancellationToken cancellationToken);
    Task<Invoice> CreateAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items, CancellationToken cancellationToken);
    Task LinkFileAsync(int invoiceId, string bucket, string objectName, CancellationToken cancellationToken);
}
public interface IInvoiceQuotationCompletionClient { Task CompleteAsync(int quotationId, int invoiceId, Guid operationId, CancellationToken cancellationToken); }
public interface IInvoiceCreationDocumentClient { Task<byte[]> RenderAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items, CancellationToken cancellationToken); }
public interface IInvoiceCreationFileClient
{
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
