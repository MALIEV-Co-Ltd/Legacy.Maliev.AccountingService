using Legacy.Maliev.AccountingService.Application.Models;
using System.Security.Cryptography;
using System.Text.Json;

namespace Legacy.Maliev.AccountingService.Application.Services;

/// <summary>Pure proposals against committed recognition inputs; no capture or legal issuance.</summary>
public static class BillingTaxService
{
    public static BillingTaxAssessment Assess(BillingSnapshot snapshot, TaxTrigger trigger,
        IReadOnlyList<BillingTaxAssessment> committedRecognitions, TaxPolicySnapshot? policy) =>
        AssessCore(snapshot, trigger, committedRecognitions, policy, true);

    private static BillingTaxAssessment AssessCore(BillingSnapshot snapshot, TaxTrigger trigger,
        IReadOnlyList<BillingTaxAssessment> committedRecognitions, TaxPolicySnapshot? policy, bool validateHistory)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(committedRecognitions);
        ValidateSnapshot(snapshot);
        if (trigger.EventId == Guid.Empty || trigger.AccountId == Guid.Empty || !Enum.IsDefined(trigger.Kind) ||
            trigger.OccurredAt.Offset != TimeSpan.Zero || trigger.Portions is null || trigger.Portions.Count == 0 ||
            trigger.Portions.Any(portion => portion is null || portion.CumulativeEligibleBase < 0 ||
                portion.CumulativeEligibleBase != decimal.Round(portion.CumulativeEligibleBase, snapshot.CurrencyPrecision) ||
                !snapshot.Lines.Any(line => line.SourceLineId == portion.SourceLineId && portion.CumulativeEligibleBase <= line.Amount.Base)) ||
            trigger.Portions.Select(portion => portion.SourceLineId).Distinct().Count() != trigger.Portions.Count)
            throw new ArgumentException("A valid source-confirmed tax trigger is required.");

        var frozenTrigger = trigger with { Portions = Array.AsReadOnly(trigger.Portions.OrderBy(portion => portion.SourceLineId).ToArray()) };
        var fingerprint = Hash(new { snapshot.Digest, Trigger = frozenTrigger });
        if (committedRecognitions.Any(prior => prior is null || prior.Trigger is null ||
            prior.State != BillingTaxAssessmentState.Ready ||
            prior.Trigger.AccountId != trigger.AccountId || prior.SnapshotDigest != snapshot.Digest || prior.Policy is null ||
            prior.DeltaLines is null || prior.DeltaLines.Any(line => line is null || line.Amount is null ||
                line.Amount.Base < 0 || line.Amount.Vat < 0 || line.Amount.Gross != line.Amount.Base + line.Amount.Vat ||
                line.Amount.Currency != snapshot.Cap.Currency ||
                !snapshot.Lines.Any(source => source.SourceLineId == line.SourceLineId && source.TaxCategory == line.TaxCategory))) ||
            committedRecognitions.Select(prior => prior.Trigger.EventId).Distinct().Count() != committedRecognitions.Count)
            throw new ArgumentException("Only coherent committed tax recognition history may be assessed.");
        if (validateHistory)
        {
            var validated = new List<BillingTaxAssessment>();
            foreach (var prior in committedRecognitions)
            {
                var expected = AssessCore(snapshot, prior.Trigger, validated, prior.Policy, false);
                if (expected.State != BillingTaxAssessmentState.Ready || Hash(expected) != Hash(prior))
                    throw new ArgumentException("Retained recognition contradicts its trigger, policy or cumulative arithmetic.");
                validated.Add(prior);
            }
        }
        var replay = committedRecognitions.SingleOrDefault(prior => prior.Trigger.EventId == trigger.EventId);
        if (replay is not null)
        {
            if (replay.Fingerprint != fingerprint)
                throw new ArgumentException("A tax event cannot be reused with changed facts.");
            return Freeze(replay with { State = BillingTaxAssessmentState.AlreadyRecognized });
        }

        BillingTaxAssessment Review(string reason) => new(BillingTaxAssessmentState.NeedsPolicyReview, frozenTrigger,
            snapshot.Digest, fingerprint, FreezePolicy(policy), Array.Empty<BillingLine>(), reason);
        if (policy is null || !policy.Confirmed || string.IsNullOrWhiteSpace(policy.Revision) ||
            string.IsNullOrWhiteSpace(policy.ApprovalReference) || policy.EffectiveFrom.Offset != TimeSpan.Zero ||
            policy.EffectiveUntil is { Offset: var offset } && offset != TimeSpan.Zero ||
            policy.EffectiveFrom > trigger.OccurredAt || policy.EffectiveUntil <= trigger.OccurredAt ||
            policy.Precision != snapshot.CurrencyPrecision || !Enum.IsDefined(policy.Rounding) ||
            policy.Categories is null || policy.Categories.Any(category => category is null ||
                string.IsNullOrWhiteSpace(category.Classification) || category.VatRate < 0 || category.VatRate > 1 ||
                category.Triggers is null || category.Triggers.Count == 0 || category.Triggers.Any(kind => !Enum.IsDefined(kind)) ||
                category.Triggers.Distinct().Count() != category.Triggers.Count) ||
            policy.Categories.Select(category => category.Classification).Distinct().Count() != policy.Categories.Count)
            return Review("An effective, confirmed and complete tax policy is required.");

