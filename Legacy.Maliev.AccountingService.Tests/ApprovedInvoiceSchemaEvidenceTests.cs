using System.Text;
using System.Text.Json.Nodes;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Actual original-runtime schema bytes are admitted before disposable initialization.</summary>
public sealed class ApprovedInvoiceSchemaEvidenceTests
{
    [Fact]
    public void OriginalArtifact_HasExactSourceRuntimeAndSqlIdentity()
    {
        var sql = ApprovedInvoiceSchemaFixture.Validate(ApprovedInvoiceSchemaFixture.ResourceBytes("sql"),
            ApprovedInvoiceSchemaFixture.ResourceBytes("manifest"));
        Assert.Contains("20261001105037_AddInvoiceNotificationCorrelation", sql);
        Assert.Contains("20261001133820_RetainInvoiceNotificationReceipt", sql);
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("source")]
    [InlineData("runtime")]
    [InlineData("hash")]
    public void ChangedArtifact_IsRejectedByPureAdmission(string changed)
    {
        var sql = ApprovedInvoiceSchemaFixture.ResourceBytes("sql");
        var manifest = JsonNode.Parse(ApprovedInvoiceSchemaFixture.ResourceBytes("manifest"))!.AsObject();
        switch (changed)
        {
            case "sql": sql[0] ^= 1; break;
            case "source": manifest["sourceRevision"] = new string('0', 40); break;
            case "runtime": manifest["efVersion"] = "10.0.13"; break;
            case "hash": manifest["sha256"] = new string('0', 64); break;
            default: throw new ArgumentOutOfRangeException(nameof(changed));
        }
        Assert.Throws<InvalidDataException>(() => ApprovedInvoiceSchemaFixture.Validate(sql,
            Encoding.UTF8.GetBytes(manifest.ToJsonString())));
    }
}
