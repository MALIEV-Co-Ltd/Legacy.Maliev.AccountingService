using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Application.Interfaces;

public interface IBillingEvidenceReader
{
    Task<IReadOnlyList<EvidenceReceipt>> VerifyAsync(int customerId, int quotationId, IReadOnlyList<int> allowedOrderIds,
        IReadOnlyList<EvidenceReference> references, CancellationToken cancellationToken);
}
public sealed class BillingDependencyException(string message) : Exception(message);
