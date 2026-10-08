namespace BillingRecoveryCandidate;

// Isolated test candidate. No producer wire, runtime registration or legal issuance authority.
public enum DocumentKind { CommercialRequest, TaxInvoice, Receipt, CreditNote }
public enum Phase { Reserved, RenderPending, Rendered, StoragePending, Issued }
public enum ActionKind { None, Render, Store }
public enum CommandKind { Read, BeginRender, AcceptRender, BeginStorage, AcceptStorage }
// Qualification is a proposed trusted-adapter observation, not proof supplied by a browser.
// No accepted FileService protocol currently produces it.
public sealed record StorageOwnerQualification(string ReceiptId, Guid OperationId, int CustomerId,
    string SnapshotSha256, string ArtifactIdentity, string ObjectVersion, string ContentSha256,
    long Length, bool Clean, bool Private);
public sealed record StorageObservation(Guid OperationId, string ArtifactIdentity, string ObjectVersion,
    string ContentSha256, long Length, StorageOwnerQualification? Qualification);
public sealed record Operation(Guid OperationId, Guid AccountId, int CustomerId, int EmployeeId,
    DocumentKind Kind, string Number, string Fingerprint, string SnapshotSha256, string TemplateRevision,
    string CoverageIdentity, Phase Phase, long Revision, string? RenderedBase64 = null,
    string? RenderedSha256 = null, StorageObservation? Storage = null);
public sealed record Command(CommandKind Kind, Guid OperationId, string Fingerprint, long ExpectedRevision,
    byte[]? RenderedBytes = null, StorageObservation? Storage = null);
public sealed record Decision(Operation Operation, ActionKind ProposedAction, bool NeedsReconciliation);
public static class RecoveryReducer
{
    public static Decision Apply(Operation operation, Command command, int maximumRenderedBytes) => throw new NotImplementedException();
}
