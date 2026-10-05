using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaymentProducerDateHttpTests(AccountingBoundaryHttpFixture fixture)
{
    [Theory]
    [InlineData("2026-10-03T00:00:00")]
    [InlineData("2026-10-03T12:34:30")]
    public async Task Create_UnqualifiedProducerDate_PreservesSourceClockThroughWireAndPostgres(string date)
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Create, AccountingPermissions.Read]);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var created = await client.PostAsJsonAsync("/payments", Body(date));
        await fixture.AssertStatusAsync(created, HttpStatusCode.Created);
        Assert.NotNull(created.Headers.Location);
        using var read = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var wire = (await read.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = wire["Id"]!.GetValue<int>();
        Assert.True(id > 0);
        Assert.Equal(date, wire["PaymentDate"]!.GetValue<string>()[..19]);
        Assert.Equal(1234.56m, wire["Amount"]!.GetValue<decimal>());
        await using var database = fixture.Database();
        var stored = await database.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(id, stored.Id);
        Assert.NotNull(stored.PaymentDate);
        Assert.Equal(ExpectedClock(date).Ticks, stored.PaymentDate.GetValueOrDefault().Ticks);
        Assert.Equal(1234.56m, stored.Amount);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Create);
    }

    [Theory]
    [InlineData("2026-10-04T00:00:00")]
    [InlineData("2026-10-04T12:34:30")]
    public async Task Update_UnqualifiedProducerDate_PreservesSourceClockAndServerIdentity(string date)
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Create, AccountingPermissions.Read, AccountingPermissions.Update]);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var created = await client.PostAsJsonAsync("/payments", Body("2026-10-03T00:00:00Z"));
        await fixture.AssertStatusAsync(created, HttpStatusCode.Created);
        Assert.NotNull(created.Headers.Location);
        var original = (await client.GetFromJsonAsync<JsonObject>(created.Headers.Location))!;
        var id = original["Id"]!.GetValue<int>();
        Assert.NotNull(original["CreatedDate"]);
        var request = Body(date);
        request["Id"] = 999999;
        request["Amount"] = 2000.25m;
        using var updated = await client.PutAsJsonAsync($"/payments/{id}", request);
        await fixture.AssertStatusAsync(updated, HttpStatusCode.NoContent);
        var current = (await client.GetFromJsonAsync<JsonObject>($"/payments/{id}"))!;
        Assert.Equal(id, current["Id"]!.GetValue<int>());
        Assert.Equal(date, current["PaymentDate"]!.GetValue<string>()[..19]);
        Assert.Equal(original["CreatedDate"]!.GetValue<string>(), current["CreatedDate"]!.GetValue<string>());
        Assert.Equal(2000.25m, current["Amount"]!.GetValue<decimal>());
        await using var database = fixture.Database();
        var stored = await database.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(id, stored.Id);
        Assert.NotNull(stored.PaymentDate);
        Assert.Equal(ExpectedClock(date).Ticks, stored.PaymentDate.GetValueOrDefault().Ticks);
        Assert.Equal(2000.25m, stored.Amount);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Update);
    }

    private static DateTime ExpectedClock(string date) =>
        DateTime.ParseExact(date, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static JsonObject Body(string date) => new()
    {
        ["PaymentDirectionId"] = 100000,
        ["PaymentMethodId"] = 100000,
        ["PaymentTypeId"] = 100000,
        ["Description"] = "Producer date compatibility",
        ["Amount"] = 1234.56m,
        ["Recipient"] = "Synthetic recipient",
        ["TransactionNumber"] = "PRODUCER-DATE-FIXTURE",
        ["CurrencyId"] = 7,
        ["PaymentDate"] = date,
    };
}
