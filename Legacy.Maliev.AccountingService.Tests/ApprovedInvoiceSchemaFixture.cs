using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Data;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

internal static class ApprovedInvoiceSchemaFixture
{
    internal const string SourceRevision = "7878362fcd563e0bd64f75d29ef5f365c279380d";
    internal const string SqlSha256 = "4c0823fceae65be97717c6dce8ba007c437e0e8ea5b64fbbb16e4b25f5a09be7";
    private const string ResourcePrefix = "Legacy.Maliev.AccountingService.Tests.ApprovedInvoiceSchema.";

    internal static byte[] ResourceBytes(string name)
    {
        var suffix = name switch
        {
            "sql" => "approved-invoice-schema.sql",
            "manifest" => "approved-invoice-schema.json",
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        using var input = typeof(ApprovedInvoiceSchemaFixture).Assembly.GetManifestResourceStream(ResourcePrefix + suffix)
            ?? throw new InvalidDataException("Approved original schema resource is missing.");
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    internal static string Validate(byte[] sql, byte[] manifest)
    {
        if (Convert.ToHexString(SHA256.HashData(sql)).ToLowerInvariant() != SqlSha256)
        {
            throw new InvalidDataException("Approved original schema SQL identity differs.");
        }
        using var document = JsonDocument.Parse(manifest);
        var value = document.RootElement;
        if (value.GetProperty("sourceRevision").GetString() != SourceRevision ||
            value.GetProperty("efVersion").GetString()?.Split('+')[0] != "10.0.12" ||
            value.GetProperty("sha256").GetString() != SqlSha256 ||
            !value.GetProperty("generatedOnly").GetBoolean() || value.GetProperty("databaseOpened").GetBoolean())
        {
            throw new InvalidDataException("Approved original schema provenance differs.");
        }
        return new UTF8Encoding(false, true).GetString(sql);
    }

    internal static async Task CreateAsync(InvoiceDbContext database)
    {
        var sql = Validate(ResourceBytes("sql"), ResourceBytes("manifest"));
        // This test-only initializer admits an empty disposable database only.
        var relations = await database.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m','S','f')
            """).SingleAsync();
        if (relations != 0)
        {
            throw new InvalidOperationException("Original schema initialization requires an empty disposable database.");
        }
        await database.Database.ExecuteSqlRawAsync(sql);
    }
}
