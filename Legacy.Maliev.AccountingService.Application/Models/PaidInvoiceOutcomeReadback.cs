namespace Legacy.Maliev.AccountingService.Application.Models;

/// <summary>A privacy-safe aggregate of paid invoices for an exclusive UTC window.</summary>
public sealed record PaidInvoiceOutcomeReadback(
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<PaidInvoiceOutcomeReadbackDay> Days);

/// <summary>A privacy-safe paid-invoice aggregate for one UTC payment day.</summary>
public sealed record PaidInvoiceOutcomeReadbackDay(
    DateTime DayUtc,
    int PaidInvoiceCount,
    int SourceAttributedPaidInvoiceCount,
    int UnattributedPaidInvoiceCount,
    IReadOnlyList<PaidInvoiceAmountByCurrency> PaidInvoiceAmountsByCurrency);

/// <summary>Paid invoice totals for one source currency.</summary>
public sealed record PaidInvoiceAmountByCurrency(
    string Currency,
    decimal PaidInvoiceTotal,
    int PaidInvoiceCount);
