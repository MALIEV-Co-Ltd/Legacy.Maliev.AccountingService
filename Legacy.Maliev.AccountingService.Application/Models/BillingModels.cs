namespace Legacy.Maliev.AccountingService.Application.Models;

public sealed record BillingMoney(decimal Base, decimal Vat, decimal Gross, string Currency);
public enum BillingStageKind { Full, Deposit, Installment, Remaining }
public sealed record BillingLine(int SourceLineId, BillingMoney Amount, string TaxCategory);
public sealed record BillingSnapshot(int QuotationId, int CustomerId, string TaxId, string Revision, string Digest,
    BillingMoney Cap, IReadOnlyList<BillingLine> Lines, int CurrencyPrecision);
public sealed record BillingRecipient(int CustomerId, string TaxId, string Recipient, string? Branch, string Address);
public enum BillingEvidenceKind { Shipment, Release, Acceptance, BillingInstruction }
public sealed record EvidenceRequirement(BillingEvidenceKind Kind, IReadOnlyList<int> OrderIds);
public sealed record EvidenceReference(Guid DocumentId, Guid VersionId);
public sealed record EvidenceReceipt(Guid DocumentId, Guid VersionId, int CustomerId, string Kind, string ContentSha256,
    string VerificationStatus, string? VerifiedBySubject, DateTimeOffset? VerifiedAtUtc, long Revision,
    int? QuotationId, IReadOnlyList<int> OrderIds);
public sealed record StageDraft(BillingStageKind Kind, decimal? Amount, decimal? Percentage, DateOnly? DueDate,
    BillingRecipient Recipient, IReadOnlyList<EvidenceRequirement> Requirements);
public sealed record BillingContext(int EmployeeId, Guid OperationId, long ExpectedRevision);
public enum BillingOperationState { Completed, NeedsReconciliation }
public sealed record BillingResult(Guid OperationId, Guid AccountId, Guid? StageId, long Revision, BillingOperationState State);
public sealed record BillingStageView(Guid Id, BillingStageKind Kind, BillingMoney Amount, decimal Cash, decimal Withholding,
    decimal Credit, DateOnly? DueDate, BillingRecipient Recipient, IReadOnlyList<EvidenceRequirement> Requirements,
    IReadOnlyList<BillingLine> Portions, IReadOnlyList<BillingLine> Credits);
public sealed record BillingAccountView(Guid Id, long Revision, BillingSnapshot Snapshot, IReadOnlyList<BillingStageView> Stages,
    BillingMoney Billed, decimal Cash, decimal Withholding, decimal Outstanding, decimal Unbilled, decimal VatRecognized);
public abstract record BillingLedgerCommand;
public sealed record IssueBillingStage(StageDraft Draft) : BillingLedgerCommand;
