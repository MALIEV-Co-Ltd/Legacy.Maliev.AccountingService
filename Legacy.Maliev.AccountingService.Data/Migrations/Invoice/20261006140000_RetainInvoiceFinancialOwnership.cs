using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice;

/// <summary>Adds unpopulated committed ownership evidence; historical operations receive no authority.</summary>
public partial class RetainInvoiceFinancialOwnership : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "FinancialOwnershipJson", table: "InvoiceCreationAdmission", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(name: "EmployeeCompletionJson", table: "InvoiceCreationAdmission", type: "text", nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Committed invoice ownership evidence cannot be removed.");
}
