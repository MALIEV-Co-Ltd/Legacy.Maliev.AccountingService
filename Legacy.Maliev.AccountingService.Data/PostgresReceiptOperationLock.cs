using System.Data;
using System.Data.Common;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Serializes one invoice receipt workflow across replicas with a PostgreSQL session lock.</summary>
public sealed class PostgresReceiptOperationLock(InvoiceDbContext invoices) : IReceiptOperationLock
{
    private const int LockNamespace = 1_729_423_817;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> AcquireAsync(int invoiceId, CancellationToken cancellationToken)
    {
        var connection = invoices.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await ExecuteAsync(connection, "SELECT pg_advisory_lock(@namespace, @invoice)", invoiceId, cancellationToken);
            return new Lease(connection, invoiceId, openedHere);
        }
        catch (Exception exception)
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }

            throw new ReceiptWorkflowDependencyException(
                $"Could not acquire the receipt workflow lock for invoice {invoiceId}.",
                exception);
        }
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        string commandText,
        int invoiceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = 10;
        var namespaceParameter = command.CreateParameter();
        namespaceParameter.ParameterName = "namespace";
        namespaceParameter.Value = LockNamespace;
        command.Parameters.Add(namespaceParameter);
        var invoiceParameter = command.CreateParameter();
        invoiceParameter.ParameterName = "invoice";
        invoiceParameter.Value = invoiceId;
        command.Parameters.Add(invoiceParameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private sealed class Lease(DbConnection connection, int invoiceId, bool closeConnection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await ExecuteAsync(
                        connection,
                        "SELECT pg_advisory_unlock(@namespace, @invoice)",
                        invoiceId,
                        CancellationToken.None);
                }
            }
            finally
            {
                if (closeConnection && connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }
}
