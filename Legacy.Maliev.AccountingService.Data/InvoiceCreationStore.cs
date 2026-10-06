using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Runtime.ExceptionServices;

namespace Legacy.Maliev.AccountingService.Data;

public sealed class InvoiceCreationStore(InvoiceDbContext context, TimeProvider timeProvider) : IInvoiceCreationStore
{
    public Task<Invoice?> FindByNumberAsync(string invoiceNumber, CancellationToken cancellationToken) => context.Invoices.AsNoTracking().SingleOrDefaultAsync(value => value.Number == invoiceNumber, cancellationToken);

    public Task<Invoice> CreateAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items, CancellationToken cancellationToken) =>
        CreateCoreAsync(invoice, items, null, cancellationToken);

    /// <summary>Commits invoice, items and verified ownership together; unknown acknowledgement never retries creation.</summary>
    public Task<Invoice> CreateAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items,
        InvoiceFinancialCommitContext authority, CancellationToken cancellationToken) =>
        CreateCoreAsync(invoice, items, authority, cancellationToken);

    private async Task<Invoice> CreateCoreAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items,
        InvoiceFinancialCommitContext? authority, CancellationToken cancellationToken)
    {
        var options = (DbContextOptions<InvoiceDbContext>)context.GetService<IDbContextOptions>();
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;
        Exception? failure = null;
        await strategy.ExecuteAsync(async () =>
        {
            if (Interlocked.Increment(ref attempts) != 1)
                throw new InvoiceCreationUnavailableException("Invoice creation outcome is unavailable.", failure);
            InvoiceDbContext? owned = null;
            IDbContextTransaction? transaction = null;
            var commitSubmitted = false;
            var cleanupUncertain = false;
            try
            {
                owned = new InvoiceDbContext(options);
                transaction = await owned.Database.BeginTransactionAsync(cancellationToken);
                if (authority is not null)
                    await new InvoiceCreationAdmissionStore(owned).ValidateOriginAsync(authority.OperationId,
                        authority.QuotationId, authority.Origin, true, cancellationToken);
                owned.Invoices.Add(invoice);
                await owned.SaveChangesAsync(cancellationToken);
                foreach (var item in items) item.InvoiceId = invoice.Id;
                owned.Items.AddRange(items);
                await owned.SaveChangesAsync(cancellationToken);
                if (authority is not null)
                    await InvoiceFinancialOwnershipStore.RetainAsync(owned, invoice.Id, authority, cancellationToken);
                commitSubmitted = true;
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                failure = exception;
                if (!commitSubmitted && transaction is not null)
                {
                    // Cleanup must survive caller abort, but remains bounded by the configured command budget.
                    using var rollbackBudget = new CancellationTokenSource(TimeSpan.FromSeconds(owned!.Database.GetCommandTimeout() ?? 120));
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
            // Propagate only outside the strategy: neither generated IDs nor an uncertain COMMIT may replay.
            if (failure is not null && (commitSubmitted || cleanupUncertain))
                failure = new InvoiceCreationUnavailableException("Invoice creation outcome is unavailable.", failure);
        });
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
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
