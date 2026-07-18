using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Domain.Receipt;
namespace Legacy.Maliev.AccountingService.Application.Interfaces;

public interface IAccountingService
{
    Task<T> CreateAsync<T>(T item, CancellationToken c) where T : class; Task<bool> DeleteAsync<T>(int id, CancellationToken c) where T : class; Task<T?> GetAsync<T>(int id, CancellationToken c) where T : class; Task<IReadOnlyList<T>> ListAsync<T>(CancellationToken c) where T : class; Task<UpdateResult> UpdateAsync<T>(int id, T item, DateTimeOffset? expected, CancellationToken c) where T : class;
    Task<PaginatedResponse<Invoice>?> GetInvoicesAsync(int? customerId, InvoiceSortType? sort, string? search, bool? paid, int page, int size, CancellationToken c); Task<IReadOnlyList<InvoiceOrderItem>> GetInvoiceItemsAsync(int invoiceId, CancellationToken c); Task<IReadOnlyList<InvoiceFile>> GetInvoiceFilesAsync(int invoiceId, CancellationToken c);
    Task<PaginatedResponse<Receipt>?> GetReceiptsAsync(string? search, int page, int size, CancellationToken c); Task<IReadOnlyList<ReceiptOrderItem>> GetReceiptItemsAsync(int receiptId, CancellationToken c); Task<IReadOnlyList<ReceiptFile>> GetReceiptFilesAsync(int receiptId, CancellationToken c);
    Task<PaginatedResponse<Payment>?> GetPaymentsAsync(string? search, int page, int size, CancellationToken c); Task<IReadOnlyList<PaymentFile>> GetPaymentFilesAsync(int paymentId, CancellationToken c); Task<IReadOnlyList<FinancialSummaryResponse>> GetSummaryAsync(string period, bool income, CancellationToken c);
    Task<FinancialSummary?> GetFinancialSummaryAsync(string period, bool jobIncomeOnly, CancellationToken c); Task<IReadOnlyDictionary<DateTime, decimal>?> GetYearlyDetailAsync(bool income, int? year, int? currencyId, CancellationToken c);
}
public interface IAccountingCache { Task<T?> GetAsync<T>(string k, CancellationToken c) where T : class; Task SetAsync<T>(string k, T v, TimeSpan t, CancellationToken c) where T : class; Task RemoveAsync(string k, CancellationToken c); }
public interface IIdempotencyStore { Task<T?> GetAsync<T>(string s, string k, CancellationToken c) where T : class; Task SetAsync<T>(string s, string k, T r, CancellationToken c) where T : class; }
