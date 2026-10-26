using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.AccountingService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

const string sourceRevision = "7878362fcd563e0bd64f75d29ef5f365c279380d";
if (args.Length != 1)
{
    throw new ArgumentException("One disposable evidence output directory is required.");
}

var efVersion = typeof(DbContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
if (efVersion?.Split('+')[0] != "10.0.12")
{
    throw new InvalidOperationException("Original schema generation requires actual EF Core 10.0.12.");
}

// GenerateScript does not open this connection or execute schema/data operations.
using var database = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>()
    .UseNpgsql("Host=127.0.0.1;Database=schema_generation_only").Options);
var sql = database.GetService<IMigrator>().GenerateScript();
var history = Regex.Matches(sql,
    """INSERT INTO "__EFMigrationsHistory" \("MigrationId", "ProductVersion"\)\s*VALUES \('(?<id>[^']+)', '(?<version>[^']+)'\);""");
string[] requiredMigrations =
[
    "20261001105037_AddInvoiceNotificationCorrelation",
    "20261001133820_RetainInvoiceNotificationReceipt",
];
foreach (var id in requiredMigrations)
{
    var matches = history.Cast<Match>().Where(match => match.Groups["id"].Value == id).ToArray();
    if (matches.Length != 1 || matches[0].Groups["version"].Value != "10.0.12")
    {
        throw new InvalidOperationException("Original generated migration history differs from approved lineage.");
    }
}

var bytes = Encoding.UTF8.GetBytes(sql);
var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
Directory.CreateDirectory(args[0]);
await File.WriteAllBytesAsync(Path.Combine(args[0], "approved-invoice-schema.sql"), bytes);
await File.WriteAllTextAsync(Path.Combine(args[0], "approved-invoice-schema.json"),
    JsonSerializer.Serialize(new { sourceRevision, efVersion, sha256, generatedOnly = true, databaseOpened = false },
        new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
Console.WriteLine($"Generated original invoice schema: source={sourceRevision}; EF={efVersion}; SHA256={sha256}; no database opened.");
