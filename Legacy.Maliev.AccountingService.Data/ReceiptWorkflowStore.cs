using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Runtime.ExceptionServices;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Persists receipt workflow state using only the existing invoice and receipt schemas.</summary>
public sealed class ReceiptWorkflowStore(
    InvoiceDbContext invoices,
    ReceiptDbContext receipts,
    TimeProvider timeProvider,
    IAccountingCache cache) : IReceiptWorkflowStore
{
    /// <inheritdoc />
    public async Task<ReceiptWorkflowSnapshot> GetAsync(int invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await invoices.Invoices.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == invoiceId, cancellationToken)
            ?? throw new ReceiptWorkflowNotFoundException($"Invoice {invoiceId} was not found.");
        var invoiceItems = await invoices.Items.AsNoTracking()
            .Where(value => value.InvoiceId == invoiceId)
            .OrderBy(value => value.Id)
            .ToListAsync(cancellationToken);

        Receipt? receipt = null;
        if (invoice.ReceiptId is { } receiptId)
        {
            receipt = await receipts.Receipts.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == receiptId, cancellationToken);
        }

        if (receipt is null)
        {
            var candidates = await receipts.Receipts.AsNoTracking()
                .Where(value => value.InvoiceNumber == invoice.Number)
                .OrderByDescending(value => value.Id)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (candidates.Count > 1)
            {
                throw new ReceiptWorkflowDependencyException(
                    $"Invoice {invoiceId} has multiple receipt candidates and cannot be reconciled automatically.");
            }

            receipt = candidates.SingleOrDefault();
        }

        if (receipt is null)
        {
            return new ReceiptWorkflowSnapshot(invoice, invoiceItems, null, [], []);
        }

        var receiptItems = await receipts.Items.AsNoTracking()
            .Where(value => value.ReceiptId == receipt.Id)
            .OrderBy(value => value.Id)
            .ToListAsync(cancellationToken);
        var receiptFiles = await receipts.Files.AsNoTracking()
            .Where(value => value.ReceiptId == receipt.Id)
            .OrderBy(value => value.Id)
            .ToListAsync(cancellationToken);
        return new ReceiptWorkflowSnapshot(invoice, invoiceItems, receipt, receiptItems, receiptFiles);
    }

    /// <inheritdoc />
    public async Task<Receipt> CreateReceiptAsync(
        Invoice invoice,
        IReadOnlyList<InvoiceOrderItem> items,
        string? comment,
        CancellationToken cancellationToken)
    {
        var existing = await receipts.Receipts.AsNoTracking()
            .Where(value => value.InvoiceNumber == invoice.Number)
            .OrderByDescending(value => value.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (existing.Count > 1)
        {
            throw new ReceiptWorkflowDependencyException(
                $"Invoice {invoice.Id} has multiple receipt candidates and cannot be reconciled automatically.");
        }

        if (existing.Count == 1)
        {
            return existing[0];
        }

        var now = Now();
        var receipt = new Receipt
        {
            CustomerId = invoice.CustomerId,
            InvoiceNumber = invoice.Number,
            PaymentDate = invoice.PaymentDate ?? now,
            Currency = invoice.Currency,
            Subtotal = invoice.Subtotal ?? 0m,
            WithholdingTax = invoice.WithholdingTax,
            Vat = invoice.Vat ?? 0m,
            Total = invoice.Total ?? 0m,
            Comment = comment ?? string.Empty,
            TaxIdentification = invoice.TaxIdentification,
            CommercialRegistration = invoice.CommercialRegistration,
            BillingAddressBuilding = invoice.BillingAddressBuilding,
            BillingAddressCompany = invoice.BillingAddressCompany,
            BillingAddressRecipient = invoice.BillingAddressRecipient,
            BillingAddressLine1 = invoice.BillingAddressLine1,
            BillingAddressLine2 = invoice.BillingAddressLine2,
            BillingAddressCity = invoice.BillingAddressCity,
            BillingAddressState = invoice.BillingAddressState,
            BillingAddressCountry = invoice.BillingAddressCountry,
            BillingAddressPostalCode = invoice.BillingAddressPostalCode,
            CreatedDate = now,
            ModifiedDate = now,
        };

        var options = (DbContextOptions<ReceiptDbContext>)receipts.GetService<IDbContextOptions>();
        Exception? failure = null;
        await receipts.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            ReceiptDbContext? owned = null;
            IDbContextTransaction? transaction = null;
            var commitSubmitted = false;
            var cleanupUncertain = false;
            try
            {
                owned = new ReceiptDbContext(options);
                transaction = await owned.Database.BeginTransactionAsync(token);
                owned.Receipts.Add(receipt);
                await owned.SaveChangesAsync(token);
                owned.Items.AddRange(items.Select(item => new ReceiptOrderItem
                {
                    ReceiptId = receipt.Id,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    CreatedDate = item.CreatedDate ?? now,
                    ModifiedDate = item.ModifiedDate ?? now,
                }));
                await owned.SaveChangesAsync(token);
                commitSubmitted = true;
                await transaction.CommitAsync(token);
                await owned.Entry(receipt).ReloadAsync(token);
            }
            catch (Exception exception)
            {
                failure = exception;
                if (!commitSubmitted && transaction is not null)
                {
                    using var rollbackBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await transaction.RollbackAsync(rollbackBudget.Token); }
                    catch (Exception rollbackFailure)
                    {
                        cleanupUncertain = true;
                        failure = new AggregateException(failure, rollbackFailure);
                    }
                }
            }
            finally
            {
                if (transaction is not null)
                {
                    try { await transaction.DisposeAsync(); }
                    catch (Exception disposalFailure)
                    {
                        cleanupUncertain = true;
                        failure = failure is null ? disposalFailure : new AggregateException(failure, disposalFailure);
                    }
                }
                if (owned is not null)
                {
                    try { await owned.DisposeAsync(); }
                    catch (Exception disposalFailure)
                    {
                        cleanupUncertain = true;
                        failure = failure is null ? disposalFailure : new AggregateException(failure, disposalFailure);
                    }
                }
            }
            // Leave failures captured until outside the strategy: generated-ID insertion never replays.
            if (failure is not null && (commitSubmitted || cleanupUncertain))
                failure = new ReceiptWorkflowUnavailableException("Receipt creation outcome is unavailable.", failure);
        }, cancellationToken);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return receipt;
    }

    /// <inheritdoc />
    public async Task LinkInvoiceAsync(int invoiceId, int receiptId, CancellationToken cancellationToken)
    {
        var modified = Now();
        var updated = await invoices.Invoices
            .Where(value => value.Id == invoiceId && (value.ReceiptId == null || value.ReceiptId == receiptId))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.ReceiptId, receiptId)
                    .SetProperty(value => value.ModifiedDate, modified),
                cancellationToken);
        if (updated == 0)
        {
            var current = await invoices.Invoices.AsNoTracking()
                .Where(value => value.Id == invoiceId)
                .Select(value => value.ReceiptId)
                .SingleOrDefaultAsync(cancellationToken);
            if (current != receiptId)
            {
                throw new ReceiptWorkflowDependencyException(
                    $"Invoice {invoiceId} is already linked to a different receipt.");
            }
        }

        await InvalidateInvoiceAsync(invoiceId);
    }

    /// <inheritdoc />
    public async Task LinkFileAsync(
        int receiptId,
        string bucket,
        string objectName,
        CancellationToken cancellationToken)
    {
        if (await receipts.Files.AsNoTracking().AnyAsync(
                value => value.ReceiptId == receiptId && value.Bucket == bucket && value.ObjectName == objectName,
                cancellationToken))
        {
            return;
        }

        var now = Now();
        receipts.Files.Add(new ReceiptFile
        {
            ReceiptId = receiptId,
            Bucket = bucket,
            ObjectName = objectName,
            CreatedDate = now,
            ModifiedDate = now,
        });
        await receipts.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteReceiptAsync(int receiptId, CancellationToken cancellationToken)
    {
        await receipts.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var transaction = await receipts.Database.BeginTransactionAsync(token);
            await receipts.Files.Where(value => value.ReceiptId == receiptId).ExecuteDeleteAsync(token);
            await receipts.Items.Where(value => value.ReceiptId == receiptId).ExecuteDeleteAsync(token);
            await receipts.Receipts.Where(value => value.Id == receiptId).ExecuteDeleteAsync(token);
            await transaction.CommitAsync(token);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UnlinkInvoiceAsync(int invoiceId, int receiptId, CancellationToken cancellationToken)
    {
        var modified = Now();
        var updated = await invoices.Invoices
            .Where(value => value.Id == invoiceId && value.ReceiptId == receiptId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.ReceiptId, (int?)null)
                    .SetProperty(value => value.ModifiedDate, modified),
                cancellationToken);
        if (updated != 0) await InvalidateInvoiceAsync(invoiceId);
    }

    private async Task InvalidateInvoiceAsync(int invoiceId)
    {
        // The normal workflow's UPDATE has committed. Caller abort must not retain the old read.
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await cache.RemoveAsync($"invoice:{invoiceId}", budget.Token);
    }

    // CreatedDate/ModifiedDate are "timestamp without time zone" wall-clock columns storing the
    // UTC instant with Kind stripped; Npgsql rejects Kind=Utc values for that column type.
    private DateTime Now() => DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
}
