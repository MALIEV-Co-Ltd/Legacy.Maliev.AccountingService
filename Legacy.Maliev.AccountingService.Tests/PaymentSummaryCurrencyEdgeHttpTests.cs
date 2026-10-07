using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaymentSummaryCurrencyEdgeHttpTests(AccountingBoundaryHttpFixture fixture)
{
    // Source: 135e526d0dab85c415b3afdcefd7b70fe2c82e2f,
    // Maliev.PaymentService.Api/Controllers/SummariesController.cs, blob f3d63cdaeecfa0bbd1607f9f747afe8b519c1388.
    // Missing/null previous groups can throw in that source. Safe zero sums and the retained "-"
    // null-currency key are intentional target resilience; these cases do not restore accidental 500s.
    [Theory]
    [InlineData("weekly", true, "missing")]
    [InlineData("weekly", false, "missing")]
    [InlineData("weekly", true, "zero")]
    [InlineData("weekly", false, "zero")]
    [InlineData("weekly", true, "null")]
    [InlineData("weekly", false, "null")]
    [InlineData("monthly", true, "missing")]
    [InlineData("monthly", false, "missing")]
    [InlineData("monthly", true, "zero")]
    [InlineData("monthly", false, "zero")]
    [InlineData("monthly", true, "null")]
    [InlineData("monthly", false, "null")]
    [InlineData("yearly", true, "missing")]
    [InlineData("yearly", false, "missing")]
    [InlineData("yearly", true, "zero")]
    [InlineData("yearly", false, "zero")]
    [InlineData("yearly", true, "null")]
    [InlineData("yearly", false, "null")]
    public async Task Summary_PreviousCurrencyEdgesPreserveSignedAmountsKeysAndReadOnlyStorage(string route, bool income, string edge)
    {
        await SeedCatalogsAsync();
        var previousDate = route == "yearly" ? new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc)
            : new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        int? currency = edge == "null" ? null : 7;
        await using (var database = fixture.Database())
        {
            database.Payments.Add(Row(CurrentDate, 120m, currency, income));
            // A real previous-only currency must neither be merged into the selected currency nor appear in Details.
            database.Payments.Add(Row(previousDate, 500m, 9, income));
            if (edge == "zero")
                database.Payments.AddRange(Row(previousDate, 40m, currency, true), Row(previousDate, 40m, currency, false));
            else if (edge == "null")
                database.Payments.Add(Row(previousDate, 30m, null, income));
            await database.SaveChangesAsync();
        }
        var before = await StorageHashAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/payments/summaries/" + route);
        await fixture.AssertStatusAsync(response, HttpStatusCode.OK);
        var details = await DetailsAsync(response);
        var detail = Assert.Single(details)!;
        var sign = income ? 1m : -1m;
        AssertDetail(detail, edge == "null" ? "-" : "7", 120m * sign,
            edge == "null" ? 30m * sign : 0m, edge == "null" ? 90m * sign : 120m * sign,
            edge == "null" ? 300m * sign : 0m);
        AssertLiveRead();
        Assert.Equal(before, await StorageHashAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("zero")]
    [InlineData("null")]
    public async Task MonthlyJob_PreviousCurrencyEdgesOmitZeroIncomeButRetainValidControlAndNullKey(string edge)
    {
        await SeedCatalogsAsync();
        var previousDate = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        int? currency = edge == "null" ? null : 7;
        await using (var database = fixture.Database())
        {
            database.Payments.AddRange(Row(CurrentDate, 120m, currency, true),
                Row(CurrentDate, 900m, currency, true, job: false), Row(CurrentDate, 800m, currency, false),
                Row(CurrentDate, 60m, 9, true), Row(previousDate, 20m, 9, true));
            if (edge == "zero")
                database.Payments.AddRange(Row(previousDate, 0m, currency, true), Row(previousDate, 999m, currency, true, job: false));
            else if (edge == "null")
                database.Payments.Add(Row(previousDate, 30m, null, true));
            await database.SaveChangesAsync();
        }
        var before = await StorageHashAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/payments/summaries/monthly/income/job");
        await fixture.AssertStatusAsync(response, HttpStatusCode.OK);
        var details = await DetailsAsync(response);
        Assert.Equal(edge == "null" ? 2 : 1, details.Count);
        AssertDetail(Assert.Single(details, row => row!["CurrencyId"]!.GetValue<string>() == "9")!, "9", 60m, 20m, 40m, 200m);
        if (edge == "null")
            AssertDetail(Assert.Single(details, row => row!["CurrencyId"]!.GetValue<string>() == "-")!, "-", 120m, 30m, 90m, 300m);
        else
            Assert.DoesNotContain(details, row => row!["CurrencyId"]!.GetValue<string>() == "7");
        AssertLiveRead();
        Assert.Equal(before, await StorageHashAsync());
    }

    [Theory]
    [InlineData("yearly/income?year=2025&currencyId=7", 2025, 100, 20)]
    [InlineData("yearly/income?year=2025", 2025, 175, 20)]
    [InlineData("yearly/expense?currencyId=7", 2026, 40, 10)]
    [InlineData("yearly/expense", 2026, 140, 10)]
    public async Task YearlyDetail_ExplicitYearAndCurrencyFiltersKeepExactDailyKeysAndSelectedDirection(string route, int year, int firstAmount, int secondAmount)
    {
        await SeedDetailRowsAsync();
        var before = await StorageHashAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/payments/summaries/" + route);
        await fixture.AssertStatusAsync(response, HttpStatusCode.OK);
        var wire = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal(2, wire.Count);
        // Date keys are public dictionary keys; each must represent midnight, not the stored noon.
        string firstKey = year.ToString(CultureInfo.InvariantCulture) + "-06-15T00:00:00Z";
        string secondKey = year.ToString(CultureInfo.InvariantCulture) + "-06-16T00:00:00Z";
        Assert.Equal(new[] { firstKey, secondKey }, wire.Select(row => row.Key).Order(StringComparer.Ordinal));
        Assert.Equal((decimal)firstAmount, wire[firstKey]!.GetValue<decimal>());
        Assert.Equal((decimal)secondAmount, wire[secondKey]!.GetValue<decimal>());
        AssertLiveRead();
        Assert.Equal(before, await StorageHashAsync());
    }

    [Theory]
    [InlineData("yearly/income?year=2024&currencyId=7")]
    [InlineData("yearly/expense?currencyId=999")]
    public async Task YearlyDetail_EmptySelectedFilterReturns404ProblemWireWithoutMutatingPopulatedStorage(string route)
    {
        await SeedDetailRowsAsync();
        var before = await StorageHashAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/payments/summaries/" + route);
        await fixture.AssertStatusAsync(response, HttpStatusCode.NotFound);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var wire = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal(404, wire["status"]!.GetValue<int>());
        Assert.Equal("Not Found", wire["title"]!.GetValue<string>());
        Assert.False(wire.ContainsKey("Details"));
        Assert.DoesNotContain("CURRENCY-EDGE-ONLY", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        AssertLiveRead();
        Assert.Equal(before, await StorageHashAsync());
    }

    private static DateTime CurrentDate => new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private async Task SeedCatalogsAsync()
    {
        await fixture.ResetAsync();
        await using var database = fixture.Database();
        (await database.Directions.SingleAsync(row => row.Id == 100000)).Name = "Income";
        (await database.Types.SingleAsync(row => row.Id == 100000)).Name = "Job";
        database.Directions.Add(new PaymentDirection { Id = 100001, Name = "Expense", Description = "Synthetic source-compatible seed" });
        database.Types.Add(new PaymentType { Id = 100001, Name = "Other", Description = "Synthetic source-compatible seed" });
        await database.SaveChangesAsync();
    }

    private async Task SeedDetailRowsAsync()
    {
        await SeedCatalogsAsync();
        await using var database = fixture.Database();
        var earlier = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var current = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        database.Payments.AddRange(Row(earlier, 100m, 7, true), Row(earlier.AddDays(1), 20m, 7, true),
            Row(earlier, 50m, 9, true), Row(earlier, 25m, null, true), Row(earlier, 900m, 7, false),
            Row(current, 700m, 7, true), Row(current, 40m, 7, false), Row(current.AddDays(1), 10m, 7, false),
            Row(current, 70m, 9, false), Row(current, 30m, null, false),
            Row(new DateTime(2027, 6, 15, 12, 0, 0, DateTimeKind.Utc), 10000m, 7, false));
        await database.SaveChangesAsync();
    }

    private static Payment Row(DateTime date, decimal amount, int? currency, bool income, bool job = true) => new()
    {
        PaymentDirectionId = income ? 100000 : 100001,
        PaymentMethodId = 100000,
        PaymentTypeId = job ? 100000 : 100001,
        PaymentDate = date,
        Amount = amount,
        CurrencyId = currency,
        Description = "Synthetic summary currency-edge acceptance",
        Recipient = "Synthetic",
        TransactionNumber = "CURRENCY-EDGE-ONLY",
    };

    private static async Task<JsonArray> DetailsAsync(HttpResponseMessage response)
    {
        var wire = JsonNode.Parse(await response.Content.ReadAsStringAsync(),
            new JsonNodeOptions { PropertyNameCaseInsensitive = false })!.AsObject();
        Assert.Equal("Details", Assert.Single(wire).Key);
        return wire["Details"]!.AsArray();
    }

    private static void AssertDetail(JsonNode detail, string currency, decimal current, decimal previous, decimal delta, decimal percent)
    {
        Assert.Equal(5, detail.AsObject().Count);
        Assert.Equal(currency, detail["CurrencyId"]!.GetValue<string>());
        Assert.Equal(current, detail["CurrentAmount"]!.GetValue<decimal>());
        Assert.Equal(previous, detail["PreviousAmount"]!.GetValue<decimal>());
        Assert.Equal(delta, detail["DeltaAmount"]!.GetValue<decimal>());
        Assert.Equal(percent, detail["DeltaPercent"]!.GetValue<decimal>());
    }

    private void AssertLiveRead()
    {
        var check = Assert.Single(fixture.LiveChecks);
        Assert.Equal(AccountingPermissions.Read, check.Permission);
        Assert.StartsWith("service:accounting-boundary-", check.Subject);
    }

    private async Task<string> StorageHashAsync()
    {
        await using var database = fixture.Database();
        var payments = await database.Payments.AsNoTracking().OrderBy(row => row.Id).Select(row => new
        {
            row.Id,
            row.EmployeeId,
            row.PaymentDirectionId,
            row.PaymentTypeId,
            row.PaymentMethodId,
            row.PaymentDate,
            row.Amount,
            row.CurrencyId,
            row.CreatedDate,
            row.ModifiedDate,
            row.Description,
            row.Recipient,
            row.TransactionNumber
        }).ToArrayAsync();
        var directions = await database.Directions.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).ToArrayAsync();
        var types = await database.Types.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).ToArrayAsync();
        var methods = await database.Methods.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).ToArrayAsync();
        var body = JsonSerializer.Serialize(new { payments, directions, types, methods, Cache = await fixture.SummaryCacheSnapshotAsync() });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    }
}
