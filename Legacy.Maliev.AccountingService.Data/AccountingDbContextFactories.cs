using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Legacy.Maliev.AccountingService.Data;

public sealed class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql(Required("ConnectionStrings__PaymentDbContext")).Options);

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required.");
}

public sealed class InvoiceDbContextFactory : IDesignTimeDbContextFactory<InvoiceDbContext>
{
    public InvoiceDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql(Required("ConnectionStrings__InvoiceDbContext")).Options);

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required.");
}

public sealed class ReceiptDbContextFactory : IDesignTimeDbContextFactory<ReceiptDbContext>
{
    public ReceiptDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ReceiptDbContext>().UseNpgsql(Required("ConnectionStrings__ReceiptDbContext")).Options);

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required.");
}
