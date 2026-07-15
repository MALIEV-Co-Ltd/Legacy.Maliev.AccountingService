using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<PaymentDirection> Directions => Set<PaymentDirection>();
    public DbSet<PaymentMethod> Methods => Set<PaymentMethod>();
    public DbSet<PaymentType> Types => Set<PaymentType>();
    public DbSet<PaymentFile> Files => Set<PaymentFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ModelRules.Apply(modelBuilder);
        modelBuilder.Entity<Payment>().Property(value => value.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<Payment>().HasOne(value => value.PaymentDirection).WithMany(value => value.Payment)
            .HasForeignKey(value => value.PaymentDirectionId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Payment_PaymentDirection");
        modelBuilder.Entity<Payment>().HasOne(value => value.PaymentMethod).WithMany(value => value.Payment)
            .HasForeignKey(value => value.PaymentMethodId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Payment_PaymentMethod");
        modelBuilder.Entity<Payment>().HasOne(value => value.PaymentType).WithMany(value => value.Payment)
            .HasForeignKey(value => value.PaymentTypeId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Payment_PaymentType");
        modelBuilder.Entity<PaymentFile>().HasOne(value => value.Payment).WithMany(value => value.PaymentFile)
            .HasForeignKey(value => value.PaymentId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_PaymentFile_Payment");
    }
}

public sealed class InvoiceDbContext(DbContextOptions<InvoiceDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceOrderItem> Items => Set<InvoiceOrderItem>();
    public DbSet<InvoiceFile> Files => Set<InvoiceFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ModelRules.Apply(modelBuilder);
        modelBuilder.Entity<InvoiceOrderItem>().ToTable("OrderItem");
        modelBuilder.Entity<InvoiceOrderItem>().Property(value => value.UnitPrice).HasPrecision(18, 2);
        modelBuilder.Entity<InvoiceOrderItem>().Property(value => value.Subtotal).HasPrecision(18, 2)
            .HasComputedColumnSql("(\"UnitPrice\" * \"Quantity\")::numeric(18,2)", stored: true);
        modelBuilder.Entity<Invoice>().Property(value => value.Fob).HasColumnName("FOB");
        modelBuilder.Entity<Invoice>().Property(value => value.Vat).HasColumnName("VAT").HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(value => value.Subtotal).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(value => value.Total).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(value => value.WithholdingTax).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(value => value.Outstanding).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(value => value.ModifiedDate).IsConcurrencyToken();
        modelBuilder.Entity<InvoiceOrderItem>().HasOne(value => value.Invoice).WithMany(value => value.InvoiceOrderItems)
            .HasForeignKey(value => value.InvoiceId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_OrderItem_Invoice");
        modelBuilder.Entity<InvoiceFile>().HasOne(value => value.Invoice).WithMany(value => value.InvoiceFiles)
            .HasForeignKey(value => value.InvoiceId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_InvoiceFile_Invoice");
    }
}

public sealed class ReceiptDbContext(DbContextOptions<ReceiptDbContext> options) : DbContext(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptOrderItem> Items => Set<ReceiptOrderItem>();
    public DbSet<ReceiptFile> Files => Set<ReceiptFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ModelRules.Apply(modelBuilder);
        modelBuilder.Entity<ReceiptOrderItem>().ToTable("OrderItem");
        modelBuilder.Entity<ReceiptOrderItem>().Property(value => value.UnitPrice).HasPrecision(18, 2);
        modelBuilder.Entity<ReceiptOrderItem>().Property(value => value.Subtotal).HasPrecision(18, 2)
            .HasComputedColumnSql("(\"UnitPrice\" * \"Quantity\")::numeric(18,2)", stored: true);
        modelBuilder.Entity<Receipt>().Property(value => value.AmountPaid).HasPrecision(18, 2)
            .HasComputedColumnSql("(\"Total\" - COALESCE(\"WithholdingTax\", 0))::numeric(18,2)", stored: true);
        modelBuilder.Entity<Receipt>().Property(value => value.Vat).HasColumnName("VAT").HasPrecision(18, 2);
        modelBuilder.Entity<Receipt>().Property(value => value.Subtotal).HasPrecision(18, 2);
        modelBuilder.Entity<Receipt>().Property(value => value.Total).HasPrecision(18, 2);
        modelBuilder.Entity<Receipt>().Property(value => value.WithholdingTax).HasPrecision(18, 2);
        modelBuilder.Entity<Receipt>().Property(value => value.ModifiedDate).IsConcurrencyToken();
        modelBuilder.Entity<ReceiptOrderItem>().HasOne(value => value.Receipt).WithMany(value => value.ReceiptOrderItem)
            .HasForeignKey(value => value.ReceiptId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_OrderItem_Receipt");
        modelBuilder.Entity<ReceiptFile>().HasOne(value => value.Receipt).WithMany(value => value.ReceiptFile)
            .HasForeignKey(value => value.ReceiptId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ReceiptFile_Receipt");
    }
}

file static class ModelRules
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(entity.ClrType.Name);
            foreach (var property in entity.GetProperties())
            {
                if (property.Name == "Id")
                {
                    property.SetColumnName("ID");
                }
                else if (property.Name.EndsWith("Id", StringComparison.Ordinal))
                {
                    property.SetColumnName(property.Name[..^2] + "ID");
                }

                if (property.Name is "CreatedDate" or "ModifiedDate")
                {
                    property.SetColumnType("timestamp with time zone");
                    property.SetDefaultValueSql("CURRENT_TIMESTAMP");
                }
            }
        }
    }
}
