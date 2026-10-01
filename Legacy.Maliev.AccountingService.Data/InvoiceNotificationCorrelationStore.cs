using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Non-expiring invoice-purpose authority; neither replay nor read grants execution.</summary>
public sealed class InvoiceNotificationCorrelationStore(
    InvoiceDbContext context, TimeProvider timeProvider, IInvoiceNotificationBindingKeyring keyring) : IInvoiceNotificationCorrelationStore
{
    /// <summary>Admits only a known fresh caller with matching Pending origin; retained replay is read-only.</summary>
    public Task<InvoiceNotificationCorrelation> AdmitAsync(InvoiceNotificationCorrelationIdentity identity,
        string payloadDigest, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = InvoiceNotificationCorrelationBinding.Frame(identity, "validation", payloadDigest);
        CryptographicOperations.ZeroMemory(validation);
        return AttemptAsync(async (owned, token) =>
        {
            var existing = await owned.InvoiceNotificationCorrelations.FromSqlInterpolated($"""
                SELECT * FROM public."InvoiceNotificationCorrelation" WHERE "IntentID"={identity.IntentId} FOR UPDATE
                """).AsNoTracking().SingleOrDefaultAsync(token);
            if (existing is not null)
            {
                if (Identity(existing) != identity) throw new InvoiceNotificationCorrelationConflictException();
                var key = Key(existing.BindingKeyId);
                byte[] binding;
                try { binding = InvoiceNotificationCorrelationBinding.Compute(identity, existing.BindingKeyId, key, payloadDigest); }
                finally { CryptographicOperations.ZeroMemory(key); }
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(binding, existing.PayloadBinding))
                        throw new InvoiceNotificationCorrelationConflictException();
                }
                finally { CryptographicOperations.ZeroMemory(binding); }
                return Snapshot(existing);
            }

            if (await owned.InvoiceNotificationCorrelations.FromSqlInterpolated($"""
                SELECT * FROM public."InvoiceNotificationCorrelation"
                WHERE "InvoiceID"={identity.InvoiceId} AND "Purpose"={identity.Purpose} FOR UPDATE
                """).AsNoTracking().AnyAsync(token)) throw new InvoiceNotificationCorrelationConflictException();
            var invoiceExists = await owned.Database.SqlQuery<bool>($"""
                SELECT true AS "Value" FROM public."Invoice" WHERE "ID"={identity.InvoiceId} FOR SHARE
                """).SingleOrDefaultAsync(token);
            var origin = await owned.InvoiceCreationAdmissions.FromSqlInterpolated($"""
                SELECT * FROM public."InvoiceCreationAdmission" WHERE "OperationID"={identity.WorkflowOperationId} FOR SHARE
                """).AsNoTracking().SingleOrDefaultAsync(token);
            if (!invoiceExists || origin is null || origin.QuotationId != identity.QuotationId ||
                origin.EmployeeSubject != identity.Origin.EmployeeSubject || origin.ServiceSubject != identity.Origin.ServiceSubject ||
                origin.State != "Pending" || origin.ResultJson is not null) throw new InvoiceNotificationCorrelationConflictException();

            var keyId = keyring.ActiveKeyId;
            var activeKey = Key(keyId);
            byte[] newBinding;
            try { newBinding = InvoiceNotificationCorrelationBinding.Compute(identity, keyId, activeKey, payloadDigest); }
            finally { CryptographicOperations.ZeroMemory(activeKey); }
            var now = timeProvider.GetUtcNow();
            var row = new InvoiceNotificationCorrelationRow
            {
                IntentId = identity.IntentId,
                InvoiceId = identity.InvoiceId,
                Purpose = identity.Purpose,
                QuotationId = identity.QuotationId,
                WorkflowOperationId = identity.WorkflowOperationId,
                OriginIssuer = identity.Origin.Issuer,
                OriginEmployeeSubject = identity.Origin.EmployeeSubject,
                OriginServiceSubject = identity.Origin.ServiceSubject,
                SenderIssuer = identity.SenderIssuer,
                SenderServiceSubject = identity.SenderServiceSubject,
                PayloadFrameVersion = identity.PayloadFrameVersion,
                BindingVersion = identity.BindingVersion,
                BindingKeyId = keyId,
                PayloadBinding = newBinding,
                Phase = "Prepared",
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
            owned.InvoiceNotificationCorrelations.Add(row);
            await owned.SaveChangesAsync(token);
            return Snapshot(row);
        }, cancellationToken);
    }

    /// <summary>Missing intent is not no-send proof; missing retained key or shape is unavailable.</summary>
    public Task<InvoiceNotificationCorrelation?> ReadAsync(Guid intentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (intentId == Guid.Empty) throw new ArgumentException("Invalid invoice notification correlation identity.");
        return AttemptAsync(async (owned, token) =>
        {
            var row = await owned.InvoiceNotificationCorrelations.AsNoTracking().SingleOrDefaultAsync(value => value.IntentId == intentId, token);
            if (row is null) return null;
            var retained = Key(row.BindingKeyId);
            CryptographicOperations.ZeroMemory(retained);
            return Snapshot(row);
        }, cancellationToken);
    }

    private byte[] Key(string keyId)
    {
        var key = keyring.Find(keyId);
        if (key is null || key.Value.Length != 32) throw new InvoiceNotificationCorrelationUnavailableException();
        return key.Value.ToArray();
    }

    private static InvoiceNotificationCorrelationIdentity Identity(InvoiceNotificationCorrelationRow row) => new(
        row.IntentId, row.InvoiceId, row.Purpose, row.QuotationId, row.WorkflowOperationId,
        new(row.OriginIssuer, row.OriginEmployeeSubject, row.OriginServiceSubject), row.SenderIssuer,
        row.SenderServiceSubject, row.PayloadFrameVersion, row.BindingVersion);

    private static InvoiceNotificationCorrelation Snapshot(InvoiceNotificationCorrelationRow row)
    {
        var frame = InvoiceNotificationCorrelationBinding.Frame(Identity(row), row.BindingKeyId, new string('a', 64));
        CryptographicOperations.ZeroMemory(frame);
        if (row.PayloadBinding.Length != 32) throw new InvoiceNotificationCorrelationUnavailableException();
        return new(Identity(row), row.BindingKeyId, row.PayloadBinding, row.Phase, row.Version, row.RemoteVersion,
            row.CreatedAt, row.UpdatedAt, row.AdmissionIssuedAt, row.ExecutionIssuedAt);
    }

    private async Task<T> AttemptAsync<T>(Func<InvoiceDbContext, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var options = (DbContextOptions<InvoiceDbContext>)context.GetService<IDbContextOptions>();
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;
        T result = default!;
        Exception? failure = null;
        await strategy.ExecuteAsync(async () =>
        {
            if (Interlocked.Increment(ref attempts) != 1) throw new InvoiceNotificationCorrelationUnavailableException(failure);
            InvoiceDbContext? owned = null;
            IDbContextTransaction? transaction = null;
            var commitSubmitted = false;
            var cleanupUnknown = false;
            try
            {
                owned = new InvoiceDbContext(options);
                transaction = await owned.Database.BeginTransactionAsync(cancellationToken);
                await owned.Database.ExecuteSqlRawAsync("""LOCK TABLE public."InvoiceNotificationCorrelation" IN ROW EXCLUSIVE MODE""", cancellationToken);
                await InvoiceNotificationCorrelationReadiness.RequireAsync(owned, cancellationToken);
                result = await action(owned, cancellationToken);
                commitSubmitted = true;
                await transaction.CommitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception exception)
            {
                failure = exception;
                if (!commitSubmitted && transaction is not null)
                {
                    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(owned!.Database.GetCommandTimeout() ?? 120));
                    try { await transaction.RollbackAsync(budget.Token); }
                    catch (Exception rollback) { cleanupUnknown = true; failure = new AggregateException(failure, rollback); }
                }
                if (!commitSubmitted && !cleanupUnknown && exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
                    failure = new InvoiceNotificationCorrelationConflictException();
                if (!commitSubmitted && !cleanupUnknown && exception is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn })
                    failure = new InvoiceNotificationCorrelationUnavailableException(exception);
            }
            finally
            {
                if (transaction is not null)
                {
                    try { await transaction.DisposeAsync(); }
                    catch (Exception disposal) { cleanupUnknown = true; failure = failure is null ? disposal : new AggregateException(failure, disposal); }
                }
                if (owned is not null)
                {
                    try { await owned.DisposeAsync(); }
                    catch (Exception disposal) { cleanupUnknown = true; failure = failure is null ? disposal : new AggregateException(failure, disposal); }
                }
            }
            if (failure is not null && (commitSubmitted || cleanupUnknown)) failure = new InvoiceNotificationCorrelationUnavailableException(failure);
        });
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
}
