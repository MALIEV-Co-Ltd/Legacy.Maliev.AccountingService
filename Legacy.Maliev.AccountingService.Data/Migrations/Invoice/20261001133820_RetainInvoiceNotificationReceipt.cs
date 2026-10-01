using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice
{
    /// <inheritdoc />
    public partial class RetainInvoiceNotificationReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemoteAdmittedAt",
                schema: "public",
                table: "InvoiceNotificationCorrelation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RemoteReceiptBinding",
                schema: "public",
                table: "InvoiceNotificationCorrelation",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteState",
                schema: "public",
                table: "InvoiceNotificationCorrelation",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemoteUpdatedAt",
                schema: "public",
                table: "InvoiceNotificationCorrelation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceNotificationCorrelation_Receipt",
                schema: "public",
                table: "InvoiceNotificationCorrelation",
                sql: "(\"RemoteVersion\" IS NULL AND \"RemoteState\" IS NULL AND\n \"RemoteAdmittedAt\" IS NULL AND \"RemoteUpdatedAt\" IS NULL AND \"RemoteReceiptBinding\" IS NULL)\nOR\n(\"RemoteVersion\" IS NOT NULL AND \"RemoteVersion\">0 AND \"RemoteState\" IS NOT NULL AND\n \"RemoteAdmittedAt\" IS NOT NULL AND \"RemoteUpdatedAt\" IS NOT NULL AND\n \"RemoteReceiptBinding\" IS NOT NULL AND octet_length(\"RemoteReceiptBinding\")=32 AND\n \"RemoteUpdatedAt\">=\"RemoteAdmittedAt\" AND\n ((\"RemoteState\"='admitted' AND \"RemoteVersion\"=1) OR\n  (\"RemoteState\"='submitting' AND \"RemoteVersion\"=2) OR\n  (\"RemoteState\" IN ('outcomeUnknown','providerAccepted') AND \"RemoteVersion\"=3)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceNotificationCorrelation_ReceiptPhase",
                schema: "public",
                table: "InvoiceNotificationCorrelation",
                sql: "(\"Phase\" IN ('Prepared','AdmissionIssued') AND \"RemoteVersion\" IS NULL)\nOR (\"Phase\" IN ('Admitted','ExecutionIssued') AND \"RemoteVersion\" IS NOT NULL AND \"RemoteState\"='admitted')\nOR (\"Phase\"='OutcomeUnknown' AND \"RemoteVersion\" IS NOT NULL AND \"RemoteState\" IN ('submitting','outcomeUnknown'))\nOR (\"Phase\"='ProviderAccepted' AND \"RemoteVersion\" IS NOT NULL AND \"RemoteState\"='providerAccepted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Invoice notification receipts retain non-expiring reconciliation authority; rollback is forbidden.");
        }
    }
}
