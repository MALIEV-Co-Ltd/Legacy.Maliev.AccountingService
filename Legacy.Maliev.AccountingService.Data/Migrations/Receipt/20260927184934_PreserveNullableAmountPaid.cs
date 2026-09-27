using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Receipt;

/// <inheritdoc />
public partial class PreserveNullableAmountPaid : Migration
{
    // pg_get_expr canonical output on the supported PostgreSQL 18 target.
    private const string OldExpression = "((\"Total\" - COALESCE(\"WithholdingTax\", (0)::numeric)))::numeric(18,2)";
    private const string NewExpression = "((\"Total\" - \"WithholdingTax\"))::numeric(18,2)";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        SetExpression(migrationBuilder, OldExpression,
            "(\"Total\" - \"WithholdingTax\")::numeric(18,2)");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        SetExpression(migrationBuilder, NewExpression,
            "(\"Total\" - COALESCE(\"WithholdingTax\", 0))::numeric(18,2)");

    private static void SetExpression(MigrationBuilder migrationBuilder, string expectedExpression, string expression)
    {
        // PostgreSQL 17+ can replace a stored generation expression in place. It rewrites
        // derived values from Total/WithholdingTax without dropping the column or its dependents.
        // Fail closed if the physical schema has drifted from this migration chain.
        migrationBuilder.Sql($"""
            SET LOCAL lock_timeout = '5s';
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_attribute AS a
                    JOIN pg_class AS c ON c.oid = a.attrelid
                    JOIN pg_namespace AS n ON n.oid = c.relnamespace
                    JOIN pg_attrdef AS d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
                    WHERE n.nspname = current_schema()
                      AND c.relname = 'Receipt'
                      AND a.attname = 'AmountPaid'
                      AND a.attgenerated = 's'
                      AND NOT a.attnotnull
                      AND format_type(a.atttypid, a.atttypmod) = 'numeric(18,2)'
                      AND pg_get_expr(d.adbin, d.adrelid) = '{expectedExpression}'
                ) THEN
                    RAISE EXCEPTION 'Receipt.AmountPaid generated-column preimage mismatch';
                END IF;
            END $$;
            ALTER TABLE "Receipt" ALTER COLUMN "AmountPaid" SET EXPRESSION AS ({expression});
            """);
    }
}
