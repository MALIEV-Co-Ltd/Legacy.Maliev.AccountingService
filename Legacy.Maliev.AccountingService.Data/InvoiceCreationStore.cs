using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

public sealed class InvoiceCreationStore(InvoiceDbContext context, TimeProvider timeProvider) : IInvoiceCreationStore
{
    public Task<Invoice?> FindByNumberAsync(string invoiceNumber, CancellationToken cancellationToken) => context.Invoices.AsNoTracking().SingleOrDefaultAsync(value => value.Number == invoiceNumber, cancellationToken);

    public async Task<Invoice> CreateAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync(cancellationToken);
        foreach (var item in items) item.InvoiceId = invoice.Id;
        context.Items.AddRange(items);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice;
    }

    public async Task LinkFileAsync(int invoiceId, string bucket, string objectName, CancellationToken cancellationToken)
    {
        if (await context.Files.AnyAsync(value => value.InvoiceId == invoiceId && value.Bucket == bucket && value.ObjectName == objectName, cancellationToken)) return;
        var now = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
        context.Files.Add(new InvoiceFile { InvoiceId = invoiceId, Bucket = bucket, ObjectName = objectName, CreatedDate = now, ModifiedDate = now });
        await context.SaveChangesAsync(cancellationToken);
    }
}
