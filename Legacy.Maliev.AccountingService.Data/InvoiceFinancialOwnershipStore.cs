using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Legacy.Maliev.AccountingService.Domain.Invoice;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Reads committed financial authority from an immutable receipt and current database rows.</summary>
public sealed class InvoiceFinancialOwnershipStore(InvoiceDbContext context) : IInvoiceFinancialOwnershipReader
{
    private sealed record Retained(InvoiceFinancialOwnership Receipt, string IntentFingerprint, string InvoiceJson, string ItemsJson);

    /// <summary>Legacy by-number reuse cannot bypass a retained employee financial or side-effect fence.</summary>
    public Task<bool> HasFenceAsync(int invoiceId, CancellationToken cancellationToken) => context.Database.SqlQuery<bool>($"""
        SELECT EXISTS (SELECT 1 FROM "InvoiceCreationAdmission"
          WHERE ("FinancialOwnershipJson" IS NOT NULL AND
                 "FinancialOwnershipJson"::jsonb->'Receipt'->>'InvoiceId'={invoiceId.ToString(CultureInfo.InvariantCulture)})
             OR ("EmployeeCompletionJson" IS NOT NULL AND
                 "EmployeeCompletionJson"::jsonb->>'InvoiceId'={invoiceId.ToString(CultureInfo.InvariantCulture)})) AS "Value"
        """).SingleAsync(cancellationToken);

    public async Task MarkDecisionUncertainAsync(Guid operationId, int quotationId, InvoiceNotificationOrigin origin,
        CancellationToken cancellationToken)
    {
        var admissions = new InvoiceCreationAdmissionStore(context);
        await admissions.ValidateOriginAsync(operationId, quotationId, origin, false, cancellationToken);
        await admissions.MarkUncertainAsync(operationId, cancellationToken);
    }

    public Task ValidatePendingAsync(Guid operationId, int quotationId, InvoiceNotificationOrigin origin, CancellationToken cancellationToken) =>
        new InvoiceCreationAdmissionStore(context).ValidateOriginAsync(operationId, quotationId, origin, true, cancellationToken);

    public async Task<InvoiceFinancialOwnership?> ReadForOriginAsync(Guid operationId, int quotationId,
        InvoiceNotificationOrigin origin, CancellationToken cancellationToken)
    {
        await new InvoiceCreationAdmissionStore(context).ValidateOriginAsync(operationId, quotationId, origin, false, cancellationToken);
        var retained = await context.InvoiceCreationAdmissions.AsNoTracking().AnyAsync(
            value => value.OperationId == operationId && value.FinancialOwnershipJson != null, cancellationToken);
        return retained ? await ReadAsync(operationId, cancellationToken) : null;
    }

    /// <summary>Uses one bounded, consistent read snapshot; no admission or financial data is changed.</summary>
    public async Task<InvoiceFinancialOwnership> ReadAsync(Guid operationId, CancellationToken cancellationToken) =>
        (await ReadCommittedAsync(operationId, cancellationToken)).Ownership;

