using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;

namespace Legacy.Maliev.AccountingService.Application.Models;

/// <summary>Server-owned input for creating and optionally delivering one receipt.</summary>
public sealed record CreateReceiptRequest(
    string? Comment,
    bool SendEmail,
    int EmployeeId);

/// <summary>Server-owned input for an explicit receipt email retry.</summary>
public sealed record SendReceiptEmailRequest(int EmployeeId);

/// <summary>Customer contact resolved from the invoice-owned customer identifier.</summary>
public sealed record ReceiptCustomerContact(string Email, string Name);

/// <summary>Observable receipt workflow state.</summary>
public enum ReceiptWorkflowState
{
    /// <summary>A new receipt workflow completed.</summary>
    Completed,
    /// <summary>Existing durable state was repaired or confirmed.</summary>
    Reconciled,
    /// <summary>The receipt and its links were removed.</summary>
    Removed,
}

/// <summary>Observable email delivery state without claiming provider certainty that is unavailable.</summary>
public enum ReceiptEmailState
{
    /// <summary>Email was not requested.</summary>
    NotRequested,
    /// <summary>The provider acknowledged the stable operation.</summary>
    Delivered,
    /// <summary>An existing receipt was reconciled; an explicit resend operation is required.</summary>
    ExplicitRetryRequired,
}

/// <summary>Idempotent receipt workflow response.</summary>
public sealed record ReceiptWorkflowResult(
    int? ReceiptId,
    ReceiptWorkflowState State,
    ReceiptEmailState EmailState,
    string? ProviderMessageId = null,
    ReceiptStoredFile? File = null);

/// <summary>Stable clean-object identity stored by AccountingService.</summary>
public sealed record ReceiptStoredFile(string Bucket, string ObjectName);

/// <summary>State used to resume a partially completed receipt workflow.</summary>
public sealed record ReceiptWorkflowSnapshot(
    Invoice Invoice,
    IReadOnlyList<InvoiceOrderItem> InvoiceItems,
    Receipt? Receipt,
    IReadOnlyList<ReceiptOrderItem> ReceiptItems,
    IReadOnlyList<ReceiptFile> ReceiptFiles);

/// <summary>Signals that a receipt workflow dependency cannot safely complete the requested step.</summary>
public sealed class ReceiptWorkflowDependencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Signals that the requested invoice or receipt does not exist.</summary>
public sealed class ReceiptWorkflowNotFoundException(string message) : Exception(message);

/// <summary>Signals that required coordination or workload authentication is temporarily unavailable.</summary>
public sealed class ReceiptWorkflowUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
