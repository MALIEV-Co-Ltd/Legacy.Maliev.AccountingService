using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoice",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<string>(type: "text", nullable: true),
                    CustomerID = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    InternalComment = table.Column<string>(type: "text", nullable: true),
                    SalesPerson = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    PurchaseOrderNumber = table.Column<string>(type: "text", nullable: true),
                    Requisitioner = table.Column<string>(type: "text", nullable: true),
                    ShippedVia = table.Column<string>(type: "text", nullable: true),
                    FOB = table.Column<string>(type: "text", nullable: true),
                    Terms = table.Column<string>(type: "text", nullable: true),
                    BillingAddressRecipient = table.Column<string>(type: "text", nullable: true),
                    BillingAddressCompany = table.Column<string>(type: "text", nullable: true),
                    BillingAddressBuilding = table.Column<string>(type: "text", nullable: true),
                    BillingAddressLine1 = table.Column<string>(type: "text", nullable: true),
                    BillingAddressLine2 = table.Column<string>(type: "text", nullable: true),
                    BillingAddressCity = table.Column<string>(type: "text", nullable: true),
                    BillingAddressState = table.Column<string>(type: "text", nullable: true),
                    BillingAddressPostalCode = table.Column<string>(type: "text", nullable: true),
                    BillingAddressCountry = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressRecipient = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressRecipientTelephone = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressCompany = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressBuilding = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressLine1 = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressLine2 = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressCity = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressState = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressPostalCode = table.Column<string>(type: "text", nullable: true),
                    ShippingAddressCountry = table.Column<string>(type: "text", nullable: true),
                    CommercialRegistration = table.Column<string>(type: "text", nullable: true),
                    TaxIdentification = table.Column<string>(type: "text", nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    VAT = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    WithholdingTax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Outstanding = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    ReceiptID = table.Column<int>(type: "integer", nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoice", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceFile",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceID = table.Column<int>(type: "integer", nullable: false),
                    Bucket = table.Column<string>(type: "text", nullable: true),
                    ObjectName = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceFile", x => x.ID);
                    table.ForeignKey(
                        name: "FK_InvoiceFile_Invoice",
                        column: x => x.InvoiceID,
                        principalTable: "Invoice",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "OrderItem",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceID = table.Column<int>(type: "integer", nullable: true),
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
                        name: "FK_OrderItem_Invoice",
                        column: x => x.InvoiceID,
                        principalTable: "Invoice",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceFile_InvoiceID",
                table: "InvoiceFile",
                column: "InvoiceID");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItem_InvoiceID",
                table: "OrderItem",
                column: "InvoiceID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceFile");

            migrationBuilder.DropTable(
                name: "OrderItem");

            migrationBuilder.DropTable(
                name: "Invoice");
        }
    }
}
