using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Persists receipt workflow state using only the existing invoice and receipt schemas.</summary>
public sealed class ReceiptWorkflowStore(
    InvoiceDbContext invoices,
    ReceiptDbContext receipts,
    TimeProvider timeProvider) : IReceiptWorkflowStore
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

        var now = timeProvider.GetUtcNow().UtcDateTime;
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

        await using var transaction = await receipts.Database.BeginTransactionAsync(cancellationToken);
        receipts.Receipts.Add(receipt);
        await receipts.SaveChangesAsync(cancellationToken);
        receipts.Items.AddRange(items.Select(item => new ReceiptOrderItem
        {
            ReceiptId = receipt.Id,
            Description = item.Description,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            CreatedDate = item.CreatedDate ?? now,
            ModifiedDate = item.ModifiedDate ?? now,
        }));
        await receipts.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await receipts.Entry(receipt).ReloadAsync(cancellationToken);
        return receipt;
    }

    /// <inheritdoc />
    public async Task LinkInvoiceAsync(int invoiceId, int receiptId, CancellationToken cancellationToken)
    {
        var modified = timeProvider.GetUtcNow().UtcDateTime;
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

        var now = timeProvider.GetUtcNow().UtcDateTime;
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
        await using var transaction = await receipts.Database.BeginTransactionAsync(cancellationToken);
        await receipts.Files.Where(value => value.ReceiptId == receiptId).ExecuteDeleteAsync(cancellationToken);
        await receipts.Items.Where(value => value.ReceiptId == receiptId).ExecuteDeleteAsync(cancellationToken);
        await receipts.Receipts.Where(value => value.Id == receiptId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UnlinkInvoiceAsync(int invoiceId, int receiptId, CancellationToken cancellationToken)
    {
        var modified = timeProvider.GetUtcNow().UtcDateTime;
        await invoices.Invoices
            .Where(value => value.Id == invoiceId && value.ReceiptId == receiptId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.ReceiptId, (int?)null)
                    .SetProperty(value => value.ModifiedDate, modified),
                cancellationToken);
    }
}
