using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Receipt
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Receipt",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerID = table.Column<int>(type: "integer", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "text", nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WithholdingTax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true, computedColumnSql: "(\"Total\" - COALESCE(\"WithholdingTax\", 0))::numeric(18,2)", stored: true),
                    VAT = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    TaxIdentification = table.Column<string>(type: "text", nullable: true),
                    CommercialRegistration = table.Column<string>(type: "text", nullable: true),
                    BillingAddressBuilding = table.Column<string>(type: "text", nullable: true),
                    BillingAddressCompany = table.Column<string>(type: "text", nullable: true),
                    BillingAddressRecipient = table.Column<string>(type: "text", nullable: true),
                    BillingAddressLine1 = table.Column<string>(type: "text", nullable: true),
                    BillingAddressLine2 = table.Column<string>(type: "text", nullable: true),
                    BillingAddressCity = table.Column<string>(type: "text", nullable: true),
                    BillingAddressState = table.Column<string>(type: "text", nullable: true),
                    BillingAddressCountry = table.Column<string>(type: "text", nullable: true),
                    BillingAddressPostalCode = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipt", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "OrderItem",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceiptID = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true, computedColumnSql: "(\"UnitPrice\" * \"Quantity\")::numeric(18,2)", stored: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItem", x => x.ID);
                    table.ForeignKey(
                        name: "FK_OrderItem_Receipt",
                        column: x => x.ReceiptID,
                        principalTable: "Receipt",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "ReceiptFile",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceiptID = table.Column<int>(type: "integer", nullable: false),
                    Bucket = table.Column<string>(type: "text", nullable: true),
                    ObjectName = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptFile", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ReceiptFile_Receipt",
                        column: x => x.ReceiptID,
                        principalTable: "Receipt",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItem_ReceiptID",
                table: "OrderItem",
                column: "ReceiptID");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptFile_ReceiptID",
                table: "ReceiptFile",
                column: "ReceiptID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderItem");

            migrationBuilder.DropTable(
                name: "ReceiptFile");

            migrationBuilder.DropTable(
                name: "Receipt");
        }
    }
}
