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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Admits only the first delegated request and never replays an uncertain side effect.</summary>
public sealed class InvoiceCreationAdmissionStore(InvoiceDbContext database)
{
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
