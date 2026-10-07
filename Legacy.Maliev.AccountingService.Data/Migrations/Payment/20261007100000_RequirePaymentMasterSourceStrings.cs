using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.AccountingService.Data.Migrations.Payment;

/// <inheritdoc />
public partial class RequirePaymentMasterSourceStrings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "Account" IN ACCESS EXCLUSIVE MODE;
        LOCK TABLE "Payment" IN ACCESS EXCLUSIVE MODE;
        LOCK TABLE "PaymentDirection" IN ACCESS EXCLUSIVE MODE;
        LOCK TABLE "PaymentMethod" IN ACCESS EXCLUSIVE MODE;
        LOCK TABLE "PaymentType" IN ACCESS EXCLUSIVE MODE;
        DO $$
        BEGIN
            IF current_setting('server_encoding') <> 'UTF8' THEN
                RAISE EXCEPTION 'Accounting source strings require UTF8';
            END IF;
            IF (SELECT count(*) FROM pg_attribute WHERE attrelid = '"Account"'::regclass
                AND attname IN ('AccountNumber', 'Bank', 'Branch', 'Swift') AND NOT attnotnull AND atttypid = 'text'::regtype AND NOT attisdropped) <> 4 THEN
                RAISE EXCEPTION 'Account source string schema preimage mismatch';
            END IF;
            IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Account"'::regclass AND conname IN ('CK_Account_AccountNumber_SourceLength', 'CK_Account_Bank_SourceLength', 'CK_Account_Branch_SourceLength', 'CK_Account_Swift_SourceLength')) THEN
                RAISE EXCEPTION 'Account source string constraints already exist';
            END IF;
            IF (SELECT count(*) FROM (SELECT 1 FROM "Account" LIMIT 10001) bounded_rows) > 10000 THEN
                RAISE EXCEPTION 'Account source string preflight exceeds bounded row admission';
            END IF;
            IF EXISTS (SELECT 1 FROM "Account" WHERE char_length("AccountNumber") + char_length(regexp_replace("AccountNumber" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 50 OR
        char_length("Bank") + char_length(regexp_replace("Bank" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 100 OR
        char_length("Branch") + char_length(regexp_replace("Branch" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 100 OR
        char_length("Swift") + char_length(regexp_replace("Swift" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 50) THEN
                RAISE EXCEPTION 'Account source strings require explicit owner reconciliation before migration';
            END IF;
            IF (SELECT count(*) FROM pg_attribute WHERE attrelid = '"Payment"'::regclass
                AND attname IN ('Description') AND NOT attnotnull AND atttypid = 'text'::regtype AND NOT attisdropped) <> 1 THEN
                RAISE EXCEPTION 'Payment source string schema preimage mismatch';
            END IF;
            IF (SELECT count(*) FROM (SELECT 1 FROM "Payment" LIMIT 10001) bounded_rows) > 10000 THEN
                RAISE EXCEPTION 'Payment source string preflight exceeds bounded row admission';
            END IF;
            IF EXISTS (SELECT 1 FROM "Payment" WHERE "Description" IS NULL) THEN
                RAISE EXCEPTION 'Payment source strings require explicit owner reconciliation before migration';
            END IF;
            IF (SELECT count(*) FROM pg_attribute WHERE attrelid = '"PaymentDirection"'::regclass
                AND attname IN ('Description', 'Name') AND NOT attnotnull AND atttypid = 'text'::regtype AND NOT attisdropped) <> 2 THEN
                RAISE EXCEPTION 'PaymentDirection source string schema preimage mismatch';
            END IF;
            IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"PaymentDirection"'::regclass AND conname IN ('CK_PaymentDirection_Name_SourceLength')) THEN
                RAISE EXCEPTION 'PaymentDirection source string constraints already exist';
            END IF;
            IF (SELECT count(*) FROM (SELECT 1 FROM "PaymentDirection" LIMIT 10001) bounded_rows) > 10000 THEN
                RAISE EXCEPTION 'PaymentDirection source string preflight exceeds bounded row admission';
            END IF;
            IF EXISTS (SELECT 1 FROM "PaymentDirection" WHERE "Description" IS NULL OR
        "Name" IS NULL OR
        char_length("Name") + char_length(regexp_replace("Name" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 50) THEN
                RAISE EXCEPTION 'PaymentDirection source strings require explicit owner reconciliation before migration';
            END IF;
            IF (SELECT count(*) FROM pg_attribute WHERE attrelid = '"PaymentMethod"'::regclass
                AND attname IN ('Description', 'Name') AND NOT attnotnull AND atttypid = 'text'::regtype AND NOT attisdropped) <> 2 THEN
                RAISE EXCEPTION 'PaymentMethod source string schema preimage mismatch';
            END IF;
            IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"PaymentMethod"'::regclass AND conname IN ('CK_PaymentMethod_Name_SourceLength')) THEN
                RAISE EXCEPTION 'PaymentMethod source string constraints already exist';
            END IF;
            IF (SELECT count(*) FROM (SELECT 1 FROM "PaymentMethod" LIMIT 10001) bounded_rows) > 10000 THEN
                RAISE EXCEPTION 'PaymentMethod source string preflight exceeds bounded row admission';
            END IF;
            IF EXISTS (SELECT 1 FROM "PaymentMethod" WHERE "Description" IS NULL OR
        "Name" IS NULL OR
        char_length("Name") + char_length(regexp_replace("Name" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 50) THEN
                RAISE EXCEPTION 'PaymentMethod source strings require explicit owner reconciliation before migration';
            END IF;
            IF (SELECT count(*) FROM pg_attribute WHERE attrelid = '"PaymentType"'::regclass
                AND attname IN ('Description', 'Name') AND NOT attnotnull AND atttypid = 'text'::regtype AND NOT attisdropped) <> 2 THEN
                RAISE EXCEPTION 'PaymentType source string schema preimage mismatch';
            END IF;
            IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"PaymentType"'::regclass AND conname IN ('CK_PaymentType_Name_SourceLength')) THEN
                RAISE EXCEPTION 'PaymentType source string constraints already exist';
            END IF;
            IF (SELECT count(*) FROM (SELECT 1 FROM "PaymentType" LIMIT 10001) bounded_rows) > 10000 THEN
                RAISE EXCEPTION 'PaymentType source string preflight exceeds bounded row admission';
            END IF;
            IF EXISTS (SELECT 1 FROM "PaymentType" WHERE "Description" IS NULL OR
        "Name" IS NULL OR
        char_length("Name") + char_length(regexp_replace("Name" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) > 50) THEN
                RAISE EXCEPTION 'PaymentType source strings require explicit owner reconciliation before migration';
            END IF;
        END $$;
        ALTER TABLE "Account" ADD CONSTRAINT "CK_Account_AccountNumber_SourceLength" CHECK (char_length("AccountNumber") + char_length(regexp_replace("AccountNumber" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50);
        ALTER TABLE "Account" ADD CONSTRAINT "CK_Account_Bank_SourceLength" CHECK (char_length("Bank") + char_length(regexp_replace("Bank" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 100);
        ALTER TABLE "Account" ADD CONSTRAINT "CK_Account_Branch_SourceLength" CHECK (char_length("Branch") + char_length(regexp_replace("Branch" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 100);
        ALTER TABLE "Account" ADD CONSTRAINT "CK_Account_Swift_SourceLength" CHECK (char_length("Swift") + char_length(regexp_replace("Swift" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50);
        ALTER TABLE "Payment" ALTER COLUMN "Description" SET NOT NULL;
        ALTER TABLE "PaymentDirection" ALTER COLUMN "Description" SET NOT NULL;
        ALTER TABLE "PaymentDirection" ALTER COLUMN "Name" SET NOT NULL;
        ALTER TABLE "PaymentDirection" ADD CONSTRAINT "CK_PaymentDirection_Name_SourceLength" CHECK (char_length("Name") + char_length(regexp_replace("Name" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50);
        ALTER TABLE "PaymentMethod" ALTER COLUMN "Description" SET NOT NULL;
        ALTER TABLE "PaymentMethod" ALTER COLUMN "Name" SET NOT NULL;
        ALTER TABLE "PaymentMethod" ADD CONSTRAINT "CK_PaymentMethod_Name_SourceLength" CHECK (char_length("Name") + char_length(regexp_replace("Name" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50);
        ALTER TABLE "PaymentType" ALTER COLUMN "Description" SET NOT NULL;
        ALTER TABLE "PaymentType" ALTER COLUMN "Name" SET NOT NULL;
        ALTER TABLE "PaymentType" ADD CONSTRAINT "CK_PaymentType_Name_SourceLength" CHECK (char_length("Name") + char_length(regexp_replace("Name" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50);
        """);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '30s';
        LOCK TABLE "Account" IN ACCESS EXCLUSIVE MODE;
        LOCK TABLE "Payment" IN ACCESS EXCLUSIVE MODE;
        LOCK TABLE "PaymentDirection" IN ACCESS EXCLUSIVE MODE;
        LOCK TABLE "PaymentMethod" IN ACCESS EXCLUSIVE MODE;
        LOCK TABLE "PaymentType" IN ACCESS EXCLUSIVE MODE;
        DO $$
        BEGIN
            IF current_setting('server_encoding') <> 'UTF8' THEN
                RAISE EXCEPTION 'Accounting source strings require UTF8';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Account"'::regclass
                AND attname = 'AccountNumber' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Account source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Account"'::regclass
                AND conname = 'CK_Account_AccountNumber_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Account"'::regclass AND attname = 'AccountNumber')]::smallint[]) THEN
                RAISE EXCEPTION 'Account source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("AccountNumber" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("AccountNumber") + char_length(regexp_replace("AccountNumber" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Account"'::regclass AND conname = 'CK_Account_AccountNumber_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Account source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Account"'::regclass
                AND attname = 'Bank' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Account source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Account"'::regclass
                AND conname = 'CK_Account_Bank_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Account"'::regclass AND attname = 'Bank')]::smallint[]) THEN
                RAISE EXCEPTION 'Account source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Bank" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Bank") + char_length(regexp_replace("Bank" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 100)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Account"'::regclass AND conname = 'CK_Account_Bank_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Account source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Account"'::regclass
                AND attname = 'Branch' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Account source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Account"'::regclass
                AND conname = 'CK_Account_Branch_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Account"'::regclass AND attname = 'Branch')]::smallint[]) THEN
                RAISE EXCEPTION 'Account source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Branch" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Branch") + char_length(regexp_replace("Branch" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 100)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Account"'::regclass AND conname = 'CK_Account_Branch_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Account source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Account"'::regclass
                AND attname = 'Swift' AND attnotnull = false AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Account source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"Account"'::regclass
                AND conname = 'CK_Account_Swift_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"Account"'::regclass AND attname = 'Swift')]::smallint[]) THEN
                RAISE EXCEPTION 'Account source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Swift" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Swift") + char_length(regexp_replace("Swift" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"Account"'::regclass AND conname = 'CK_Account_Swift_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'Account source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"Payment"'::regclass
                AND attname = 'Description' AND attnotnull = true AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'Payment source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"PaymentDirection"'::regclass
                AND attname = 'Description' AND attnotnull = true AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'PaymentDirection source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"PaymentDirection"'::regclass
                AND attname = 'Name' AND attnotnull = true AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'PaymentDirection source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"PaymentDirection"'::regclass
                AND conname = 'CK_PaymentDirection_Name_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"PaymentDirection"'::regclass AND attname = 'Name')]::smallint[]) THEN
                RAISE EXCEPTION 'PaymentDirection source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Name" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Name") + char_length(regexp_replace("Name" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"PaymentDirection"'::regclass AND conname = 'CK_PaymentDirection_Name_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'PaymentDirection source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"PaymentMethod"'::regclass
                AND attname = 'Description' AND attnotnull = true AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'PaymentMethod source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"PaymentMethod"'::regclass
                AND attname = 'Name' AND attnotnull = true AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'PaymentMethod source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"PaymentMethod"'::regclass
                AND conname = 'CK_PaymentMethod_Name_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"PaymentMethod"'::regclass AND attname = 'Name')]::smallint[]) THEN
                RAISE EXCEPTION 'PaymentMethod source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Name" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Name") + char_length(regexp_replace("Name" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"PaymentMethod"'::regclass AND conname = 'CK_PaymentMethod_Name_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'PaymentMethod source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"PaymentType"'::regclass
                AND attname = 'Description' AND attnotnull = true AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'PaymentType source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"PaymentType"'::regclass
                AND attname = 'Name' AND attnotnull = true AND atttypid = 'text'::regtype AND NOT attisdropped) THEN
                RAISE EXCEPTION 'PaymentType source string downgrade preimage mismatch';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"PaymentType"'::regclass
                AND conname = 'CK_PaymentType_Name_SourceLength' AND contype = 'c' AND convalidated AND NOT connoinherit
                AND conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '"PaymentType"'::regclass AND attname = 'Name')]::smallint[]) THEN
                RAISE EXCEPTION 'PaymentType source string downgrade constraint mismatch';
            END IF;
            IF to_regclass('pg_temp."__AccountingSourceStringDowngradeGuard"') IS NOT NULL THEN
                RAISE EXCEPTION 'Accounting source string downgrade guard already exists';
            END IF;
            CREATE TEMP TABLE "__AccountingSourceStringDowngradeGuard" ("Name" text, CONSTRAINT "ExpectedSourceLength" CHECK (char_length("Name") + char_length(regexp_replace("Name" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50)) ON COMMIT DROP;
            IF (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = '"PaymentType"'::regclass AND conname = 'CK_PaymentType_Name_SourceLength')
                IS DISTINCT FROM (SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conrelid = 'pg_temp."__AccountingSourceStringDowngradeGuard"'::regclass AND conname = 'ExpectedSourceLength') THEN
                RAISE EXCEPTION 'PaymentType source string downgrade definition mismatch';
            END IF;
            DROP TABLE pg_temp."__AccountingSourceStringDowngradeGuard";
        END $$;
        ALTER TABLE "Account" DROP CONSTRAINT "CK_Account_AccountNumber_SourceLength";
        ALTER TABLE "Account" DROP CONSTRAINT "CK_Account_Bank_SourceLength";
        ALTER TABLE "Account" DROP CONSTRAINT "CK_Account_Branch_SourceLength";
        ALTER TABLE "Account" DROP CONSTRAINT "CK_Account_Swift_SourceLength";
        ALTER TABLE "PaymentDirection" DROP CONSTRAINT "CK_PaymentDirection_Name_SourceLength";
        ALTER TABLE "PaymentMethod" DROP CONSTRAINT "CK_PaymentMethod_Name_SourceLength";
        ALTER TABLE "PaymentType" DROP CONSTRAINT "CK_PaymentType_Name_SourceLength";
        ALTER TABLE "Payment" ALTER COLUMN "Description" DROP NOT NULL;
        ALTER TABLE "PaymentDirection" ALTER COLUMN "Description" DROP NOT NULL;
        ALTER TABLE "PaymentDirection" ALTER COLUMN "Name" DROP NOT NULL;
        ALTER TABLE "PaymentMethod" ALTER COLUMN "Description" DROP NOT NULL;
        ALTER TABLE "PaymentMethod" ALTER COLUMN "Name" DROP NOT NULL;
        ALTER TABLE "PaymentType" ALTER COLUMN "Description" DROP NOT NULL;
        ALTER TABLE "PaymentType" ALTER COLUMN "Name" DROP NOT NULL;
        """);
}
