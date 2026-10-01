using Microsoft.EntityFrameworkCore;
using Legacy.Maliev.AccountingService.Application.Models;
using System.Security.Cryptography;
using System.Text;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Uncached exact public-schema authority admission; never creates or repairs storage.</summary>
internal static class InvoiceNotificationCorrelationReadiness
{
    public static async Task RequireAsync(InvoiceDbContext database, CancellationToken token)
    {
        var relation = await database.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relname='InvoiceNotificationCorrelation' AND c.relkind='r'
              AND NOT c.relispartition AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
            """).SingleAsync(token);
        if (relation != 1) throw Unavailable();
        var columns = await database.Database.SqlQueryRaw<string>("""
            SELECT a.attname || ':' || format_type(a.atttypid,a.atttypmod) || ':' ||
              CASE WHEN a.attnotnull THEN 'required' ELSE 'optional' END || ':' ||
              CASE WHEN a.attgenerated='' AND a.attidentity='' AND NOT a.atthasdef AND NOT a.atthasmissing
                AND (a.attcollation=0 OR a.attcollation=(SELECT oid FROM pg_catalog.pg_collation
                  WHERE collname='default' AND collnamespace='pg_catalog'::regnamespace AND collisdeterministic))
                THEN 'plain' ELSE 'unexpected' END AS "Value"
            FROM pg_attribute a WHERE a.attrelid='public."InvoiceNotificationCorrelation"'::regclass
              AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum
            """).ToListAsync(token);
        if (!columns.SequenceEqual(Columns, StringComparer.Ordinal)) throw Unavailable();
        var constraints = await database.Database.SqlQueryRaw<string>("""
            SELECT conname || ':' || contype::text || ':' || convalidated::text || ':' ||
              condeferrable::text || ':' || condeferred::text AS "Value"
            FROM pg_constraint WHERE conrelid='public."InvoiceNotificationCorrelation"'::regclass AND contype<>'n' ORDER BY conname
            """).ToListAsync(token);
        if (!constraints.SequenceEqual(new[]
        {
            "CK_InvoiceNotificationCorrelation_Identity:c:true:false:false", "CK_InvoiceNotificationCorrelation_State:c:true:false:false",
            "PK_InvoiceNotificationCorrelation:p:true:false:false", "UQ_InvoiceNotificationCorrelation_InvoicePurpose:u:true:false:false",
        }, StringComparer.Ordinal)) throw Unavailable();
        var keys = await database.Database.SqlQueryRaw<string>("""
            SELECT c.conname || ':' || array_to_string(ARRAY(SELECT a.attname FROM unnest(c.conkey) WITH ORDINALITY k(attnum,ordinal)
              JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum ORDER BY k.ordinal),',') || ':' ||
              (i.indisvalid AND i.indisready AND i.indislive AND i.indpred IS NULL AND i.indexprs IS NULL
              AND i.indnatts=i.indnkeyatts AND m.amname='btree')::text || ':' ||
              array_to_string(ARRAY(SELECT n.nspname || '.' || p.opcname || ':' || p.opcdefault::text
                FROM unnest(i.indclass::oid[]) WITH ORDINALITY k(oid,ordinal)
                JOIN pg_catalog.pg_opclass p ON p.oid=k.oid JOIN pg_catalog.pg_namespace n ON n.oid=p.opcnamespace
                WHERE p.opcmethod=x.relam ORDER BY k.ordinal),',') || ':' ||
              array_to_string(ARRAY(SELECT COALESCE(n.nspname || '.' || p.collname || ':' || p.collisdeterministic::text,'none')
                FROM unnest(i.indcollation::oid[]) WITH ORDINALITY k(oid,ordinal)
                LEFT JOIN pg_catalog.pg_collation p ON p.oid=k.oid LEFT JOIN pg_catalog.pg_namespace n ON n.oid=p.collnamespace
                ORDER BY k.ordinal),',') AS "Value"
            FROM pg_constraint c JOIN pg_index i ON i.indexrelid=c.conindid JOIN pg_class x ON x.oid=i.indexrelid JOIN pg_am m ON m.oid=x.relam
            WHERE c.conrelid='public."InvoiceNotificationCorrelation"'::regclass AND c.contype IN ('p','u') ORDER BY c.conname
            """).ToListAsync(token);
        if (!keys.SequenceEqual(new[]
        {
            "PK_InvoiceNotificationCorrelation:IntentID:true:pg_catalog.uuid_ops:true:none",
            "UQ_InvoiceNotificationCorrelation_InvoicePurpose:InvoiceID,Purpose:true:pg_catalog.int4_ops:true,pg_catalog.text_ops:true:none,pg_catalog.default:true",
        }, StringComparer.Ordinal)) throw Unavailable();
        var checks = await database.Database.SqlQueryRaw<string>("""
            SELECT conname || ':' || pg_get_expr(conbin,conrelid,true) AS "Value" FROM pg_constraint
            WHERE conrelid='public."InvoiceNotificationCorrelation"'::regclass AND contype='c' ORDER BY conname
            """).ToListAsync(token);
        var hashes = checks.Select(value => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value[(value.IndexOf(':') + 1)..])))).ToArray();
        if (!hashes.SequenceEqual(new[] { "DA46F967D09D3FA847E81EB64A5B2305AE13AD3116E81AB432F387F767699D81", "5DBD2F14FE4DE524928B01DB1AE28E08FCFA5E80820B2D3B749835600A4B5E0D" }, StringComparer.Ordinal)) throw Unavailable();
        var unexpected = await database.Database.SqlQueryRaw<long>("""
            SELECT (SELECT count(*) FROM pg_rewrite WHERE ev_class='public."InvoiceNotificationCorrelation"'::regclass)
              + (SELECT count(*) FROM pg_trigger WHERE tgrelid='public."InvoiceNotificationCorrelation"'::regclass AND NOT tgisinternal)
              + (SELECT count(*) FROM pg_inherits WHERE inhrelid='public."InvoiceNotificationCorrelation"'::regclass OR inhparent='public."InvoiceNotificationCorrelation"'::regclass)
              + (SELECT count(*) FROM pg_index WHERE indrelid='public."InvoiceNotificationCorrelation"'::regclass
                 AND indexrelid NOT IN (SELECT conindid FROM pg_constraint WHERE conrelid='public."InvoiceNotificationCorrelation"'::regclass AND contype IN ('p','u')))
              AS "Value"
            """).SingleAsync(token);
        if (unexpected != 0) throw Unavailable();
        var migration = await database.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM public."__EFMigrationsHistory"
            WHERE "MigrationId"='20261001105037_AddInvoiceNotificationCorrelation' AND "ProductVersion"='10.0.12'
            """).SingleAsync(token);
        if (migration != 1 || database.Database.HasPendingModelChanges()) throw Unavailable();
    }

    private static InvoiceNotificationCorrelationUnavailableException Unavailable() => new();

    private static readonly string[] Columns =
    [
        "IntentID:uuid:required:plain", "InvoiceID:integer:required:plain", "Purpose:character varying(32):required:plain",
        "QuotationID:integer:required:plain", "WorkflowOperationID:uuid:required:plain", "OriginIssuer:character varying(512):required:plain",
        "OriginEmployeeSubject:character varying(256):required:plain", "OriginServiceSubject:character varying(128):required:plain",
        "SenderIssuer:character varying(512):required:plain", "SenderServiceSubject:character varying(128):required:plain",
        "PayloadFrameVersion:character varying(64):required:plain", "BindingVersion:character varying(64):required:plain",
        "BindingKeyID:character varying(64):required:plain", "PayloadBinding:bytea:required:plain", "Phase:character varying(32):required:plain",
        "Version:bigint:required:plain", "RemoteVersion:bigint:optional:plain", "CreatedAt:timestamp with time zone:required:plain",
        "UpdatedAt:timestamp with time zone:required:plain", "AdmissionIssuedAt:timestamp with time zone:optional:plain",
        "ExecutionIssuedAt:timestamp with time zone:optional:plain",
    ];
}
