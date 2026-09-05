using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice
{
    /// <inheritdoc />
    public partial class AddInvoiceMarketingAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceJourneyID",
                table: "Invoice",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceRequestID",
                table: "Invoice",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_SourceJourneyID",
                table: "Invoice",
                column: "SourceJourneyID",
                filter: "\"SourceJourneyID\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_SourceRequestID",
                table: "Invoice",
                column: "SourceRequestID",
                filter: "\"SourceRequestID\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoice_SourceJourneyID",
                table: "Invoice");

            migrationBuilder.DropIndex(
                name: "IX_Invoice_SourceRequestID",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "SourceJourneyID",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "SourceRequestID",
                table: "Invoice");
        }
    }
}
