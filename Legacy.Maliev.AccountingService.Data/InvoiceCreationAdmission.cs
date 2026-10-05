using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>A durable admission for one delegated invoice-create intent without the raw editable request.</summary>
public sealed class InvoiceCreationAdmission
{
    public Guid OperationId { get; set; }
    public int QuotationId { get; set; }
    public required string EmployeeSubject { get; set; }
    public required string ServiceSubject { get; set; }
    public required string IntentFingerprint { get; set; }
    public required string State { get; set; }
    public string? ResultJson { get; set; }
    public string? OriginIssuer { get; set; }
    public string? FinancialResultJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Admits only the first delegated request and never replays an uncertain side effect.</summary>
public sealed class InvoiceCreationAdmissionStore(InvoiceDbContext database)
{
    /// <summary>Retained V2 origin fences keep strict actor validation even after feature deactivation.</summary>
    public Task<bool> RequiresOriginValidationAsync(Guid operationId, CancellationToken cancellationToken) =>
        database.InvoiceCreationAdmissions.AsNoTracking().AnyAsync(value => value.OperationId == operationId && value.OriginIssuer != null, cancellationToken);

    /// <summary>Retains a verified origin; uncertain replay is read-only and grants no new authority.</summary>
    public async Task<(bool IsNew, InvoiceCreationResult? Completed, bool NeedsReconciliation)> AdmitAsync(
        Guid operationId, int quotationId, InvoiceNotificationOrigin origin, string fingerprint, CancellationToken cancellationToken)
    {
        ValidateInput(operationId, quotationId, origin);
        if (fingerprint is null || fingerprint.Length != 64 || fingerprint.Any(value => !Uri.IsHexDigit(value))) throw Conflict();
        var inserted = await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "InvoiceCreationAdmission"
                ("OperationID", "QuotationID", "OriginIssuer", "EmployeeSubject", "ServiceSubject", "IntentFingerprint", "State", "CreatedAt", "UpdatedAt")
            VALUES ({operationId}, {quotationId}, {origin.Issuer}, {origin.EmployeeSubject}, {origin.ServiceSubject}, {fingerprint}, 'Pending', now(), now())
            ON CONFLICT ("OperationID") DO NOTHING
            """, cancellationToken);
        if (inserted == 1) return (true, null, false);
        var row = await OriginRowAsync(operationId, quotationId, origin, cancellationToken);
        if (row.IntentFingerprint != fingerprint) throw Conflict();
        if (row.State == "Completed") return (false, ParseResult(row.ResultJson, false), false);
        return (false, null, true);
    }

    /// <summary>Checks retained verified origin; old null issuer rows cannot authorize notification.</summary>
    public async Task ValidateOriginAsync(Guid operationId, int quotationId, InvoiceNotificationOrigin origin,
        bool requirePending, CancellationToken cancellationToken)
    {
        var row = await OriginRowAsync(operationId, quotationId, origin, cancellationToken);
        if (requirePending ? row.State != "Pending" : row.State is not ("Pending" or "NeedsReconciliation" or "Completed"))
            throw Conflict();
    }

    /// <summary>Retains independently persisted financial completion once, before any notification result.</summary>
    public async Task SaveFinancialResultAsync(Guid operationId, int quotationId, InvoiceNotificationOrigin origin,
        InvoiceCreationResult result, CancellationToken cancellationToken)
    {
        ValidateResult(result, true);
        var row = await OriginRowAsync(operationId, quotationId, origin, cancellationToken);
        if (row.State != "Pending") throw Conflict();
        if (!await database.Invoices.AsNoTracking().AnyAsync(value => value.Id == result.InvoiceId, cancellationToken) ||
            !await database.Files.AsNoTracking().AnyAsync(value => value.InvoiceId == result.InvoiceId &&
                value.Bucket == result.StoredFile.Bucket && value.ObjectName == result.StoredFile.ObjectName, cancellationToken)) throw Conflict();
        var json = JsonSerializer.Serialize(result);
        var changed = await database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "InvoiceCreationAdmission" SET "FinancialResultJson"={json}, "UpdatedAt"=now()
            WHERE "OperationID"={operationId} AND "QuotationID"={quotationId} AND "OriginIssuer"={origin.Issuer}
             AND "EmployeeSubject"={origin.EmployeeSubject} AND "ServiceSubject"={origin.ServiceSubject}
             AND "State"='Pending' AND "FinancialResultJson" IS NULL
            """, cancellationToken);
        if (changed == 1) return;
        row = await OriginRowAsync(operationId, quotationId, origin, cancellationToken);
        if (row.State != "Pending" || ParseResult(row.FinancialResultJson, true) != result) throw Conflict();
    }

