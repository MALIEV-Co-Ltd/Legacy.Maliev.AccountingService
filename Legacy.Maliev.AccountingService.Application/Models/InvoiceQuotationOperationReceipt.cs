namespace Legacy.Maliev.AccountingService.Application.Models;

/// <summary>Quotation-owned durable decision and Order convergence evidence; not invoice completion.</summary>
public sealed record InvoiceQuotationOperationReceipt(int ContractVersion, string OperationId, int QuotationId, int InvoiceId,
    string OriginIssuer, string EmployeeSubject, string RequesterSubject, string ExecutorSubject,
    string OriginalQuotationVersion, string FinancialBinding, string FinancialBindingVersion, string State,
    string DecisionOrderVersion, int CompletedOrders, int TotalOrders, string ModifiedDate);
