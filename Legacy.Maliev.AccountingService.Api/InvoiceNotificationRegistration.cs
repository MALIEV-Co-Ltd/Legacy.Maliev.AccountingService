using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Data;
using Maliev.Aspire.ServiceDefaults;

namespace Legacy.Maliev.AccountingService.Api;

/// <summary>Invoice-only opt-in transport; default-off never activates provider calls or changes receipt V1.</summary>
public static class InvoiceNotificationRegistration
{
    public static IServiceCollection AddInvoiceNotificationV2(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool>("InvoiceNotifications:DeliveryIntentsEnabled");
        var issuer = configuration["Jwt:Issuer"] ?? "";
        var endpoint = configuration["Services:Notification"] ?? "https+http://legacy-maliev-notification-service";
        if (enabled && (string.IsNullOrWhiteSpace(issuer) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var address) || address.Scheme != "https"))
            throw new InvalidOperationException("Invoice notification prerequisites are unavailable.");
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var activeKey = configuration["InvoiceNotifications:BindingActiveKeyId"] ?? "";
        if (enabled)
        {
            try
            {
                foreach (var entry in configuration.GetSection("InvoiceNotifications:BindingKeys").GetChildren())
                {
                    if (entry.Value is null) throw new FormatException();
                    keys.Add(entry.Key, Convert.FromBase64String(entry.Value));
                }
                if (!keys.ContainsKey(activeKey) || keys.Any(value => value.Value.Length != 32 ||
                        value.Key.Length is < 1 or > 64 || value.Key.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')))
                    throw new FormatException();
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                throw new InvalidOperationException("Invoice notification prerequisites are unavailable.");
            }
        }

        services.AddSingleton(new InvoiceNotificationIntentOptions(enabled, issuer, "service:legacy-accounting"));
        services.AddSingleton<IInvoiceNotificationBindingKeyring>(new RuntimeKeyring(activeKey, keys));
        services.AddScoped<InvoiceNotificationCorrelationStore>();
        services.AddScoped<IInvoiceNotificationCorrelationStore>(provider => provider.GetRequiredService<InvoiceNotificationCorrelationStore>());
        services.AddScoped<IInvoiceNotificationPhaseStore>(provider => provider.GetRequiredService<InvoiceNotificationCorrelationStore>());
        services.AddScoped<IInvoiceNotificationWorkflow, InvoiceNotificationWorkflow>();
        services.AddHttpClient<InvoiceNotificationIntentClient>(client =>
        {
            client.BaseAddress = new Uri(endpoint);
            client.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .AddServiceDiscovery().AddLegacyServiceAuthentication();
        return services;
    }

    private sealed class RuntimeKeyring(string activeKeyId, IReadOnlyDictionary<string, byte[]> keys) : IInvoiceNotificationBindingKeyring
    {
        private readonly IReadOnlyDictionary<string, byte[]> owned = keys.ToDictionary(value => value.Key, value => value.Value.ToArray(), StringComparer.Ordinal);
        public string ActiveKeyId { get; } = activeKeyId;
        public ReadOnlyMemory<byte>? Find(string keyId) => owned.TryGetValue(keyId, out var key) ? key.ToArray() : null;
    }
}
