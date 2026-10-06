using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice;

/// <inheritdoc />
public partial class RequireFileMetadata : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "InvoiceFile" IN ACCESS EXCLUSIVE MODE;
        DO $$
        BEGIN
            IF current_setting('server_encoding') <> 'UTF8' OR
               (SELECT count(*) FROM pg_attribute
                WHERE attrelid = '"InvoiceFile"'::regclass AND attname IN ('Bucket', 'ObjectName')
                  AND NOT attnotnull AND atttypid = 'text'::regtype AND NOT attisdropped) <> 2 OR
               EXISTS (SELECT 1 FROM pg_constraint
                       WHERE conrelid = '"InvoiceFile"'::regclass AND conname = 'CK_InvoiceFile_BucketLength') THEN
                RAISE EXCEPTION 'InvoiceFile metadata schema preimage mismatch';
            END IF;
            IF EXISTS (SELECT 1 FROM "InvoiceFile"
                       WHERE "Bucket" IS NULL OR "ObjectName" IS NULL OR
                         char_length("Bucket") + char_length(regexp_replace("Bucket" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 50) THEN
                RAISE EXCEPTION 'InvoiceFile metadata requires explicit owner reconciliation before migration';
            END IF;
        END $$;
        ALTER TABLE "InvoiceFile" ALTER COLUMN "Bucket" SET NOT NULL, ALTER COLUMN "ObjectName" SET NOT NULL;
        ALTER TABLE "InvoiceFile" ADD CONSTRAINT "CK_InvoiceFile_BucketLength"
            CHECK (char_length("Bucket") + char_length(regexp_replace("Bucket" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50);
        """);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "InvoiceFile" IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE "InvoiceFile" DROP CONSTRAINT "CK_InvoiceFile_BucketLength";
        ALTER TABLE "InvoiceFile" ALTER COLUMN "Bucket" DROP NOT NULL, ALTER COLUMN "ObjectName" DROP NOT NULL;
        """);
}