        var deltas = new List<BillingLine>();
        foreach (var portion in frozenTrigger.Portions)
        {
            var line = snapshot.Lines.Single(line => line.SourceLineId == portion.SourceLineId);
            var rule = policy.Categories.SingleOrDefault(category => category.Classification == line.TaxCategory);
            if (rule is null || !rule.Triggers.Contains(trigger.Kind))
                return Review("The confirmed policy does not cover this classification and factual trigger.");
            var histories = committedRecognitions.Where(prior => prior.DeltaLines.Any(delta => delta.SourceLineId == line.SourceLineId)).ToArray();
            if (histories.Any(prior => Hash(prior.Policy) != Hash(policy)))
                return Review("Policy rollover requires explicit reconciliation; issued recognition cannot be rerated.");
            var previous = histories.SelectMany(prior => prior.DeltaLines).Where(delta => delta.SourceLineId == line.SourceLineId).ToArray();
            var priorBase = previous.Sum(delta => delta.Amount.Base);
            var priorVat = previous.Sum(delta => delta.Amount.Vat);
            if (priorBase > line.Amount.Base || priorVat > line.Amount.Vat)
                throw new ArgumentException("Committed tax recognition exceeds the source cap.");
            if (portion.CumulativeEligibleBase <= priorBase) continue;
            var cumulativeVat = decimal.Round(portion.CumulativeEligibleBase * rule.VatRate, policy.Precision, policy.Rounding);
            if (cumulativeVat > line.Amount.Vat || cumulativeVat < priorVat ||
                portion.CumulativeEligibleBase == line.Amount.Base && cumulativeVat != line.Amount.Vat)
                return Review("Confirmed tax obligation conflicts with the retained source amounts.");
            var basis = portion.CumulativeEligibleBase - priorBase;
            var vat = cumulativeVat - priorVat;
            deltas.Add(line with { Amount = new(basis, vat, basis + vat, snapshot.Cap.Currency) });
        }
        return new(BillingTaxAssessmentState.Ready, frozenTrigger, snapshot.Digest, fingerprint,
            FreezePolicy(policy), Array.AsReadOnly(deltas.ToArray()), null);
    }

    private static void ValidateSnapshot(BillingSnapshot snapshot)
    {
        if (snapshot.QuotationId <= 0 || snapshot.CustomerId <= 0 || string.IsNullOrWhiteSpace(snapshot.TaxId) ||
            snapshot.Digest is null || snapshot.Digest.Length != 64 ||
            snapshot.Digest.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            snapshot.Revision != "sha256:" + snapshot.Digest || snapshot.CurrencyPrecision is < 0 or > 4 ||
            snapshot.Cap is null || string.IsNullOrWhiteSpace(snapshot.Cap.Currency) || snapshot.Lines is null || snapshot.Lines.Count == 0 ||
            snapshot.Lines.Any(line => line is null || line.SourceLineId <= 0 || string.IsNullOrWhiteSpace(line.TaxCategory) ||
                line.Amount is null || line.Amount.Base < 0 || line.Amount.Vat < 0 ||
                line.Amount.Base == 0 && line.Amount.Vat != 0 ||
                line.Amount.Gross != line.Amount.Base + line.Amount.Vat || line.Amount.Currency != snapshot.Cap.Currency ||
                line.Amount.Base != decimal.Round(line.Amount.Base, snapshot.CurrencyPrecision) ||
                line.Amount.Vat != decimal.Round(line.Amount.Vat, snapshot.CurrencyPrecision)) ||
            snapshot.Lines.Select(line => line.SourceLineId).Distinct().Count() != snapshot.Lines.Count ||
            snapshot.Lines.Sum(line => line.Amount.Base) != snapshot.Cap.Base ||
            snapshot.Lines.Sum(line => line.Amount.Vat) != snapshot.Cap.Vat ||
            snapshot.Lines.Sum(line => line.Amount.Gross) != snapshot.Cap.Gross)
            throw new ArgumentException("A coherent authoritative commercial snapshot is required.");
    }

    private static TaxPolicySnapshot? FreezePolicy(TaxPolicySnapshot? policy) => policy is null ? null : policy with
    {
        Categories = policy.Categories is null ? [] : Array.AsReadOnly(policy.Categories.Select(category => category is null
            ? category! : category with { Triggers = category.Triggers is null ? [] : Array.AsReadOnly(category.Triggers.ToArray()) }).ToArray())
    };

    private static BillingTaxAssessment Freeze(BillingTaxAssessment assessment) => assessment with
    {
        Trigger = assessment.Trigger with { Portions = Array.AsReadOnly(assessment.Trigger.Portions.ToArray()) },
        Policy = FreezePolicy(assessment.Policy),
        DeltaLines = Array.AsReadOnly(assessment.DeltaLines.ToArray())
    };

    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
