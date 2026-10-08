using System.Security.Cryptography;
using System.Text;
namespace BillingRecoveryCandidate;

public sealed class RecoveryTests
{
    private static Decision Decide(Operation state, Command command) => RecoveryReducer.Apply(state, command, 1024);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static Operation Reserved() => new(Guid.NewGuid(), Guid.NewGuid(), 21, 42,
        DocumentKind.TaxInvoice, "SYNTHETIC-ONLY-0001", new string('a', 64), Hash(Encoding.UTF8.GetBytes("synthetic-source")),
        "synthetic-template-v1", "synthetic-tax-portions:1", Phase.Reserved, 1);
    private static Command Input(Operation state, CommandKind kind, byte[]? bytes = null, StorageObservation? storage = null) =>
        new(kind, state.OperationId, state.Fingerprint, state.Revision, bytes, storage);
    private static Operation Rendered()
    {
        var reserved = Reserved();
        var pending = Decide(reserved, Input(reserved, CommandKind.BeginRender)).Operation;
        return Decide(pending, Input(pending, CommandKind.AcceptRender, [1, 2, 3])).Operation;
    }
    private static StorageObservation Receipt(Operation state) => new(state.OperationId, "synthetic-owner-artifact-1",
        "synthetic-immutable-generation-1", state.RenderedSha256!, 3,
        new("synthetic-owner-receipt", state.OperationId, state.CustomerId, state.SnapshotSha256,
            "synthetic-owner-artifact-1", "synthetic-immutable-generation-1", state.RenderedSha256!, 3, true, true));

