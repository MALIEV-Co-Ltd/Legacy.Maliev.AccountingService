using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaymentOffsetDateHttpTests(AccountingBoundaryHttpFixture fixture)
{
    [Theory]
    [InlineData(false, "2026-10-03T02:30:00+07:00", "2026-10-02T19:30:00Z")]
    [InlineData(true, "2026-10-03T02:30:00+07:00", "2026-10-02T19:30:00Z")]
    [InlineData(false, "2026-10-03T22:30:00-05:00", "2026-10-04T03:30:00Z")]
    [InlineData(true, "2026-10-03T22:30:00-05:00", "2026-10-04T03:30:00Z")]
    [InlineData(false, "2026-10-03T12:30:00+00:00", "2026-10-03T12:30:00Z")]
    [InlineData(true, "2026-10-03T12:30:00+00:00", "2026-10-03T12:30:00Z")]
    public async Task ExplicitOffsetPostAndPutPreserveIndependentUtcInstantAndServerIdentity(bool update, string input, string expected)
    {
        await fixture.ResetAsync();
        using var client = Writer();
        int? id = update ? await SeedAsync(client) : null;
        var original = id.HasValue ? await client.GetFromJsonAsync<JsonObject>($"/payments/{id}") : null;
        var before = await fixture.ReceiptSnapshotAsync();
        var body = Body(JsonValue.Create(input));
        body["Id"] = 999999;
        var parsed = JsonSerializer.Deserialize<Legacy.Maliev.AccountingService.Domain.Payment.Payment>(body.ToJsonString())!;
        Assert.NotNull(parsed.PaymentDate);
        var parsedDate = parsed.PaymentDate.GetValueOrDefault();
        Assert.Equal(DateTimeKind.Local, parsedDate.Kind);
        Assert.Equal(DateTime.ParseExact(expected, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), parsedDate.ToUniversalTime());
        using var response = update
            ? await client.PutAsJsonAsync($"/payments/{id}", body)
            : await client.PostAsJsonAsync("/payments", body);
        await fixture.AssertStatusAsync(response, update ? HttpStatusCode.NoContent : HttpStatusCode.Created);
        var wire = (await client.GetFromJsonAsync<JsonObject>(update ? $"/payments/{id}" : response.Headers.Location!.ToString()))!;
        int storedId = wire["Id"]!.GetValue<int>();
        Assert.NotEqual(999999, storedId);
        if (update)
        {
            Assert.Equal(id, storedId);
            Assert.Equal(original!["CreatedDate"]!.GetValue<string>(), wire["CreatedDate"]!.GetValue<string>());
        }
        Assert.Equal(expected, wire["PaymentDate"]!.GetValue<string>());
        Assert.Equal(1234.56m, wire["Amount"]!.GetValue<decimal>());
        Assert.Equal(7, wire["CurrencyId"]!.GetValue<int>());
        await using var database = fixture.Database();
        var stored = await database.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(storedId, stored.Id);
        Assert.Equal(DateTime.ParseExact(expected, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), stored.PaymentDate);
        Assert.Equal(DateTimeKind.Utc, stored.PaymentDate!.Value.Kind);
        Assert.Equal(1234.56m, stored.Amount);
        Assert.Equal(DateTimeKind.Unspecified, stored.CreatedDate!.Value.Kind);
        Assert.Equal(DateTimeKind.Unspecified, stored.ModifiedDate!.Value.Kind);
        var after = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(before.Invoice, after.Invoice);
        Assert.Equal(before.Receipt, after.Receipt);
        Assert.Equal(before.Journal, after.Journal);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == (update ? AccountingPermissions.Update : AccountingPermissions.Create));
        Assert.Empty(fixture.FailureMetadata);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullablePaymentDatePostAndPutRemainNullWithExactWireOmission(bool update)
    {
        await fixture.ResetAsync();
        using var client = Writer();
        int? id = update ? await SeedAsync(client) : null;
        var before = await fixture.ReceiptSnapshotAsync();
        using var response = update
            ? await client.PutAsJsonAsync($"/payments/{id}", Body(null))
            : await client.PostAsJsonAsync("/payments", Body(null));
        await fixture.AssertStatusAsync(response, update ? HttpStatusCode.NoContent : HttpStatusCode.Created);
        var wire = (await client.GetFromJsonAsync<JsonObject>(update ? $"/payments/{id}" : response.Headers.Location!.ToString()))!;
        Assert.False(wire.ContainsKey("PaymentDate"));
        Assert.Equal(1234.56m, wire["Amount"]!.GetValue<decimal>());
        await using var database = fixture.Database();
        Assert.Null((await database.Payments.AsNoTracking().SingleAsync()).PaymentDate);
        var after = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(before.Invoice, after.Invoice);
        Assert.Equal(before.Receipt, after.Receipt);
        Assert.Equal(before.Journal, after.Journal);
        Assert.Empty(fixture.FailureMetadata);
    }

    [Theory]
    [InlineData(false, "\"not-a-date\"")]
    [InlineData(true, "\"not-a-date\"")]
    [InlineData(false, "\"2026-02-30T12:00:00+07:00\"")]
    [InlineData(true, "\"2026-02-30T12:00:00+07:00\"")]
    [InlineData(false, "123")]
    [InlineData(true, "123")]
    public async Task InvalidDateRepresentationsAreModelBinding400WithoutPhysicalMutation(bool update, string valueJson)
    {
        await fixture.ResetAsync();
        using var client = Writer();
        int? id = update ? await SeedAsync(client) : null;
        var before = await fixture.ReceiptSnapshotAsync();
        using var response = update
            ? await client.PutAsJsonAsync($"/payments/{id}", Body(JsonNode.Parse(valueJson)))
            : await client.PostAsJsonAsync("/payments", Body(JsonNode.Parse(valueJson)));
        await fixture.AssertStatusAsync(response, HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(400, problem["status"]!.GetValue<int>());
        Assert.True(problem["errors"]!.AsObject().Any(pair => pair.Key.Contains("PaymentDate", StringComparison.Ordinal)));
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Empty(fixture.FailureMetadata);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ExplicitOffsetWritesStillRequireAuthenticationAndLivePermission(bool update, bool authenticated)
    {
        await fixture.ResetAsync();
        using var seedClient = Writer();
        int? id = update ? await SeedAsync(seedClient) : null;
        var before = await fixture.ReceiptSnapshotAsync();
        using var client = fixture.Client(authenticated ? [AccountingPermissions.Create, AccountingPermissions.Update] : null, allowLive: false);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = update
            ? await client.PutAsJsonAsync($"/payments/{id}", Body(JsonValue.Create("2026-10-03T02:30:00+07:00")))
            : await client.PostAsJsonAsync("/payments", Body(JsonValue.Create("2026-10-03T02:30:00+07:00")));
        Assert.Equal(authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Empty(fixture.FailureMetadata);
    }

    [Fact]
    public async Task ExplicitOffsetUpdateStillRejectsStaleConcurrencyWithoutPhysicalMutation()
    {
        await fixture.ResetAsync();
        using var client = Writer();
        int id = await SeedAsync(client);
        var before = await fixture.ReceiptSnapshotAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/payments/{id}")
        {
            Content = JsonContent.Create(Body(JsonValue.Create("2026-10-03T02:30:00+07:00"))),
        };
        request.Headers.TryAddWithoutValidation("If-Unmodified-Since", "2000-01-01T00:00:00Z");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Empty(fixture.FailureMetadata);
    }

    private HttpClient Writer()
    {
        var client = fixture.Client([AccountingPermissions.Create, AccountingPermissions.Read, AccountingPermissions.Update]);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return client;
    }

    private async Task<int> SeedAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/payments", Body(JsonValue.Create("2026-10-01T12:00:00Z")));
        await fixture.AssertStatusAsync(response, HttpStatusCode.Created);
        return (await client.GetFromJsonAsync<JsonObject>(response.Headers.Location!))!["Id"]!.GetValue<int>();
    }

    private static JsonObject Body(JsonNode? date) => new()
    {
        ["PaymentDirectionId"] = 100000,
        ["PaymentMethodId"] = 100000,
        ["PaymentTypeId"] = 100000,
        ["Amount"] = 1234.56m,
        ["CurrencyId"] = 7,
        ["Description"] = "Synthetic offset boundary",
        ["Recipient"] = "Synthetic",
        ["TransactionNumber"] = "OFFSET-BOUNDARY",
        ["PaymentDate"] = date,
    };
}
