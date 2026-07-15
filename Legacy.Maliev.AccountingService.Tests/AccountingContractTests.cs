using System.Reflection;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class AccountingContractTests
{
    [Fact]
    public void Api_PreservesAllLegacyActionsAndRouteTemplates()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();
        var actions = controllers.SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToArray();

        Assert.Equal(13, controllers.Length);
        Assert.Equal(66, actions.Length);
        Assert.Equal(67, actions.Sum(method => method.GetCustomAttributes<HttpMethodAttribute>().Count()));
        Assert.All(controllers, type => Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>()));
    }

    [Fact]
    public void EfModels_KeepThreeIndependentLegacyDatabaseBoundaries()
    {
        using var payment = new PaymentDbContext(PaymentOptions());
        using var invoice = new InvoiceDbContext(InvoiceOptions());
        using var receipt = new ReceiptDbContext(ReceiptOptions());

        Assert.Equal(6, payment.Model.GetEntityTypes().Count());
        Assert.Equal(3, invoice.Model.GetEntityTypes().Count());
        Assert.Equal(3, receipt.Model.GetEntityTypes().Count());
        Assert.Null(payment.Model.FindEntityType(typeof(Invoice)));
        Assert.Null(invoice.Model.FindEntityType(typeof(Receipt)));
        Assert.Null(receipt.Model.FindEntityType(typeof(Payment)));
    }

    [Fact]
    public void EfModels_PreserveFinancialComputedColumnsAndLegacyNames()
    {
        using var invoice = new InvoiceDbContext(InvoiceOptions());
        using var receipt = new ReceiptDbContext(ReceiptOptions());

        var invoiceItem = invoice.Model.FindEntityType(typeof(InvoiceOrderItem))!;
        var receiptItem = receipt.Model.FindEntityType(typeof(ReceiptOrderItem))!;
        var receiptEntity = receipt.Model.FindEntityType(typeof(Receipt))!;

        Assert.Equal("OrderItem", invoiceItem.GetTableName());
        Assert.Contains("UnitPrice", invoiceItem.FindProperty(nameof(InvoiceOrderItem.Subtotal))!.GetComputedColumnSql());
        Assert.Equal("OrderItem", receiptItem.GetTableName());
        Assert.Contains("WithholdingTax", receiptEntity.FindProperty(nameof(Receipt.AmountPaid))!.GetComputedColumnSql());
        Assert.Equal("VAT", receiptEntity.FindProperty(nameof(Receipt.Vat))!.GetColumnName());
    }

    [Fact]
    public void ReceiptPaymentDate_RemainsOnTheWire()
    {
        var json = JsonSerializer.Serialize(new Receipt { PaymentDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc) });
        Assert.Contains("PaymentDate", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_ContainsNoPaymentProviderExecutionDependency()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}Legacy.Maliev.AccountingService.Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        var text = string.Join('\n', files.Select(File.ReadAllText));

        Assert.DoesNotContain("PayPal", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Omise", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Opn", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stripe", text, StringComparison.OrdinalIgnoreCase);
    }

    private static DbContextOptions<PaymentDbContext> PaymentOptions() =>
        new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql("Host=localhost;Database=accounting_test;Username=test;Password=test").Options;

    private static DbContextOptions<InvoiceDbContext> InvoiceOptions() =>
        new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql("Host=localhost;Database=invoice_test;Username=test;Password=test").Options;

    private static DbContextOptions<ReceiptDbContext> ReceiptOptions() =>
        new DbContextOptionsBuilder<ReceiptDbContext>().UseNpgsql("Host=localhost;Database=receipt_test;Username=test;Password=test").Options;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.AccountingService.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
