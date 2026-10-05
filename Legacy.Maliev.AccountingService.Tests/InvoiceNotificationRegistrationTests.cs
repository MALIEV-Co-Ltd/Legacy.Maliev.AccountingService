using Legacy.Maliev.AccountingService.Api;
using Legacy.Maliev.AccountingService.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceNotificationRegistrationTests
{
    [Fact]
    public void DefaultOff_DoesNotRequireProviderEndpointOrSecret()
    {
        var services = new ServiceCollection();
        services.AddInvoiceNotificationV2(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<InvoiceNotificationIntentOptions>().Enabled);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("http")]
    [InlineData("missing-key")]
    [InlineData("invalid-base64")]
    [InlineData("wrong-length")]
    public void Enabled_MissingPrerequisiteFailsBeforeHostOrHttp(string missing)
    {
        var settings = Settings();
        switch (missing)
        {
            case "issuer": settings.Remove("Jwt:Issuer"); break;
            case "http": settings["Services:Notification"] = "http://notification.example.invalid"; break;
            case "missing-key": settings.Remove("InvoiceNotifications:BindingKeys:fixture-runtime"); break;
            case "invalid-base64": settings["InvoiceNotifications:BindingKeys:fixture-runtime"] = "invalid"; break;
            default: settings["InvoiceNotifications:BindingKeys:fixture-runtime"] = Convert.ToBase64String(new byte[31]); break;
        }
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInvoiceNotificationV2(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));
    }

    [Fact]
    public void Enabled_UsesExplicitHttpsAndIndependentRuntimeKey()
    {
        var services = new ServiceCollection();
        services.AddInvoiceNotificationV2(new ConfigurationBuilder().AddInMemoryCollection(Settings()).Build());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<InvoiceNotificationIntentOptions>();
        Assert.True(options.Enabled);
        Assert.Equal("service:legacy-accounting", options.SenderServiceSubject);
        Assert.Equal("https://auth.example.invalid", options.SenderIssuer);
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["InvoiceNotifications:DeliveryIntentsEnabled"] = "true",
        ["Jwt:Issuer"] = "https://auth.example.invalid",
        ["Services:Notification"] = "https://notification.example.invalid",
        ["InvoiceNotifications:BindingActiveKeyId"] = "fixture-runtime",
        ["InvoiceNotifications:BindingKeys:fixture-runtime"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
    };
}