    [Fact]
    public void CrashAfterRenderClaimRetainsNumberAndOrdinaryRetryCannotRenderAgain()
    {
        var original = Reserved();
        var claimed = Decide(original, Input(original, CommandKind.BeginRender));
        Assert.Equal(ActionKind.Render, claimed.ProposedAction);
        Assert.Equal(Phase.RenderPending, claimed.Operation.Phase);
        Assert.Equal(original.Number, claimed.Operation.Number);
        Assert.Equal(original.SnapshotSha256, claimed.Operation.SnapshotSha256);
        var retry = Decide(claimed.Operation, Input(claimed.Operation, CommandKind.BeginRender));
        Assert.Equal(ActionKind.None, retry.ProposedAction);
        Assert.True(retry.NeedsReconciliation);
        Assert.Equal(claimed.Operation, retry.Operation);
    }
    [Fact]
    public void RenderObservationRetainsDefensiveBytesBeforeProposingStorage()
    {
        var original = Reserved();
        var pending = Decide(original, Input(original, CommandKind.BeginRender)).Operation;
        byte[] bytes = [1, 2, 3];
        var captured = Decide(pending, Input(pending, CommandKind.AcceptRender, bytes));
        bytes[0] = 99;
        Assert.Equal(new byte[] { 1, 2, 3 }, Convert.FromBase64String(captured.Operation.RenderedBase64!));
        Assert.Equal(Hash([1, 2, 3]), captured.Operation.RenderedSha256);
        Assert.Equal(ActionKind.None, captured.ProposedAction);
        var storing = Decide(captured.Operation, Input(captured.Operation, CommandKind.BeginStorage));
        Assert.Equal(ActionKind.Store, storing.ProposedAction);
        Assert.Equal(Phase.StoragePending, storing.Operation.Phase);
    }
    [Fact]
    public void LostStorageReplyCannotProposeAnotherUploadOrAnotherNumber()
    {
        var rendered = Rendered();
        var pending = Decide(rendered, Input(rendered, CommandKind.BeginStorage)).Operation;
        var read = Decide(pending, Input(pending, CommandKind.Read));
        var retry = Decide(pending, Input(pending, CommandKind.BeginStorage));
        Assert.Equal(ActionKind.None, read.ProposedAction);
        Assert.Equal(ActionKind.None, retry.ProposedAction);
        Assert.True(read.NeedsReconciliation);
        Assert.True(retry.NeedsReconciliation);
        Assert.Equal(rendered.Number, retry.Operation.Number);
        Assert.Equal(pending, retry.Operation);
    }
    [Theory]
    [InlineData("operation")]
    [InlineData("digest")]
    [InlineData("length")]
    [InlineData("version")]
    [InlineData("clean")]
    [InlineData("private")]
    [InlineData("authority")]
    [InlineData("customer")]
    [InlineData("snapshot")]
    [InlineData("object")]
    [InlineData("receipt-id")]
    [InlineData("qualified-operation")]
    [InlineData("qualified-version")]
    [InlineData("qualified-digest")]
    [InlineData("qualified-length")]
    public void UnqualifiedStorageObservationCannotBecomeIssued(string defect)
    {
        var rendered = Rendered();
        var pending = Decide(rendered, Input(rendered, CommandKind.BeginStorage)).Operation;
        var receipt = Receipt(pending);
        receipt = defect switch
        {
            "operation" => receipt with { OperationId = Guid.NewGuid() },
            "digest" => receipt with { ContentSha256 = new string('b', 64) },
            "length" => receipt with { Length = 4 },
            "version" => receipt with { ObjectVersion = "" },
            "clean" => receipt with { Qualification = receipt.Qualification! with { Clean = false } },
            "private" => receipt with { Qualification = receipt.Qualification! with { Private = false } },
            "customer" => receipt with { Qualification = receipt.Qualification! with { CustomerId = 99 } },
            "snapshot" => receipt with { Qualification = receipt.Qualification! with { SnapshotSha256 = new string('b', 64) } },
            "object" => receipt with { ArtifactIdentity = "different-object" },
            "receipt-id" => receipt with { Qualification = receipt.Qualification! with { ReceiptId = "" } },
            "qualified-operation" => receipt with { Qualification = receipt.Qualification! with { OperationId = Guid.NewGuid() } },
            "qualified-version" => receipt with { Qualification = receipt.Qualification! with { ObjectVersion = "different-generation" } },
            "qualified-digest" => receipt with { Qualification = receipt.Qualification! with { ContentSha256 = new string('b', 64) } },
            "qualified-length" => receipt with { Qualification = receipt.Qualification! with { Length = 4 } },
            _ => receipt with { Qualification = null }
        };
        Assert.Throws<ArgumentException>(() => Decide(pending, Input(pending, CommandKind.AcceptStorage, storage: receipt)));
        Assert.Equal(Phase.StoragePending, pending.Phase);
    }
    [Fact]
    public void ExactStorageReadbackIssuesOnceAndReplayKeepsImmutableHistory()
    {
        var rendered = Rendered();
        var pending = Decide(rendered, Input(rendered, CommandKind.BeginStorage)).Operation;
        var receipt = Receipt(pending);
        var issued = Decide(pending, Input(pending, CommandKind.AcceptStorage, storage: receipt));
        Assert.Equal(Phase.Issued, issued.Operation.Phase);
        Assert.False(issued.NeedsReconciliation);
        Assert.Equal(ActionKind.None, issued.ProposedAction);
        var replay = Decide(issued.Operation, Input(issued.Operation, CommandKind.AcceptStorage, storage: receipt));
        Assert.Equal(issued.Operation, replay.Operation);
        Assert.Equal(ActionKind.None, replay.ProposedAction);
    }
    [Fact]
    public void ChangedIntentOrStaleMutationCannotDispatchAnEffect()
    {
        var state = Reserved();
        Assert.Throws<ArgumentException>(() => Decide(state, Input(state, CommandKind.BeginRender) with { Fingerprint = new string('b', 64) }));
        Assert.Throws<ArgumentException>(() => Decide(state, Input(state, CommandKind.BeginRender) with { OperationId = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => Decide(state, Input(state, CommandKind.BeginRender) with { ExpectedRevision = 0 }));
    }
    [Fact]
    public void DifferentBytesCannotOverwritePreviouslyRetainedRender()
    {
        var state = Rendered();
        Assert.Throws<ArgumentException>(() => Decide(state, Input(state, CommandKind.AcceptRender, [9, 2, 3])));
        Assert.Equal(Hash([1, 2, 3]), state.RenderedSha256);
    }
    [Fact]
    public void ForgedRetainedBytesCannotReachStorage()
    {
        var state = Rendered() with { RenderedBase64 = Convert.ToBase64String([9, 2, 3]) };
        Assert.Throws<ArgumentException>(() => Decide(state, Input(state, CommandKind.BeginStorage)));
    }
    [Theory]
    [InlineData("coverage")]
    [InlineData("template")]
    [InlineData("customer")]
    [InlineData("number")]
    public void IncompleteRetainedAuthorityCannotDispatch(string defect)
    {
        var state = Reserved();
        state = defect switch
        {
            "coverage" => state with { CoverageIdentity = "" },
            "template" => state with { TemplateRevision = "" },
            "customer" => state with { CustomerId = 0 },
            _ => state with { Number = "" }
        };
        Assert.Throws<ArgumentException>(() => Decide(state, Input(state, CommandKind.BeginRender)));
    }

    [Fact]
    public void TerminalReplayCannotRenderStoreOrReplaceItsArtifact()
    {
        var rendered = Rendered();
        var pending = Decide(rendered, Input(rendered, CommandKind.BeginStorage)).Operation;
        var issued = Decide(pending, Input(pending, CommandKind.AcceptStorage, storage: Receipt(pending))).Operation;
        foreach (var command in new[] { CommandKind.Read, CommandKind.BeginRender, CommandKind.BeginStorage })
        {
            var unchanged = Decide(issued, Input(issued, command));
            Assert.Equal(ActionKind.None, unchanged.ProposedAction);
            Assert.Equal(issued, unchanged.Operation);
        }
        Assert.Throws<ArgumentException>(() => Decide(issued, Input(issued, CommandKind.AcceptStorage,
            storage: issued.Storage! with { ArtifactIdentity = "different-object" })));
        Assert.Throws<ArgumentException>(() => Decide(issued with { RenderedSha256 = new string('b', 64) }, Input(issued, CommandKind.Read)));
    }

    [Fact]
    public void EachAcknowledgedTransitionAdvancesOnceAndIdenticalObservationDoesNotAdvanceAgain()
    {
        var original = Reserved();
        var pending = Decide(original, Input(original, CommandKind.BeginRender)).Operation;
        Assert.Equal(original.Revision + 1, pending.Revision);
        var rendered = Decide(pending, Input(pending, CommandKind.AcceptRender, [1, 2, 3])).Operation;
        Assert.Equal(pending.Revision + 1, rendered.Revision);
        var replay = Decide(rendered, Input(rendered, CommandKind.AcceptRender, [1, 2, 3]));
        Assert.Equal(rendered, replay.Operation);
        Assert.Throws<ArgumentException>(() => Decide(rendered, Input(pending, CommandKind.AcceptRender, [1, 2, 3])));
        var storage = Decide(rendered, Input(rendered, CommandKind.BeginStorage)).Operation;
        Assert.Equal(rendered.Revision + 1, storage.Revision);
        var issued = Decide(storage, Input(storage, CommandKind.AcceptStorage, storage: Receipt(storage))).Operation;
        Assert.Equal(storage.Revision + 1, issued.Revision);
    }

    [Theory]
    [InlineData("reserved-bytes")]
    [InlineData("rendered-missing-bytes")]
    [InlineData("storage-missing-digest")]
    [InlineData("issued-missing-proof")]
    [InlineData("invalid-phase")]
    [InlineData("empty-operation")]
    [InlineData("empty-account")]
    [InlineData("empty-actor")]
    [InlineData("malformed-base64")]
    public void MalformedRetainedStateIsRejectedEvenOnRead(string defect)
    {
        var state = Rendered();
        state = defect switch
        {
            "reserved-bytes" => state with { Phase = Phase.Reserved },
            "rendered-missing-bytes" => state with { RenderedBase64 = null },
            "storage-missing-digest" => state with { Phase = Phase.StoragePending, RenderedSha256 = null },
            "issued-missing-proof" => state with { Phase = Phase.Issued },
            "invalid-phase" => state with { Phase = (Phase)999 },
            "empty-operation" => state with { OperationId = Guid.Empty },
            "empty-account" => state with { AccountId = Guid.Empty },
            "empty-actor" => state with { EmployeeId = 0 },
            _ => state with { RenderedBase64 = "invalid!" }
        };
        Assert.Throws<ArgumentException>(() => Decide(state, Input(state, CommandKind.Read)));
    }

    [Fact]
    public void EmptyOversizedOrWrongPhaseRenderAndUnknownCommandsAreRefused()
    {
        var original = Reserved();
        var pending = Decide(original, Input(original, CommandKind.BeginRender)).Operation;
        Assert.Throws<ArgumentException>(() => Decide(pending, Input(pending, CommandKind.AcceptRender, [])));
        Assert.Throws<ArgumentException>(() => Decide(pending, Input(pending, CommandKind.AcceptRender, new byte[1025])));
        Assert.Throws<ArgumentException>(() => Decide(original, Input(original, CommandKind.AcceptRender, [1, 2, 3])));
        Assert.Throws<ArgumentException>(() => Decide(original, Input(original, (CommandKind)999)));
        Assert.Throws<ArgumentException>(() => RecoveryReducer.Apply(original, Input(original, CommandKind.Read), 0));
        Assert.Throws<ArgumentException>(() => RecoveryReducer.Apply(original, Input(original, CommandKind.Read), -1));
    }
}
