using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Invoice;

/// <inheritdoc />
public partial class RequireInvoiceMasterSourceStrings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "Invoice" IN ACCESS EXCLUSIVE MODE;
        DO $$
        BEGIN
            IF current_setting('server_encoding') <> 'UTF8' THEN
                RAISE EXCEPTION 'Accounting source strings require UTF8';
            END IF;
            IF (SELECT count(*) FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname IN ('BillingAddressBuilding', 'BillingAddressCity', 'BillingAddressCompany', 'BillingAddressCountry', 'BillingAddressLine1', 'BillingAddressLine2', 'BillingAddressPostalCode', 'BillingAddressRecipient', 'BillingAddressState', 'CommercialRegistration', 'Currency', 'Fob', 'Number', 'PurchaseOrderNumber', 'Requisitioner', 'SalesPerson', 'ShippedVia', 'ShippingAddressBuilding', 'ShippingAddressCity', 'ShippingAddressCompany', 'ShippingAddressCountry', 'ShippingAddressLine1', 'ShippingAddressLine2', 'ShippingAddressPostalCode', 'ShippingAddressRecipient', 'ShippingAddressRecipientTelephone', 'ShippingAddressState', 'TaxIdentification', 'Terms') AND NOT attnotnull AND atttypid = 'text'::regtype AND NOT attisdropped) <> 29 THEN
                RAISE EXCEPTION 'Invoice source string schema preimage mismatch';
            END IF;
            IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname IN ('CK_Invoice_BillingAddressBuilding_SourceLength', 'CK_Invoice_BillingAddressCity_SourceLength', 'CK_Invoice_BillingAddressCompany_SourceLength', 'CK_Invoice_BillingAddressCountry_SourceLength', 'CK_Invoice_BillingAddressLine1_SourceLength', 'CK_Invoice_BillingAddressLine2_SourceLength', 'CK_Invoice_BillingAddressPostalCode_SourceLength', 'CK_Invoice_BillingAddressRecipient_SourceLength', 'CK_Invoice_BillingAddressState_SourceLength', 'CK_Invoice_CommercialRegistration_SourceLength', 'CK_Invoice_Currency_SourceLength', 'CK_Invoice_Fob_SourceLength', 'CK_Invoice_Number_SourceLength', 'CK_Invoice_PurchaseOrderNumber_SourceLength', 'CK_Invoice_Requisitioner_SourceLength', 'CK_Invoice_SalesPerson_SourceLength', 'CK_Invoice_ShippedVia_SourceLength', 'CK_Invoice_ShippingAddressBuilding_SourceLength', 'CK_Invoice_ShippingAddressCity_SourceLength', 'CK_Invoice_ShippingAddressCompany_SourceLength', 'CK_Invoice_ShippingAddressCountry_SourceLength', 'CK_Invoice_ShippingAddressLine1_SourceLength', 'CK_Invoice_ShippingAddressLine2_SourceLength', 'CK_Invoice_ShippingAddressPostalCode_SourceLength', 'CK_Invoice_ShippingAddressRecipient_SourceLength', 'CK_Invoice_ShippingAddressRecipientTelephone_SourceLength', 'CK_Invoice_ShippingAddressState_SourceLength', 'CK_Invoice_TaxIdentification_SourceLength', 'CK_Invoice_Terms_SourceLength')) THEN
                RAISE EXCEPTION 'Invoice source string constraints already exist';
            END IF;
            IF (SELECT count(*) FROM (SELECT 1 FROM "Invoice" LIMIT 10001) bounded_rows) > 10000 THEN
                RAISE EXCEPTION 'Invoice source string preflight exceeds bounded row admission';
            END IF;
            IF EXISTS (SELECT 1 FROM "Invoice" WHERE char_length("BillingAddressBuilding") + char_length(regexp_replace("BillingAddressBuilding" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressCity") + char_length(regexp_replace("BillingAddressCity" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressCompany") + char_length(regexp_replace("BillingAddressCompany" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressCountry") + char_length(regexp_replace("BillingAddressCountry" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressLine1") + char_length(regexp_replace("BillingAddressLine1" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressLine2") + char_length(regexp_replace("BillingAddressLine2" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressPostalCode") + char_length(regexp_replace("BillingAddressPostalCode" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressRecipient") + char_length(regexp_replace("BillingAddressRecipient" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("BillingAddressState") + char_length(regexp_replace("BillingAddressState" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("CommercialRegistration") + char_length(regexp_replace("CommercialRegistration" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("Currency") + char_length(regexp_replace("Currency" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 50 OR
        char_length("Fob") + char_length(regexp_replace("Fob" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        "Number" IS NULL OR
        char_length("Number") + char_length(regexp_replace("Number" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 100 OR
        char_length("PurchaseOrderNumber") + char_length(regexp_replace("PurchaseOrderNumber" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("Requisitioner") + char_length(regexp_replace("Requisitioner" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("SalesPerson") + char_length(regexp_replace("SalesPerson" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippedVia") + char_length(regexp_replace("ShippedVia" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippingAddressBuilding") + char_length(regexp_replace("ShippingAddressBuilding" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippingAddressCity") + char_length(regexp_replace("ShippingAddressCity" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippingAddressCompany") + char_length(regexp_replace("ShippingAddressCompany" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippingAddressCountry") + char_length(regexp_replace("ShippingAddressCountry" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippingAddressLine1") + char_length(regexp_replace("ShippingAddressLine1" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippingAddressLine2") + char_length(regexp_replace("ShippingAddressLine2" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippingAddressPostalCode") + char_length(regexp_replace("ShippingAddressPostalCode" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippingAddressRecipient") + char_length(regexp_replace("ShippingAddressRecipient" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippingAddressRecipientTelephone") + char_length(regexp_replace("ShippingAddressRecipientTelephone" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("ShippingAddressState") + char_length(regexp_replace("ShippingAddressState" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256 OR
        char_length("TaxIdentification") + char_length(regexp_replace("TaxIdentification" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 100 OR
        char_length("Terms") + char_length(regexp_replace("Terms" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 256) THEN
                RAISE EXCEPTION 'Invoice source strings require explicit owner reconciliation before migration';
            END IF;
        END $$;
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_BillingAddressBuilding_SourceLength" CHECK (char_length("BillingAddressBuilding") + char_length(regexp_replace("BillingAddressBuilding" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_BillingAddressCity_SourceLength" CHECK (char_length("BillingAddressCity") + char_length(regexp_replace("BillingAddressCity" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_BillingAddressCompany_SourceLength" CHECK (char_length("BillingAddressCompany") + char_length(regexp_replace("BillingAddressCompany" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_BillingAddressCountry_SourceLength" CHECK (char_length("BillingAddressCountry") + char_length(regexp_replace("BillingAddressCountry" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_BillingAddressLine1_SourceLength" CHECK (char_length("BillingAddressLine1") + char_length(regexp_replace("BillingAddressLine1" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_BillingAddressLine2_SourceLength" CHECK (char_length("BillingAddressLine2") + char_length(regexp_replace("BillingAddressLine2" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_BillingAddressPostalCode_SourceLength" CHECK (char_length("BillingAddressPostalCode") + char_length(regexp_replace("BillingAddressPostalCode" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_BillingAddressRecipient_SourceLength" CHECK (char_length("BillingAddressRecipient") + char_length(regexp_replace("BillingAddressRecipient" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_BillingAddressState_SourceLength" CHECK (char_length("BillingAddressState") + char_length(regexp_replace("BillingAddressState" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_CommercialRegistration_SourceLength" CHECK (char_length("CommercialRegistration") + char_length(regexp_replace("CommercialRegistration" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_Currency_SourceLength" CHECK (char_length("Currency") + char_length(regexp_replace("Currency" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_Fob_SourceLength" CHECK (char_length("Fob") + char_length(regexp_replace("Fob" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ALTER COLUMN "Number" SET NOT NULL;
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_Number_SourceLength" CHECK (char_length("Number") + char_length(regexp_replace("Number" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 100);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_PurchaseOrderNumber_SourceLength" CHECK (char_length("PurchaseOrderNumber") + char_length(regexp_replace("PurchaseOrderNumber" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_Requisitioner_SourceLength" CHECK (char_length("Requisitioner") + char_length(regexp_replace("Requisitioner" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_SalesPerson_SourceLength" CHECK (char_length("SalesPerson") + char_length(regexp_replace("SalesPerson" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippedVia_SourceLength" CHECK (char_length("ShippedVia") + char_length(regexp_replace("ShippedVia" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippingAddressBuilding_SourceLength" CHECK (char_length("ShippingAddressBuilding") + char_length(regexp_replace("ShippingAddressBuilding" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippingAddressCity_SourceLength" CHECK (char_length("ShippingAddressCity") + char_length(regexp_replace("ShippingAddressCity" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippingAddressCompany_SourceLength" CHECK (char_length("ShippingAddressCompany") + char_length(regexp_replace("ShippingAddressCompany" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippingAddressCountry_SourceLength" CHECK (char_length("ShippingAddressCountry") + char_length(regexp_replace("ShippingAddressCountry" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippingAddressLine1_SourceLength" CHECK (char_length("ShippingAddressLine1") + char_length(regexp_replace("ShippingAddressLine1" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippingAddressLine2_SourceLength" CHECK (char_length("ShippingAddressLine2") + char_length(regexp_replace("ShippingAddressLine2" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippingAddressPostalCode_SourceLength" CHECK (char_length("ShippingAddressPostalCode") + char_length(regexp_replace("ShippingAddressPostalCode" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippingAddressRecipient_SourceLength" CHECK (char_length("ShippingAddressRecipient") + char_length(regexp_replace("ShippingAddressRecipient" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippingAddressRecipientTelephone_SourceLength" CHECK (char_length("ShippingAddressRecipientTelephone") + char_length(regexp_replace("ShippingAddressRecipientTelephone" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_ShippingAddressState_SourceLength" CHECK (char_length("ShippingAddressState") + char_length(regexp_replace("ShippingAddressState" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_TaxIdentification_SourceLength" CHECK (char_length("TaxIdentification") + char_length(regexp_replace("TaxIdentification" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 100);
        ALTER TABLE "Invoice" ADD CONSTRAINT "CK_Invoice_Terms_SourceLength" CHECK (char_length("Terms") + char_length(regexp_replace("Terms" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256);
        """);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "Invoice" IN ACCESS EXCLUSIVE MODE;
        DO $$
        BEGIN
            IF current_setting('server_encoding') <> 'UTF8' THEN
                RAISE EXCEPTION 'Accounting source strings require UTF8';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'BillingAddressBuilding' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_BillingAddressBuilding_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'BillingAddressBuilding')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressBuilding" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressBuilding") + char_length(regexp_replace("BillingAddressBuilding" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_BillingAddressBuilding_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'BillingAddressCity' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_BillingAddressCity_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'BillingAddressCity')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressCity" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressCity") + char_length(regexp_replace("BillingAddressCity" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_BillingAddressCity_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'BillingAddressCompany' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_BillingAddressCompany_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'BillingAddressCompany')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressCompany" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressCompany") + char_length(regexp_replace("BillingAddressCompany" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_BillingAddressCompany_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'BillingAddressCountry' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_BillingAddressCountry_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'BillingAddressCountry')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressCountry" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressCountry") + char_length(regexp_replace("BillingAddressCountry" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_BillingAddressCountry_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'BillingAddressLine1' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_BillingAddressLine1_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'BillingAddressLine1')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressLine1" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressLine1") + char_length(regexp_replace("BillingAddressLine1" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_BillingAddressLine1_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'BillingAddressLine2' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_BillingAddressLine2_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'BillingAddressLine2')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressLine2" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressLine2") + char_length(regexp_replace("BillingAddressLine2" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_BillingAddressLine2_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'BillingAddressPostalCode' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_BillingAddressPostalCode_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'BillingAddressPostalCode')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressPostalCode" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressPostalCode") + char_length(regexp_replace("BillingAddressPostalCode" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_BillingAddressPostalCode_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'BillingAddressRecipient' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_BillingAddressRecipient_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'BillingAddressRecipient')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressRecipient" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressRecipient") + char_length(regexp_replace("BillingAddressRecipient" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_BillingAddressRecipient_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'BillingAddressState' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_BillingAddressState_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'BillingAddressState')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("BillingAddressState" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("BillingAddressState") + char_length(regexp_replace("BillingAddressState" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_BillingAddressState_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'CommercialRegistration' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_CommercialRegistration_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'CommercialRegistration')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("CommercialRegistration" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("CommercialRegistration") + char_length(regexp_replace("CommercialRegistration" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_CommercialRegistration_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'Currency' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_Currency_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'Currency')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Currency" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Currency") + char_length(regexp_replace("Currency" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_Currency_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'Fob' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_Fob_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'Fob')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Fob" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Fob") + char_length(regexp_replace("Fob" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_Fob_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'Number' AND attnotnull = true AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_Number_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'Number')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Number" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Number") + char_length(regexp_replace("Number" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 100)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_Number_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'PurchaseOrderNumber' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_PurchaseOrderNumber_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'PurchaseOrderNumber')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("PurchaseOrderNumber" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("PurchaseOrderNumber") + char_length(regexp_replace("PurchaseOrderNumber" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_PurchaseOrderNumber_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'Requisitioner' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_Requisitioner_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'Requisitioner')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Requisitioner" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Requisitioner") + char_length(regexp_replace("Requisitioner" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_Requisitioner_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'SalesPerson' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_SalesPerson_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'SalesPerson')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("SalesPerson" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("SalesPerson") + char_length(regexp_replace("SalesPerson" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_SalesPerson_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippedVia' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippedVia_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippedVia')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippedVia" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippedVia") + char_length(regexp_replace("ShippedVia" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippedVia_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippingAddressBuilding' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippingAddressBuilding_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippingAddressBuilding')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippingAddressBuilding" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippingAddressBuilding") + char_length(regexp_replace("ShippingAddressBuilding" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippingAddressBuilding_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippingAddressCity' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippingAddressCity_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippingAddressCity')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippingAddressCity" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippingAddressCity") + char_length(regexp_replace("ShippingAddressCity" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippingAddressCity_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippingAddressCompany' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippingAddressCompany_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippingAddressCompany')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippingAddressCompany" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippingAddressCompany") + char_length(regexp_replace("ShippingAddressCompany" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippingAddressCompany_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippingAddressCountry' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippingAddressCountry_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippingAddressCountry')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippingAddressCountry" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippingAddressCountry") + char_length(regexp_replace("ShippingAddressCountry" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippingAddressCountry_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippingAddressLine1' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippingAddressLine1_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippingAddressLine1')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippingAddressLine1" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippingAddressLine1") + char_length(regexp_replace("ShippingAddressLine1" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippingAddressLine1_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippingAddressLine2' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippingAddressLine2_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippingAddressLine2')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippingAddressLine2" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippingAddressLine2") + char_length(regexp_replace("ShippingAddressLine2" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippingAddressLine2_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippingAddressPostalCode' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippingAddressPostalCode_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippingAddressPostalCode')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippingAddressPostalCode" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippingAddressPostalCode") + char_length(regexp_replace("ShippingAddressPostalCode" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippingAddressPostalCode_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippingAddressRecipient' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippingAddressRecipient_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippingAddressRecipient')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippingAddressRecipient" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippingAddressRecipient") + char_length(regexp_replace("ShippingAddressRecipient" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippingAddressRecipient_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippingAddressRecipientTelephone' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippingAddressRecipientTelephone_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippingAddressRecipientTelephone')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippingAddressRecipientTelephone" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippingAddressRecipientTelephone") + char_length(regexp_replace("ShippingAddressRecipientTelephone" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippingAddressRecipientTelephone_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'ShippingAddressState' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_ShippingAddressState_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'ShippingAddressState')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("ShippingAddressState" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("ShippingAddressState") + char_length(regexp_replace("ShippingAddressState" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_ShippingAddressState_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'TaxIdentification' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_TaxIdentification_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'TaxIdentification')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("TaxIdentification" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("TaxIdentification") + char_length(regexp_replace("TaxIdentification" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 100)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_TaxIdentification_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass
                AND attname = 'Terms' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Invoice source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass
                AND conname = 'CK_Invoice_Terms_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Invoice"'::regclass AND attname = 'Terms')]::smallint[]) THEN
                RAISE EXCEPTION 'Invoice source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Terms" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Terms") + char_length(regexp_replace("Terms" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 256)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Invoice"'::regclass AND conname = 'CK_Invoice_Terms_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Invoice source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
        END $$;
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_BillingAddressBuilding_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_BillingAddressCity_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_BillingAddressCompany_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_BillingAddressCountry_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_BillingAddressLine1_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_BillingAddressLine2_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_BillingAddressPostalCode_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_BillingAddressRecipient_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_BillingAddressState_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_CommercialRegistration_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_Currency_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_Fob_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_Number_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_PurchaseOrderNumber_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_Requisitioner_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_SalesPerson_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippedVia_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippingAddressBuilding_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippingAddressCity_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippingAddressCompany_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippingAddressCountry_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippingAddressLine1_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippingAddressLine2_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippingAddressPostalCode_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippingAddressRecipient_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippingAddressRecipientTelephone_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_ShippingAddressState_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_TaxIdentification_SourceLength";
        ALTER TABLE "Invoice" DROP CONSTRAINT "CK_Invoice_Terms_SourceLength";
        ALTER TABLE "Invoice" ALTER COLUMN "Number" DROP NOT NULL;
        """);
}
