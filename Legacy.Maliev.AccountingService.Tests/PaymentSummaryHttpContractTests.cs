using System.Net;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaymentSummaryHttpContractTests(AccountingBoundaryHttpFixture fixture)
{
    private async Task SeedAsync()
    {
        await fixture.ResetAsync();
        await using var database = fixture.Database();
        (await database.Directions.SingleAsync(row => row.Id == 100000)).Name = "Income";
        (await database.Types.SingleAsync(row => row.Id == 100000)).Name = "Job";
        database.Directions.Add(new PaymentDirection { Id = 100001, Name = "Expense", Description = "Synthetic source-compatible seed" });
        database.Types.Add(new PaymentType { Id = 100001, Name = "Other", Description = "Synthetic source-compatible seed" });
        database.Payments.AddRange(
            Row(2026, 10, 6, 100m, 7),
            Row(2026, 10, 6, 40m, 7, job: false),
            Row(2026, 10, 6, 30m, 7, income: false),
            Row(2026, 9, 29, 80m, 7),
            Row(2026, 9, 29, 20m, 7, income: false),
            Row(2025, 6, 15, 70m, 7),
            Row(2025, 6, 15, 20m, 7, income: false),
            Row(2026, 10, 6, 7m, 9),
            Row(2026, 9, 29, 3m, 9),
            Row(2025, 6, 15, 2m, 9),
            Row(2027, 1, 5, 10000m, 7),
            new Payment
            {
                PaymentDirectionId = 100000,
                PaymentMethodId = 100000,
                PaymentTypeId = 100000,
                Amount = 20000m,
                Description = "Synthetic undated exclusion",
                Recipient = "Synthetic",
                TransactionNumber = "SUMMARY-UNDATED",
            });
        await database.SaveChangesAsync();
    }

    private static Payment Row(int year, int month, int day, decimal amount, int currency, bool income = true, bool job = true) => new()
    {
        PaymentDirectionId = income ? 100000 : 100001,
        PaymentMethodId = 100000,
        PaymentTypeId = job ? 100000 : 100001,
        Amount = amount,
        CurrencyId = currency,
        PaymentDate = new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc),
        Description = "Synthetic summary interior-date payment",
        Recipient = "Synthetic",
        TransactionNumber = "SUMMARY-ONLY",
    };

    private async Task<string[]> RowsAsync()
    {
        await using var database = fixture.Database();
        return (await database.Payments.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Amount, row.CurrencyId, row.PaymentDate, row.ModifiedDate }).ToArrayAsync())
            .Select(row => System.Text.Json.JsonSerializer.Serialize(row)).ToArray();
    }

    [Theory]
    [InlineData("monthly")]
    [InlineData("weekly")]
    [InlineData("monthly/income/job")]
    [InlineData("yearly")]
    [InlineData("yearly/income?year=2025&currencyId=7")]
    [InlineData("yearly/expense?currencyId=7")]
    public async Task Summary_RealSqlInteriorPeriodsReturnSourceAndConsumerFinancialContract(string projection)
    {
        await SeedAsync();
        var before = await RowsAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync($"/payments/summaries/{projection}");
        await fixture.AssertStatusAsync(response, HttpStatusCode.OK);
        var wire = JsonNode.Parse(await response.Content.ReadAsStringAsync(),
            new JsonNodeOptions { PropertyNameCaseInsensitive = false })!.AsObject();
        if (projection.StartsWith("yearly/income", StringComparison.Ordinal))
        {
            Assert.Single(wire);
            Assert.Equal(70m, Assert.Single(wire).Value!.GetValue<decimal>());
            Assert.Equal(new DateTime(2025, 6, 15), DateTime.Parse(Assert.Single(wire).Key,
                System.Globalization.CultureInfo.InvariantCulture).Date);
        }
        else if (projection.StartsWith("yearly/expense", StringComparison.Ordinal))
        {
            Assert.Equal(2, wire.Count);
            var daily = wire.ToDictionary(row => DateTime.Parse(row.Key,
                System.Globalization.CultureInfo.InvariantCulture).Date, row => row.Value!.GetValue<decimal>());
            Assert.Equal(20m, daily[new DateTime(2026, 9, 29)]);
            Assert.Equal(30m, daily[new DateTime(2026, 10, 6)]);
        }
        else
        {
            Assert.False(wire.ContainsKey("details"));
            var details = wire["Details"]!.AsArray();
            Assert.Equal(2, details.Count);
            var currency7 = Assert.Single(details, row => row!["CurrencyId"]!.GetValue<string>() == "7")!;
            var currency9 = Assert.Single(details, row => row!["CurrencyId"]!.GetValue<string>() == "9")!;
            if (projection == "monthly/income/job")
            {
                AssertDetail(currency7, 100m, 80m, 20m, 25m);
                AssertDetail(currency9, 7m, 3m, 4m, 133.33m);
            }
            else if (projection == "yearly")
            {
                AssertDetail(currency7, 170m, 50m, 120m, 240m);
                AssertDetail(currency9, 10m, 2m, 8m, 400m);
            }
            else
            {
                AssertDetail(currency7, 110m, 60m, 50m, 83.33m);
                AssertDetail(currency9, 7m, 3m, 4m, 133.33m);
            }
        }

        Assert.Equal(before, await RowsAsync());
    }

    private static void AssertDetail(JsonNode detail, decimal current, decimal previous, decimal delta, decimal percent)
    {
        Assert.Equal(current, detail["CurrentAmount"]!.GetValue<decimal>());
        Assert.Equal(previous, detail["PreviousAmount"]!.GetValue<decimal>());
        Assert.Equal(delta, detail["DeltaAmount"]!.GetValue<decimal>());
        Assert.Equal(percent, detail["DeltaPercent"]!.GetValue<decimal>());
    }

    [Theory]
    [InlineData("monthly")]
    [InlineData("weekly")]
    [InlineData("monthly/income/job")]
    [InlineData("yearly")]
    [InlineData("yearly/income?year=2025&currencyId=7")]
    [InlineData("yearly/expense?currencyId=7")]
    public async Task Summary_EmptyPeriodReturnsSource404WithoutCreatingPayments(string projection)
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync($"/payments/summaries/{projection}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await RowsAsync());
    }

    [Theory]
    [InlineData("monthly")]
    [InlineData("weekly")]
    [InlineData("monthly/income/job")]
    [InlineData("yearly")]
    [InlineData("yearly/income?year=2025&currencyId=7")]
    [InlineData("yearly/expense?currencyId=7")]
    public async Task Summary_AnonymousCallerCannotReadFinancialProjection(string projection)
    {
        await SeedAsync();
        var before = await RowsAsync();
        using var client = fixture.Client(summaryClockHost: true);
        using var response = await client.GetAsync($"/payments/summaries/{projection}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await RowsAsync());
    }

    [Theory]
    [InlineData("monthly")]
    [InlineData("weekly")]
    [InlineData("monthly/income/job")]
    [InlineData("yearly")]
    [InlineData("yearly/income?year=2025&currencyId=7")]
    [InlineData("yearly/expense?currencyId=7")]
    public async Task Summary_LiveDeniedCallerCannotReadOrMutateFinancialProjection(string projection)
    {
        await SeedAsync();
        var before = await RowsAsync();
        using var client = fixture.Client([AccountingPermissions.Read], allowLive: false, summaryClockHost: true);
        using var response = await client.GetAsync($"/payments/summaries/{projection}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await RowsAsync());
    }
}
