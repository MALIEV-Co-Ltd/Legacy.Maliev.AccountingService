using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Application.Interfaces;

public interface IBillingTaxPolicyReader
{
    Task<TaxPolicySnapshot?> ReadConfirmedAsync(string classification, DateTimeOffset effectiveAt,
        CancellationToken cancellationToken);
}
