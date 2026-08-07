using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice;

/// <inheritdoc />
public partial class FixTimestampAndInvoiceColumnCasing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "VAT",
            table: "Invoice",
            newName: "Vat");

        migrationBuilder.RenameColumn(
            name: "FOB",
            table: "Invoice",
            newName: "Fob");

        ConvertUtcTimestampColumns(migrationBuilder, toTimestampWithoutTimeZone: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ConvertUtcTimestampColumns(migrationBuilder, toTimestampWithoutTimeZone: false);

        migrationBuilder.RenameColumn(
            name: "Vat",
            table: "Invoice",
            newName: "VAT");

        migrationBuilder.RenameColumn(
            name: "Fob",
            table: "Invoice",
            newName: "FOB");
    }

    private static void ConvertUtcTimestampColumns(MigrationBuilder migrationBuilder, bool toTimestampWithoutTimeZone)
    {
        var targetType = toTimestampWithoutTimeZone
            ? "timestamp without time zone"
            : "timestamp with time zone";
        var defaultSql = toTimestampWithoutTimeZone
            ? "CURRENT_TIMESTAMP AT TIME ZONE 'UTC'"
            : "CURRENT_TIMESTAMP";

        foreach (var (table, column) in UtcTimestampColumns)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE "{table}"
                ALTER COLUMN "{column}" DROP DEFAULT;
                ALTER TABLE "{table}"
                ALTER COLUMN "{column}" TYPE {targetType}
                USING "{column}" AT TIME ZONE 'UTC';
                ALTER TABLE "{table}"
                ALTER COLUMN "{column}" SET DEFAULT {defaultSql};
                """);
        }
    }

    private static readonly (string Table, string Column)[] UtcTimestampColumns =
    [
        ("OrderItem", "ModifiedDate"),
        ("OrderItem", "CreatedDate"),
        ("InvoiceFile", "ModifiedDate"),
        ("InvoiceFile", "CreatedDate"),
        ("Invoice", "ModifiedDate"),
        ("Invoice", "CreatedDate")
    ];
}
