using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

// Source487d06df2f4614c53bead4b19d53d2613a46fd64. Actual HTTP/JWT/repository/PostgreSQL,
// with the existing controlled live-permission adapter; this is not joined IAM acceptance.
[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaidInvoiceSourceReadbackHttpTests(AccountingBoundaryHttpFixture fixture) : IAsyncLifetime
{
    private const string Marker = "SOURCE-READBACK-";
    private static readonly DateTime From = new(2068, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = From.AddDays(2);
    private static readonly Guid Journey = Guid.Parse("28e3a736-882c-4b4e-b254-b85432d994ee");

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var database = fixture.InvoiceDatabase();
        // Delete only this class's childless synthetic rows, never other fixture invoices.
        await database.Invoices.Where(row => row.Number.StartsWith(Marker)).ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Window_SourceIncludesStartExcludesEndAndSkipsUnpaidOrUndatedRows()
    {
        var unpaid = Row(6, From); unpaid.IsPaid = false;
        await SeedAsync(Row(1, From.AddSeconds(-1)), Row(2, From), Row(3, From.AddHours(23)),
            Row(4, To.AddSeconds(-1)), Row(5, To), unpaid, Row(7, null));
        using var client = fixture.Client([AccountingPermissions.Read]);
        var before = await StoredRowsAsync();
        var wire = await ReadAsync(client);
        var days = wire.GetProperty("Days").EnumerateArray().ToArray();
        Assert.Equal(2, days.Length);
        Assert.Equal(From, days[0].GetProperty("DayUtc").GetDateTime());
        Assert.Equal(From.AddDays(1), days[1].GetProperty("DayUtc").GetDateTime());
        Assert.Equal(2, days[0].GetProperty("PaidInvoiceCount").GetInt32());
        Assert.Equal(1, days[1].GetProperty("PaidInvoiceCount").GetInt32());
        Assert.Equal(20m, Assert.Single(days[0].GetProperty("PaidInvoiceAmountsByCurrency").EnumerateArray()).GetProperty("PaidInvoiceTotal").GetDecimal());
        Assert.Equal(10m, Assert.Single(days[1].GetProperty("PaidInvoiceAmountsByCurrency").EnumerateArray()).GetProperty("PaidInvoiceTotal").GetDecimal());
        Assert.Equal(before, await StoredRowsAsync());
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Read);
    }

    [Fact]
    public async Task Attribution_SourceCountsBothNullableKeysWithoutRequiringPositiveOrNonemptyValues()
    {
        var both = Row(1, From); both.SourceRequestId = 17; both.SourceJourneyId = Journey;
        var requestOnly = Row(2, From); requestOnly.SourceRequestId = 17;
        var journeyOnly = Row(3, From); journeyOnly.SourceJourneyId = Journey;
        var presentDefaults = Row(5, From); presentDefaults.SourceRequestId = 0; presentDefaults.SourceJourneyId = Guid.Empty;
        await SeedAsync(both, requestOnly, journeyOnly, Row(4, From), presentDefaults);
        using var client = fixture.Client([AccountingPermissions.Read]);
        var before = await StoredRowsAsync();
        var day = Assert.Single((await ReadAsync(client)).GetProperty("Days").EnumerateArray());
        Assert.Equal(5, day.GetProperty("PaidInvoiceCount").GetInt32());
        Assert.Equal(2, day.GetProperty("SourceAttributedPaidInvoiceCount").GetInt32());
        Assert.Equal(3, day.GetProperty("UnattributedPaidInvoiceCount").GetInt32());
        Assert.Equal(before, await StoredRowsAsync());
    }

    [Fact]
    public async Task Currency_SourceKeepsOrdinalCaseAndPaddingAndCombinesOnlyBlankValues()
    {
        var currencies = new string?[] { "thb", "THB", " THB ", null, "", " \t " };
        var totals = new decimal?[] { -1.25m, 20.50m, 3m, null, 0m, 4.75m };
        await SeedAsync(currencies.Select((currency, index) =>
        {
            var row = Row(index + 1, From); row.Currency = currency!; row.Total = totals[index]; return row;
        }).ToArray());
        using var client = fixture.Client([AccountingPermissions.Read]);
        var before = await StoredRowsAsync();
        var day = Assert.Single((await ReadAsync(client)).GetProperty("Days").EnumerateArray());
        var amounts = day.GetProperty("PaidInvoiceAmountsByCurrency").EnumerateArray().ToArray();
        Assert.Equal(new[] { " THB ", "THB", "UNSPECIFIED", "thb" }, amounts.Select(value => value.GetProperty("Currency").GetString()));
        Assert.Equal(new[] { 3m, 20.50m, 4.75m, -1.25m }, amounts.Select(value => value.GetProperty("PaidInvoiceTotal").GetDecimal()));
        Assert.Equal(new[] { 1, 1, 3, 1 }, amounts.Select(value => value.GetProperty("PaidInvoiceCount").GetInt32()));
        Assert.Equal(6, day.GetProperty("PaidInvoiceCount").GetInt32());
        Assert.Equal(before, await StoredRowsAsync());
    }

    [Fact]
    public async Task EmptyValidWindow_SourceReturns200WithEmptyDays()
    {
        using var client = fixture.Client([AccountingPermissions.Read]);
        Assert.Empty((await ReadAsync(client)).GetProperty("Days").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvoiceUpdate_SourcePreservesCreateTimeAttributionDespiteOmittedOrReplacementKeys(bool replaceKeys)
    {
        var seeded = Row(1, From); seeded.SourceRequestId = 17; seeded.SourceJourneyId = Journey;
        await SeedAsync(seeded);
        using var client = fixture.Client([AccountingPermissions.Read, AccountingPermissions.Update]);
        var original = (await client.GetFromJsonAsync<JsonObject>($"/invoices/{seeded.Id}"))!;
        var body = (JsonObject)original.DeepClone();
        body["Comment"] = "SYNTHETIC-PRIVATE-UPDATED";
        if (replaceKeys)
        {
            body["SourceRequestId"] = 99;
            body["SourceJourneyId"] = "d0e42bbf-3563-4f6f-8157-25d2a0b5d582";
        }
        else
        {
            body.Remove("SourceRequestId");
            body.Remove("SourceJourneyId");
        }

        using var update = await client.PutAsJsonAsync($"/invoices/{seeded.Id}", body);
        await fixture.AssertStatusAsync(update, HttpStatusCode.NoContent);
        var read = (await client.GetFromJsonAsync<JsonObject>($"/invoices/{seeded.Id}"))!;
        Assert.Equal(17, read["SourceRequestId"]!.GetValue<int>());
        Assert.Equal(Journey, Guid.Parse(read["SourceJourneyId"]!.GetValue<string>()));
        Assert.Equal("SYNTHETIC-PRIVATE-UPDATED", read["Comment"]!.GetValue<string>());
        Assert.Equal(original["CreatedDate"]!.GetValue<string>(), read["CreatedDate"]!.GetValue<string>());
        await using var database = fixture.InvoiceDatabase();
        var stored = await database.Invoices.AsNoTracking().SingleAsync(row => row.Id == seeded.Id);
        Assert.Equal(17, stored.SourceRequestId);
        Assert.Equal(Journey, stored.SourceJourneyId);
        Assert.Equal(seeded.Id, stored.Id);
        var day = Assert.Single((await ReadAsync(client)).GetProperty("Days").EnumerateArray());
        Assert.Equal(1, day.GetProperty("SourceAttributedPaidInvoiceCount").GetInt32());
        Assert.Equal(0, day.GetProperty("UnattributedPaidInvoiceCount").GetInt32());
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    public async Task MissingAdmission_DoesNotExposeFinancialOrAttributionData(bool validClaim, HttpStatusCode expected)
    {
        await SeedAsync(Row(1, From));
        var before = await StoredRowsAsync();
        using var client = fixture.Client(validClaim ? [AccountingPermissions.Read] : null, allowLive: false);
        using var response = await client.GetAsync(Path());
        Assert.Equal(expected, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PaidInvoiceTotal", content, StringComparison.Ordinal);
        Assert.DoesNotContain("SYNTHETIC-PRIVATE", content, StringComparison.Ordinal);
        Assert.Equal(before, await StoredRowsAsync());
    }

    private static Invoice Row(int ordinal, DateTime? date) => new()
    {
        Number = $"{Marker}{ordinal}", CustomerId = 42, IsPaid = true, PaymentDate = date,
        Currency = "THB", Total = 10m, Comment = "SYNTHETIC-PRIVATE-COMMENT",
        BillingAddressRecipient = "SYNTHETIC-PRIVATE-RECIPIENT",
    };

    private async Task SeedAsync(params Invoice[] rows)
    {
        await using var database = fixture.InvoiceDatabase();
        database.Invoices.AddRange(rows);
        await database.SaveChangesAsync();
    }

    private async Task<string[]> StoredRowsAsync()
    {
        await using var database = fixture.InvoiceDatabase();
        var rows = await database.Invoices.AsNoTracking().Where(row => row.Number.StartsWith(Marker)).OrderBy(row => row.Id).ToArrayAsync();
        return rows.Select(row => JsonSerializer.Serialize(row)).ToArray();
    }

    private static string Path() => $"/invoices/outcomes/readback?fromUtc={Uri.EscapeDataString(From.ToString("O", CultureInfo.InvariantCulture))}&toUtc={Uri.EscapeDataString(To.ToString("O", CultureInfo.InvariantCulture))}";

    private static async Task<JsonElement> ReadAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Path());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var wire = document.RootElement.Clone();
        Assert.Equal(new[] { "Days", "FromUtc", "ToUtc" }, wire.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(From, wire.GetProperty("FromUtc").GetDateTime());
        Assert.Equal(To, wire.GetProperty("ToUtc").GetDateTime());
        foreach (var day in wire.GetProperty("Days").EnumerateArray())
        {
            Assert.Equal(new[] { "DayUtc", "PaidInvoiceAmountsByCurrency", "PaidInvoiceCount", "SourceAttributedPaidInvoiceCount", "UnattributedPaidInvoiceCount" }, day.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal(DateTimeKind.Utc, day.GetProperty("DayUtc").GetDateTime().Kind);
            foreach (var amount in day.GetProperty("PaidInvoiceAmountsByCurrency").EnumerateArray())
                Assert.Equal(new[] { "Currency", "PaidInvoiceCount", "PaidInvoiceTotal" }, amount.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        }
        Assert.DoesNotContain("SYNTHETIC-PRIVATE", wire.GetRawText(), StringComparison.Ordinal);
        return wire;
    }
}
