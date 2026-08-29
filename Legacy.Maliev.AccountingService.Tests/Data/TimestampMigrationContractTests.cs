using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

namespace Legacy.Maliev.AccountingService.Tests.Data;

public sealed class TimestampMigrationContractTests
{
    [Theory]
    [InlineData(
        "Legacy.Maliev.AccountingService.Data/Migrations/Invoice/20260721024204_FixTimestampAndInvoiceColumnCasing.cs",
        "OrderItem", "ModifiedDate", "OrderItem", "CreatedDate", "InvoiceFile", "ModifiedDate", "InvoiceFile", "CreatedDate", "Invoice", "ModifiedDate", "Invoice", "CreatedDate")]
    [InlineData(
        "Legacy.Maliev.AccountingService.Data/Migrations/Payment/20260721024322_FixTimestampColumnType.cs",
        "PaymentType", "ModifiedDate", "PaymentType", "CreatedDate", "PaymentMethod", "ModifiedDate", "PaymentMethod", "CreatedDate", "PaymentFile", "ModifiedDate", "PaymentFile", "CreatedDate", "PaymentDirection", "ModifiedDate", "PaymentDirection", "CreatedDate", "Payment", "ModifiedDate", "Payment", "CreatedDate", "Account", "ModifiedDate", "Account", "CreatedDate")]
    [InlineData(
        "Legacy.Maliev.AccountingService.Data/Migrations/Receipt/20260721024327_FixTimestampColumnType.cs",
        "ReceiptFile", "ModifiedDate", "ReceiptFile", "CreatedDate", "Receipt", "ModifiedDate", "Receipt", "CreatedDate", "OrderItem", "ModifiedDate", "OrderItem", "CreatedDate")]
    public void TimestampMigrations_UseExplicitUtcConversions(string relativePath, params string[] tableColumns)
    {
        var source = File.ReadAllText(FindRepositoryFile(relativePath));

        Assert.DoesNotContain("migrationBuilder.AlterColumn<DateTime>", source, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(source, "toTimestampWithoutTimeZone:").Count);
        Assert.Contains("ALTER COLUMN \"{column}\" DROP DEFAULT;", source, StringComparison.Ordinal);
        Assert.Contains("USING \"{column}\" AT TIME ZONE 'UTC'", source, StringComparison.Ordinal);

        for (var index = 0; index < tableColumns.Length; index += 2)
        {
            Assert.Contains($"(\"{tableColumns[index]}\", \"{tableColumns[index + 1]}\")", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InvoiceMigration_RenamesOnlyVatAndFobColumnsWithoutChangingValues()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "Legacy.Maliev.AccountingService.Data/Migrations/Invoice/20260721024204_FixTimestampAndInvoiceColumnCasing.cs"));

        Assert.Equal(4, Regex.Matches(source, "migrationBuilder.RenameColumn\\(").Count);
        Assert.Contains("name: \"VAT\"", source, StringComparison.Ordinal);
        Assert.Contains("newName: \"Vat\"", source, StringComparison.Ordinal);
        Assert.Contains("name: \"FOB\"", source, StringComparison.Ordinal);
        Assert.Contains("newName: \"Fob\"", source, StringComparison.Ordinal);
        Assert.Contains("name: \"Vat\"", source, StringComparison.Ordinal);
        Assert.Contains("newName: \"VAT\"", source, StringComparison.Ordinal);
        Assert.Contains("name: \"Fob\"", source, StringComparison.Ordinal);
        Assert.Contains("newName: \"FOB\"", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath, [CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { new DirectoryInfo(Path.GetDirectoryName(sourceFile)!), new DirectoryInfo(Directory.GetCurrentDirectory()), new DirectoryInfo(AppContext.BaseDirectory) })
        {
            for (var directory = start; directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException($"Could not find migration source '{relativePath}'.");
    }
}
