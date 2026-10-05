using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice;

/// <summary>Retains verified new admission origins and acknowledged financial evidence without backfilling old authority.</summary>
public partial class RetainInvoiceCreationOriginAndFinancialResult : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "OriginIssuer", table: "InvoiceCreationAdmission",
            type: "character varying(512)", maxLength: 512, nullable: true);
        migrationBuilder.AddColumn<string>(name: "FinancialResultJson", table: "InvoiceCreationAdmission",
            type: "text", nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retained invoice-create origin and financial authority cannot be removed.");
}
