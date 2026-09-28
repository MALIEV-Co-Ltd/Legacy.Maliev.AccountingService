using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice
{
    /// <inheritdoc />
    public partial class AddInvoiceCreationAdmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceCreationAdmission",
                columns: table => new
                {
                    OperationID = table.Column<Guid>(type: "uuid", nullable: false),
                    QuotationID = table.Column<int>(type: "integer", nullable: false),
                    EmployeeSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ServiceSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IntentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceCreationAdmission", x => x.OperationID);
                    table.CheckConstraint("CK_InvoiceCreationAdmission_Quotation", "\"QuotationID\" > 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceCreationAdmission");
        }
    }
}
