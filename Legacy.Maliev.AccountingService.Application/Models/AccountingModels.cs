namespace Legacy.Maliev.AccountingService.Application.Models;

public sealed record PaginatedResponse<T>(IReadOnlyList<T> Items, int PageIndex, int TotalPages, int TotalRecords) { public bool HasNextPage => PageIndex < TotalPages; public bool HasPreviousPage => PageIndex > 1; }
public sealed record FinancialSummaryResponse(string Period, decimal Income, decimal Expense, decimal Net);
public enum InvoiceSortType
{
    InvoiceId_Ascending,
    InvoiceId_Descending,
    InvoiceCreatedDate_Ascending,
    InvoiceCreatedDate_Descending,
    InvoicePaymentDate_Ascending,
    InvoicePaymentDate_Descending,
}
public sealed class FinancialSummary { public List<SummaryDetail> Details { get; } = []; }
public sealed class SummaryDetail { public string CurrencyId { get; set; } = "-"; public decimal CurrentAmount { get; set; } public decimal PreviousAmount { get; set; } public decimal DeltaAmount { get; set; } public decimal DeltaPercent { get; set; } }
public enum UpdateResult { Updated, NotFound, Conflict }
