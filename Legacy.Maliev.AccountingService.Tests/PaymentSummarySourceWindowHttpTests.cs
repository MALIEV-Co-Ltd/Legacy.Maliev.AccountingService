using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaymentSummarySourceWindowHttpTests(AccountingBoundaryHttpFixture fixture)
{
    // Source helpers return final-day midnight and source predicates use <= that boundary.
    // This acceptance oracle intentionally does not bless the target's known half-open-window deviation.
    [Theory]
    [InlineData("weekly", "2026-10-06", "2026-10-11", "2026-10-12", "2026-09-29")]
    [InlineData("monthly", "2026-10-06", "2026-10-31", "2026-11-01", "2026-09-29")]
    [InlineData("yearly", "2026-10-06", "2026-12-31", "2027-01-01", "2025-06-15")]
    public async Task Summary_SourceLastDayMidnightBoundary_ExcludesLaterPayments(string route, string interior, string lastDay, string nextPeriod, string previous)
    {
        await fixture.ResetAsync();
        await using (var database = fixture.Database())
        {
            (await database.Directions.SingleAsync(row => row.Id == 100000)).Name = "Income";
            (await database.Types.SingleAsync(row => row.Id == 100000)).Name = "Job";
            database.Directions.Add(new PaymentDirection { Id = 100001, Name = "Expense", Description = "Synthetic source-compatible seed" });
            database.Payments.AddRange(
                Payment(interior, 12, 100m),
                Payment(lastDay, 0, 10m),
                Payment(lastDay, 12, 20m),
                Payment(nextPeriod, 0, 10000m),
                Payment(previous, 12, 80m));
            await database.SaveChangesAsync();
        }
        var before = await StoredRowsAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync($"/payments/summaries/{route}");
        await fixture.AssertStatusAsync(response, HttpStatusCode.OK);
        var wire = JsonNode.Parse(await response.Content.ReadAsStringAsync(),
            new JsonNodeOptions { PropertyNameCaseInsensitive = false })!.AsObject();
        var detail = Assert.Single(wire["Details"]!.AsArray())!;
        Assert.Equal("7", detail["CurrencyId"]!.GetValue<string>());
        Assert.Equal(110m, detail["CurrentAmount"]!.GetValue<decimal>());
        Assert.Equal(80m, detail["PreviousAmount"]!.GetValue<decimal>());
        Assert.Equal(30m, detail["DeltaAmount"]!.GetValue<decimal>());
        Assert.Equal(37.5m, detail["DeltaPercent"]!.GetValue<decimal>());
        Assert.Equal(before, await StoredRowsAsync());
    }

    [Fact]
    public async Task MonthlyJobIncome_SourceZeroPreviousJobGroup_OmitsCurrencyWithoutInventingException()
    {
        await fixture.ResetAsync();
        await using (var database = fixture.Database())
        {
            (await database.Directions.SingleAsync(row => row.Id == 100000)).Name = "Income";
            (await database.Types.SingleAsync(row => row.Id == 100000)).Name = "Job";
            database.Types.Add(new PaymentType { Id = 100001, Name = "Other", Description = "Synthetic source-compatible seed" });
            var previous = Payment("2026-09-29", 12, 80m);
            previous.PaymentTypeId = 100001;
            // Keep a real previous currency group: source can compute zero Job income without dereferencing a missing group.
            database.Payments.AddRange(Payment("2026-10-06", 12, 100m), previous);
            await database.SaveChangesAsync();
        }
        var before = await StoredRowsAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/payments/summaries/monthly/income/job");
        await fixture.AssertStatusAsync(response, HttpStatusCode.OK);
        var wire = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Empty(wire["Details"]!.AsArray());
        Assert.Equal(before, await StoredRowsAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task YearlyDetail_SourceLastDayMidnightBoundary_ExcludesLaterPaymentForSelectedDirection(bool income)
    {
        await fixture.ResetAsync();
        await using (var database = fixture.Database())
        {
            (await database.Directions.SingleAsync(row => row.Id == 100000)).Name = "Income";
            database.Directions.Add(new PaymentDirection { Id = 100001, Name = "Expense", Description = "Synthetic source-compatible seed" });
            var midnight = Payment("2026-12-31", 0, 10m);
            var noon = Payment("2026-12-31", 12, 20m);
            var otherDirection = Payment("2026-12-31", 0, 10000m);
            if (!income) midnight.PaymentDirectionId = noon.PaymentDirectionId = 100001;
            otherDirection.PaymentDirectionId = income ? 100001 : 100000;
            database.Payments.AddRange(midnight, noon, otherDirection);
            await database.SaveChangesAsync();
        }
        var before = await StoredRowsAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync($"/payments/summaries/yearly/{(income ? "income" : "expense")}?currencyId=7");
        await fixture.AssertStatusAsync(response, HttpStatusCode.OK);
        var wire = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        var row = Assert.Single(wire);
        Assert.Equal(new DateTime(2026, 12, 31), DateTime.Parse(row.Key, CultureInfo.InvariantCulture).Date);
        Assert.Equal(10m, row.Value!.GetValue<decimal>());
        Assert.Equal(before, await StoredRowsAsync());
    }

    private static Payment Payment(string date, int hour, decimal amount) => new()
    {
        PaymentDirectionId = 100000,
        PaymentMethodId = 100000,
        PaymentTypeId = 100000,
        Amount = amount,
        CurrencyId = 7,
        PaymentDate = DateTime.SpecifyKind(DateTime.Parse(date, CultureInfo.InvariantCulture).AddHours(hour), DateTimeKind.Utc),
        Description = "Synthetic source period-boundary acceptance",
        Recipient = "Synthetic",
        TransactionNumber = "SOURCE-WINDOW-ONLY",
    };

    private async Task<string[]> StoredRowsAsync()
    {
        await using var database = fixture.Database();
        return (await database.Payments.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Amount, row.CurrencyId, row.PaymentDate, row.ModifiedDate }).ToArrayAsync())
            .Select(row => System.Text.Json.JsonSerializer.Serialize(row)).ToArray();
    }
}
