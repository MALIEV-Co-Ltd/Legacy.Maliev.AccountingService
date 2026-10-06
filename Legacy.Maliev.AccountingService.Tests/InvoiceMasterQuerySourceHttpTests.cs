using System.Net;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceMasterQuerySourceHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    private const string Thai = "\u0e0a\u0e34\u0e49\u0e19\u0e07\u0e32\u0e19";

    [Theory]
    [InlineData(" part", "1101")]
    [InlineData("part ", "1101")]
    [InlineData(" part ", "1101")]
    [InlineData(" ", "1101,1102,1103,1104")]
    [InlineData("%", "1101")]
    [InlineData("_", "1101")]
    [InlineData("\\", "1101")]
    [InlineData("110", "1101,1102,1103,1104")]
    [InlineData("9102", "1102")]
    [InlineData("purchase-only", "1103")]
    public async Task LiteralAndNumericSourceFields_FilterWithoutTrimmingOrWildcardExpansion(string search, string expected)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/invoices?search={Uri.EscapeDataString(search)}&size=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected.Split(',').Select(int.Parse), Ids(document.RootElement));
        Assert.Equal(before, await SnapshotAsync());
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Read);
    }

    [Theory]
    [InlineData("InvoiceCreatedDate_Ascending", "1101,1103,1104,1102")]
    [InlineData("InvoiceCreatedDate_Descending", "1102,1104,1103,1101")]
    [InlineData("InvoicePaymentDate_Ascending", "1101,1103,1104,1102")]
    [InlineData("InvoicePaymentDate_Descending", "1102,1104,1103,1101")]
    public async Task NullableDateSourceOrder_PrecedesPagingWithStableIdentifierTies(string sort, string expected)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/invoices?sort={sort}&index=2&size=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = document.RootElement;
        Assert.Equal(expected.Split(',').Skip(2).Select(int.Parse), Ids(page));
        Assert.Equal(2, page.GetProperty("PageIndex").GetInt32());
        Assert.Equal(2, page.GetProperty("TotalPages").GetInt32());
        Assert.Equal(4, page.GetProperty("TotalRecords").GetInt32());
        Assert.True(page.GetProperty("HasPreviousPage").GetBoolean());
        Assert.False(page.GetProperty("HasNextPage").GetBoolean());
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("/invoices?index=100&size=1")]
    [InlineData("/invoices?search=no-match")]
    public async Task SelectedEmptyPage_SourceReturns404WithoutFinancialMutation(string route)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("\"Items\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("true", 1101)]
    [InlineData("false", 1102)]
    public async Task CustomerAndPaidFilters_ApplyBeforeSelectingPage(string paid, int expected)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/invoices/customers/71?paid={paid}&size=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { expected }, Ids(document.RootElement));
        Assert.Equal(1, document.RootElement.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnonymousAndLiveDenied_RejectBeforeFinancialDisclosure(bool anonymous)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client(anonymous ? null : [AccountingPermissions.Read], allowLive: false);
        using var response = await client.GetAsync("/invoices?search=part");
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(Thai, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task ThaiLiteralAndNonnumericSearch_DoNotMatchReceiptZeroOrMutateStorage()
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/invoices?search={Uri.EscapeDataString(Thai)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { 1101 }, Ids(document.RootElement));
        Assert.Equal(before, await SnapshotAsync());
    }

    private async Task SeedAsync()
    {
        await fixture.ResetAsync();
        await using var database = fixture.InvoiceDatabase();
        await database.Files.ExecuteDeleteAsync();
        await database.Items.ExecuteDeleteAsync();
        await database.Invoices.ExecuteDeleteAsync();
        database.Invoices.AddRange(
            Row(1101, "INV% part _\\" + Thai, 71, true, null),
            Row(1102, "INVXpartY", 71, false, 3),
            Row(1103, "Other", 72, true, 2),
            Row(1104, "Unrelated", 72, false, 2));
        await database.SaveChangesAsync();
        await database.Invoices.Where(row => row.Id == 1101)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedDate, (DateTime?)null));
        var storedNull = await database.Invoices.AsNoTracking().SingleAsync(row => row.Id == 1101);
        Assert.Null(storedNull.CreatedDate);
        Assert.Null(storedNull.PaymentDate);
    }

    private static Invoice Row(int id, string number, int customer, bool paid, int? day) => new()
    {
        Id = id,
        Number = number,
        CustomerId = customer,
        IsPaid = paid,
        ReceiptId = id == 1102 ? 9102 : 0,
        PurchaseOrderNumber = id == 1103 ? "purchase-only" : null!,
        Currency = "THB",
        Subtotal = 100m,
        Vat = 7m,
        Total = 107m,
        WithholdingTax = 3m,
        CreatedDate = day is null ? null : new DateTime(2026, 10, day.Value),
        PaymentDate = day is null ? null : new DateTime(2026, 10, day.Value, 0, 0, 0, DateTimeKind.Utc),
        ModifiedDate = new DateTime(2026, 10, 6),
    };

    private static int[] Ids(JsonElement page) => page.GetProperty("Items").EnumerateArray()
        .Select(row => row.GetProperty("Id").GetInt32()).ToArray();

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
