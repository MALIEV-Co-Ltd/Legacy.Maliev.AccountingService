using System.Net;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Tests.Fixtures;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class PaymentListHttpContractTests(PaymentListHttpFixture fixture)
    : IClassFixture<PaymentListHttpFixture>
{
    [Theory]
    [InlineData("PaymentId_Ascending", 11, 22, 33)]
    [InlineData("PaymentId_Descending", 33, 22, 11)]
    [InlineData("PaymentDate_Ascending", 33, 11, 22)]
    [InlineData("PaymentDate_Descending", 22, 11, 33)]
    [InlineData("PaymentCreatedDate_Ascending", 22, 33, 11)]
    [InlineData("PaymentCreatedDate_Descending", 11, 33, 22)]
    [InlineData("PaymentModifiedDate_Ascending", 33, 11, 22)]
    [InlineData("PaymentModifiedDate_Descending", 22, 11, 33)]
    [InlineData("PaymentDirection_Ascending", 33, 11, 22)]
    [InlineData("PaymentDirection_Descending", 22, 11, 33)]
    [InlineData("PaymentType_Ascending", 11, 22, 33)]
    [InlineData("PaymentType_Descending", 33, 22, 11)]
    [InlineData("PaymentMethod_Ascending", 22, 33, 11)]
    [InlineData("PaymentMethod_Descending", 11, 33, 22)]
    [InlineData("Recipient_Ascending", 22, 33, 11)]
    [InlineData("Recipient_Descending", 11, 33, 22)]
    public async Task NamedSort_OrdersActualPaymentRows(string sort, int first, int second, int third)
    {
        await SeedSortRows();
        using var client = fixture.Client();
        var page = await Page(client, $"/payments?sort={sort}&size=3");

        Assert.Equal(new[] { first, second, third }, Ids(page));
        Assert.Equal(3, page.GetProperty("TotalRecords").GetInt32());
    }

    [Fact]
    public async Task OmittedSort_DefaultsToPaymentIdAscending()
    {
        await SeedSortRows();
        using var client = fixture.Client();

        Assert.Equal(new[] { 11, 22, 33 }, Ids(await Page(client, "/payments?size=3")));
    }

    [Fact]
    public async Task NumericSearch_SelectsExactIdNotFinancialText()
    {
        var exact = Row(22);
        var textOnly = Row(33);
        textOnly.Description = "Payment 22 is a synthetic text reference";
        textOnly.TransactionNumber = "TX-22";
        await fixture.SeedAsync(exact, textOnly);
        using var client = fixture.Client();

        Assert.Equal(new[] { 22 }, Ids(await Page(client, "/payments?search=22")));
    }

    [Theory]
    [InlineData("%", "fee%literal", "feeXliteral")]
    [InlineData("_", "fee_literal", "feeXliteral")]
    [InlineData("\\", "fee\\literal", "feeXliteral")]
    public async Task TextSearch_TreatsSqlWildcardsAsLiteralCharacters(string search, string matching, string nonmatching)
    {
        var literal = Row(11);
        literal.Description = matching;
        var other = Row(22);
        other.Description = nonmatching;
        await fixture.SeedAsync(literal, other);
        using var client = fixture.Client();

        Assert.Equal(new[] { 11 }, Ids(await Page(client, $"/payments?search={Uri.EscapeDataString(search)}")));
    }

    [Fact]
    public async Task TextSearch_MatchesRecipientCaseInsensitively()
    {
        var matching = Row(11);
        matching.Recipient = "Thai SYNTHETIC fixture";
        await fixture.SeedAsync(matching, Row(22));
        using var client = fixture.Client();

        Assert.Equal(new[] { 11 }, Ids(await Page(client, "/payments?search=thai%20synthetic")));
    }

    [Fact]
    public async Task TextSearch_MatchesThaiUnicodeDescription()
    {
        var matching = Row(11);
        matching.Description = "รายการสังเคราะห์ ชำระเงิน ทดสอบ";
        await fixture.SeedAsync(matching, Row(22));
        using var client = fixture.Client();

        Assert.Equal(new[] { 11 }, Ids(await Page(client,
            $"/payments?search={Uri.EscapeDataString("ชำระเงิน")}")));
    }

    [Fact]
    public async Task TextSearch_MatchesTransactionNumberCaseInsensitively()
    {
        var matching = Row(11);
        matching.TransactionNumber = "TX-THAI-SYNTHETIC";
        await fixture.SeedAsync(matching, Row(22));
        using var client = fixture.Client();

        Assert.Equal(new[] { 11 }, Ids(await Page(client, "/payments?search=tx-thai")));
    }

    [Theory]
    [InlineData("PaymentDate_Ascending")]
    [InlineData("PaymentDate_Descending")]
    public async Task EqualSortKeys_UseStableAscendingIdAcrossPages(string sort)
    {
        await fixture.SeedAsync(Row(33), Row(11), Row(22));
        using var client = fixture.Client();
        var first = await Page(client, $"/payments?sort={sort}&size=2&index=1");
        var second = await Page(client, $"/payments?sort={sort}&size=2&index=2");

        Assert.Equal(new[] { 11, 22 }, Ids(first));
        Assert.Equal(new[] { 33 }, Ids(second));
        Assert.True(first.GetProperty("HasNextPage").GetBoolean());
        Assert.True(second.GetProperty("HasPreviousPage").GetBoolean());
    }

    [Theory]
    [InlineData("PaymentDate_Ascending", 11, 33, 22)]
    [InlineData("PaymentDate_Descending", 22, 33, 11)]
    [InlineData("PaymentCreatedDate_Ascending", 11, 33, 22)]
    [InlineData("PaymentCreatedDate_Descending", 22, 33, 11)]
    [InlineData("PaymentModifiedDate_Ascending", 11, 33, 22)]
    [InlineData("PaymentModifiedDate_Descending", 22, 33, 11)]
    public async Task NullableDates_PreserveSqlServerNullPlacement(string sort, int first, int second, int third)
    {
        var withoutDate = Row(11);
        withoutDate.PaymentDate = null;
        var latest = Row(22);
        latest.PaymentDate = DateTime.SpecifyKind(new DateTime(2026, 1, 3), DateTimeKind.Utc);
        latest.CreatedDate = WallDate(3);
        latest.ModifiedDate = WallDate(3);
        await fixture.SeedAsync(withoutDate, latest, Row(33));
        // Explicit NULL updates avoid CreatedDate/ModifiedDate insert defaults masking the fixture.
        await using (var database = fixture.Database())
        {
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ExecuteUpdateAsync(
                database.Payments.Where(row => row.Id == 11),
                setters => setters.SetProperty(row => row.CreatedDate, (DateTime?)null)
                    .SetProperty(row => row.ModifiedDate, (DateTime?)null));
        }
        using var client = fixture.Client();

        Assert.Equal(new[] { first, second, third }, Ids(await Page(client, $"/payments?sort={sort}")));
    }

    [Theory]
    [InlineData("Recipient_Ascending", 11, 33, 22)]
    [InlineData("Recipient_Descending", 22, 33, 11)]
    [InlineData("PaymentDirection_Ascending", 11, 33, 22)]
    [InlineData("PaymentDirection_Descending", 22, 33, 11)]
    [InlineData("PaymentMethod_Ascending", 11, 33, 22)]
    [InlineData("PaymentMethod_Descending", 22, 33, 11)]
    [InlineData("PaymentType_Ascending", 11, 33, 22)]
    [InlineData("PaymentType_Descending", 22, 33, 11)]
    public async Task NullableStringSortKeys_PreserveSqlServerNullPlacement(string sort, int first, int second, int third)
    {
        var withoutName = Row(11);
        withoutName.Recipient = null!;
        var alpha = Row(33);
        alpha.Recipient = "Alpha";
        alpha.PaymentDirectionId = 2;
        alpha.PaymentMethodId = 2;
        alpha.PaymentTypeId = 2;
        var charlie = Row(22);
        charlie.Recipient = "Charlie";
        charlie.PaymentDirectionId = 3;
        charlie.PaymentMethodId = 3;
        charlie.PaymentTypeId = 3;
        await fixture.SeedAsync(withoutName, alpha, charlie);
        if (!sort.StartsWith("Recipient_", StringComparison.Ordinal))
        {
            await fixture.SetNullableLookupNamesAsync(sort);
        }

        using var client = fixture.Client();
        Assert.Equal(new[] { first, second, third }, Ids(await Page(client, $"/payments?sort={sort}")));
    }

    [Theory]
    [InlineData("not-a-payment-sort")]
    [InlineData("999")]
    public async Task InvalidSort_Returns400RatherThanSilentlySelectingAnotherOrder(string sort)
    {
        await SeedSortRows();
        using var client = fixture.Client();
        using var response = await client.GetAsync($"/payments?sort={Uri.EscapeDataString(sort)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousListing_Returns401InsteadOfFinancialRows()
    {
        await fixture.SeedAsync(Row(11));
        using var client = fixture.Client(authenticated: false);
        using var response = await client.GetAsync("/payments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("Synthetic financial fixture", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LivePermissionDenial_Returns403DespiteValidReadClaim()
    {
        await fixture.SeedAsync(Row(11));
        fixture.AllowLive = false;
        using var client = fixture.Client();
        using var response = await client.GetAsync("/payments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Synthetic financial fixture", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedPage_Retains250RowCapAndActualMetadata()
    {
        await fixture.SeedAsync(Enumerable.Range(1, 260).Select(Row).ToArray());
        using var client = fixture.Client();
        var page = await Page(client, "/payments?size=999&index=1");

        Assert.Equal(250, Ids(page).Length);
        Assert.Equal(260, page.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(2, page.GetProperty("TotalPages").GetInt32());
        Assert.True(page.GetProperty("HasNextPage").GetBoolean());
    }

    [Fact]
    public async Task OmittedSize_RetainsBounded20RowDefault()
    {
        await fixture.SeedAsync(Enumerable.Range(1, 25).Select(Row).ToArray());
        using var client = fixture.Client();
        var page = await Page(client, "/payments");

        Assert.Equal(20, Ids(page).Length);
        Assert.Equal(25, page.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(2, page.GetProperty("TotalPages").GetInt32());
    }

    [Fact]
    public async Task NonpositivePageAndSize_RetainMinimumBounds()
    {
        await fixture.SeedAsync(Row(11), Row(22));
        using var client = fixture.Client();
        var page = await Page(client, "/payments?index=-4&size=0");

        Assert.Single(Ids(page));
        Assert.Equal(1, page.GetProperty("PageIndex").GetInt32());
        Assert.Equal(2, page.GetProperty("TotalPages").GetInt32());
    }

    [Fact]
    public async Task EmptySearchResult_Returns404()
    {
        await fixture.SeedAsync(Row(11));
        using var client = fixture.Client();
        using var response = await client.GetAsync("/payments?search=no-match-fixture");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BeyondLastPage_RetainsEmpty200PageWithTotalMetadata()
    {
        await fixture.SeedAsync(Row(11), Row(22));
        using var client = fixture.Client();
        var page = await Page(client, "/payments?index=3&size=1");

        Assert.Empty(Ids(page));
        Assert.Equal(2, page.GetProperty("TotalRecords").GetInt32());
        Assert.Equal(3, page.GetProperty("PageIndex").GetInt32());
        Assert.Equal(2, page.GetProperty("TotalPages").GetInt32());
    }

    private async Task SeedSortRows()
    {
        var first = Row(11);
        first.CreatedDate = WallDate(3);
        first.ModifiedDate = WallDate(2);
        first.PaymentDate = DateTime.SpecifyKind(WallDate(2), DateTimeKind.Utc);
        first.PaymentDirectionId = 2;
        first.PaymentMethodId = 3;
        first.PaymentTypeId = 1;
        first.Recipient = "Charlie";
        var second = Row(22);
        second.CreatedDate = WallDate(1);
        second.ModifiedDate = WallDate(3);
        second.PaymentDate = DateTime.SpecifyKind(WallDate(3), DateTimeKind.Utc);
        second.PaymentDirectionId = 3;
        second.PaymentMethodId = 1;
        second.PaymentTypeId = 2;
        second.Recipient = "Alpha";
        var third = Row(33);
        third.CreatedDate = WallDate(2);
        third.ModifiedDate = WallDate(1);
        third.PaymentDate = DateTime.SpecifyKind(WallDate(1), DateTimeKind.Utc);
        third.PaymentDirectionId = 1;
        third.PaymentMethodId = 2;
        third.PaymentTypeId = 3;
        third.Recipient = "Bravo";
        await fixture.SeedAsync(first, second, third);
    }

    private static Payment Row(int id) => new()
    {
        Id = id,
        Amount = 1234.56m,
        CurrencyId = 1,
        EmployeeId = 42,
        Description = "Synthetic financial fixture",
        TransactionNumber = "TX-synthetic",
        Recipient = "Thai fixture",
        PaymentDirectionId = 1,
        PaymentMethodId = 1,
        PaymentTypeId = 1,
        CreatedDate = WallDate(1),
        ModifiedDate = WallDate(1),
        PaymentDate = DateTime.SpecifyKind(WallDate(1), DateTimeKind.Utc),
    };

    private static DateTime WallDate(int day) => new(2026, 1, day, 0, 0, 0, DateTimeKind.Unspecified);

    private static async Task<JsonElement> Page(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = document.RootElement.Clone();
        var row = page.GetProperty("Items").EnumerateArray().FirstOrDefault();
        if (row.ValueKind == JsonValueKind.Object)
        {
            Assert.Equal(1234.56m, row.GetProperty("Amount").GetDecimal());
            Assert.False(row.TryGetProperty("PaymentDirection", out _));
            Assert.False(row.TryGetProperty("PaymentMethod", out _));
            Assert.False(row.TryGetProperty("PaymentType", out _));
            Assert.False(row.TryGetProperty("PaymentFile", out _));
        }

        return page;
    }

    private static int[] Ids(JsonElement page) => page.GetProperty("Items").EnumerateArray()
        .Select(row => row.GetProperty("Id").GetInt32()).ToArray();
}
