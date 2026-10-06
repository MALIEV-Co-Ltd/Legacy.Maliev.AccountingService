using Legacy.Maliev.AccountingService.Domain.Invoice;

namespace Legacy.Maliev.AccountingService.Application.Models;

/// <summary>Verified creation authority; no bearer or editable invoice data is retained here.</summary>
public sealed record InvoiceFinancialCommitContext(Guid OperationId, int QuotationId,
    InvoiceNotificationOrigin Origin, DateTime OriginalQuotationVersion);

/// <summary>Versioned read-only financial receipt for independently authorized attachment minting.</summary>
public sealed record InvoiceFinancialOwnership(int ContractVersion, Guid OperationId, int QuotationId,
    int InvoiceId, string OriginIssuer, string EmployeeSubject, string RequesterSubject,
    string OriginalQuotationVersion, string FinancialBinding);

/// <summary>Private workflow input from one verified database snapshot; never exposed by the ownership route.</summary>
public sealed record InvoiceCommittedFinancialSnapshot(InvoiceFinancialOwnership Ownership, Invoice Invoice,
    IReadOnlyList<InvoiceOrderItem> Items);
