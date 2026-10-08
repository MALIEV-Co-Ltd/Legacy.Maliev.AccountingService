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
    public static Decision Apply(Operation operation, Command command, int maximumRenderedBytes)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(command);
        if (maximumRenderedBytes <= 0 || operation.OperationId == Guid.Empty || operation.AccountId == Guid.Empty ||
            operation.CustomerId <= 0 || operation.EmployeeId <= 0 || operation.Revision <= 0 ||
            !Enum.IsDefined(operation.Kind) || !Enum.IsDefined(operation.Phase) ||
            !Text(operation.Number) || !Text(operation.TemplateRevision) || !Text(operation.CoverageIdentity) ||
            !Digest(operation.Fingerprint) || !Digest(operation.SnapshotSha256))
            throw new ArgumentException("A coherent retained operation and positive byte bound are required.");

        byte[]? retained = null;
        if (operation.Phase is Phase.Reserved or Phase.RenderPending)
        {
            if (operation.RenderedBase64 is not null || operation.RenderedSha256 is not null || operation.Storage is not null)
                throw new ArgumentException("An unrendered phase cannot retain an artifact.");
        }
        else
        {
            if (operation.RenderedBase64 is null || operation.RenderedBase64.Length > 4L * ((maximumRenderedBytes + 2L) / 3L) ||
                !Digest(operation.RenderedSha256))
                throw new ArgumentException("A rendered phase requires bounded immutable bytes and digest.");
            try { retained = Convert.FromBase64String(operation.RenderedBase64); }
            catch (FormatException error) { throw new ArgumentException("Retained bytes must be valid base64.", error); }
            if (retained.Length == 0 || retained.Length > maximumRenderedBytes ||
                Hash(retained) != operation.RenderedSha256 || Convert.ToBase64String(retained) != operation.RenderedBase64)
                throw new ArgumentException("Retained artifact integrity is invalid.");
            if (operation.Phase == Phase.Issued)
                ValidateStorage(operation, operation.Storage, retained.Length);
            else if (operation.Storage is not null)
                throw new ArgumentException("Only an issued phase retains acknowledged storage.");
        }

        if (!Enum.IsDefined(command.Kind) || command.OperationId != operation.OperationId ||
            command.Fingerprint != operation.Fingerprint || command.ExpectedRevision != operation.Revision)
            throw new ArgumentException("Command identity and revision must match the retained operation.");
        if (command.Kind != CommandKind.AcceptRender && command.RenderedBytes is not null ||
            command.Kind != CommandKind.AcceptStorage && command.Storage is not null)
            throw new ArgumentException("Observation fields are restricted to their exact command.");

        var unresolved = operation.Phase is Phase.RenderPending or Phase.StoragePending;
        switch (command.Kind)
        {
            case CommandKind.Read:
                return new(operation, ActionKind.None, unresolved);
            case CommandKind.BeginRender:
                return operation.Phase == Phase.Reserved
                    ? new(Advance(operation, Phase.RenderPending), ActionKind.Render, true)
                    : new(operation, ActionKind.None, unresolved);
            case CommandKind.AcceptRender:
                if (command.RenderedBytes is null || command.RenderedBytes.Length == 0 || command.RenderedBytes.Length > maximumRenderedBytes)
                    throw new ArgumentException("A render observation requires bounded nonempty bytes.");
                if (retained is not null)
                {
                    if (!retained.AsSpan().SequenceEqual(command.RenderedBytes))
                        throw new ArgumentException("A render observation cannot replace retained bytes.");
                    return new(operation, ActionKind.None, unresolved);
                }
                if (operation.Phase != Phase.RenderPending)
                    throw new ArgumentException("Render acknowledgment requires an already claimed render phase.");
                var bytes = command.RenderedBytes.ToArray();
                return new(Advance(operation, Phase.Rendered) with
                {
                    RenderedBase64 = Convert.ToBase64String(bytes),
                    RenderedSha256 = Hash(bytes),
                }, ActionKind.None, false);
            case CommandKind.BeginStorage:
                if (operation.Phase == Phase.Reserved)
                    throw new ArgumentException("Storage cannot start before rendering.");
                return operation.Phase == Phase.Rendered
                    ? new(Advance(operation, Phase.StoragePending), ActionKind.Store, true)
                    : new(operation, ActionKind.None, unresolved);
            case CommandKind.AcceptStorage:
                if (operation.Phase is not (Phase.StoragePending or Phase.Issued) || retained is null)
                    throw new ArgumentException("Storage acknowledgment requires a claimed storage phase.");
                ValidateStorage(operation, command.Storage, retained.Length);
                if (operation.Phase == Phase.Issued)
                {
                    if (operation.Storage != command.Storage)
                        throw new ArgumentException("An issued artifact and receipt cannot be replaced.");
                    return new(operation, ActionKind.None, false);
                }
                return new(Advance(operation, Phase.Issued) with { Storage = command.Storage }, ActionKind.None, false);
            default:
                throw new ArgumentException("Unsupported command.");
        }
    }

    private static Operation Advance(Operation operation, Phase phase)
    {
        if (operation.Revision == long.MaxValue)
            throw new ArgumentException("Operation revision cannot advance further.");
        return operation with { Phase = phase, Revision = operation.Revision + 1 };
    }

    private static void ValidateStorage(Operation operation, StorageObservation? storage, int length)
    {
        var proof = storage?.Qualification;
        if (storage is null || proof is null || !Text(storage.ArtifactIdentity) || !Text(storage.ObjectVersion) ||
            !Text(proof.ReceiptId) || storage.OperationId != operation.OperationId ||
            storage.ContentSha256 != operation.RenderedSha256 || storage.Length != length ||
            proof.OperationId != operation.OperationId || proof.CustomerId != operation.CustomerId ||
            proof.SnapshotSha256 != operation.SnapshotSha256 || proof.ArtifactIdentity != storage.ArtifactIdentity ||
            proof.ObjectVersion != storage.ObjectVersion || proof.ContentSha256 != storage.ContentSha256 ||
            proof.Length != storage.Length || !proof.Clean || !proof.Private)
            throw new ArgumentException("Exact private immutable clean-storage qualification is required.");
    }

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}
