using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;
using Legacy.Maliev.AccountingService.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Transactional commercial cap, durable replay and document intent in the Invoice database.</summary>
public sealed class BillingLedger(DbContext database, TimeProvider clock) : IBillingLedger
{
    public Task<BillingResult> OpenAsync(BillingSnapshot snapshot, BillingContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var frozen = snapshot with { Lines = Array.AsReadOnly(snapshot.Lines.ToArray()) };
        return database.Database.CreateExecutionStrategy().ExecuteAsync(() => OpenCoreAsync(frozen, context, cancellationToken));
    }

    private async Task<BillingResult> OpenCoreAsync(BillingSnapshot snapshot, BillingContext context, CancellationToken cancellationToken)
    {
        ResetBillingTracking();
        ValidateContext(context);
        ValidateSnapshot(snapshot);
        var fingerprint = Fingerprint(new { Kind = "Open", context.EmployeeId, Snapshot = snapshot });
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await LockOperationAsync(context.OperationId, cancellationToken);
        var replay = await ReplayAsync(context.OperationId, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"billing:quotation:" + snapshot.QuotationId}, 0))", cancellationToken);
        if (context.ExpectedRevision != 0 || await database.Set<BillingAccountRow>().AnyAsync(row => row.QuotationId == snapshot.QuotationId, cancellationToken))
            throw new BillingConflictException("This quotation already has a billing account or the opening revision differs.");
        var accountId = Guid.NewGuid();
        var state = new BillingAccountView(accountId, 1, snapshot, [], new(0m, 0m, 0m, snapshot.Cap.Currency), 0m, 0m, 0m, snapshot.Cap.Gross, 0m);
        database.Add(new BillingAccountRow { Id = accountId, QuotationId = snapshot.QuotationId, CustomerId = snapshot.CustomerId, Revision = 1, StateJson = JsonSerializer.Serialize(state) });
        var result = new BillingResult(context.OperationId, accountId, null, 1, BillingOperationState.Completed);
        AppendOperation(context, fingerprint, result, snapshot);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<BillingAccountView?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var row = await database.Set<BillingAccountRow>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == accountId, cancellationToken);
        return row is null ? null : ReadState(row);
    }

    public Task<BillingResult> ExecuteAsync(Guid accountId, BillingContext context, BillingLedgerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var frozen = command is IssueBillingStage issue ? new IssueBillingStage(issue.Draft with
        {
            Requirements = Array.AsReadOnly(issue.Draft.Requirements.Select(requirement => requirement with
            { OrderIds = Array.AsReadOnly(requirement.OrderIds.ToArray()) }).ToArray())
        }) : command;
        return database.Database.CreateExecutionStrategy().ExecuteAsync(() => ExecuteCoreAsync(accountId, context, frozen, cancellationToken));
    }

    private async Task<BillingResult> ExecuteCoreAsync(Guid accountId, BillingContext context, BillingLedgerCommand command, CancellationToken cancellationToken)
    {
        ResetBillingTracking();
        ValidateContext(context);
        ArgumentNullException.ThrowIfNull(command);
        var fingerprint = Fingerprint(new { AccountId = accountId, context.EmployeeId, Command = JsonSerializer.Serialize(command, command.GetType()) });
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await LockOperationAsync(context.OperationId, cancellationToken);
        var replay = await ReplayAsync(context.OperationId, fingerprint, cancellationToken);
        if (replay is not null) return replay;
        var row = await database.Set<BillingAccountRow>().FromSqlInterpolated($"SELECT * FROM \"BillingAccount\" WHERE \"Id\" = {accountId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Billing account does not exist.");
        if (row.Revision != context.ExpectedRevision) throw new BillingConflictException("The billing account has changed.");
        var state = ReadState(row);
        if (command is not IssueBillingStage issue) throw new ArgumentException("Unsupported billing command.", nameof(command));
        var portions = BillingAmountCalculator.AllocateLines(state.Snapshot, issue.Draft, state);
        var money = new BillingMoney(portions.Sum(line => line.Amount.Base), portions.Sum(line => line.Amount.Vat),
            portions.Sum(line => line.Amount.Gross), state.Snapshot.Cap.Currency);
        var stageId = Guid.NewGuid();
        var stage = new BillingStageView(stageId, issue.Draft.Kind, money, 0m, 0m, 0m, issue.Draft.DueDate, issue.Draft.Recipient, issue.Draft.Requirements, portions, []);
        var billed = new BillingMoney(state.Billed.Base + money.Base, state.Billed.Vat + money.Vat, state.Billed.Gross + money.Gross, money.Currency);
        state = state with
        {
            Revision = checked(state.Revision + 1),
            Stages = [.. state.Stages, stage],
            Billed = billed,
            Outstanding = billed.Gross - state.Cash - state.Withholding,
            Unbilled = state.Snapshot.Cap.Gross - billed.Gross
        };
        row.Revision = state.Revision;
        row.StateJson = JsonSerializer.Serialize(state);
        var result = new BillingResult(context.OperationId, accountId, stageId, state.Revision, BillingOperationState.Completed);
        AppendOperation(context, fingerprint, result, command);
        database.Add(new BillingIntentRow
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            OperationId = context.OperationId,
            Kind = "CommercialRequest",
            PayloadJson = JsonSerializer.Serialize(stage)
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private Task LockOperationAsync(Guid operationId, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({operationId.ToString("D")}, 0))", cancellationToken);

    private async Task<BillingResult?> ReplayAsync(Guid operationId, string fingerprint, CancellationToken cancellationToken)
    {
        var row = await database.Set<BillingOperationRow>().AsNoTracking().SingleOrDefaultAsync(row => row.OperationId == operationId, cancellationToken);
        if (row is null) return null;
        if (row.Fingerprint != fingerprint) throw new BillingConflictException("An operation cannot be reused with changed intent or actor.");
        return JsonSerializer.Deserialize<BillingResult>(row.ResultJson) ?? throw new InvalidOperationException("Invalid retained billing operation.");
    }

    private void AppendOperation(BillingContext context, string fingerprint, BillingResult result, object command) =>
        database.Add(new BillingOperationRow
        {
            OperationId = context.OperationId,
            AccountId = result.AccountId,
            EmployeeId = context.EmployeeId,
            Fingerprint = fingerprint,
            ResultJson = JsonSerializer.Serialize(result),
            CommandJson = JsonSerializer.Serialize(command, command.GetType()),
            RecordedAtUtc = clock.GetUtcNow()
        });

    private static BillingAccountView ReadState(BillingAccountRow row) => JsonSerializer.Deserialize<BillingAccountView>(row.StateJson)
        ?? throw new InvalidOperationException("Invalid retained billing account.");
    private static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private void ResetBillingTracking()
    {
        foreach (var entry in database.ChangeTracker.Entries().ToArray())
        {
            if (entry.Entity is BillingAccountRow or BillingOperationRow or BillingIntentRow) entry.State = EntityState.Detached;
            else if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Billing requires an exclusive unit of work without pending unrelated writes.");
        }
    }
    private static void ValidateContext(BillingContext context)
    {
        if (context.EmployeeId <= 0 || context.OperationId == Guid.Empty || context.ExpectedRevision < 0)
            throw new ArgumentException("Trusted actor, stable operation and revision are required.");
    }
    private static void ValidateSnapshot(BillingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.QuotationId <= 0 || snapshot.CustomerId <= 0 || string.IsNullOrWhiteSpace(snapshot.Revision)
            || snapshot.Digest.Length != 64 || snapshot.Digest.Any(character => !Uri.IsHexDigit(character))
            || string.IsNullOrWhiteSpace(snapshot.Cap.Currency) || snapshot.Lines.Count == 0
            || snapshot.Lines.Any(line => line.SourceLineId <= 0 || string.IsNullOrWhiteSpace(line.TaxCategory) || line.Amount.Currency != snapshot.Cap.Currency
                || line.Amount.Base < 0m || line.Amount.Vat < 0m || line.Amount.Base + line.Amount.Vat != line.Amount.Gross)
            || snapshot.Lines.Select(line => line.SourceLineId).Distinct().Count() != snapshot.Lines.Count
            || snapshot.Lines.Sum(line => line.Amount.Base) != snapshot.Cap.Base || snapshot.Lines.Sum(line => line.Amount.Vat) != snapshot.Cap.Vat
            || snapshot.Lines.Sum(line => line.Amount.Gross) != snapshot.Cap.Gross)
            throw new ArgumentException("A coherent, reconciled quotation snapshot is required.");
        _ = BillingAmountCalculator.AllocateComponents(snapshot.Cap.Base, snapshot.Cap.Vat, 0m, null, null, snapshot.CurrencyPrecision);
        if (snapshot.Lines.Any(line => decimal.Round(line.Amount.Base, snapshot.CurrencyPrecision) != line.Amount.Base ||
            decimal.Round(line.Amount.Vat, snapshot.CurrencyPrecision) != line.Amount.Vat ||
            decimal.Round(line.Amount.Gross, snapshot.CurrencyPrecision) != line.Amount.Gross))
            throw new ArgumentException("Source line amounts must match the currency precision.");
    }
}
