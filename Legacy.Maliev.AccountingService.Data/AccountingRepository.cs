using System.Reflection;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>
/// Preserves the three legacy accounting databases behind one API boundary. This repository records
/// historical financial facts only; it never contacts or executes a payment provider.
/// </summary>
public sealed class AccountingRepository(
    PaymentDbContext payments,
    InvoiceDbContext invoices,
    ReceiptDbContext receipts,
    IAccountingCache cache,
    TimeProvider clock) : IAccountingService
{
    public async Task<T> CreateAsync<T>(T item, CancellationToken cancellationToken) where T : class
    {
        var context = ContextFor<T>();
        SetIdentity(item, 0);
        SetDate(item, "CreatedDate", Now());
        SetDate(item, "ModifiedDate", Now());
        context.Set<T>().Add(item);
        await context.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<bool> DeleteAsync<T>(int id, CancellationToken cancellationToken) where T : class
    {
        var deleted = await ContextFor<T>().Set<T>()
            .Where(item => EF.Property<int>(item, "Id") == id)
            .ExecuteDeleteAsync(cancellationToken) == 1;
        if (deleted)
        {
            await cache.RemoveAsync(CacheKey<T>(id), cancellationToken);
        }

        return deleted;
    }

    public async Task<T?> GetAsync<T>(int id, CancellationToken cancellationToken) where T : class
    {
        var key = CacheKey<T>(id);
        var cached = await cache.GetAsync<T>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var item = await ContextFor<T>().Set<T>().AsNoTracking()
            .SingleOrDefaultAsync(value => EF.Property<int>(value, "Id") == id, cancellationToken);
        if (item is not null)
        {
            await cache.SetAsync(key, item, TimeSpan.FromMinutes(2), cancellationToken);
        }

        return item;
    }

    public async Task<IReadOnlyList<T>> ListAsync<T>(CancellationToken cancellationToken) where T : class =>
        await ContextFor<T>().Set<T>().AsNoTracking()
            .OrderBy(item => EF.Property<int>(item, "Id"))
            .ToListAsync(cancellationToken);

    public async Task<UpdateResult> UpdateAsync<T>(
        int id,
        T item,
        DateTimeOffset? expected,
        CancellationToken cancellationToken) where T : class
    {
        var context = ContextFor<T>();
        var existing = await context.Set<T>().FindAsync([id], cancellationToken);
        if (existing is null)
        {
            return UpdateResult.NotFound;
        }

        var created = ReadDate(existing, "CreatedDate");
        context.Entry(existing).CurrentValues.SetValues(item);
        SetIdentity(existing, id);
        SetDate(existing, "CreatedDate", created);
        SetDate(existing, "ModifiedDate", Now());
        if (expected is not null && context.Entry(existing).Metadata.FindProperty("ModifiedDate") is not null)
        {
            context.Entry(existing).Property("ModifiedDate").OriginalValue = expected.Value.UtcDateTime;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await cache.RemoveAsync(CacheKey<T>(id), cancellationToken);
            return UpdateResult.Updated;
        }
        catch (DbUpdateConcurrencyException)
        {
            return UpdateResult.Conflict;
        }
    }

    public async Task<PaginatedResponse<Invoice>?> GetInvoicesAsync(
        int? customerId,
        InvoiceSortType? sort,
        string? search,
        bool? paid,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        IQueryable<Invoice> query = invoices.Invoices.AsNoTracking();
        if (customerId is not null)
        {
            query = query.Where(invoice => invoice.CustomerId == customerId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            var isNumeric = int.TryParse(normalizedSearch, out var searchAsInteger);
            var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
            query = query.Where(invoice =>
                (isNumeric && (invoice.Id == searchAsInteger || invoice.ReceiptId == searchAsInteger))
                || EF.Functions.ILike(invoice.Number, pattern, "\\")
                || EF.Functions.ILike(invoice.PurchaseOrderNumber, pattern, "\\")
                || EF.Functions.ILike(invoice.Id.ToString(), pattern, "\\"));
        }

        if (paid is not null)
        {
            query = query.Where(invoice => invoice.IsPaid == paid.Value);
        }

        query = sort switch
        {
            InvoiceSortType.InvoiceId_Ascending => query.OrderBy(invoice => invoice.Id),
            InvoiceSortType.InvoiceId_Descending => query.OrderByDescending(invoice => invoice.Id),
            InvoiceSortType.InvoiceCreatedDate_Ascending => query.OrderBy(invoice => invoice.CreatedDate).ThenBy(invoice => invoice.Id),
            InvoiceSortType.InvoiceCreatedDate_Descending => query.OrderByDescending(invoice => invoice.CreatedDate).ThenByDescending(invoice => invoice.Id),
            InvoiceSortType.InvoicePaymentDate_Ascending => query.OrderBy(invoice => invoice.PaymentDate).ThenBy(invoice => invoice.Id),
            InvoiceSortType.InvoicePaymentDate_Descending => query.OrderByDescending(invoice => invoice.PaymentDate).ThenByDescending(invoice => invoice.Id),
            _ => query.OrderBy(invoice => invoice.Id),
        };
        return await PageAsync(query, page, size, cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceOrderItem>> GetInvoiceItemsAsync(int invoiceId, CancellationToken cancellationToken) =>
        await invoices.Items.AsNoTracking().Where(item => item.InvoiceId == invoiceId).OrderBy(item => item.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InvoiceFile>> GetInvoiceFilesAsync(int invoiceId, CancellationToken cancellationToken) =>
        await invoices.Files.AsNoTracking().Where(file => file.InvoiceId == invoiceId).OrderBy(file => file.Id).ToListAsync(cancellationToken);

    public async Task<PaginatedResponse<Receipt>?> GetReceiptsAsync(
        string? search,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        IQueryable<Receipt> query = receipts.Receipts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(receipt => EF.Functions.ILike(receipt.InvoiceNumber, pattern)
                || EF.Functions.ILike(receipt.TaxIdentification, pattern));
        }

        return await PageAsync(query.OrderByDescending(receipt => receipt.Id), page, size, cancellationToken);
    }

    public async Task<IReadOnlyList<ReceiptOrderItem>> GetReceiptItemsAsync(int receiptId, CancellationToken cancellationToken) =>
        await receipts.Items.AsNoTracking().Where(item => item.ReceiptId == receiptId).OrderBy(item => item.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReceiptFile>> GetReceiptFilesAsync(int receiptId, CancellationToken cancellationToken) =>
        await receipts.Files.AsNoTracking().Where(file => file.ReceiptId == receiptId).OrderBy(file => file.Id).ToListAsync(cancellationToken);

    public async Task<PaginatedResponse<Payment>?> GetPaymentsAsync(
        string? search,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        IQueryable<Payment> query = payments.Payments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(payment => EF.Functions.ILike(payment.Description, pattern)
                || EF.Functions.ILike(payment.Recipient, pattern)
                || EF.Functions.ILike(payment.TransactionNumber, pattern));
        }

        return await PageAsync(query.OrderByDescending(payment => payment.Id), page, size, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentFile>> GetPaymentFilesAsync(int paymentId, CancellationToken cancellationToken) =>
        await payments.Files.AsNoTracking().Where(file => file.PaymentId == paymentId).OrderBy(file => file.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FinancialSummaryResponse>> GetSummaryAsync(
        string period,
        bool income,
        CancellationToken cancellationToken)
    {
        var rows = await payments.Payments.AsNoTracking()
            .Where(payment => payment.PaymentDate != null)
            .Select(payment => new
            {
                Date = payment.PaymentDate!.Value,
                payment.Amount,
                Direction = payment.PaymentDirection.Name,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => string.Equals(row.Direction, income ? "Income" : "Expense", StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => PeriodKey(row.Date, period))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var amount = group.Sum(row => row.Amount);
                return new FinancialSummaryResponse(group.Key, income ? amount : 0m, income ? 0m : amount, income ? amount : -amount);
            })
            .ToList();
    }

    public async Task<FinancialSummary?> GetFinancialSummaryAsync(string period, bool jobIncomeOnly, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var (currentStart, currentEnd, previousStart, previousEnd) = SummaryWindows(now, period);
        var rows = await payments.Payments.AsNoTracking()
            .Where(payment => payment.PaymentDate >= previousStart && payment.PaymentDate < currentEnd)
            .Select(payment => new
            {
                Date = payment.PaymentDate!.Value,
                payment.Amount,
                payment.CurrencyId,
                Direction = payment.PaymentDirection.Name,
                Type = payment.PaymentType.Name,
            })
            .ToListAsync(cancellationToken);
        var current = rows.Where(row => row.Date >= currentStart && row.Date < currentEnd).ToList();
        if (current.Count == 0)
        {
            return null;
        }

        var previous = rows.Where(row => row.Date >= previousStart && row.Date < previousEnd).ToList();
        var result = new FinancialSummary();
        foreach (var currency in current.GroupBy(row => row.CurrencyId))
        {
            decimal Value(IEnumerable<dynamic> source) => jobIncomeOnly
                ? source.Where(row => string.Equals(row.Direction, "Income", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.Type, "Job", StringComparison.OrdinalIgnoreCase)).Sum(row => (decimal)row.Amount)
                : source.Where(row => string.Equals(row.Direction, "Income", StringComparison.OrdinalIgnoreCase)).Sum(row => (decimal)row.Amount)
                    - source.Where(row => string.Equals(row.Direction, "Expense", StringComparison.OrdinalIgnoreCase)).Sum(row => (decimal)row.Amount);
            var currentAmount = Value(currency);
            var previousAmount = Value(previous.Where(row => row.CurrencyId == currency.Key));
            var delta = currentAmount - previousAmount;
            result.Details.Add(new SummaryDetail
            {
                CurrencyId = currency.Key?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
                CurrentAmount = currentAmount,
                PreviousAmount = previousAmount,
                DeltaAmount = delta,
                DeltaPercent = previousAmount == 0m ? 0m : Math.Round(delta / Math.Abs(previousAmount) * 100m, 2),
            });
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<DateTime, decimal>?> GetYearlyDetailAsync(bool income, int? year, int? currencyId, CancellationToken cancellationToken)
    {
        var selectedYear = year ?? clock.GetUtcNow().Year;
        var start = new DateTime(selectedYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddYears(1);
        var rows = await payments.Payments.AsNoTracking()
            .Where(payment => payment.PaymentDate >= start && payment.PaymentDate < end
                && (!currencyId.HasValue || payment.CurrencyId == currencyId))
            .Where(payment => income ? payment.PaymentDirection.Name == "Income" : payment.PaymentDirection.Name == "Expense")
            .Select(payment => new { Date = payment.PaymentDate!.Value.Date, payment.Amount })
            .ToListAsync(cancellationToken);
        return rows.Count == 0
            ? null
            : rows.GroupBy(row => row.Date).OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
    }

    private DbContext ContextFor<T>() where T : class
    {
        var ns = typeof(T).Namespace;
        if (ns == typeof(Payment).Namespace)
        {
            return payments;
        }

        if (ns == typeof(Invoice).Namespace)
        {
            return invoices;
        }

        if (ns == typeof(Receipt).Namespace)
        {
            return receipts;
        }

        throw new NotSupportedException($"{typeof(T).FullName} is not an accounting aggregate.");
    }

    private static async Task<PaginatedResponse<T>?> PageAsync<T>(
        IQueryable<T> query,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        size = Math.Clamp(size, 1, 250);
        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
        {
            return null;
        }

        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PaginatedResponse<T>(items, page, (int)Math.Ceiling(total / (double)size), total);
    }

    private static string PeriodKey(DateTime date, string period) => period.ToLowerInvariant() switch
    {
        "week" => $"{System.Globalization.ISOWeek.GetYear(date):0000}-W{System.Globalization.ISOWeek.GetWeekOfYear(date):00}",
        "year" => date.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture),
        _ => date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
    };

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static (DateTime CurrentStart, DateTime CurrentEnd, DateTime PreviousStart, DateTime PreviousEnd) SummaryWindows(DateTime now, string period)
    {
        DateTime currentStart;
        DateTime currentEnd;
        switch (period.ToLowerInvariant())
        {
            case "week":
                var offset = ((int)now.DayOfWeek + 6) % 7;
                currentStart = now.Date.AddDays(-offset);
                currentEnd = currentStart.AddDays(7);
                break;
            case "year":
                currentStart = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                currentEnd = currentStart.AddYears(1);
                break;
            default:
                currentStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                currentEnd = currentStart.AddMonths(1);
                break;
        }

        var previousStart = period.Equals("week", StringComparison.OrdinalIgnoreCase) ? currentStart.AddDays(-7)
            : period.Equals("year", StringComparison.OrdinalIgnoreCase) ? currentStart.AddYears(-1)
            : currentStart.AddMonths(-1);
        return (currentStart, currentEnd, previousStart, currentStart);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static string CacheKey<T>(int id) => $"{typeof(T).Name.ToLowerInvariant()}:{id}";

    private static DateTime? ReadDate<T>(T item, string name) where T : class =>
        typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) as DateTime?;

    private static void SetDate<T>(T item, string name, DateTime? value) where T : class
    {
        var property = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property?.CanWrite == true)
        {
            property.SetValue(item, value);
        }
    }

    private static void SetIdentity<T>(T item, int value) where T : class =>
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.SetValue(item, value);
}
