namespace BillingSettlementCandidate;

// Isolated pure test proposal, not a public contract, accepted Payment producer or runtime registration.
// AuthorityReceipt denotes a trusted adapter observation; it must never be populated from browser fields.
public sealed record VerifiedCash(int PaymentId, int CustomerId, string Currency, string Revision,
    decimal Cash, string AuthorityReceipt);
public sealed record Allocation(Guid OperationId, Guid AccountId, Guid StageId, int CustomerId,
    string Currency, int PaymentId, string PaymentRevision, decimal Cash,
    string PaymentAuthorityReceipt, decimal CashAtVerification);
public sealed record Stage(Guid AccountId, Guid StageId, int CustomerId, string Currency,
    int Precision, decimal Outstanding);
public sealed record Request(Guid OperationId, string ExpectedPaymentRevision, decimal Cash);
public sealed record Proposal(Allocation Allocation, decimal SourceUnapplied, decimal StageOutstanding, bool Replay);
public static class CashAllocationPlanner
{
    // This planner receives only acknowledged cash allocations. Pending two-database reservations
    // and immutable certificate/withholding settlement are separate proofs, not cash in this model.
    public static Proposal Plan(Stage stage, VerifiedCash cash, IReadOnlyList<Allocation> committed, Request request)
        => throw new NotImplementedException();
}
