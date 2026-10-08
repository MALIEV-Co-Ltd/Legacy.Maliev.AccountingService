namespace Legacy.Maliev.AccountingService.Application.Billing.Settlement;

// Internal pure calculation only; no accepted Payment producer or runtime registration.
// AuthorityReceipt denotes a trusted adapter observation; it must never be populated from browser fields.
internal sealed record VerifiedCash(int PaymentId, int CustomerId, string Currency, string Revision,
    decimal Cash, string AuthorityReceipt);
internal sealed record Allocation(Guid OperationId, Guid AccountId, Guid StageId, int CustomerId,
    string Currency, int PaymentId, string PaymentRevision, decimal Cash,
    string PaymentAuthorityReceipt, decimal CashAtVerification);
internal sealed record Stage(Guid AccountId, Guid StageId, int CustomerId, string Currency,
    int Precision, decimal Outstanding);
internal sealed record Request(Guid OperationId, string ExpectedPaymentRevision, decimal Cash);
internal sealed record Proposal(Allocation Allocation, decimal SourceUnapplied, decimal StageOutstanding, bool Replay);
internal static class CashAllocationPlanner
{
    // This planner receives only acknowledged cash allocations. Pending two-database reservations
    // and immutable certificate/withholding settlement are separate proofs, not cash in this model.
    public static Proposal Plan(Stage stage, VerifiedCash cash, IReadOnlyList<Allocation> committed, Request request)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(cash);
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentNullException.ThrowIfNull(request);
        if (stage.AccountId == Guid.Empty || stage.StageId == Guid.Empty || stage.CustomerId <= 0 ||
            stage.Precision is < 0 or > 4 || !Currency(stage.Currency) || cash.PaymentId <= 0 ||
            cash.CustomerId != stage.CustomerId || cash.Currency != stage.Currency ||
            string.IsNullOrWhiteSpace(cash.Revision) || string.IsNullOrWhiteSpace(cash.AuthorityReceipt) ||
            !Amount(cash.Cash, stage.Precision) || !Amount(stage.Outstanding, stage.Precision) ||
            request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExpectedPaymentRevision) ||
            !Amount(request.Cash, stage.Precision) || request.Cash == 0 ||
            committed.Any(prior => prior is null || prior.OperationId == Guid.Empty || prior.AccountId == Guid.Empty ||
                prior.StageId == Guid.Empty || prior.CustomerId <= 0 || prior.PaymentId <= 0 || !Currency(prior.Currency) ||
                prior.Cash <= 0 || string.IsNullOrWhiteSpace(prior.PaymentRevision) ||
                string.IsNullOrWhiteSpace(prior.PaymentAuthorityReceipt) || prior.CashAtVerification < 0) ||
            committed.Select(prior => prior.OperationId).Distinct().Count() != committed.Count)
            throw new ArgumentException("Canonical stage, verified cash, request and coherent history are required.");
        if (request.ExpectedPaymentRevision != cash.Revision)
            throw new InvalidOperationException("Payment verification revision changed.");

        decimal consumed = 0;
        foreach (var prior in committed.Where(prior => prior.PaymentId == cash.PaymentId))
        {
            if (prior.CustomerId != cash.CustomerId || prior.Currency != cash.Currency ||
                prior.PaymentRevision != cash.Revision || prior.PaymentAuthorityReceipt != cash.AuthorityReceipt ||
                prior.CashAtVerification != cash.Cash || !Amount(prior.Cash, stage.Precision))
                throw new InvalidOperationException("Acknowledged attribution contradicts verified payment evidence.");
            if (prior.Cash > cash.Cash - consumed)
                throw new InvalidOperationException("Acknowledged history exceeds verified source cash.");
            consumed += prior.Cash;
        }

        var existing = committed.SingleOrDefault(prior => prior.OperationId == request.OperationId);
        if (existing is not null)
        {
            if (existing.AccountId != stage.AccountId || existing.StageId != stage.StageId ||
                existing.CustomerId != stage.CustomerId || existing.Currency != stage.Currency ||
                existing.PaymentId != cash.PaymentId || existing.PaymentRevision != request.ExpectedPaymentRevision ||
                existing.Cash != request.Cash || existing.PaymentAuthorityReceipt != cash.AuthorityReceipt ||
                existing.CashAtVerification != cash.Cash)
                throw new InvalidOperationException("An operation cannot be reused for another allocation intent.");
            return new(existing, cash.Cash - consumed, stage.Outstanding, true);
        }
        if (request.Cash > cash.Cash - consumed || request.Cash > stage.Outstanding)
            throw new InvalidOperationException("Allocation exceeds available source cash or stage debt.");
        var allocation = new Allocation(request.OperationId, stage.AccountId, stage.StageId, stage.CustomerId,
            stage.Currency, cash.PaymentId, cash.Revision, request.Cash, cash.AuthorityReceipt, cash.Cash);
        return new(allocation, cash.Cash - consumed - request.Cash, stage.Outstanding - request.Cash, false);
    }

    private static bool Amount(decimal value, int precision) => value >= 0 && value == decimal.Round(value, precision);
    private static bool Currency(string? value) => value is { Length: 3 } && value.All(character => character is >= 'A' and <= 'Z');
}
