using Legacy.Maliev.AccountingService.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Additive Invoice-database model, installed only through the coordinated owner integration.</summary>
public static class BillingModelConfiguration
{
    public static void Apply(ModelBuilder model)
    {
        model.Entity<BillingAccountRow>(entity =>
        {
            entity.ToTable("BillingAccount", table =>
            {
                table.HasCheckConstraint("CK_BillingAccount_Identity", "\"QuotationId\" > 0 AND \"CustomerId\" > 0 AND \"Revision\" > 0");
                table.HasCheckConstraint("CK_BillingAccount_FinancialState", """
                    COALESCE(
                      ("StateJson"->>'Revision')::bigint = "Revision"
                      AND ("StateJson"->'Snapshot'->>'QuotationId')::int = "QuotationId"
                      AND ("StateJson"->'Snapshot'->>'CustomerId')::int = "CustomerId"
                      AND jsonb_typeof("StateJson"->'Stages') = 'array'
                      AND ("StateJson"->'Snapshot'->'Cap'->>'Base')::numeric >= 0
                      AND ("StateJson"->'Snapshot'->'Cap'->>'Vat')::numeric >= 0
                      AND ("StateJson"->'Snapshot'->'Cap'->>'Gross')::numeric =
                        ("StateJson"->'Snapshot'->'Cap'->>'Base')::numeric + ("StateJson"->'Snapshot'->'Cap'->>'Vat')::numeric
                      AND ("StateJson"->'Billed'->>'Base')::numeric BETWEEN 0 AND ("StateJson"->'Snapshot'->'Cap'->>'Base')::numeric
                      AND ("StateJson"->'Billed'->>'Vat')::numeric BETWEEN 0 AND ("StateJson"->'Snapshot'->'Cap'->>'Vat')::numeric
                      AND ("StateJson"->'Billed'->>'Gross')::numeric =
                        ("StateJson"->'Billed'->>'Base')::numeric + ("StateJson"->'Billed'->>'Vat')::numeric
                      AND ("StateJson"->'Billed'->>'Currency') = ("StateJson"->'Snapshot'->'Cap'->>'Currency')
                      AND ("StateJson"->>'Cash')::numeric >= 0 AND ("StateJson"->>'Withholding')::numeric >= 0
                      AND ("StateJson"->>'Outstanding')::numeric >= 0
                      AND ("StateJson"->>'Outstanding')::numeric = ("StateJson"->'Billed'->>'Gross')::numeric
                        - ("StateJson"->>'Cash')::numeric - ("StateJson"->>'Withholding')::numeric
                      AND ("StateJson"->>'Unbilled')::numeric >= 0
                      AND ("StateJson"->>'Unbilled')::numeric = ("StateJson"->'Snapshot'->'Cap'->>'Gross')::numeric
                        - ("StateJson"->'Billed'->>'Gross')::numeric
                      AND ("StateJson"->>'VatRecognized')::numeric >= 0, FALSE)
                    """);
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasIndex(row => row.QuotationId).IsUnique();
            entity.Property(row => row.StateJson).IsRequired().HasColumnType("jsonb");
            entity.Property(row => row.Revision).IsConcurrencyToken();
        });
        model.Entity<BillingOperationRow>(entity =>
        {
            entity.ToTable("BillingOperation");
            entity.HasKey(row => row.OperationId);
            entity.Property(row => row.OperationId).ValueGeneratedNever();
            entity.Property(row => row.Fingerprint).IsRequired().HasMaxLength(64);
            entity.Property(row => row.ResultJson).IsRequired().HasColumnType("jsonb");
            entity.Property(row => row.CommandJson).IsRequired().HasColumnType("jsonb");
            entity.HasOne<BillingAccountRow>().WithMany().HasForeignKey(row => row.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<BillingIntentRow>(entity =>
        {
            entity.ToTable("BillingIntent");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasIndex(row => new { row.OperationId, row.Kind }).IsUnique();
            entity.Property(row => row.Kind).IsRequired().HasMaxLength(64);
            entity.Property(row => row.State).IsRequired().HasMaxLength(32);
            entity.Property(row => row.PayloadJson).IsRequired().HasColumnType("jsonb");
            entity.HasOne<BillingOperationRow>().WithMany().HasForeignKey(row => row.OperationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
