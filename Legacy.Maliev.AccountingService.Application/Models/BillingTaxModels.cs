namespace Legacy.Maliev.AccountingService.Application.Models;

public enum TaxTriggerKind { Payment, Delivery, ServiceUse, EarlyTaxInvoice }
public enum BillingTaxAssessmentState { Ready, NeedsPolicyReview, AlreadyRecognized }
/// <summary>Source-confirmed cumulative eligible base, not a second commercial billing amount.</summary>
public sealed record TaxPortion(int SourceLineId, decimal CumulativeEligibleBase);
public sealed record TaxTrigger(Guid EventId, Guid AccountId, TaxTriggerKind Kind, DateTimeOffset OccurredAt,
    IReadOnlyList<TaxPortion> Portions);
public sealed record TaxCategoryPolicy(string Classification, decimal VatRate, IReadOnlyList<TaxTriggerKind> Triggers);
/// <summary>Explicit approved policy; no built-in jurisdiction, rate or tax-point defaults.</summary>
public sealed record TaxPolicySnapshot(string Revision, string ApprovalReference, bool Confirmed,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveUntil, int Precision, MidpointRounding Rounding,
    IReadOnlyList<TaxCategoryPolicy> Categories);
/// <summary>A calculation proposal only; no database capture, legal number or document issuance.</summary>
public sealed record BillingTaxAssessment(BillingTaxAssessmentState State, TaxTrigger Trigger, string SnapshotDigest,
    string Fingerprint, TaxPolicySnapshot? Policy, IReadOnlyList<BillingLine> DeltaLines, string? ReviewReason);
