using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Application.Services;

/// <summary>Allocates commercial amounts only; this does not recognize a VAT tax point.</summary>
public static class BillingAmountCalculator
{
    public static BillingMoney Allocate(BillingSnapshot snapshot, StageDraft draft, BillingAccountView current)
    {
        var portions = AllocateLines(snapshot, draft, current);
        return new(portions.Sum(line => line.Amount.Base), portions.Sum(line => line.Amount.Vat),
            portions.Sum(line => line.Amount.Gross), snapshot.Cap.Currency);
    }

    public static IReadOnlyList<BillingLine> AllocateLines(BillingSnapshot snapshot, StageDraft draft, BillingAccountView current)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(current);
        if (!Enum.IsDefined(draft.Kind) || snapshot != current.Snapshot)
            throw new ArgumentException("An authoritative billing snapshot and stage kind are required.");
        if (draft.Recipient.CustomerId != snapshot.CustomerId || draft.Recipient.TaxId != snapshot.TaxId)
            throw new ArgumentException("Head-office routing cannot change the legal debtor.");
        if (draft.Kind is BillingStageKind.Full or BillingStageKind.Remaining && (draft.Amount is not null || draft.Percentage is not null))
            throw new ArgumentException("Full and remaining stages use the authoritative remaining amount.");
        if (draft.Kind == BillingStageKind.Full && current.Billed.Gross != 0m)
            throw new ArgumentException("A full request cannot follow an issued stage.");
        if (draft.Kind is BillingStageKind.Deposit or BillingStageKind.Installment && draft.Amount is null && draft.Percentage is null)
            throw new ArgumentException("An amount or percentage is required.");
        if (draft.Amount is not null && draft.Percentage is not null)
            throw new ArgumentException("Choose an amount or percentage.");
        var remainingBase = snapshot.Cap.Base - current.Billed.Base;
        var remainingVat = snapshot.Cap.Vat - current.Billed.Vat;
        var amount = draft.Percentage is { } percent
            ? Round(snapshot.Cap.Gross * ValidatePercent(percent) / 100m, snapshot.CurrencyPrecision)
            : draft.Amount;
        var allocation = AllocateComponents(remainingBase, remainingVat, 0m, amount, null, snapshot.CurrencyPrecision);
        var remaining = snapshot.Lines.OrderBy(line => line.SourceLineId).Select(line =>
        {
            var issued = current.Stages.SelectMany(stage => stage.Portions).Where(portion => portion.SourceLineId == line.SourceLineId).ToArray();
            var credited = current.Stages.SelectMany(stage => stage.Credits).Where(portion => portion.SourceLineId == line.SourceLineId).ToArray();
            var basis = line.Amount.Base - issued.Sum(portion => portion.Amount.Base) + credited.Sum(portion => portion.Amount.Base);
            var vat = line.Amount.Vat - issued.Sum(portion => portion.Amount.Vat) + credited.Sum(portion => portion.Amount.Vat);
            if (basis < 0m || vat < 0m) throw new ArgumentException("Issued line allocations exceed the approved cap.");
            return line with { Amount = new(basis, vat, basis + vat, line.Amount.Currency) };
        }).Where(line => line.Amount.Gross > 0m).ToArray();
        if (remaining.Sum(line => line.Amount.Base) != remainingBase || remaining.Sum(line => line.Amount.Vat) != remainingVat)
            throw new ArgumentException("Retained line allocations do not reconcile to the billing account.");
        var scale = 1m;
        for (var index = 0; index < snapshot.CurrencyPrecision; index++) scale *= 10m;
        var units = allocation.Gross * scale;
        var weighted = remaining.Select(line =>
        {
            var exact = units * (line.Amount.Gross / (remainingBase + remainingVat));
            return new { Line = line, Units = Math.Floor(exact), Fraction = exact - Math.Floor(exact) };
        }).ToArray();
        var residual = checked((int)(units - weighted.Sum(value => value.Units)));
        var extra = weighted.OrderByDescending(value => value.Fraction).ThenBy(value => value.Line.SourceLineId)
            .Take(residual).Select(value => value.Line.SourceLineId).ToHashSet();
        var portions = weighted.Select(value =>
        {
            var gross = (value.Units + (extra.Contains(value.Line.SourceLineId) ? 1m : 0m)) / scale;
            var basis = gross == value.Line.Amount.Gross ? value.Line.Amount.Base
                : Round(gross * (value.Line.Amount.Base / value.Line.Amount.Gross), snapshot.CurrencyPrecision);
            basis = Math.Clamp(basis, Math.Max(0m, gross - value.Line.Amount.Vat), Math.Min(gross, value.Line.Amount.Base));
            return value.Line with { Amount = new(basis, gross - basis, gross, snapshot.Cap.Currency) };
        }).Where(line => line.Amount.Gross > 0m).ToArray();
        if (portions.Sum(line => line.Amount.Gross) != allocation.Gross)
            throw new ArgumentException("Line allocation rounding cannot reconcile the request.");
        return Array.AsReadOnly(portions);
    }

    /// <summary>Uses cumulative rounding so an eligible final stage consumes the exact component residual.</summary>
    public static BillingMoney AllocateComponents(decimal baseCap, decimal vatCap, decimal billedGross,
        decimal? amount, decimal? percentage, int precision)
    {
        if (precision is < 0 or > 4 || baseCap < 0m || vatCap < 0m || billedGross < 0m)
            throw new ArgumentException("Nonnegative components and supported currency precision are required.");
        var grossCap = checked(baseCap + vatCap);
        if (grossCap <= 0m || billedGross > grossCap || Round(baseCap, precision) != baseCap
            || Round(vatCap, precision) != vatCap || Round(billedGross, precision) != billedGross)
            throw new ArgumentException("Commercial amounts must reconcile at currency precision.");
        if (amount is not null && percentage is not null)
            throw new ArgumentException("Choose an amount or percentage.");
        var gross = percentage is { } percent ? Round(grossCap * ValidatePercent(percent) / 100m, precision)
            : amount ?? grossCap - billedGross;
        if (gross <= 0m || gross > grossCap - billedGross || Round(gross, precision) != gross)
            throw new ArgumentException("The request must be positive and within the unbilled amount.");
        var priorBase = Round(baseCap * (billedGross / grossCap), precision);
        var cumulativeBase = billedGross + gross == grossCap ? baseCap
            : Round(baseCap * ((billedGross + gross) / grossCap), precision);
        var allocatedBase = cumulativeBase - priorBase;
        return new(allocatedBase, gross - allocatedBase, gross, string.Empty);
    }

    private static decimal ValidatePercent(decimal percentage) => percentage is > 0m and <= 100m
        ? percentage : throw new ArgumentException("Percentage must be greater than zero and at most 100.");
    private static decimal Round(decimal value, int precision) => Math.Round(value, precision, MidpointRounding.AwayFromZero);
}
