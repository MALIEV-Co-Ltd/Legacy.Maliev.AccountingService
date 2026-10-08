using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Data;

namespace Legacy.Maliev.AccountingService.Api.Billing;

public static class BillingRegistration
{
    /// <summary>Registers ledger read/preview dependencies only; no producer clients or issuance dispatchers.</summary>
    public static IServiceCollection AddStagedBillingReadModel(this IServiceCollection services)
    {
        services.AddScoped<IBillingLedger>(provider => new BillingLedger(provider.GetRequiredService<InvoiceDbContext>(),
            provider.GetRequiredService<TimeProvider>()));
        return services;
    }
}
