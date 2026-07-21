using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;

namespace Legacy.Maliev.AccountingService.Application.Services;

/// <summary>
/// Coordinates receipt persistence and downstream side effects while deriving recovery state from
/// existing legacy invoice, receipt, and file-link rows. It never auto-resends email while
/// reconciling an earlier request whose provider outcome is unknown.
/// </summary>
public sealed class ReceiptWorkflowService(
    IReceiptWorkflowStore store,
    IReceiptDocumentClient documents,
    IReceiptFileClient files,
    IReceiptNotificationClient notifications,
    IReceiptCustomerClient customers,
    IReceiptSignatureClient signatures,
    IReceiptOperationJournal journal,
    IReceiptOperationLock operationLock) : IReceiptWorkflow
{
    private const string Bucket = "maliev.com";

    /// <inheritdoc />
    public async Task<ReceiptWorkflowResult> CreateAsync(
        int invoiceId,
        CreateReceiptRequest request,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperation(invoiceId, operationId);
        ValidateEmployee(request.EmployeeId);
        var scope = $"create:{invoiceId}";
        var replay = await journal.GetAsync(scope, operationId, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var lease = await operationLock.AcquireAsync(invoiceId, cancellationToken);
        replay = await journal.GetAsync(scope, operationId, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var snapshot = await store.GetAsync(invoiceId, cancellationToken);
        var reconciled = snapshot.Receipt is not null;
        var receipt = snapshot.Receipt ?? await store.CreateReceiptAsync(
            snapshot.Invoice,
            snapshot.InvoiceItems,
            request.Comment,
            cancellationToken);
        var receiptItems = reconciled
            ? snapshot.ReceiptItems
            : MapItems(snapshot.InvoiceItems, receipt.Id);

        await store.LinkInvoiceAsync(invoiceId, receipt.Id, cancellationToken);
        var (storedFile, pdf) = await EnsureStoredPdfAsync(
            receipt,
            receiptItems,
            snapshot.ReceiptFiles,
            request.EmployeeId,
            operationId,
            cancellationToken);

        var emailState = ReceiptEmailState.NotRequested;
        string? messageId = null;
        if (request.SendEmail)
        {
            if (reconciled)
            {
                emailState = ReceiptEmailState.ExplicitRetryRequired;
            }
            else
            {
                var customer = await customers.GetAsync(snapshot.Invoice.CustomerId, cancellationToken);
                if (pdf is null)
                {
                    var signature = await signatures.GetAsync(request.EmployeeId, cancellationToken);
                    pdf = await documents.RenderAsync(receipt, receiptItems, signature, cancellationToken);
                }

                messageId = await notifications.SendAsync(
                    customer.Email,
                    customer.Name,
                    receipt,
                    pdf,
                    operationId,
                    cancellationToken);
                emailState = ReceiptEmailState.Delivered;
            }
        }

        var result = new ReceiptWorkflowResult(
            receipt.Id,
            reconciled ? ReceiptWorkflowState.Reconciled : ReceiptWorkflowState.Completed,
            emailState,
            messageId,
            storedFile);
        await journal.SetAsync(scope, operationId, result, cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<ReceiptWorkflowResult> RemoveAsync(
        int invoiceId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperation(invoiceId, operationId);
        var scope = $"remove:{invoiceId}";
        var replay = await journal.GetAsync(scope, operationId, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var lease = await operationLock.AcquireAsync(invoiceId, cancellationToken);
        replay = await journal.GetAsync(scope, operationId, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var snapshot = await store.GetAsync(invoiceId, cancellationToken);
        var receiptId = snapshot.Receipt?.Id ?? snapshot.Invoice.ReceiptId;
        if (receiptId is null)
        {
            var alreadyRemoved = new ReceiptWorkflowResult(null, ReceiptWorkflowState.Removed, ReceiptEmailState.NotRequested);
            await journal.SetAsync(scope, operationId, alreadyRemoved, cancellationToken);
            return alreadyRemoved;
        }

        foreach (var file in snapshot.ReceiptFiles)
        {
            await files.DeleteAsync(file.Bucket, file.ObjectName, cancellationToken);
        }

        if (snapshot.Receipt is not null)
        {
            await store.DeleteReceiptAsync(receiptId.Value, cancellationToken);
        }

        await store.UnlinkInvoiceAsync(invoiceId, receiptId.Value, cancellationToken);
        var result = new ReceiptWorkflowResult(receiptId, ReceiptWorkflowState.Removed, ReceiptEmailState.NotRequested);
        await journal.SetAsync(scope, operationId, result, cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<ReceiptWorkflowResult> SendEmailAsync(
        int invoiceId,
        SendReceiptEmailRequest request,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateOperation(invoiceId, operationId);
        ValidateEmployee(request.EmployeeId);
        var scope = $"email:{invoiceId}";
        var replay = await journal.GetAsync(scope, operationId, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var lease = await operationLock.AcquireAsync(invoiceId, cancellationToken);
        replay = await journal.GetAsync(scope, operationId, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var snapshot = await store.GetAsync(invoiceId, cancellationToken);
        var receipt = snapshot.Receipt ?? throw new ReceiptWorkflowNotFoundException("Invoice has no receipt to send.");
        var signature = await signatures.GetAsync(request.EmployeeId, cancellationToken);
        var customer = await customers.GetAsync(snapshot.Invoice.CustomerId, cancellationToken);
        var pdf = await documents.RenderAsync(receipt, snapshot.ReceiptItems, signature, cancellationToken);
        var messageId = await notifications.SendAsync(
            customer.Email,
            customer.Name,
            receipt,
            pdf,
            operationId,
            cancellationToken);
        var result = new ReceiptWorkflowResult(
            receipt.Id,
            ReceiptWorkflowState.Reconciled,
            ReceiptEmailState.Delivered,
            messageId,
            snapshot.ReceiptFiles.Select(value => new ReceiptStoredFile(value.Bucket, value.ObjectName)).LastOrDefault());
        await journal.SetAsync(scope, operationId, result, cancellationToken);
        return result;
    }

    private async Task<(ReceiptStoredFile File, byte[]? Pdf)> EnsureStoredPdfAsync(
        Receipt receipt,
        IReadOnlyList<ReceiptOrderItem> receiptItems,
        IReadOnlyList<ReceiptFile> linkedFiles,
        int employeeId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var path = $"receipts/{receipt.Id}";
        var fileName = $"receipt_{receipt.Id}.pdf";
        var objectName = $"{path}/{fileName}".ToLowerInvariant();
        var linked = linkedFiles.LastOrDefault(value =>
            string.Equals(value.Bucket, Bucket, StringComparison.Ordinal) &&
            string.Equals(value.ObjectName, objectName, StringComparison.Ordinal));
        if (linked is not null)
        {
            return (new ReceiptStoredFile(linked.Bucket, linked.ObjectName), null);
        }

        var signature = await signatures.GetAsync(employeeId, cancellationToken);
        var pdf = await documents.RenderAsync(receipt, receiptItems, signature, cancellationToken);
        ReceiptStoredFile stored;
        if (await files.ExistsAsync(Bucket, objectName, cancellationToken))
        {
            stored = new ReceiptStoredFile(Bucket, objectName);
        }
        else
        {
            stored = await files.UploadAsync(Bucket, path, fileName, pdf, operationId, cancellationToken);
            if (!string.Equals(stored.Bucket, Bucket, StringComparison.Ordinal) ||
                !string.Equals(stored.ObjectName, objectName, StringComparison.Ordinal))
            {
                throw new ReceiptWorkflowDependencyException("FileService returned an unexpected receipt object identity.");
            }
        }

        await store.LinkFileAsync(receipt.Id, stored.Bucket, stored.ObjectName, cancellationToken);
        return (stored, pdf);
    }

    private static IReadOnlyList<ReceiptOrderItem> MapItems(IReadOnlyList<InvoiceOrderItem> items, int receiptId) =>
        items.Select(item => new ReceiptOrderItem
        {
            ReceiptId = receiptId,
            Description = item.Description,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            Subtotal = item.Subtotal,
            CreatedDate = item.CreatedDate,
            ModifiedDate = item.ModifiedDate,
        }).ToArray();

    private static void ValidateOperation(int invoiceId, Guid operationId)
    {
        if (invoiceId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(invoiceId));
        }

        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A stable operation UUID is required.", nameof(operationId));
        }
    }

    private static void ValidateEmployee(int employeeId)
    {
        if (employeeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(employeeId), "A trusted employee identifier is required.");
        }
    }
}
