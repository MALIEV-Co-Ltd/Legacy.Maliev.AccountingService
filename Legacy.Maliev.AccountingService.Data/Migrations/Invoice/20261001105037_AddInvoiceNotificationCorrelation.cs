using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice
{
    /// <inheritdoc />
    public partial class AddInvoiceNotificationCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "InvoiceNotificationCorrelation",
                schema: "public",
                columns: table => new
                {
                    IntentID = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceID = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QuotationID = table.Column<int>(type: "integer", nullable: false),
                    WorkflowOperationID = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginIssuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OriginEmployeeSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OriginServiceSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SenderIssuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SenderServiceSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadFrameVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BindingVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BindingKeyID = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadBinding = table.Column<byte[]>(type: "bytea", nullable: false),
                    Phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RemoteVersion = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AdmissionIssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExecutionIssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNotificationCorrelation", x => x.IntentID);
                    table.UniqueConstraint("UQ_InvoiceNotificationCorrelation_InvoicePurpose", x => new { x.InvoiceID, x.Purpose });
                    table.CheckConstraint("CK_InvoiceNotificationCorrelation_Identity", "\"InvoiceID\" > 0 AND \"QuotationID\" > 0 AND \"Purpose\" = 'invoice-issued'\nAND \"SenderServiceSubject\" = 'service:legacy-accounting'\nAND \"IntentID\" <> '00000000-0000-0000-0000-000000000000'::uuid\nAND \"WorkflowOperationID\" <> '00000000-0000-0000-0000-000000000000'::uuid\nAND \"IntentID\" <> \"WorkflowOperationID\"\nAND octet_length(\"PayloadBinding\") = 32\nAND length(\"OriginIssuer\") > 0 AND length(\"OriginEmployeeSubject\") > 0\nAND length(\"OriginServiceSubject\") > 0 AND length(\"SenderIssuer\") > 0\nAND length(\"BindingKeyID\") > 0\nAND \"PayloadFrameVersion\" = 'notification-payload-v1'\nAND \"BindingVersion\" = 'accounting-invoice-notification-hmac-v1'");
                    table.CheckConstraint("CK_InvoiceNotificationCorrelation_State", "\"Version\" > 0 AND (\"RemoteVersion\" IS NULL OR \"RemoteVersion\" > 0)\nAND \"UpdatedAt\" >= \"CreatedAt\"\nAND \"Phase\" IN ('Prepared','AdmissionIssued','Admitted','ExecutionIssued',\n                'OutcomeUnknown','ProviderAccepted','RejectedBeforeSubmission')\nAND (\"AdmissionIssuedAt\" IS NULL OR \"AdmissionIssuedAt\" >= \"CreatedAt\")\nAND (\"ExecutionIssuedAt\" IS NULL OR\n  (\"AdmissionIssuedAt\" IS NOT NULL AND \"ExecutionIssuedAt\" >= \"AdmissionIssuedAt\"))\nAND (\"Phase\" = 'Prepared' OR \"AdmissionIssuedAt\" IS NOT NULL)\nAND (\"Phase\" <> 'Prepared' OR\n  (\"AdmissionIssuedAt\" IS NULL AND \"ExecutionIssuedAt\" IS NULL AND \"RemoteVersion\" IS NULL))\nAND (\"Phase\" <> 'AdmissionIssued' OR\n  (\"AdmissionIssuedAt\" IS NOT NULL AND \"ExecutionIssuedAt\" IS NULL))\nAND (\"Phase\" <> 'Admitted' OR\n  (\"AdmissionIssuedAt\" IS NOT NULL AND \"ExecutionIssuedAt\" IS NULL AND \"RemoteVersion\" IS NOT NULL))\nAND (\"Phase\" <> 'ExecutionIssued' OR\n  (\"ExecutionIssuedAt\" IS NOT NULL AND \"RemoteVersion\" IS NOT NULL))\nAND (\"Phase\" NOT IN ('OutcomeUnknown','ProviderAccepted') OR\n  (\"ExecutionIssuedAt\" IS NOT NULL AND \"RemoteVersion\" IS NOT NULL))\nAND (\"Phase\" NOT IN ('ProviderAccepted','RejectedBeforeSubmission') OR \"RemoteVersion\" IS NOT NULL)");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Invoice notification correlation retains non-expiring reconciliation authority; rollback is forbidden.");
        }
    }
}
