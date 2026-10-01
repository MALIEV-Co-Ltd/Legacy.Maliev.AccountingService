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
    public DbSet<InvoiceCreationAdmission> InvoiceCreationAdmissions => Set<InvoiceCreationAdmission>();
    public DbSet<InvoiceNotificationCorrelationRow> InvoiceNotificationCorrelations => Set<InvoiceNotificationCorrelationRow>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceOrderItem> Items => Set<InvoiceOrderItem>();
    public DbSet<InvoiceFile> Files => Set<InvoiceFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ModelRules.Apply(modelBuilder);
        modelBuilder.Entity<InvoiceCreationAdmission>(entity =>
        {
            entity.HasKey(value => value.OperationId);
            entity.Property(value => value.OperationId).HasColumnName("OperationID");
            entity.Property(value => value.QuotationId).HasColumnName("QuotationID");
            entity.Property(value => value.EmployeeSubject).HasMaxLength(256);
            entity.Property(value => value.ServiceSubject).HasMaxLength(128);
            entity.Property(value => value.IntentFingerprint).HasMaxLength(64);
            entity.Property(value => value.State).HasMaxLength(32);
            entity.ToTable(table => table.HasCheckConstraint("CK_InvoiceCreationAdmission_Quotation", "\"QuotationID\" > 0"));
        });
        modelBuilder.Entity<InvoiceNotificationCorrelationRow>(entity =>
        {
            entity.HasKey(value => value.IntentId).HasName("PK_InvoiceNotificationCorrelation");
            entity.Property(value => value.IntentId).HasColumnName("IntentID").ValueGeneratedNever();
            entity.Property(value => value.InvoiceId).HasColumnName("InvoiceID");
            entity.Property(value => value.QuotationId).HasColumnName("QuotationID");
            entity.Property(value => value.WorkflowOperationId).HasColumnName("WorkflowOperationID");
            entity.Property(value => value.Purpose).HasMaxLength(32);
            entity.Property(value => value.OriginIssuer).HasMaxLength(512);
            entity.Property(value => value.OriginEmployeeSubject).HasMaxLength(256);
            entity.Property(value => value.OriginServiceSubject).HasMaxLength(128);
            entity.Property(value => value.SenderIssuer).HasMaxLength(512);
            entity.Property(value => value.SenderServiceSubject).HasMaxLength(128);
            entity.Property(value => value.PayloadFrameVersion).HasMaxLength(64);
            entity.Property(value => value.BindingVersion).HasMaxLength(64);
            entity.Property(value => value.BindingKeyId).HasColumnName("BindingKeyID").HasMaxLength(64);
            entity.Property(value => value.Phase).HasMaxLength(32);
            entity.Property(value => value.RemoteState).HasMaxLength(32);
            entity.Property(value => value.Version).IsConcurrencyToken();
            entity.HasAlternateKey(value => new { value.InvoiceId, value.Purpose })
                .HasName("UQ_InvoiceNotificationCorrelation_InvoicePurpose");
            entity.ToTable("InvoiceNotificationCorrelation", "public", table =>
            {
                table.HasCheckConstraint("CK_InvoiceNotificationCorrelation_Receipt", """
                    ("RemoteVersion" IS NULL AND "RemoteState" IS NULL AND
                     "RemoteAdmittedAt" IS NULL AND "RemoteUpdatedAt" IS NULL AND "RemoteReceiptBinding" IS NULL)
                    OR
                    ("RemoteVersion" IS NOT NULL AND "RemoteVersion">0 AND "RemoteState" IS NOT NULL AND
                     "RemoteAdmittedAt" IS NOT NULL AND "RemoteUpdatedAt" IS NOT NULL AND
                     "RemoteReceiptBinding" IS NOT NULL AND octet_length("RemoteReceiptBinding")=32 AND
                     "RemoteUpdatedAt">="RemoteAdmittedAt" AND
                     (("RemoteState"='admitted' AND "RemoteVersion"=1) OR
                      ("RemoteState"='submitting' AND "RemoteVersion"=2) OR
                      ("RemoteState" IN ('outcomeUnknown','providerAccepted') AND "RemoteVersion"=3)))
                    """);
                table.HasCheckConstraint("CK_InvoiceNotificationCorrelation_ReceiptPhase", """
                    ("Phase" IN ('Prepared','AdmissionIssued') AND "RemoteVersion" IS NULL)
                    OR ("Phase" IN ('Admitted','ExecutionIssued') AND "RemoteVersion" IS NOT NULL AND "RemoteState"='admitted')
                    OR ("Phase"='OutcomeUnknown' AND "RemoteVersion" IS NOT NULL AND "RemoteState" IN ('submitting','outcomeUnknown'))
                    OR ("Phase"='ProviderAccepted' AND "RemoteVersion" IS NOT NULL AND "RemoteState"='providerAccepted')
                    """);
                table.HasCheckConstraint("CK_InvoiceNotificationCorrelation_Identity", """
                    "InvoiceID" > 0 AND "QuotationID" > 0 AND "Purpose" = 'invoice-issued'
                    AND "SenderServiceSubject" = 'service:legacy-accounting'
                    AND "IntentID" <> '00000000-0000-0000-0000-000000000000'::uuid
                    AND "WorkflowOperationID" <> '00000000-0000-0000-0000-000000000000'::uuid
                    AND "IntentID" <> "WorkflowOperationID"
                    AND octet_length("PayloadBinding") = 32
                    AND length("OriginIssuer") > 0 AND length("OriginEmployeeSubject") > 0
                    AND length("OriginServiceSubject") > 0 AND length("SenderIssuer") > 0
                    AND length("BindingKeyID") > 0
                    AND "PayloadFrameVersion" = 'notification-payload-v1'
                    AND "BindingVersion" = 'accounting-invoice-notification-hmac-v1'
                    """);
                table.HasCheckConstraint("CK_InvoiceNotificationCorrelation_State", """
                    "Version" > 0 AND ("RemoteVersion" IS NULL OR "RemoteVersion" > 0)
                    AND "UpdatedAt" >= "CreatedAt"
                    AND "Phase" IN ('Prepared','AdmissionIssued','Admitted','ExecutionIssued',
                                    'OutcomeUnknown','ProviderAccepted','RejectedBeforeSubmission')
                    AND ("AdmissionIssuedAt" IS NULL OR "AdmissionIssuedAt" >= "CreatedAt")
                    AND ("ExecutionIssuedAt" IS NULL OR
                      ("AdmissionIssuedAt" IS NOT NULL AND "ExecutionIssuedAt" >= "AdmissionIssuedAt"))
                    AND ("Phase" = 'Prepared' OR "AdmissionIssuedAt" IS NOT NULL)
                    AND ("Phase" <> 'Prepared' OR
                      ("AdmissionIssuedAt" IS NULL AND "ExecutionIssuedAt" IS NULL AND "RemoteVersion" IS NULL))
                    AND ("Phase" <> 'AdmissionIssued' OR
                      ("AdmissionIssuedAt" IS NOT NULL AND "ExecutionIssuedAt" IS NULL))
                    AND ("Phase" <> 'Admitted' OR
                      ("AdmissionIssuedAt" IS NOT NULL AND "ExecutionIssuedAt" IS NULL AND "RemoteVersion" IS NOT NULL))
                    AND ("Phase" <> 'ExecutionIssued' OR
                      ("ExecutionIssuedAt" IS NOT NULL AND "RemoteVersion" IS NOT NULL))
                    AND ("Phase" NOT IN ('OutcomeUnknown','ProviderAccepted') OR
                      ("ExecutionIssuedAt" IS NOT NULL AND "RemoteVersion" IS NOT NULL))
                    AND ("Phase" NOT IN ('ProviderAccepted','RejectedBeforeSubmission') OR "RemoteVersion" IS NOT NULL)
                    """);
            });
        });
        modelBuilder.Entity<InvoiceOrderItem>().ToTable("OrderItem");
        modelBuilder.Entity<InvoiceOrderItem>().Property(value => value.UnitPrice).HasPrecision(18, 2);
        modelBuilder.Entity<InvoiceOrderItem>().Property(value => value.Subtotal).HasPrecision(18, 2)
            .HasComputedColumnSql("(\"UnitPrice\" * \"Quantity\")::numeric(18,2)", stored: true);
        modelBuilder.Entity<Invoice>().Property(value => value.Vat).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(value => value.Subtotal).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(value => value.Total).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(value => value.WithholdingTax).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(value => value.Outstanding).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().HasIndex(value => value.SourceRequestId)
            .HasFilter("\"SourceRequestID\" IS NOT NULL");
        modelBuilder.Entity<Invoice>().HasIndex(value => value.SourceJourneyId)
            .HasFilter("\"SourceJourneyID\" IS NOT NULL");
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
            .HasComputedColumnSql("(\"Total\" - \"WithholdingTax\")::numeric(18,2)", stored: true);
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
                    property.SetColumnType("timestamp without time zone");
                    property.SetDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");
                }
            }
        }
    }
}
