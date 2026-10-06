namespace Legacy.Maliev.AccountingService.Application.Models;

/// <summary>Durable side-effect phase bound to original financial and completed Quotation ownership, never a bearer.</summary>
public sealed record InvoiceEmployeeCompletionPhase(int ContractVersion, Guid OperationId, int QuotationId, int InvoiceId,
    string FinancialBinding, string DecisionOrderVersion, int TotalOrders, string State,
    string? PdfSha256, InvoiceCreationStoredFile? StoredFile, InvoiceCreationResult? Result);
