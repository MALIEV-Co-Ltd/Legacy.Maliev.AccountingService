using System.Reflection;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Application.Models;
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

        Assert.Equal(16, controllers.Length);
        Assert.Equal(75, actions.Length);
        Assert.Equal(76, actions.Sum(method => method.GetCustomAttributes<HttpMethodAttribute>().Count()));
        var employeeActions = actions.Where(method => method.DeclaringType == typeof(InvoiceFinancialOwnershipController) ||
            method.DeclaringType == typeof(InvoiceCreationController) && (method.Name is nameof(InvoiceCreationController.PrepareAsync) or nameof(InvoiceCreationController.CompleteEmployeeAsync))).ToArray();
        Assert.Equal(3, employeeActions.Length);
        Assert.Single(employeeActions, method => method.DeclaringType == typeof(InvoiceFinancialOwnershipController));
        Assert.Equal(15, controllers.Count(type => type != typeof(InvoiceFinancialOwnershipController)));
        var legacyActions = actions.Except(employeeActions).ToArray();
        Assert.Equal(72, legacyActions.Length);
        Assert.Equal(73, legacyActions.Sum(method => method.GetCustomAttributes<HttpMethodAttribute>().Count()));
        Assert.Equal("{quotationId:int}/prepare", typeof(InvoiceCreationController).GetMethod(nameof(InvoiceCreationController.PrepareAsync))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{quotationId:int}/complete", typeof(InvoiceCreationController).GetMethod(nameof(InvoiceCreationController.CompleteEmployeeAsync))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{operationId:guid}/financial-ownership", typeof(InvoiceFinancialOwnershipController).GetMethod(nameof(InvoiceFinancialOwnershipController.ReadAsync))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.All(controllers, type => Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>()));
    }

    [Fact]
    public void InvoicePagination_PreservesTheTypedLegacySortContract()
    {
        var controller = typeof(Program).Assembly.GetType(
            "Legacy.Maliev.AccountingService.Api.Controllers.Invoice.InvoicesController")!;
        var action = controller.GetMethod("GetPaginatedAsync", BindingFlags.Instance | BindingFlags.Public)!;
        var parameters = action.GetParameters();

        Assert.Equal(typeof(InvoiceSortType?), parameters.Single(parameter => parameter.Name == "sort").ParameterType);
        Assert.Equal(
            ["InvoiceId_Ascending", "InvoiceId_Descending", "InvoiceCreatedDate_Ascending", "InvoiceCreatedDate_Descending", "InvoicePaymentDate_Ascending", "InvoicePaymentDate_Descending"],
            Enum.GetNames<InvoiceSortType>());
        Assert.Equal(2, action.GetCustomAttributes<HttpGetAttribute>().Count());
    }

    [Fact]
    public void ReceiptPagination_PreservesSixTypedLegacySortValues()
    {
        var controller = typeof(Program).Assembly.GetType(
            "Legacy.Maliev.AccountingService.Api.Controllers.Receipt.ReceiptsController")!;
        var action = controller.GetMethod("GetPaginatedAsync", BindingFlags.Instance | BindingFlags.Public)!;
        Assert.Equal(typeof(ReceiptSortType?), action.GetParameters().Single(parameter => parameter.Name == "sort").ParameterType);
        Assert.Equal(
            ["ReceiptId_Ascending", "ReceiptId_Descending", "ReceiptCreatedDate_Ascending", "ReceiptCreatedDate_Descending", "ReceiptPaymentDate_Ascending", "ReceiptPaymentDate_Descending"],
            Enum.GetNames<ReceiptSortType>());
        Assert.Equal(Enumerable.Range(0, 6), Enum.GetValues<ReceiptSortType>().Select(value => (int)value));
        Assert.Single(action.GetCustomAttributes<HttpGetAttribute>());
    }

    [Fact]
    public void EfModels_KeepThreeIndependentLegacyDatabaseBoundaries()
    {
        using var payment = new PaymentDbContext(PaymentOptions());
        using var invoice = new InvoiceDbContext(InvoiceOptions());
        using var receipt = new ReceiptDbContext(ReceiptOptions());

        Assert.Equal(6, payment.Model.GetEntityTypes().Count());
        Assert.Equal(5, invoice.Model.GetEntityTypes().Count());
        var correlation = invoice.Model.FindEntityType(typeof(InvoiceNotificationCorrelationRow))!;
        Assert.Equal("InvoiceNotificationCorrelation", correlation.GetTableName());
        Assert.Equal("public", correlation.GetSchema());
        Assert.Empty(correlation.GetForeignKeys());
        Assert.Null(payment.Model.FindEntityType(typeof(InvoiceNotificationCorrelationRow)));
        Assert.Null(receipt.Model.FindEntityType(typeof(InvoiceNotificationCorrelationRow)));
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
                && !path.Contains($"{Path.DirectorySeparatorChar}.dependencies{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}Legacy.Maliev.AccountingService.Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        var text = string.Join('\n', files.Select(File.ReadAllText));

        Assert.DoesNotContain("PayPal", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Omise", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Opn", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stripe", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadOnlyDownstreamClients_UseLegacyResiliencePipeline()
    {
        var program = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.AccountingService.Api",
            "Program.cs"));

        var objectDownload = Slice(
            program,
            "AddHttpClient(ReceiptFileClient.ObjectDownloadClientName)",
            "AddHttpClient<IReceiptNotificationClient");
        Assert.Contains("AddLegacyStandardResilienceHandler", objectDownload, StringComparison.Ordinal);

        var customer = Slice(
            program,
            "AddHttpClient<IReceiptCustomerClient, ReceiptCustomerClient>",
            "AddHttpClient<IReceiptSignatureClient, ReceiptSignatureClient>");
        Assert.Contains("AddLegacyStandardResilienceHandler", customer, StringComparison.Ordinal);

        var signature = Slice(
            program,
            "AddHttpClient<IReceiptSignatureClient, ReceiptSignatureClient>",
            "AddHttpClient<IInvoiceCreationDocumentClient, InvoiceCreationDocumentClient>");
        Assert.Contains("AddLegacyStandardResilienceHandler", signature, StringComparison.Ordinal);

        var sourceClients = Slice(program, "void AddInvoiceSourceClient", "public partial class Program;");
        Assert.Contains("AddLegacyStandardResilienceHandler", sourceClients, StringComparison.Ordinal);
    }

    private static DbContextOptions<PaymentDbContext> PaymentOptions() =>
        new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql("Host=localhost;Database=accounting_test;Username=test").Options;

    private static DbContextOptions<InvoiceDbContext> InvoiceOptions() =>
        new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql("Host=localhost;Database=invoice_test;Username=test").Options;

    private static DbContextOptions<ReceiptDbContext> ReceiptOptions() =>
        new DbContextOptionsBuilder<ReceiptDbContext>().UseNpgsql("Host=localhost;Database=receipt_test;Username=test").Options;

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

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing source boundary '{start}'.");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing source boundary '{end}'.");
        return source[startIndex..endIndex];
    }
}