    /// <summary>Returns the exact verified persisted rows for later document work, without mapping current source data.</summary>
    public Task<InvoiceCommittedFinancialSnapshot> ReadCommittedAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty) throw Conflict();
        var options = (DbContextOptions<InvoiceDbContext>)context.GetService<IDbContextOptions>();
        // Every retry owns a new context and re-reads the complete read-only snapshot.
        // This never changes the one-attempt financial COMMIT fence.
        return context.Database.CreateExecutionStrategy().ExecuteAsync(() => ReadCoreAsync(options, operationId, cancellationToken));
    }

    private static async Task<InvoiceCommittedFinancialSnapshot> ReadCoreAsync(DbContextOptions<InvoiceDbContext> options,
        Guid operationId, CancellationToken cancellationToken)
    {
        await using var database = new InvoiceDbContext(options);
        database.Database.SetCommandTimeout(15);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var row = await database.InvoiceCreationAdmissions.AsNoTracking().SingleOrDefaultAsync(
            value => value.OperationId == operationId, cancellationToken);
        if (row?.OriginIssuer is null || row.FinancialOwnershipJson is null ||
            row.State is not ("Pending" or "NeedsReconciliation" or "Completed")) throw Conflict();
        Retained retained;
        try { retained = JsonSerializer.Deserialize<Retained>(row.FinancialOwnershipJson) ?? throw Conflict(); }
        catch (JsonException) { throw Conflict(); }
        var receipt = retained.Receipt;
        if (receipt is null || receipt.ContractVersion != 1 || receipt.OperationId != operationId ||
            receipt.QuotationId != row.QuotationId || receipt.InvoiceId <= 0 ||
            receipt.OriginIssuer != row.OriginIssuer || receipt.EmployeeSubject != row.EmployeeSubject ||
            receipt.RequesterSubject != row.ServiceSubject || receipt.RequesterSubject != "service:legacy-intranet" ||
            retained.IntentFingerprint != row.IntentFingerprint ||
            !DateTime.TryParseExact(receipt.OriginalQuotationVersion, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var version) || version.Kind != DateTimeKind.Utc || version.Ticks <= 0 ||
            receipt.OriginalQuotationVersion != version.ToString("O", CultureInfo.InvariantCulture) ||
            receipt.FinancialBinding != Binding(receipt, retained.IntentFingerprint, retained.InvoiceJson, retained.ItemsJson))
            throw Conflict();
        var current = await SnapshotAsync(database, receipt.InvoiceId, cancellationToken);
        if (current.InvoiceJson != retained.InvoiceJson || current.ItemsJson != retained.ItemsJson) throw Conflict();
        await transaction.CommitAsync(cancellationToken);
        return new(receipt, current.Invoice, current.Items);
    }

    internal static async Task RetainAsync(InvoiceDbContext database, int invoiceId,
        InvoiceFinancialCommitContext authority, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null || authority.OriginalQuotationVersion.Kind == DateTimeKind.Local ||
            authority.OriginalQuotationVersion.Ticks <= 0 || authority.Origin.ServiceSubject != "service:legacy-intranet") throw Conflict();
        var row = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(
            value => value.OperationId == authority.OperationId, cancellationToken);
        if (row.State != "Pending" || row.FinancialOwnershipJson is not null || row.OriginIssuer != authority.Origin.Issuer ||
            row.EmployeeSubject != authority.Origin.EmployeeSubject || row.ServiceSubject != authority.Origin.ServiceSubject ||
            row.QuotationId != authority.QuotationId) throw Conflict();
        var snapshot = await SnapshotAsync(database, invoiceId, cancellationToken);
        var receipt = new InvoiceFinancialOwnership(1, authority.OperationId, authority.QuotationId, invoiceId,
            authority.Origin.Issuer, authority.Origin.EmployeeSubject, authority.Origin.ServiceSubject,
            DateTime.SpecifyKind(authority.OriginalQuotationVersion, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture), "");
        receipt = receipt with { FinancialBinding = Binding(receipt, row.IntentFingerprint, snapshot.InvoiceJson, snapshot.ItemsJson) };
        var json = JsonSerializer.Serialize(new Retained(receipt, row.IntentFingerprint, snapshot.InvoiceJson, snapshot.ItemsJson));
        var updated = await database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "InvoiceCreationAdmission" SET "FinancialOwnershipJson"={json}, "UpdatedAt"=now()
            WHERE "OperationID"={authority.OperationId} AND "QuotationID"={authority.QuotationId}
             AND "OriginIssuer"={authority.Origin.Issuer} AND "EmployeeSubject"={authority.Origin.EmployeeSubject}
             AND "ServiceSubject"={authority.Origin.ServiceSubject} AND "State"='Pending' AND "FinancialOwnershipJson" IS NULL
            """, cancellationToken);
        if (updated != 1) throw Conflict();
    }

    private static string Binding(InvoiceFinancialOwnership receipt, string intentFingerprint, string invoiceJson, string itemsJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { BindingVersion = "invoice-creation-financial-v1", Receipt = receipt with { FinancialBinding = "" }, IntentFingerprint = intentFingerprint, InvoiceJson = invoiceJson, ItemsJson = itemsJson }))));

    private static async Task<(string InvoiceJson, string ItemsJson, Invoice Invoice, IReadOnlyList<InvoiceOrderItem> Items)> SnapshotAsync(InvoiceDbContext database,
        int invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await database.Invoices.AsNoTracking().SingleOrDefaultAsync(value => value.Id == invoiceId, cancellationToken);
        if (invoice is null) throw Conflict();
        var items = await database.Items.AsNoTracking().Where(value => value.InvoiceId == invoiceId)
            .OrderBy(value => value.Id).ToArrayAsync(cancellationToken);
        if (items.Length == 0) throw Conflict();
        // Only mapped scalar columns enter the frame: no navigation, credential or request object.
        SortedDictionary<string, object?> Scalars(object value)
        {
            var properties = database.Model.FindEntityType(value.GetType())?.GetProperties() ?? throw Conflict();
            var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in properties)
                result.Add(property.Name, property.PropertyInfo?.GetValue(value));
            return result;
        }
        return (JsonSerializer.Serialize(Scalars(invoice)), JsonSerializer.Serialize(items.Select(value => Scalars(value))), invoice, items);
    }

    private static InvoiceCreationConflictException Conflict() => new("Verified committed financial ownership requires reconciliation.");
}
