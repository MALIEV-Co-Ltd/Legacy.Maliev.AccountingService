using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

// These rules preserve committed source model intent. Live data compatibility requires
// a separate owner gate; neither this model nor the API applies persistent migrations.
internal static class MasterStringConstraints
{
    private readonly record struct Rule(Type EntityType, string Property, int? Maximum, bool Required);

    private static readonly Rule[] Rules =
    [
        new(typeof(Invoice), nameof(Invoice.BillingAddressBuilding), 256, false),
        new(typeof(Invoice), nameof(Invoice.BillingAddressCity), 256, false),
        new(typeof(Invoice), nameof(Invoice.BillingAddressCompany), 256, false),
        new(typeof(Invoice), nameof(Invoice.BillingAddressCountry), 256, false),
        new(typeof(Invoice), nameof(Invoice.BillingAddressLine1), 256, false),
        new(typeof(Invoice), nameof(Invoice.BillingAddressLine2), 256, false),
        new(typeof(Invoice), nameof(Invoice.BillingAddressPostalCode), 256, false),
        new(typeof(Invoice), nameof(Invoice.BillingAddressRecipient), 256, false),
        new(typeof(Invoice), nameof(Invoice.BillingAddressState), 256, false),
        new(typeof(Invoice), nameof(Invoice.CommercialRegistration), 256, false),
        new(typeof(Invoice), nameof(Invoice.Currency), 50, false),
        new(typeof(Invoice), nameof(Invoice.Fob), 256, false),
        new(typeof(Invoice), nameof(Invoice.Number), 100, true),
        new(typeof(Invoice), nameof(Invoice.PurchaseOrderNumber), 256, false),
        new(typeof(Invoice), nameof(Invoice.Requisitioner), 256, false),
        new(typeof(Invoice), nameof(Invoice.SalesPerson), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippedVia), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippingAddressBuilding), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippingAddressCity), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippingAddressCompany), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippingAddressCountry), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippingAddressLine1), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippingAddressLine2), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippingAddressPostalCode), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippingAddressRecipient), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippingAddressRecipientTelephone), 256, false),
        new(typeof(Invoice), nameof(Invoice.ShippingAddressState), 256, false),
        new(typeof(Invoice), nameof(Invoice.TaxIdentification), 100, false),
        new(typeof(Invoice), nameof(Invoice.Terms), 256, false),
        new(typeof(Receipt), nameof(Receipt.BillingAddressBuilding), 256, false),
        new(typeof(Receipt), nameof(Receipt.BillingAddressCity), 256, false),
        new(typeof(Receipt), nameof(Receipt.BillingAddressCompany), 256, false),
        new(typeof(Receipt), nameof(Receipt.BillingAddressCountry), 256, false),
        new(typeof(Receipt), nameof(Receipt.BillingAddressPostalCode), 256, false),
        new(typeof(Receipt), nameof(Receipt.BillingAddressRecipient), 256, false),
        new(typeof(Receipt), nameof(Receipt.BillingAddressState), 256, false),
        new(typeof(Receipt), nameof(Receipt.CommercialRegistration), 256, false),
        new(typeof(Receipt), nameof(Receipt.Currency), 256, true),
        new(typeof(Receipt), nameof(Receipt.InvoiceNumber), 256, false),
        new(typeof(Receipt), nameof(Receipt.TaxIdentification), 256, false),
        new(typeof(Account), nameof(Account.AccountNumber), 50, false),
        new(typeof(Account), nameof(Account.Bank), 100, false),
        new(typeof(Account), nameof(Account.Branch), 100, false),
        new(typeof(Account), nameof(Account.Swift), 50, false),
        new(typeof(Payment), nameof(Payment.Description), null, true),
        new(typeof(PaymentDirection), nameof(PaymentDirection.Description), null, true),
        new(typeof(PaymentDirection), nameof(PaymentDirection.Name), 50, true),
        new(typeof(PaymentMethod), nameof(PaymentMethod.Description), null, true),
        new(typeof(PaymentMethod), nameof(PaymentMethod.Name), 50, true),
        new(typeof(PaymentType), nameof(PaymentType.Description), null, true),
        new(typeof(PaymentType), nameof(PaymentType.Name), 50, true),
    ];

    internal static void Apply(ModelBuilder builder)
    {
        foreach (var rule in Rules)
        {
            // Do not introduce entities from either of the other independent databases.
            if (builder.Model.FindEntityType(rule.EntityType) is null)
            {
                continue;
            }

            var property = builder.Entity(rule.EntityType).Property<string>(rule.Property)
                .IsRequired(rule.Required).HasColumnType("text");
            if (rule.Maximum is int maximum)
            {
                property.HasMaxLength(maximum);
                builder.Entity(rule.EntityType).ToTable(table => table.HasCheckConstraint(
                    $"CK_{rule.EntityType.Name}_{rule.Property}_SourceLength", LengthSql(rule.Property, maximum)));
            }
        }
    }

    internal static void Validate<T>(T item) where T : class
    {
        foreach (var rule in Rules)
        {
            if (rule.EntityType != typeof(T))
            {
                continue;
            }

            var value = (string?)rule.EntityType.GetProperty(rule.Property)!.GetValue(item);
            if ((rule.Required && value is null) || (value is not null && rule.Maximum is int maximum && value.Length > maximum))
            {
                // Never include financial/customer values in errors or shared failure logs.
                throw new ArgumentException("The request is invalid.", nameof(item));
            }
        }
    }

    private static string LengthSql(string property, int maximum) =>
        $"char_length(\"{property}\") + char_length(regexp_replace(\"{property}\" COLLATE \"C\", U&'[\\0001-\\FFFF]', '', 'g')) <= {maximum}";
}
