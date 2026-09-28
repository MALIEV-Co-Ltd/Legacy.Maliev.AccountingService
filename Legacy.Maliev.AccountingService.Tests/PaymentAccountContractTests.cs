using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Api.Controllers.Payment;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class PaymentAccountContractTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public void GetAccounts_DeclaresAuthenticatedLiveCheckedAccountingReadPermission()
    {
        var controller = typeof(AccountsController);
        var action = controller.GetMethod(nameof(AccountsController.GetAllAccountsAsync))!;

        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("payments/[controller]", controller.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        var permission = action.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(permission);
        Assert.Equal(AccountingPermissions.Read, permission.Permission);
        Assert.True(permission.RequireLiveCheck);
    }

    [Fact]
    public async Task GetAccounts_PostgresRowWithUnknownBranchAndSwift_PreservesNullableWireShape()
    {
        await using var payments = PaymentContext();
        await payments.Database.MigrateAsync();
        payments.Accounts.Add(new Account
        {
            Bank = "Example Bank",
            AccountNumber = "TEST-ACCOUNT",
            Branch = null!,
            Swift = null!,
        });
        await payments.SaveChangesAsync();
        payments.ChangeTracker.Clear();

        await using var invoices = new InvoiceDbContext(Options<InvoiceDbContext>());
        await using var receipts = new ReceiptDbContext(Options<ReceiptDbContext>());
        var repository = new AccountingRepository(
            payments,
            invoices,
            receipts,
            Mock.Of<IAccountingCache>(),
            TimeProvider.System);
        var controller = new AccountsController(repository, Mock.Of<IIdempotencyStore>());

        var response = await controller.GetAllAccountsAsync(CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        var accounts = Assert.IsAssignableFrom<IReadOnlyList<Account>>(result.Value);
        var account = Assert.Single(accounts);
        Assert.Equal("Example Bank", account.Bank);
        Assert.Equal("TEST-ACCOUNT", account.AccountNumber);
        Assert.Null(account.Branch);
        Assert.Null(account.Swift);

        // These options mirror the Accounting API's MVC JSON configuration. Web's
        // case-insensitive JSON reader accepts this PascalCase response shape.
        var json = JsonSerializer.Serialize(accounts, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null,
        });
        using var document = JsonDocument.Parse(json);
        var payload = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("Example Bank", payload.GetProperty("Bank").GetString());
        Assert.Equal("TEST-ACCOUNT", payload.GetProperty("AccountNumber").GetString());
        Assert.False(payload.TryGetProperty("Branch", out _));
        Assert.False(payload.TryGetProperty("Swift", out _));

        var webReader = JsonSerializer.Deserialize<BankAccountWire[]>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var webAccount = Assert.Single(webReader!);
        Assert.Equal("Example Bank", webAccount.Bank);
        Assert.Equal("TEST-ACCOUNT", webAccount.AccountNumber);
        Assert.Null(webAccount.Branch);
        Assert.Null(webAccount.Swift);
    }

    private PaymentDbContext PaymentContext() => new(Options<PaymentDbContext>());

    private DbContextOptions<TContext> Options<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseNpgsql(postgres.GetConnectionString()).Options;

    private sealed record BankAccountWire(string Bank, string AccountNumber, string? Branch, string? Swift);
}
