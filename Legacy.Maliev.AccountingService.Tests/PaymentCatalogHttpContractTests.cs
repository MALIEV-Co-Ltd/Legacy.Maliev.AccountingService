using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaymentCatalogHttpContractTests(AccountingBoundaryHttpFixture fixture)
{
    private static JsonObject Body(string resource) => resource == "Accounts"
        ? new JsonObject { ["Bank"] = "ธนาคารทดสอบ", ["AccountNumber"] = "SYNTHETIC-ONLY", ["Branch"] = "Test", ["Swift"] = "TESTONLY" }
        : new JsonObject { ["Name"] = "รายการทดสอบ", ["Description"] = "Synthetic lookup" };

    [Theory]
    [InlineData("Accounts")]
    [InlineData("Directions")]
    [InlineData("Methods")]
    [InlineData("Types")]
    public async Task Catalog_RoundTripHasResolvableLocationAndPersistentWire(string resource)
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Create, AccountingPermissions.Read,
            AccountingPermissions.Update, AccountingPermissions.Delete]);
        var body = Body(resource);
        using var created = await client.PostAsJsonAsync($"/payments/{resource}", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var payload = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = payload["Id"]!.GetValue<int>();
        Assert.True(id > 0);
        Assert.NotNull(created.Headers.Location);
        if (resource == "Accounts")
        {
            // Arrange historical NULLs before any entity GET populates the real two-minute cache.
            await using var database = fixture.Database();
            await database.Accounts.Where(row => row.Id == id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Branch, (string)null!)
                    .SetProperty(row => row.Swift, (string)null!));
        }

        using var read = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var current = (await read.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(id, current["Id"]!.GetValue<int>());
        Assert.False(current.ContainsKey("Payment"));
        if (resource == "Accounts")
        {
            Assert.False(current.ContainsKey("Branch"));
            Assert.False(current.ContainsKey("Swift"));
        }

        var field = resource == "Accounts" ? "Bank" : "Name";
        body[field] = "Updated synthetic value";
        using var updated = await client.PutAsJsonAsync($"/payments/{resource}/{id}", body);
        await fixture.AssertStatusAsync(updated, HttpStatusCode.NoContent);
        using var readback = await client.GetAsync($"/payments/{resource}/{id}");
        Assert.Equal(HttpStatusCode.OK, readback.StatusCode);
        Assert.Equal("Updated synthetic value", (await readback.Content.ReadFromJsonAsync<JsonObject>())![field]!.GetValue<string>());
        using var list = await client.GetAsync($"/payments/{resource}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains((await list.Content.ReadFromJsonAsync<JsonArray>())!, row => row!["Id"]!.GetValue<int>() == id);
        using var deleted = await client.DeleteAsync($"/payments/{resource}/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var absent = await client.GetAsync($"/payments/{resource}/{id}");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Create);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Update);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Delete);
    }

    [Theory]
    [InlineData("Accounts")]
    [InlineData("Directions")]
    [InlineData("Methods")]
    [InlineData("Types")]
    public async Task Catalog_MissingReadUpdateDeleteReturn404(string resource)
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Read, AccountingPermissions.Update, AccountingPermissions.Delete]);
        using var read = await client.GetAsync($"/payments/{resource}/2147483647");
        using var update = await client.PutAsJsonAsync($"/payments/{resource}/2147483647", Body(resource));
        using var delete = await client.DeleteAsync($"/payments/{resource}/2147483647");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Theory]
    [InlineData("Accounts")]
    [InlineData("Directions")]
    [InlineData("Methods")]
    [InlineData("Types")]
    public async Task Catalog_AnonymousReadAndCreateAre401(string resource)
    {
        await fixture.ResetAsync();
        using var client = fixture.Client();
        using var read = await client.GetAsync($"/payments/{resource}");
        using var create = await client.PostAsJsonAsync($"/payments/{resource}", Body(resource));
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Empty(fixture.LiveChecks);
    }

    [Theory]
    [InlineData("Accounts")]
    [InlineData("Directions")]
    [InlineData("Methods")]
    [InlineData("Types")]
    public async Task Catalog_LiveDeniedCreateIs403AndDoesNotPersist(string resource)
    {
        await fixture.ResetAsync();
        using var readClient = fixture.Client([AccountingPermissions.Read]);
        using var before = await readClient.GetAsync($"/payments/{resource}");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var baseline = (await before.Content.ReadFromJsonAsync<JsonArray>())!.ToJsonString();
        var sqlBaseline = await SqlSnapshotAsync(resource);
        using var denied = fixture.Client([AccountingPermissions.Create], allowLive: false);
        using var create = await denied.PostAsJsonAsync($"/payments/{resource}", Body(resource));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        using var after = await readClient.GetAsync($"/payments/{resource}");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(baseline, (await after.Content.ReadFromJsonAsync<JsonArray>())!.ToJsonString());
        Assert.Equal(sqlBaseline, await SqlSnapshotAsync(resource));
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Create);
    }

    [Theory]
    [InlineData("Accounts")]
    [InlineData("Directions")]
    [InlineData("Methods")]
    [InlineData("Types")]
    public async Task Catalog_InvalidJsonIs400AndDoesNotPersist(string resource)
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Create, AccountingPermissions.Read]);
        using var before = await client.GetAsync($"/payments/{resource}");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var baseline = (await before.Content.ReadFromJsonAsync<JsonArray>())!.ToJsonString();
        var sqlBaseline = await SqlSnapshotAsync(resource);
        using var malformed = new StringContent("{", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"/payments/{resource}", malformed);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var after = await client.GetAsync($"/payments/{resource}");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(baseline, (await after.Content.ReadFromJsonAsync<JsonArray>())!.ToJsonString());
        Assert.Equal(sqlBaseline, await SqlSnapshotAsync(resource));
    }

    private async Task<string> SqlSnapshotAsync(string resource)
    {
        await using var database = fixture.Database();
        // Independent fresh SQL materialization, never an API/cache read proving its own no-effect result.
        return resource switch
        {
            "Accounts" => JsonSerializer.Serialize(await database.Accounts.AsNoTracking().OrderBy(row => row.Id).ToListAsync()),
            "Directions" => JsonSerializer.Serialize(await database.Directions.AsNoTracking().OrderBy(row => row.Id).ToListAsync()),
            "Methods" => JsonSerializer.Serialize(await database.Methods.AsNoTracking().OrderBy(row => row.Id).ToListAsync()),
            "Types" => JsonSerializer.Serialize(await database.Types.AsNoTracking().OrderBy(row => row.Id).ToListAsync()),
            _ => throw new ArgumentException("Unsupported catalog snapshot.", nameof(resource)),
        };
    }
}
