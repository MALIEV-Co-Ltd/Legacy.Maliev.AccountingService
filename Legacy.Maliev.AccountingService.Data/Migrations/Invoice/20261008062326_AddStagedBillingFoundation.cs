using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice
{
    /// <inheritdoc />
    public partial class AddStagedBillingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingAccount",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuotationId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    StateJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAccount", x => x.Id);
                    table.CheckConstraint("CK_BillingAccount_FinancialState", "COALESCE(\n  (\"StateJson\"->>'Revision')::bigint = \"Revision\"\n  AND (\"StateJson\"->'Snapshot'->>'QuotationId')::int = \"QuotationId\"\n  AND (\"StateJson\"->'Snapshot'->>'CustomerId')::int = \"CustomerId\"\n  AND jsonb_typeof(\"StateJson\"->'Stages') = 'array'\n  AND (\"StateJson\"->'Snapshot'->'Cap'->>'Base')::numeric >= 0\n  AND (\"StateJson\"->'Snapshot'->'Cap'->>'Vat')::numeric >= 0\n  AND (\"StateJson\"->'Snapshot'->'Cap'->>'Gross')::numeric =\n    (\"StateJson\"->'Snapshot'->'Cap'->>'Base')::numeric + (\"StateJson\"->'Snapshot'->'Cap'->>'Vat')::numeric\n  AND (\"StateJson\"->'Billed'->>'Base')::numeric BETWEEN 0 AND (\"StateJson\"->'Snapshot'->'Cap'->>'Base')::numeric\n  AND (\"StateJson\"->'Billed'->>'Vat')::numeric BETWEEN 0 AND (\"StateJson\"->'Snapshot'->'Cap'->>'Vat')::numeric\n  AND (\"StateJson\"->'Billed'->>'Gross')::numeric =\n    (\"StateJson\"->'Billed'->>'Base')::numeric + (\"StateJson\"->'Billed'->>'Vat')::numeric\n  AND (\"StateJson\"->'Billed'->>'Currency') = (\"StateJson\"->'Snapshot'->'Cap'->>'Currency')\n  AND (\"StateJson\"->>'Cash')::numeric >= 0 AND (\"StateJson\"->>'Withholding')::numeric >= 0\n  AND (\"StateJson\"->>'Outstanding')::numeric >= 0\n  AND (\"StateJson\"->>'Outstanding')::numeric = (\"StateJson\"->'Billed'->>'Gross')::numeric\n    - (\"StateJson\"->>'Cash')::numeric - (\"StateJson\"->>'Withholding')::numeric\n  AND (\"StateJson\"->>'Unbilled')::numeric >= 0\n  AND (\"StateJson\"->>'Unbilled')::numeric = (\"StateJson\"->'Snapshot'->'Cap'->>'Gross')::numeric\n    - (\"StateJson\"->'Billed'->>'Gross')::numeric\n  AND (\"StateJson\"->>'VatRecognized')::numeric >= 0, FALSE)");
                    table.CheckConstraint("CK_BillingAccount_Identity", "\"QuotationId\" > 0 AND \"CustomerId\" > 0 AND \"Revision\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "BillingOperation",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    CommandJson = table.Column<string>(type: "jsonb", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingOperation", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_BillingOperation_BillingAccount_AccountId",
                        column: x => x.AccountId,
                        principalTable: "BillingAccount",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingIntent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingIntent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingIntent_BillingOperation_OperationId",
                        column: x => x.OperationId,
                        principalTable: "BillingOperation",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccount_QuotationId",
                table: "BillingAccount",
                column: "QuotationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingIntent_OperationId_Kind",
                table: "BillingIntent",
                columns: new[] { "OperationId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingOperation_AccountId",
                table: "BillingOperation",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingIntent");

            migrationBuilder.DropTable(
                name: "BillingOperation");

            migrationBuilder.DropTable(
                name: "BillingAccount");
        }
    }
}
