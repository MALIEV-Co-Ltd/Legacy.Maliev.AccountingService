using System.Net;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class ReceiptMasterQuerySourceHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    private const string Thai = "\u0e0a\u0e34\u0e49\u0e19\u0e07\u0e32\u0e19";

    [Theory]
    [InlineData("comment-only", "1101")]
    [InlineData("registration-only", "1101")]
    [InlineData("701", "1101")]
    [InlineData("1103", "1103")]
    [InlineData("110", "1104,1103,1102,1101")]
    [InlineData("tax-one", "1101")]
    [InlineData("INV", "1102,1101")]
    public async Task SourceFields_AllSixParticipateWithNumericSubstringSearch(string search, string ids)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/receipts?search={Uri.EscapeDataString(search)}&size=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ids.Split(',').Select(int.Parse), document.RootElement.GetProperty("Items").EnumerateArray().Select(row => row.GetProperty("Id").GetInt32()));
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task LiteralCharacters_SourceContainsDoesNotExpandWildcards(string search)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/receipts?search={Uri.EscapeDataString(search)}&size=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1101, Assert.Single(document.RootElement.GetProperty("Items").EnumerateArray()).GetProperty("Id").GetInt32());
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(" part")]
    [InlineData("part ")]
    [InlineData(" part ")]
    public async Task NonemptyLiteralText_SourceRetainsSignificantSpaces(string search)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/receipts?search={Uri.EscapeDataString(search)}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task SelectedEmptyPage_SourceReturns404WithoutFinancialMutation()
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync("/receipts?index=100&size=1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("\"Items\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task ThaiLiteral_RemainsExactAcrossAuthenticatedHttpAndStorage()
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/receipts?search={Uri.EscapeDataString(Thai)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.EndsWith(Thai, Assert.Single(document.RootElement.GetProperty("Items").EnumerateArray()).GetProperty("InvoiceNumber").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnonymousAndLiveDenial_RejectWithoutFinancialDisclosure(bool anonymous)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client(anonymous ? null : [AccountingPermissions.Read], allowLive: false);
        using var response = await client.GetAsync("/receipts?search=part");
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("INV%part", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    private async Task SeedAsync()
    {
        await using var database = fixture.ReceiptDatabase();
        await database.Files.ExecuteDeleteAsync();
        await database.Items.ExecuteDeleteAsync();
        await database.Receipts.ExecuteDeleteAsync();
        var exact = Row(1101, "INV%part_\\" + Thai);
        exact.Comment = "comment-only";
        exact.CommercialRegistration = "registration-only";
        exact.TaxIdentification = "tax-one";
        exact.CustomerId = 7019;
        var other = Row(1102, "INVXplateY");
        other.TaxIdentification = "tax-two";
        other.CustomerId = 8029;
        database.Receipts.AddRange(exact, other, Row(1103, "Other"), Row(1104, null!));
        await database.SaveChangesAsync();
    }

    private static Receipt Row(int id, string invoiceNumber) => new()
    {
        Id = id,
        InvoiceNumber = invoiceNumber,
        Currency = "THB",
        Subtotal = 100m,
        Vat = 7m,
        Total = 107m,
        WithholdingTax = 3m,
        PaymentDate = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
        CreatedDate = new DateTime(2026, 10, 5),
        ModifiedDate = new DateTime(2026, 10, 5),
    };

    private async Task<(string Payment, string Invoice, string Receipt)> SnapshotAsync()
    {
        await using var payment = fixture.Database();
        await using var invoice = fixture.InvoiceDatabase();
        await using var receipt = fixture.ReceiptDatabase();
        return (JsonSerializer.Serialize(await payment.Payments.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await invoice.Invoices.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await receipt.Receipts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()));
    }
}
