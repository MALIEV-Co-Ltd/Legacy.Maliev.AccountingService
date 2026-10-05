using System.Net;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class PaymentMasterQuerySourceHttpTests(PaymentListHttpFixture fixture)
    : IClassFixture<PaymentListHttpFixture>
{
    private const string Thai = "\u0e0a\u0e34\u0e49\u0e19\u0e07\u0e32\u0e19";

    [Theory]
    [InlineData(" part")]
    [InlineData("part ")]
    [InlineData(" part ")]
    public async Task LiteralText_SourcePreservesSignificantSpaces(string search)
    {
        await SeedAsync();
        using var client = fixture.Client();
        using var response = await client.GetAsync($"/payments?search={Uri.EscapeDataString(search)}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertUnchangedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlternatingPlainSpacedRequests_RetainLiteralQueryScope(bool spacedFirst)
    {
        await SeedAsync();
        using var client = fixture.Client();
        var searches = spacedFirst ? new[] { " part ", "part", " part " } : new[] { "part", " part ", "part" };
        foreach (var search in searches)
        {
            using var response = await client.GetAsync($"/payments?search={Uri.EscapeDataString(search)}");
            Assert.Equal(search == "part" ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
            if (search == "part") Assert.Equal(11, await SingleIdAsync(response));
        }
        await AssertUnchangedAsync();
    }

    [Fact]
    public async Task SelectedEmptyPage_SourceReturnsNotFound()
    {
        await SeedAsync();
        using var client = fixture.Client();
        using var response = await client.GetAsync("/payments?index=100&size=1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertUnchangedAsync();
    }

    [Theory]
    [InlineData("11")]
    [InlineData("0011")]
    [InlineData(" 11 ")]
    public async Task NumericSearch_PreservesExclusiveExactId(string search)
    {
        await SeedAsync();
        using var client = fixture.Client();
        using var response = await client.GetAsync($"/payments?search={Uri.EscapeDataString(search)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(11, await SingleIdAsync(response));
        await AssertUnchangedAsync();
    }

    [Fact]
    public async Task ThaiLiteral_RemainsExactAcrossHttpAndPostgreSql()
    {
        await SeedAsync();
        using var client = fixture.Client();
        using var response = await client.GetAsync($"/payments?search={Uri.EscapeDataString(Thai)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(11, await SingleIdAsync(response));
        await using var database = fixture.Database();
        Assert.EndsWith(Thai, (await database.Payments.SingleAsync(row => row.Id == 11)).Description, StringComparison.Ordinal);
        await AssertUnchangedAsync();
    }

    private Task SeedAsync() => fixture.SeedAsync(
        Row(11, "Fixture%part_" + Thai), Row(22, "Synthetic reference 11"));

    private static Payment Row(int id, string description) => new()
    {
        Id = id,
        Amount = 1234.56m,
        CurrencyId = 1,
        EmployeeId = 42,
        Description = description,
        TransactionNumber = "TX-synthetic",
        Recipient = "Thai fixture",
        PaymentDirectionId = 1,
        PaymentMethodId = 1,
        PaymentTypeId = 1,
        CreatedDate = new DateTime(2026, 10, 5),
        ModifiedDate = new DateTime(2026, 10, 5),
        PaymentDate = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
    };

    private static async Task<int> SingleIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("Items").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal(1, document.RootElement.GetProperty("TotalRecords").GetInt32());
        return items[0].GetProperty("Id").GetInt32();
    }

    private async Task AssertUnchangedAsync()
    {
        await using var database = fixture.Database();
        Assert.Equal(new[] { 11, 22 }, await database.Payments.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync());
        Assert.All(await database.Payments.ToArrayAsync(), row => Assert.Equal(1234.56m, row.Amount));
        Assert.Empty(await database.Files.ToArrayAsync());
    }
}