    /// <summary>Reads only independently retained financial completion; provider status cannot infer it.</summary>
    public async Task<InvoiceCreationResult> ReadFinancialResultAsync(Guid operationId, int quotationId,
        InvoiceNotificationOrigin origin, CancellationToken cancellationToken)
    {
        var row = await OriginRowAsync(operationId, quotationId, origin, cancellationToken);
        if (row.State is not ("Pending" or "NeedsReconciliation" or "Completed")) throw Conflict();
        return ParseResult(row.FinancialResultJson, true);
    }

    /// <summary>Completes bounded reconciliation only for the retained invoice and file after financial completion.</summary>
    public async Task CompleteReconciledAsync(Guid operationId, int quotationId, InvoiceNotificationOrigin origin,
        InvoiceCreationResult result, CancellationToken cancellationToken)
    {
        ValidateResult(result, false);
        if ((int)result.EmailState is not (2 or 3)) throw Conflict();
        var row = await OriginRowAsync(operationId, quotationId, origin, cancellationToken);
        var financial = ParseResult(row.FinancialResultJson, true);
        if (row.State is not ("Pending" or "NeedsReconciliation") || result.InvoiceId != financial.InvoiceId ||
            result.State != financial.State || result.StoredFile != financial.StoredFile) throw Conflict();
        var changed = await database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "InvoiceCreationAdmission" SET "State"='Completed', "ResultJson"={JsonSerializer.Serialize(result)}, "UpdatedAt"=now()
            WHERE "OperationID"={operationId} AND "QuotationID"={quotationId} AND "OriginIssuer"={origin.Issuer}
             AND "EmployeeSubject"={origin.EmployeeSubject} AND "ServiceSubject"={origin.ServiceSubject}
             AND "State" IN ('Pending','NeedsReconciliation') AND "FinancialResultJson"={row.FinancialResultJson}
            """, cancellationToken);
        if (changed != 1) throw Conflict();
    }

    private async Task<InvoiceCreationAdmission> OriginRowAsync(Guid operationId, int quotationId,
        InvoiceNotificationOrigin origin, CancellationToken cancellationToken)
    {
        ValidateInput(operationId, quotationId, origin);
        var row = await database.InvoiceCreationAdmissions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.OperationId == operationId, cancellationToken);
        if (row is null || row.OriginIssuer is null || row.OriginIssuer != origin.Issuer || row.QuotationId != quotationId ||
            row.EmployeeSubject != origin.EmployeeSubject || row.ServiceSubject != origin.ServiceSubject) throw Conflict();
        return row;
    }

    private static void ValidateInput(Guid operationId, int quotationId, InvoiceNotificationOrigin origin)
    {
        if (operationId == Guid.Empty || quotationId <= 0 || origin is null ||
            !ValidText(origin.Issuer, 512) || !ValidText(origin.EmployeeSubject, 256) || !ValidText(origin.ServiceSubject, 128)) throw Conflict();
    }

    private static bool ValidText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Contains('\0', StringComparison.Ordinal);

    private static InvoiceCreationResult ParseResult(string? json, bool financial)
    {
        if (json is null) throw Conflict();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw Conflict();
            var fields = document.RootElement.EnumerateObject().Select(value => value.Name).ToArray();
            if (fields.Length != 5 || new[] { "InvoiceId", "State", "EmailState", "ProviderMessageId", "StoredFile" }
                .Any(value => fields.Count(field => field == value) != 1)) throw Conflict();
            var result = JsonSerializer.Deserialize<InvoiceCreationResult>(json) ?? throw Conflict();
            ValidateResult(result, financial);
            return result;
        }
        catch (JsonException) { throw Conflict(); }
    }

    private static void ValidateResult(InvoiceCreationResult result, bool financial)
    {
        if (result is null || result.InvoiceId <= 0 || (int)result.State is not (0 or 1) ||
            (int)result.EmailState is < 0 or > 3 || result.StoredFile is null ||
            !ValidText(result.StoredFile.Bucket, 256) || !ValidText(result.StoredFile.ObjectName, 1024) ||
            financial && (result.State != InvoiceCreationState.Completed || result.EmailState != InvoiceCreationEmailState.NotRequested ||
                result.ProviderMessageId is not null) ||
            (int)result.EmailState == 3 && !ValidText(result.ProviderMessageId, 256) ||
            (int)result.EmailState == 2 && result.ProviderMessageId is not null) throw Conflict();
    }

    private static InvoiceCreationConflictException Conflict() => new("The invoice-create admission requires reconciliation.");

    public static string Fingerprint(CreateInvoiceFromQuotationRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));

    public async Task<(bool IsNew, InvoiceCreationResult? Completed)> AdmitAsync(
        Guid operationId, int quotationId, string employeeSubject, string serviceSubject,
        string fingerprint, CancellationToken cancellationToken)
    {
        var inserted = await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "InvoiceCreationAdmission"
                ("OperationID", "QuotationID", "EmployeeSubject", "ServiceSubject", "IntentFingerprint", "State", "CreatedAt", "UpdatedAt")
            VALUES ({operationId}, {quotationId}, {employeeSubject}, {serviceSubject}, {fingerprint}, 'Pending', now(), now())
            ON CONFLICT ("OperationID") DO NOTHING
            """, cancellationToken);
        if (inserted == 1) return (true, null);

