using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Application.Interfaces;

public interface IBillingSource
{
    Task<BillingSnapshot> ReadAsync(int quotationId, CancellationToken cancellationToken);
}
