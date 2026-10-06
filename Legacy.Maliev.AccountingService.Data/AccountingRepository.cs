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
        PreservePaymentClock(item);
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

    public Task<bool> ExistsAsync<T>(int id, CancellationToken cancellationToken) where T : class =>
        ContextFor<T>().Set<T>().AsNoTracking().AnyAsync(item => EF.Property<int>(item, "Id") == id, cancellationToken);

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

        // Attribution is authoritative at invoice creation and cannot be replaced by a full PUT.
        if (existing is Invoice storedInvoice && item is Invoice suppliedInvoice)
        {
            suppliedInvoice.SourceRequestId = storedInvoice.SourceRequestId;
            suppliedInvoice.SourceJourneyId = storedInvoice.SourceJourneyId;
        }

        PreservePaymentClock(item);
        var created = ReadDate(existing, "CreatedDate");
        SetIdentity(item, id);
        context.Entry(existing).CurrentValues.SetValues(item);
        SetDate(existing, "CreatedDate", created);
        SetDate(existing, "ModifiedDate", Now());
        if (expected is not null && context.Entry(existing).Metadata.FindProperty("ModifiedDate") is not null)
        {
            context.Entry(existing).Property("ModifiedDate").OriginalValue =
                DateTime.SpecifyKind(expected.Value.UtcDateTime, DateTimeKind.Unspecified);
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

    public Task<Invoice?> GetInvoiceByNumberAsync(string number, CancellationToken cancellationToken)
    {
        var literal = EscapeLikePattern(number);
        return invoices.Invoices.AsNoTracking()
            .SingleOrDefaultAsync(invoice => EF.Functions.ILike(invoice.Number, literal, "\\"), cancellationToken);
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

        if (!string.IsNullOrEmpty(search))
        {
            var isNumeric = int.TryParse(search, out var searchAsInteger);
            var pattern = $"%{EscapeLikePattern(search)}%";
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
            InvoiceSortType.InvoiceCreatedDate_Ascending => query
                .OrderBy(invoice => invoice.CreatedDate != null)
                .ThenBy(invoice => invoice.CreatedDate)
                .ThenBy(invoice => invoice.Id),
            InvoiceSortType.InvoiceCreatedDate_Descending => query
                .OrderBy(invoice => invoice.CreatedDate == null)
                .ThenByDescending(invoice => invoice.CreatedDate)
                .ThenByDescending(invoice => invoice.Id),
            InvoiceSortType.InvoicePaymentDate_Ascending => query
                .OrderBy(invoice => invoice.PaymentDate != null)
                .ThenBy(invoice => invoice.PaymentDate)
                .ThenBy(invoice => invoice.Id),
            InvoiceSortType.InvoicePaymentDate_Descending => query
                .OrderBy(invoice => invoice.PaymentDate == null)
                .ThenByDescending(invoice => invoice.PaymentDate)
                .ThenByDescending(invoice => invoice.Id),
            _ => query.OrderBy(invoice => invoice.Id),
        };
        var result = await PageAsync(query, page, size, cancellationToken);
        return result is { Items.Count: > 0 } ? result : null;
    }

    public async Task<IReadOnlyList<InvoiceOrderItem>> GetInvoiceItemsAsync(int invoiceId, CancellationToken cancellationToken) =>
        await invoices.Items.AsNoTracking().Where(item => item.InvoiceId == invoiceId).OrderBy(item => item.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InvoiceFile>> GetInvoiceFilesAsync(int invoiceId, CancellationToken cancellationToken) =>
        await invoices.Files.AsNoTracking().Where(file => file.InvoiceId == invoiceId).OrderBy(file => file.Id).ToListAsync(cancellationToken);

    public async Task<PaidInvoiceOutcomeReadback> GetPaidInvoiceOutcomeReadbackAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var paidInvoices = await invoices.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.IsPaid
                && invoice.PaymentDate.HasValue
                && invoice.PaymentDate.Value >= fromUtc
                && invoice.PaymentDate.Value < toUtc)
            .Select(invoice => new
            {
                PaidUtc = invoice.PaymentDate!.Value,
                invoice.Currency,
                invoice.Total,
                IsSourceAttributed = invoice.SourceRequestId.HasValue && invoice.SourceJourneyId.HasValue,
            })
            .ToListAsync(cancellationToken);

        return new PaidInvoiceOutcomeReadback(
            fromUtc,
            toUtc,
            paidInvoices
                .GroupBy(invoice => invoice.PaidUtc.Date)
                .OrderBy(group => group.Key)
                .Select(group => new PaidInvoiceOutcomeReadbackDay(
                    DateTime.SpecifyKind(group.Key, DateTimeKind.Utc),
                    group.Count(),
                    group.Count(invoice => invoice.IsSourceAttributed),
                    group.Count(invoice => !invoice.IsSourceAttributed),
                    group.GroupBy(invoice => string.IsNullOrWhiteSpace(invoice.Currency)
                            ? "UNSPECIFIED"
                            : invoice.Currency)
                        .OrderBy(currency => currency.Key, StringComparer.Ordinal)
                        .Select(currency => new PaidInvoiceAmountByCurrency(
                            currency.Key,
                            currency.Sum(invoice => invoice.Total.GetValueOrDefault()),
                            currency.Count()))
                        .ToArray()))
                .ToArray());
    }

    public async Task<PaginatedResponse<Receipt>?> GetReceiptsAsync(
        ReceiptSortType? sort,
        string? search,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        IQueryable<Receipt> query = receipts.Receipts.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{EscapeLikePattern(search)}%";
            query = query.Where(receipt => EF.Functions.ILike(receipt.Comment, pattern, "\\")
                || EF.Functions.ILike(receipt.CommercialRegistration, pattern, "\\")
                || EF.Functions.ILike(receipt.TaxIdentification, pattern, "\\")
                || EF.Functions.ILike(receipt.InvoiceNumber, pattern, "\\")
                || (receipt.CustomerId.HasValue && EF.Functions.ILike(receipt.CustomerId.Value.ToString(), pattern, "\\"))
                || EF.Functions.ILike(receipt.Id.ToString(), pattern, "\\"));
        }

        query = sort switch
        {
            ReceiptSortType.ReceiptId_Descending => query.OrderByDescending(receipt => receipt.Id),
            // SQL Server places NULL first ascending and last descending; keep this explicit on PostgreSQL.
            ReceiptSortType.ReceiptCreatedDate_Ascending => query.OrderBy(receipt => receipt.CreatedDate != null)
                .ThenBy(receipt => receipt.CreatedDate).ThenBy(receipt => receipt.Id),
            ReceiptSortType.ReceiptCreatedDate_Descending => query.OrderBy(receipt => receipt.CreatedDate == null)
                .ThenByDescending(receipt => receipt.CreatedDate).ThenBy(receipt => receipt.Id),
            ReceiptSortType.ReceiptPaymentDate_Ascending => query.OrderBy(receipt => receipt.PaymentDate).ThenBy(receipt => receipt.Id),
            ReceiptSortType.ReceiptPaymentDate_Descending => query.OrderByDescending(receipt => receipt.PaymentDate).ThenBy(receipt => receipt.Id),
            _ => query.OrderBy(receipt => receipt.Id),
        };
        var result = await PageAsync(query, page, size, cancellationToken);
        return result is not null && result.Items.Count == 0 ? null : result;
    }

    public async Task<IReadOnlyList<ReceiptOrderItem>> GetReceiptItemsAsync(int receiptId, CancellationToken cancellationToken) =>
        await receipts.Items.AsNoTracking().Where(item => item.ReceiptId == receiptId).OrderBy(item => item.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReceiptFile>> GetReceiptFilesAsync(int receiptId, CancellationToken cancellationToken) =>
        await receipts.Files.AsNoTracking().Where(file => file.ReceiptId == receiptId).OrderBy(file => file.Id).ToListAsync(cancellationToken);

    public async Task<PaginatedResponse<Payment>?> GetPaymentsAsync(
        PaymentSortType? sort,
        string? search,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        IQueryable<Payment> query = payments.Payments.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            var normalizedSearch = search;
            if (int.TryParse(normalizedSearch, out var paymentId))
            {
                query = query.Where(payment => payment.Id == paymentId);
            }
            else
            {
                var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
                query = query.Where(payment => EF.Functions.ILike(payment.Description, pattern, "\\")
                    || EF.Functions.ILike(payment.Recipient, pattern, "\\")
                    || EF.Functions.ILike(payment.TransactionNumber, pattern, "\\"));
            }
        }

        query = sort switch
        {
            PaymentSortType.PaymentId_Ascending => query.OrderBy(payment => payment.Id),
            PaymentSortType.PaymentId_Descending => query.OrderByDescending(payment => payment.Id),
            // SQL Server orders NULL dates first ascending and last descending; PostgreSQL differs.
            PaymentSortType.PaymentDate_Ascending => query.OrderBy(payment => payment.PaymentDate != null)
                .ThenBy(payment => payment.PaymentDate).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentDate_Descending => query.OrderBy(payment => payment.PaymentDate == null)
                .ThenByDescending(payment => payment.PaymentDate).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentCreatedDate_Ascending => query.OrderBy(payment => payment.CreatedDate != null)
                .ThenBy(payment => payment.CreatedDate).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentCreatedDate_Descending => query.OrderBy(payment => payment.CreatedDate == null)
                .ThenByDescending(payment => payment.CreatedDate).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentModifiedDate_Ascending => query.OrderBy(payment => payment.ModifiedDate != null)
                .ThenBy(payment => payment.ModifiedDate).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentModifiedDate_Descending => query.OrderBy(payment => payment.ModifiedDate == null)
                .ThenByDescending(payment => payment.ModifiedDate).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentDirection_Ascending => query.OrderBy(payment => payment.PaymentDirection.Name != null)
                .ThenBy(payment => payment.PaymentDirection.Name).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentDirection_Descending => query.OrderBy(payment => payment.PaymentDirection.Name == null)
                .ThenByDescending(payment => payment.PaymentDirection.Name).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentType_Ascending => query.OrderBy(payment => payment.PaymentType.Name != null)
                .ThenBy(payment => payment.PaymentType.Name).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentType_Descending => query.OrderBy(payment => payment.PaymentType.Name == null)
                .ThenByDescending(payment => payment.PaymentType.Name).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentMethod_Ascending => query.OrderBy(payment => payment.PaymentMethod.Name != null)
                .ThenBy(payment => payment.PaymentMethod.Name).ThenBy(payment => payment.Id),
            PaymentSortType.PaymentMethod_Descending => query.OrderBy(payment => payment.PaymentMethod.Name == null)
                .ThenByDescending(payment => payment.PaymentMethod.Name).ThenBy(payment => payment.Id),
            PaymentSortType.Recipient_Ascending => query.OrderBy(payment => payment.Recipient != null)
                .ThenBy(payment => payment.Recipient).ThenBy(payment => payment.Id),
            PaymentSortType.Recipient_Descending => query.OrderBy(payment => payment.Recipient == null)
                .ThenByDescending(payment => payment.Recipient).ThenBy(payment => payment.Id),
            _ => query.OrderBy(payment => payment.Id),
        };
        var result = await PageAsync(query, page, size, cancellationToken);
        return result is not null && result.Items.Count == 0 ? null : result;
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
            .Where(payment => payment.PaymentDate >= previousStart && payment.PaymentDate <= currentEnd)
            .Select(payment => new
            {
                Date = payment.PaymentDate!.Value,
                payment.Amount,
                payment.CurrencyId,
                payment.PaymentDirectionId,
                payment.PaymentTypeId,
            })
            .ToListAsync(cancellationToken);
        var current = rows.Where(row => row.Date >= currentStart && row.Date <= currentEnd).ToList();
        if (current.Count == 0)
        {
            return null;
        }

        // Resolve authoritative catalog IDs only after the source's empty-current check.
        // Ambiguous names must fail rather than combine payments from distinct IDs.
        var expenseId = jobIncomeOnly ? (int?)null : await payments.Directions.AsNoTracking()
            .Where(direction => direction.Name == "Expense").Select(direction => direction.Id).SingleAsync(cancellationToken);
        var incomeId = await payments.Directions.AsNoTracking()
            .Where(direction => direction.Name == "Income").Select(direction => direction.Id).SingleAsync(cancellationToken);
        var jobId = jobIncomeOnly ? await payments.Types.AsNoTracking()
            .Where(type => type.Name == "Job").Select(type => type.Id).SingleAsync(cancellationToken) : (int?)null;

        var previous = rows.Where(row => row.Date >= previousStart && row.Date <= previousEnd).ToList();
        var result = new FinancialSummary();
        foreach (var currency in current.GroupBy(row => row.CurrencyId))
        {
            decimal Value(IEnumerable<dynamic> source) => jobIncomeOnly
                ? source.Where(row => row.PaymentDirectionId == incomeId
                    && row.PaymentTypeId == jobId).Sum(row => (decimal)row.Amount)
                : source.Where(row => row.PaymentDirectionId == incomeId).Sum(row => (decimal)row.Amount)
                    - source.Where(row => row.PaymentDirectionId == expenseId).Sum(row => (decimal)row.Amount);
            var currentAmount = Value(currency);
            var previousAmount = Value(previous.Where(row => row.CurrencyId == currency.Key));
            if (jobIncomeOnly && previousAmount == 0m)
            {
                continue;
            }

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
        var end = start.AddYears(1).AddDays(-1);
        // Source yearly detail resolves the direction before querying the selected window.
        var directionId = await payments.Directions.AsNoTracking()
            .Where(direction => direction.Name == (income ? "Income" : "Expense"))
            .Select(direction => (int?)direction.Id).SingleOrDefaultAsync(cancellationToken);
        var rows = await payments.Payments.AsNoTracking()
            .Where(payment => payment.PaymentDate >= start && payment.PaymentDate <= end
                && (!currencyId.HasValue || payment.CurrencyId == currencyId))
            .Where(payment => payment.PaymentDirectionId == directionId)
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
        return (currentStart, currentEnd.AddDays(-1), previousStart, currentStart.AddDays(-1));
    }

    // CreatedDate/ModifiedDate are "timestamp without time zone" wall-clock columns storing the
    // UTC instant with Kind stripped (matching the column's own CURRENT_TIMESTAMP AT TIME ZONE 'UTC'
    // default); Npgsql rejects Kind=Utc values for that column type.
    private DateTime Now() => DateTime.SpecifyKind(clock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);

    // The legacy date input carries a clock value without an offset. Keep those ticks,
    // using UTC storage representation only to satisfy the existing timestamptz column.
    // Explicit UTC values retain their existing behavior; no local timezone conversion occurs.
    private static void PreservePaymentClock<T>(T item) where T : class
    {
        if (item is Payment { PaymentDate: { } date } payment)
        {
            // JSON explicit offsets produce Local DateTime values. Preserve their instant;
            // offset-free source clocks keep the previously reviewed tick representation.
            payment.PaymentDate = date.Kind == DateTimeKind.Local
                ? date.ToUniversalTime()
                : DateTime.SpecifyKind(date, DateTimeKind.Utc);
        }
        else if (item is Invoice { PaymentDate: { Kind: DateTimeKind.Unspecified } invoiceDate } invoice)
        {
            invoice.PaymentDate = DateTime.SpecifyKind(invoiceDate, DateTimeKind.Utc);
        }
        else if (item is Receipt { PaymentDate.Kind: DateTimeKind.Unspecified } receipt)
        {
            receipt.PaymentDate = DateTime.SpecifyKind(receipt.PaymentDate, DateTimeKind.Utc);
        }
    }

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
