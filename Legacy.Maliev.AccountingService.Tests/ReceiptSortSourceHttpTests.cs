using System.Net;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class ReceiptSortSourceHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    [Theory]
    [InlineData("ReceiptId_Ascending", "11,22,33,44")]
    [InlineData("0", "11,22,33,44")]
    [InlineData("ReceiptId_Descending", "44,33,22,11")]
    [InlineData("1", "44,33,22,11")]
    [InlineData("ReceiptCreatedDate_Ascending", "11,33,44,22")]
    [InlineData("2", "11,33,44,22")]
    [InlineData("ReceiptCreatedDate_Descending", "22,33,44,11")]
    [InlineData("3", "22,33,44,11")]
    [InlineData("ReceiptPaymentDate_Ascending", "22,33,44,11")]
    [InlineData("4", "22,33,44,11")]
    [InlineData("ReceiptPaymentDate_Descending", "11,33,44,22")]
    [InlineData("5", "11,33,44,22")]
    public async Task SixSourceOrders_NameAndNumericBindingPreserveDatesNullPlacementAndStableTies(string sort, string expected)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/receipts?sort={sort}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected.Split(',').Select(int.Parse), Ids(document.RootElement));
        Assert.Equal(4, document.RootElement.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?sort=")]
    [InlineData("?sort=&index=1&size=10")]
    public async Task MissingOrEmptySort_SourceDefaultsToAscendingIdentifier(string query)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync("/receipts" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { 11, 22, 33, 44 }, Ids(document.RootElement));
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("not-a-receipt-sort")]
    [InlineData("999")]
    [InlineData("-1")]
    public async Task InvalidSort_RejectsBeforeFinancialDisclosureOrMutation(string sort)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/receipts?sort={sort}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("SORT-GROUP%", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("ReceiptId_Ascending", "33,44")]
    [InlineData("ReceiptId_Descending", "22,11")]
    public async Task SelectedPage_OrdersBeforePagingAndPreservesPascalMetadata(string sort, string expected)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/receipts?sort={sort}&index=2&size=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = document.RootElement;
        Assert.Equal(expected.Split(',').Select(int.Parse), Ids(page));
        Assert.Equal(2, page.GetProperty("PageIndex").GetInt32());
        Assert.Equal(2, page.GetProperty("TotalPages").GetInt32());
        Assert.Equal(4, page.GetProperty("TotalRecords").GetInt32());
        Assert.True(page.GetProperty("HasPreviousPage").GetBoolean());
        Assert.False(page.GetProperty("HasNextPage").GetBoolean());
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task LiteralSearch_SortsOnlyMatchingRowsBeforeSelectingPage()
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync("/receipts?search=GROUP%25&sort=ReceiptPaymentDate_Descending&index=2&size=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { 44 }, Ids(document.RootElement));
        Assert.Equal(3, document.RootElement.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task DefaultSizeAndMaximumPageCap_RemainTwentyAndTwoHundredFifty()
    {
        await SeedAsync(260);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var defaultResponse = await client.GetAsync("/receipts");
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        using var defaults = JsonDocument.Parse(await defaultResponse.Content.ReadAsStringAsync());
        Assert.Equal(Enumerable.Range(1, 20), Ids(defaults.RootElement));
        using var cappedResponse = await client.GetAsync("/receipts?size=999&sort=ReceiptId_Descending");
        Assert.Equal(HttpStatusCode.OK, cappedResponse.StatusCode);
        using var capped = JsonDocument.Parse(await cappedResponse.Content.ReadAsStringAsync());
        Assert.Equal(Enumerable.Range(11, 250).Reverse(), Ids(capped.RootElement));
        Assert.Equal(260, capped.RootElement.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(2, capped.RootElement.GetProperty("TotalPages").GetInt32());
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SortedRead_AnonymousOrLiveDeniedCannotDiscloseOrMutateFinancialRows(bool anonymous)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client(anonymous ? null : [AccountingPermissions.Read], allowLive: false);
        using var response = await client.GetAsync("/receipts?sort=ReceiptCreatedDate_Descending");
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("SORT-GROUP%", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    private static int[] Ids(JsonElement page) => page.GetProperty("Items").EnumerateArray()
        .Select(row => row.GetProperty("Id").GetInt32()).ToArray();

    private async Task SeedAsync(int count = 0)
    {
        await using var database = fixture.ReceiptDatabase();
        await database.Files.ExecuteDeleteAsync();
        await database.Items.ExecuteDeleteAsync();
        await database.Receipts.ExecuteDeleteAsync();
        database.Receipts.AddRange(count > 0 ? Enumerable.Range(1, count).Select(id => Row(id, 1, 1)) :
            [Row(11, null, 3), Row(22, 3, 1), Row(33, 1, 2), Row(44, 1, 2)]);
        await database.SaveChangesAsync();
        if (count == 0)
        {
            // The model's insert default supplies CreatedDate; materialize an actual stored NULL for the sort proof.
            await database.Receipts.Where(row => row.Id == 11)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedDate, (DateTime?)null));
        }
    }

    private static Receipt Row(int id, int? createdDay, int paymentDay) => new()
    {
        Id = id,
        InvoiceNumber = id == 11 ? "OTHER" : "SORT-GROUP%",
        Currency = "THB",
        Subtotal = 100m,
        Vat = 7m,
        Total = 107m,
        WithholdingTax = 3m,
        PaymentDate = new DateTime(2026, 10, paymentDay, 0, 0, 0, DateTimeKind.Utc),
        CreatedDate = createdDay.HasValue ? new DateTime(2026, 10, createdDay.Value) : null,
        ModifiedDate = new DateTime(2026, 10, 6),
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
