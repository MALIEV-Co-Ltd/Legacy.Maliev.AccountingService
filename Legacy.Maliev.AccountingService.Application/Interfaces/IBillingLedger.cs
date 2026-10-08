using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Application.Interfaces;

public interface IBillingLedger
{
    Task<BillingResult> OpenAsync(BillingSnapshot snapshot, BillingContext context, CancellationToken cancellationToken);
    Task<BillingAccountView?> GetAsync(Guid accountId, CancellationToken cancellationToken);
    Task<BillingResult> ExecuteAsync(Guid accountId, BillingContext context, BillingLedgerCommand command, CancellationToken cancellationToken);
}
public sealed class BillingConflictException(string message) : Exception(message);
