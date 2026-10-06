using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Receipt;

/// <inheritdoc />
public partial class RequireFileMetadata : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "ReceiptFile" IN ACCESS EXCLUSIVE MODE;
        DO $$
        BEGIN
            IF current_setting('server_encoding') <> 'UTF8' OR
               (SELECT count(*) FROM pg_attribute
                WHERE attrelid = '"ReceiptFile"'::regclass AND attname IN ('Bucket', 'ObjectName')
                  AND NOT attnotnull AND atttypid = 'text'::regtype AND NOT attisdropped) <> 2 OR
               EXISTS (SELECT 1 FROM pg_constraint
                       WHERE conrelid = '"ReceiptFile"'::regclass AND conname = 'CK_ReceiptFile_BucketLength') THEN
                RAISE EXCEPTION 'ReceiptFile metadata schema preimage mismatch';
            END IF;
            IF EXISTS (SELECT 1 FROM "ReceiptFile"
                       WHERE "Bucket" IS NULL OR "ObjectName" IS NULL OR
                         char_length("Bucket") + char_length(regexp_replace("Bucket" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 50) THEN
                RAISE EXCEPTION 'ReceiptFile metadata requires explicit owner reconciliation before migration';
            END IF;
        END $$;
        ALTER TABLE "ReceiptFile" ALTER COLUMN "Bucket" SET NOT NULL, ALTER COLUMN "ObjectName" SET NOT NULL;
        ALTER TABLE "ReceiptFile" ADD CONSTRAINT "CK_ReceiptFile_BucketLength"
            CHECK (char_length("Bucket") + char_length(regexp_replace("Bucket" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50);
        """);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "ReceiptFile" IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE "ReceiptFile" DROP CONSTRAINT "CK_ReceiptFile_BucketLength";
        ALTER TABLE "ReceiptFile" ALTER COLUMN "Bucket" DROP NOT NULL, ALTER COLUMN "ObjectName" DROP NOT NULL;
        """);
}