        var existing = await database.InvoiceCreationAdmissions.AsNoTracking()
            .SingleAsync(value => value.OperationId == operationId, cancellationToken);
        if (existing.QuotationId != quotationId || existing.EmployeeSubject != employeeSubject ||
            existing.ServiceSubject != serviceSubject || existing.IntentFingerprint != fingerprint)
            throw new InvoiceCreationConflictException("The operation key is bound to a different invoice-create intent or actor.");
        if (existing.State == "Completed" && existing.ResultJson is not null)
        {
            try
            {
                return (false, JsonSerializer.Deserialize<InvoiceCreationResult>(existing.ResultJson)
                    ?? throw new InvoiceCreationConflictException("The prior invoice-create result requires reconciliation."));
            }
            catch (JsonException)
            {
                throw new InvoiceCreationConflictException("The prior invoice-create result requires reconciliation.");
            }
        }
        throw new InvoiceCreationConflictException("The prior invoice-create outcome is uncertain; reconciliation is required before retry.");
    }

    public async Task CompleteAsync(Guid operationId, InvoiceCreationResult result, CancellationToken cancellationToken)
    {
        var transitioned = await database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "InvoiceCreationAdmission" SET "State" = 'Completed', "ResultJson" = {JsonSerializer.Serialize(result)},
                "UpdatedAt" = now()
            WHERE "OperationID" = {operationId} AND "State" = 'Pending'
            """, cancellationToken);
        if (transitioned != 1)
            throw new InvoiceCreationConflictException("The invoice-create admission requires reconciliation.");
    }

    public Task MarkUncertainAsync(Guid operationId, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "InvoiceCreationAdmission" SET "State" = 'NeedsReconciliation', "UpdatedAt" = now()
            WHERE "OperationID" = {operationId} AND "State" = 'Pending'
            """, cancellationToken);
}
