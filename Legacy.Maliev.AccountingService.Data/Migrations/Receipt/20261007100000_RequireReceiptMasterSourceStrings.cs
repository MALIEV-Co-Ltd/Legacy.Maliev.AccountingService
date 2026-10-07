using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Receipt;

/// <inheritdoc />
public partial class RequireReceiptMasterSourceStrings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "Receipt" IN ACCESS EXCLUSIVE MODE;
        DO $$
        BEGIN
            IF current_setting('server_encoding') <> 'UTF8' THEN
                RAISE EXCEPTION 'Accounting source strings require UTF8';
            END IF;
            IF (SELECT count(*) FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname IN ('BillingAddressBuilding', 'BillingAddressCity', 'BillingAddressCompany', 'BillingAddressCountry', 'BillingAddressPostalCode', 'BillingAddressRecipient', 'BillingAddressState', 'CommercialRegistration', 'Currency', 'InvoiceNumber', 'TaxIdentification') AND NOT attnotnull AND atttypid = 'text'::regtype AND NOT attisdropped) <> 11 THEN
                RAISE EXCEPTION 'Receipt source string schema preimage mismatch';
            END IF;
            IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname IN ('CK_Receipt_BillingAddressBuilding_SourceLength', 'CK_Receipt_BillingAddressCity_SourceLength', 'CK_Receipt_BillingAddressCompany_SourceLength', 'CK_Receipt_BillingAddressCountry_SourceLength', 'CK_Receipt_BillingAddressPostalCode_SourceLength', 'CK_Receipt_BillingAddressRecipient_SourceLength', 'CK_Receipt_BillingAddressState_SourceLength', 'CK_Receipt_CommercialRegistration_SourceLength', 'CK_Receipt_Currency_SourceLength', 'CK_Receipt_InvoiceNumber_SourceLength', 'CK_Receipt_TaxIdentification_SourceLength')) THEN
                RAISE EXCEPTION 'Receipt source string constraints already exist';
            END IF;
            IF (SELECT count(*) FROM (SELECT 1 FROM "Receipt" LIMIT 10001) bounded_rows) > 10000 THEN
                RAISE EXCEPTION 'Receipt source string preflight exceeds bounded row admission';
            END IF;
            IF EXISTS (SELECT 1 FROM "Receipt" WHERE char_length("BillingAddressBuilding") + char_length(regexp_replace("BillingAddressBuilding" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressCity") + char_length(regexp_replace("BillingAddressCity" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressCompany") + char_length(regexp_replace("BillingAddressCompany" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressCountry") + char_length(regexp_replace("BillingAddressCountry" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressPostalCode") + char_length(regexp_replace("BillingAddressPostalCode" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressRecipient") + char_length(regexp_replace("BillingAddressRecipient" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressState") + char_length(regexp_replace("BillingAddressState" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("CommercialRegistration") + char_length(regexp_replace("CommercialRegistration" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        "Currency" IS NULL OR
        char_length("Currency") + char_length(regexp_replace("Currency" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("InvoiceNumber") + char_length(regexp_replace("InvoiceNumber" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("TaxIdentification") + char_length(regexp_replace("TaxIdentification" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256) THEN
                RAISE EXCEPTION 'Receipt source strings require explicit owner reconciliation before migration';
            END IF;
        END $$;
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_BillingAddressBuilding_SourceLength" CHECK (char_length("BillingAddressBuilding") + char_length(regexp_replace("BillingAddressBuilding" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_BillingAddressCity_SourceLength" CHECK (char_length("BillingAddressCity") + char_length(regexp_replace("BillingAddressCity" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_BillingAddressCompany_SourceLength" CHECK (char_length("BillingAddressCompany") + char_length(regexp_replace("BillingAddressCompany" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_BillingAddressCountry_SourceLength" CHECK (char_length("BillingAddressCountry") + char_length(regexp_replace("BillingAddressCountry" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_BillingAddressPostalCode_SourceLength" CHECK (char_length("BillingAddressPostalCode") + char_length(regexp_replace("BillingAddressPostalCode" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_BillingAddressRecipient_SourceLength" CHECK (char_length("BillingAddressRecipient") + char_length(regexp_replace("BillingAddressRecipient" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_BillingAddressState_SourceLength" CHECK (char_length("BillingAddressState") + char_length(regexp_replace("BillingAddressState" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_CommercialRegistration_SourceLength" CHECK (char_length("CommercialRegistration") + char_length(regexp_replace("CommercialRegistration" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Receipt" ALTER COLUMN "Currency" SET NOT NULL;
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_Currency_SourceLength" CHECK (char_length("Currency") + char_length(regexp_replace("Currency" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_InvoiceNumber_SourceLength" CHECK (char_length("InvoiceNumber") + char_length(regexp_replace("InvoiceNumber" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Receipt" ADD CONSTRAINT "CK_Receipt_TaxIdentification_SourceLength" CHECK (char_length("TaxIdentification") + char_length(regexp_replace("TaxIdentification" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        """);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "Receipt" IN ACCESS EXCLUSIVE MODE;
        DO $$
        BEGIN
            IF current_setting('server_encoding') <> 'UTF8' THEN
                RAISE EXCEPTION 'Accounting source strings require UTF8';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'BillingAddressBuilding' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_BillingAddressBuilding_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'BillingAddressBuilding')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressBuilding" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressBuilding") + char_length(regexp_replace("BillingAddressBuilding" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_BillingAddressBuilding_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'BillingAddressCity' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_BillingAddressCity_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'BillingAddressCity')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressCity" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressCity") + char_length(regexp_replace("BillingAddressCity" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_BillingAddressCity_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'BillingAddressCompany' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_BillingAddressCompany_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'BillingAddressCompany')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressCompany" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressCompany") + char_length(regexp_replace("BillingAddressCompany" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_BillingAddressCompany_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'BillingAddressCountry' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_BillingAddressCountry_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'BillingAddressCountry')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressCountry" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressCountry") + char_length(regexp_replace("BillingAddressCountry" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_BillingAddressCountry_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'BillingAddressPostalCode' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_BillingAddressPostalCode_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'BillingAddressPostalCode')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressPostalCode" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressPostalCode") + char_length(regexp_replace("BillingAddressPostalCode" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_BillingAddressPostalCode_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'BillingAddressRecipient' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_BillingAddressRecipient_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'BillingAddressRecipient')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressRecipient" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressRecipient") + char_length(regexp_replace("BillingAddressRecipient" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_BillingAddressRecipient_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'BillingAddressState' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_BillingAddressState_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'BillingAddressState')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressState" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressState") + char_length(regexp_replace("BillingAddressState" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_BillingAddressState_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'CommercialRegistration' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_CommercialRegistration_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'CommercialRegistration')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("CommercialRegistration" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("CommercialRegistration") + char_length(regexp_replace("CommercialRegistration" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_CommercialRegistration_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'Currency' AND attnotnull = true AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_Currency_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'Currency')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Currency" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Currency") + char_length(regexp_replace("Currency" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_Currency_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'InvoiceNumber' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_InvoiceNumber_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'InvoiceNumber')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("InvoiceNumber" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("InvoiceNumber") + char_length(regexp_replace("InvoiceNumber" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_InvoiceNumber_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass
                AND attname = 'TaxIdentification' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Receipt source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass
                AND conname = 'CK_Receipt_TaxIdentification_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Receipt"'::regclass AND attname = 'TaxIdentification')]::smallint[]) THEN
                RAISE EXCEPTION 'Receipt source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("TaxIdentification" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("TaxIdentification") + char_length(regexp_replace("TaxIdentification" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Receipt"'::regclass AND conname = 'CK_Receipt_TaxIdentification_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Receipt source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
        END $$;
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_BillingAddressBuilding_SourceLength";
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_BillingAddressCity_SourceLength";
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_BillingAddressCompany_SourceLength";
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_BillingAddressCountry_SourceLength";
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_BillingAddressPostalCode_SourceLength";
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_BillingAddressRecipient_SourceLength";
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_BillingAddressState_SourceLength";
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_CommercialRegistration_SourceLength";
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_Currency_SourceLength";
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_InvoiceNumber_SourceLength";
        ALTER TABLE "Receipt" DROP CONSTRAINT "CK_Receipt_TaxIdentification_SourceLength";
        ALTER TABLE "Receipt" ALTER COLUMN "Currency" DROP NOT NULL;
        """);
}
