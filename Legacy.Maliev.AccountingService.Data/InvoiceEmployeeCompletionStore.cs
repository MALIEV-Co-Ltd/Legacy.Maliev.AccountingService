using System.Globalization;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Single-use document/send fences; ambiguous acknowledgments never issue a new external attempt.</summary>
public sealed class InvoiceEmployeeCompletionStore(InvoiceDbContext database) : IInvoiceEmployeeCompletionStore
{
    public async Task<InvoiceEmployeeCompletionPhase?> ReadAsync(InvoiceFinancialOwnership ownership, CancellationToken cancellationToken)
    {
        var row = await RowAsync(ownership, cancellationToken);
        var phase = ParsePhase(row.EmployeeCompletionJson, ownership);
        if (phase?.StoredFile is not null && !await database.Files.AsNoTracking().AnyAsync(value => value.InvoiceId == ownership.InvoiceId &&
            value.Bucket == phase.StoredFile.Bucket && value.ObjectName == phase.StoredFile.ObjectName, cancellationToken)) throw Conflict();
        if (phase is not null && row.State == "Completed" && row.ResultJson is not null)
            return phase with { Result = ParseResult(row.ResultJson, phase) };
        return phase;
    }

    public async Task<bool> BeginDocumentAsync(InvoiceFinancialOwnership ownership, InvoiceQuotationOperationReceipt decision,
        CancellationToken cancellationToken)
    {
        if (decision.ContractVersion != 1 || decision.State != "Completed" || decision.CompletedOrders != decision.TotalOrders || decision.TotalOrders < 0 ||
            decision.OperationId != ownership.OperationId.ToString("D") || decision.QuotationId != ownership.QuotationId || decision.InvoiceId != ownership.InvoiceId ||
            decision.OriginIssuer != ownership.OriginIssuer || decision.EmployeeSubject != ownership.EmployeeSubject || decision.RequesterSubject != ownership.RequesterSubject ||
            decision.ExecutorSubject != "service:legacy-accounting" || decision.OriginalQuotationVersion != ownership.OriginalQuotationVersion ||
            decision.FinancialBinding != ownership.FinancialBinding || decision.FinancialBindingVersion != "invoice-creation-financial-v1" || !Utc(decision.DecisionOrderVersion)) throw Conflict();
        var row = await RowAsync(ownership, cancellationToken);
        if (row.EmployeeCompletionJson is not null || row.ResultJson is not null || row.FinancialResultJson is not null || row.State == "Completed") return false;
        var phase = new InvoiceEmployeeCompletionPhase(1, ownership.OperationId, ownership.QuotationId, ownership.InvoiceId, ownership.FinancialBinding,
            decision.DecisionOrderVersion, decision.TotalOrders, "DocumentExecuting", null, null, null);
        return await database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "InvoiceCreationAdmission" SET "EmployeeCompletionJson"={JsonSerializer.Serialize(phase)}, "UpdatedAt"=now()
            WHERE "OperationID"={ownership.OperationId} AND "FinancialOwnershipJson"={row.FinancialOwnershipJson}
             AND "State" IN ('Pending','NeedsReconciliation') AND "EmployeeCompletionJson" IS NULL
             AND "FinancialResultJson" IS NULL AND "ResultJson" IS NULL
            """, cancellationToken) == 1;
    }

    public async Task<InvoiceEmployeeCompletionPhase> RetainDocumentAsync(InvoiceFinancialOwnership ownership, string pdfSha256,
        InvoiceCreationStoredFile stored, CancellationToken cancellationToken)
    {
        if (!Digest(pdfSha256) || stored.Bucket != "maliev.com" || string.IsNullOrWhiteSpace(stored.ObjectName) ||
            stored.ObjectName.Length > 1024 || !stored.ObjectName.StartsWith($"invoices/{ownership.InvoiceId}/", StringComparison.Ordinal)) throw Conflict();
        var row = await RowAsync(ownership, cancellationToken);
        var phase = ParsePhase(row.EmployeeCompletionJson, ownership) ?? throw Conflict();
        var ready = phase with { State = "DocumentReady", PdfSha256 = pdfSha256, StoredFile = stored };
        if (phase.State == "DocumentReady")
        {
            if (phase != ready) throw Conflict();
            return phase;
        }
        if (phase.State != "DocumentExecuting" || !await database.Files.AsNoTracking().AnyAsync(value => value.InvoiceId == ownership.InvoiceId &&
            value.Bucket == stored.Bucket && value.ObjectName == stored.ObjectName, cancellationToken)) throw Conflict();
        var financial = new InvoiceCreationResult(ownership.InvoiceId, InvoiceCreationState.Completed, InvoiceCreationEmailState.NotRequested, null, stored);
        _ = await database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "InvoiceCreationAdmission" SET "EmployeeCompletionJson"={JsonSerializer.Serialize(ready)},
             "FinancialResultJson"={JsonSerializer.Serialize(financial)}, "UpdatedAt"=now()
            WHERE "OperationID"={ownership.OperationId} AND "FinancialOwnershipJson"={row.FinancialOwnershipJson}
             AND "EmployeeCompletionJson"={row.EmployeeCompletionJson} AND "FinancialResultJson" IS NULL
             AND "State" IN ('Pending','NeedsReconciliation')
            """, cancellationToken);
        var retained = await ReadAsync(ownership, cancellationToken) ?? throw Conflict();
        if (retained != ready) throw Conflict();
        return retained;
    }

    public async Task<bool> BeginNotificationAsync(InvoiceFinancialOwnership ownership, CancellationToken cancellationToken)
    {
        var row = await RowAsync(ownership, cancellationToken);
        var phase = ParsePhase(row.EmployeeCompletionJson, ownership) ?? throw Conflict();
        if (phase.State != "DocumentReady" || row.FinancialResultJson is null || row.ResultJson is not null) return false;
        var financial = ParseResult(row.FinancialResultJson, phase);
        if (financial.EmailState != InvoiceCreationEmailState.NotRequested || financial.ProviderMessageId is not null) throw Conflict();
        // This acknowledged single-use marker is the only path back to Pending, before
        // the first notification attempt. Existing V2 correlations prohibit a new send.
        var executing = phase with { State = "NotificationExecuting" };
        return await database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "InvoiceCreationAdmission" AS admission SET "EmployeeCompletionJson"={JsonSerializer.Serialize(executing)},
             "State"='Pending', "UpdatedAt"=now()
            WHERE admission."OperationID"={ownership.OperationId} AND admission."FinancialOwnershipJson"={row.FinancialOwnershipJson}
             AND admission."EmployeeCompletionJson"={row.EmployeeCompletionJson} AND admission."FinancialResultJson"={row.FinancialResultJson}
             AND admission."ResultJson" IS NULL AND admission."State" IN ('Pending','NeedsReconciliation')
             AND NOT EXISTS (SELECT 1 FROM "InvoiceNotificationCorrelation" AS correlation
                 WHERE correlation."InvoiceID"={ownership.InvoiceId} AND correlation."Purpose"='invoice-issued')
            """, cancellationToken) == 1;
    }

    public async Task RetainCompletedAsync(InvoiceFinancialOwnership ownership, InvoiceCreationResult result, CancellationToken cancellationToken)
    {
        var row = await RowAsync(ownership, cancellationToken);
        var phase = ParsePhase(row.EmployeeCompletionJson, ownership) ?? throw Conflict();
        _ = ParseResult(JsonSerializer.Serialize(result), phase);
        if (result.EmailState == InvoiceCreationEmailState.ExplicitRetryRequired || phase.State == "DocumentExecuting" ||
            phase.State == "DocumentReady" && result.EmailState != InvoiceCreationEmailState.NotRequested ||
            phase.State == "NotificationExecuting" && result.EmailState == InvoiceCreationEmailState.NotRequested) throw Conflict();
        if (row.ResultJson is not null && ParseResult(row.ResultJson, phase) != result) throw Conflict();
        var completed = phase with { State = "Completed", Result = result };
        if (phase.State == "Completed") { if (phase != completed) throw Conflict(); return; }
        _ = await database.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "InvoiceCreationAdmission" SET "EmployeeCompletionJson"={JsonSerializer.Serialize(completed)},
             "State"='Completed', "ResultJson"={JsonSerializer.Serialize(result)}, "UpdatedAt"=now()
            WHERE "OperationID"={ownership.OperationId} AND "FinancialOwnershipJson"={row.FinancialOwnershipJson}
             AND "EmployeeCompletionJson"={row.EmployeeCompletionJson} AND "FinancialResultJson"={row.FinancialResultJson}
             AND ("ResultJson" IS NULL OR "ResultJson"={JsonSerializer.Serialize(result)})
             AND "State" IN ('Pending','NeedsReconciliation','Completed')
            """, cancellationToken);
        if (await ReadAsync(ownership, cancellationToken) != completed) throw Conflict();
    }

    private async Task<InvoiceCreationAdmission> RowAsync(InvoiceFinancialOwnership ownership, CancellationToken cancellationToken)
    {
        if (await new InvoiceFinancialOwnershipStore(database).ReadAsync(ownership.OperationId, cancellationToken) != ownership) throw Conflict();
        var row = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == ownership.OperationId, cancellationToken);
        if (row.FinancialOwnershipJson is null || row.State is not ("Pending" or "NeedsReconciliation" or "Completed")) throw Conflict();
        return row;
    }

    private static InvoiceEmployeeCompletionPhase? ParsePhase(string? json, InvoiceFinancialOwnership ownership)
    {
        if (json is null) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            Exact(document.RootElement, ["ContractVersion", "OperationId", "QuotationId", "InvoiceId", "FinancialBinding", "DecisionOrderVersion", "TotalOrders", "State", "PdfSha256", "StoredFile", "Result"]);
            var phase = JsonSerializer.Deserialize<InvoiceEmployeeCompletionPhase>(json) ?? throw Conflict();
            if (phase.ContractVersion != 1 || phase.OperationId != ownership.OperationId || document.RootElement.GetProperty("OperationId").GetString() != ownership.OperationId.ToString("D") ||
                phase.QuotationId != ownership.QuotationId || phase.InvoiceId != ownership.InvoiceId || phase.FinancialBinding != ownership.FinancialBinding ||
                !Utc(phase.DecisionOrderVersion) || phase.TotalOrders < 0 || phase.State is not ("DocumentExecuting" or "DocumentReady" or "NotificationExecuting" or "Completed")) throw Conflict();
            if (phase.State == "DocumentExecuting")
            { if (phase.PdfSha256 is not null || phase.StoredFile is not null || phase.Result is not null) throw Conflict(); }
            else
            {
                if (!Digest(phase.PdfSha256) || phase.StoredFile is null || phase.StoredFile.Bucket != "maliev.com" ||
                    string.IsNullOrWhiteSpace(phase.StoredFile.ObjectName) || phase.StoredFile.ObjectName.Length > 1024 ||
                    !phase.StoredFile.ObjectName.StartsWith($"invoices/{ownership.InvoiceId}/", StringComparison.Ordinal)) throw Conflict();
                Exact(document.RootElement.GetProperty("StoredFile"), ["Bucket", "ObjectName"]);
                if (phase.State == "Completed")
                {
                    if (phase.Result is null || phase.Result.EmailState == InvoiceCreationEmailState.ExplicitRetryRequired ||
                        ParseResult(document.RootElement.GetProperty("Result").GetRawText(), phase) != phase.Result) throw Conflict();
                }
                else if (phase.Result is not null) throw Conflict();
            }
            return phase;
        }
        catch (JsonException) { throw Conflict(); }
        catch (InvalidOperationException) { throw Conflict(); }
    }

    private static InvoiceCreationResult ParseResult(string json, InvoiceEmployeeCompletionPhase phase)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            Exact(document.RootElement, ["InvoiceId", "State", "EmailState", "ProviderMessageId", "StoredFile"]);
            Exact(document.RootElement.GetProperty("StoredFile"), ["Bucket", "ObjectName"]);
            var result = JsonSerializer.Deserialize<InvoiceCreationResult>(json) ?? throw Conflict();
            if (result.InvoiceId != phase.InvoiceId || result.State != InvoiceCreationState.Completed || result.StoredFile != phase.StoredFile ||
                (int)result.EmailState is < 0 or > 3 || result.EmailState == InvoiceCreationEmailState.ProviderAccepted && string.IsNullOrWhiteSpace(result.ProviderMessageId) ||
                result.EmailState is InvoiceCreationEmailState.NotRequested or InvoiceCreationEmailState.ExplicitRetryRequired && result.ProviderMessageId is not null) throw Conflict();
            return result;
        }
        catch (JsonException) { throw Conflict(); }
    }

    private static void Exact(JsonElement value, string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Conflict();
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != fields.Length || actual.Distinct(StringComparer.Ordinal).Count() != fields.Length || fields.Any(field => !actual.Contains(field, StringComparer.Ordinal))) throw Conflict();
    }
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool Utc(string value) => DateTime.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) &&
        parsed.Kind == DateTimeKind.Utc && parsed.Ticks > 0 && parsed.ToString("O", CultureInfo.InvariantCulture) == value;
    private static InvoiceCreationConflictException Conflict() => new("Employee invoice completion requires retained phase reconciliation.");
}
