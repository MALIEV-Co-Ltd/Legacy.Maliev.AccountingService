using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaymentSummaryLookupIdentityHttpTests(AccountingBoundaryHttpFixture fixture)
{
    [Theory]
    [InlineData("weekly", "Income")]
    [InlineData("weekly", "Expense")]
    [InlineData("monthly", "Income")]
    [InlineData("monthly", "Expense")]
    [InlineData("yearly", "Income")]
    [InlineData("yearly", "Expense")]
    [InlineData("monthly/income/job", "Income")]
    [InlineData("monthly/income/job", "Job")]
    [InlineData("yearly/income?year=2026&currencyId=7", "Income")]
    [InlineData("yearly/expense?currencyId=7", "Expense")]
    public async Task DuplicateAuthoritativeName_RefusesCombinedFinancialResultWithoutMutation(string route, string duplicate)
    {
        await SeedAsync(duplicate, populated: true);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/payments/summaries/" + route);
        await AssertOpaqueFailureAsync(response);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("weekly", "Income")]
    [InlineData("monthly", "Expense")]
    [InlineData("yearly", "Income")]
    [InlineData("monthly/income/job", "Job")]
    public async Task EmptyCurrentWindow_Returns404BeforeAmbiguousLookup(string route, string duplicate)
    {
        await SeedAsync(duplicate, populated: false);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/payments/summaries/" + route);
        await fixture.AssertStatusAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("yearly/income?year=2025&currencyId=999", "Income")]
    [InlineData("yearly/expense?currencyId=999", "Expense")]
    public async Task EmptyYearlyDetail_RejectsAmbiguousLookupBeforePaymentQuery(string route, string duplicate)
    {
        await SeedAsync(duplicate, populated: false);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/payments/summaries/" + route);
        await AssertOpaqueFailureAsync(response);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task UniqueCatalogIds_PreservePascalCaseWindowAndDeltaWithoutMutation()
    {
        await SeedAsync(null, populated: true);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/payments/summaries/monthly");
        await fixture.AssertStatusAsync(response, HttpStatusCode.OK);
        var wire = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.False(wire.ContainsKey("details"));
        var detail = Assert.Single(wire["Details"]!.AsArray())!;
        Assert.Equal("7", detail["CurrencyId"]!.GetValue<string>());
        Assert.Equal(90m, detail["CurrentAmount"]!.GetValue<decimal>());
        Assert.Equal(60m, detail["PreviousAmount"]!.GetValue<decimal>());
        Assert.Equal(30m, detail["DeltaAmount"]!.GetValue<decimal>());
        Assert.Equal(50m, detail["DeltaPercent"]!.GetValue<decimal>());
        Assert.Equal(before, await SnapshotAsync());
    }

    private async Task SeedAsync(string? duplicate, bool populated)
    {
        await fixture.ResetAsync();
        await using var database = fixture.Database();
        (await database.Directions.SingleAsync(row => row.Id == 100000)).Name = "Income";
        (await database.Types.SingleAsync(row => row.Id == 100000)).Name = "Job";
        database.Directions.Add(new PaymentDirection { Id = 100001, Name = "Expense" });
        if (duplicate == "Job") database.Types.Add(new PaymentType { Id = 100002, Name = duplicate });
        else if (duplicate is not null) database.Directions.Add(new PaymentDirection { Id = 100002, Name = duplicate });
        if (populated)
        {
            database.Payments.AddRange(Row(100000, 100000, 10, 6, 100m), Row(100001, 100000, 10, 6, 10m),
                Row(100000, 100000, 9, 29, 80m), Row(100001, 100000, 9, 29, 20m),
                Row(100000, 100000, 6, 15, 60m, 2025), Row(100001, 100000, 6, 15, 10m, 2025));
            if (duplicate is not null)
            {
                // Both IDs have actual current and previous rows; refusing an unused duplicate alone is insufficient.
                var direction = duplicate == "Job" ? 100000 : 100002;
                var type = duplicate == "Job" ? 100002 : 100000;
                database.Payments.AddRange(Row(direction, type, 10, 6, 700m), Row(direction, type, 9, 29, 500m),
                    Row(direction, type, 6, 15, 400m, 2025));
            }
        }
        else database.Payments.Add(Row(100000, 100000, 9, 29, 80m, 2025));
        await database.SaveChangesAsync();
    }

    private static Payment Row(int direction, int type, int month, int day, decimal amount, int year = 2026) => new()
    {
        PaymentDirectionId = direction,
        PaymentTypeId = type,
        PaymentMethodId = 100000,
        PaymentDate = new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc),
        CurrencyId = 7,
        Amount = amount,
        Description = "Synthetic lookup identity regression",
        Recipient = "Synthetic",
        TransactionNumber = "LOOKUP-IDENTITY-ONLY",
    };

    private async Task<string> SnapshotAsync()
    {
        await using var database = fixture.Database();
        var directions = await database.Directions.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).ToArrayAsync();
        var types = await database.Types.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).ToArrayAsync();
        var payments = await database.Payments.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new
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
        return JsonSerializer.Serialize(new { directions, types, payments, Cache = await fixture.SummaryCacheSnapshotAsync() });
    }

    private async Task AssertOpaqueFailureAsync(HttpResponseMessage response)
    {
        // Retain the normal Defaults mapping; this producer does not change error contracts.
        await fixture.AssertStatusAsync(response, HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        var wire = JsonNode.Parse(body)!.AsObject();
        Assert.Equal("The request cannot be processed.", wire["error"]!.GetValue<string>());
        Assert.Equal(400, wire["statusCode"]!.GetValue<int>());
        Assert.Null(wire["details"]);
        Assert.Contains("InvalidOperationException", fixture.FailureMetadata);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sequence contains", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LOOKUP-IDENTITY-ONLY", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Details", body, StringComparison.Ordinal);
    }
}
