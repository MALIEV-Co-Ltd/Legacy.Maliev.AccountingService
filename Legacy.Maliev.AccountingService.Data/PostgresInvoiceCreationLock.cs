using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Legacy.Maliev.AccountingService.Data;

public sealed class PostgresInvoiceCreationLock(InvoiceDbContext context) : IInvoiceCreationLock
{
    public async ValueTask<IAsyncDisposable> AcquireAsync(int quotationId, CancellationToken cancellationToken)
    {
        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock({0}, {1})", [0x494E5643, quotationId], cancellationToken);
            return new Lease(context, quotationId);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            throw new InvoiceCreationUnavailableException("Invoice creation lock is unavailable.", exception);
        }
    }

    private sealed class Lease(InvoiceDbContext context, int quotationId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0}, {1})", [0x494E5643, quotationId]); }
            finally { await context.Database.CloseConnectionAsync(); }
        }
    }
}
