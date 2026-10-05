using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaymentRecordHttpBoundaryTests(AccountingBoundaryHttpFixture fixture)
{
    private static JsonObject Body() => new()
    {
        ["PaymentDirectionId"] = 100000,
        ["PaymentMethodId"] = 100000,
        ["PaymentTypeId"] = 100000,
        ["Description"] = "ค่าใช้จ่ายทดสอบ",
        ["Amount"] = 1234.56m,
        ["Recipient"] = "Synthetic recipient",
        ["TransactionNumber"] = "SYNTHETIC-TX",
        ["CurrencyId"] = 7,
        ["PaymentDate"] = "2026-10-01T12:00:00Z",
    };

    private async Task<int> CreateAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/payments", Body());
        await fixture.AssertStatusAsync(response, HttpStatusCode.Created);
        Assert.NotNull(response.Headers.Location);
        using var read = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var wire = (await read.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(1234.56m, wire["Amount"]!.GetValue<decimal>());
        Assert.Equal("ค่าใช้จ่ายทดสอบ", wire["Description"]!.GetValue<string>());
        Assert.False(wire.ContainsKey("PaymentDirection"));
        Assert.False(wire.ContainsKey("PaymentMethod"));
        Assert.False(wire.ContainsKey("PaymentType"));
        Assert.False(wire.ContainsKey("PaymentFile"));
        return wire["Id"]!.GetValue<int>();
    }

    [Fact]
    public async Task Payment_RoundTripPreservesMoneyAndServerIdentity()
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Create, AccountingPermissions.Read,
            AccountingPermissions.Update, AccountingPermissions.Delete]);
        var id = await CreateAsync(client);
        var body = Body();
        body["Id"] = 999;
        body["Amount"] = 2000.25m;
        using var update = await client.PutAsJsonAsync($"/payments/{id}", body);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        using var read = await client.GetAsync($"/payments/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var wire = (await read.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(id, wire["Id"]!.GetValue<int>());
        Assert.Equal(2000.25m, wire["Amount"]!.GetValue<decimal>());
        await using var database = fixture.Database();
        Assert.Equal(2000.25m, (await database.Payments.AsNoTracking().SingleAsync(row => row.Id == id)).Amount);
        using var delete = await client.DeleteAsync($"/payments/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var missing = await client.GetAsync($"/payments/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Payment_StaleConcurrencyHeaderReturns409WithoutFinancialMutation()
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Create, AccountingPermissions.Read, AccountingPermissions.Update]);
        var id = await CreateAsync(client);
        var body = Body();
        body["Amount"] = 999999m;
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/payments/{id}") { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Unmodified-Since", "2000-01-01T00:00:00.0000000Z");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var read = await client.GetAsync($"/payments/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(1234.56m, (await read.Content.ReadFromJsonAsync<JsonObject>())!["Amount"]!.GetValue<decimal>());
        await using var database = fixture.Database();
        Assert.Equal(1234.56m, (await database.Payments.AsNoTracking().SingleAsync()).Amount);
    }

    [Fact]
    public async Task Payment_MissingReadUpdateDeleteAre404()
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Read, AccountingPermissions.Update, AccountingPermissions.Delete]);
        using var read = await client.GetAsync("/payments/2147483647");
        using var update = await client.PutAsJsonAsync("/payments/2147483647", Body());
        using var delete = await client.DeleteAsync("/payments/2147483647");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Payment_MalformedBodyIs400WithoutRows()
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Create]);
        using var response = await client.PostAsync("/payments", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var database = fixture.Database();
        Assert.Empty(await database.Payments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Payment_AnonymousReadCreateDeleteAre401WithoutRows()
    {
        await fixture.ResetAsync();
        using var client = fixture.Client();
        using var read = await client.GetAsync("/payments/1");
        using var create = await client.PostAsJsonAsync("/payments", Body());
        using var delete = await client.DeleteAsync("/payments/1");
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
        Assert.Empty(fixture.LiveChecks);
        await using var database = fixture.Database();
        Assert.Empty(await database.Payments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Payment_LiveDeniedCreateIs403WithoutRows()
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Create], allowLive: false);
        using var response = await client.PostAsJsonAsync("/payments", Body());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Create);
        await using var database = fixture.Database();
        Assert.Empty(await database.Payments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PaymentFile_MetadataRoundTripUsesScalarOwnerAndNoObjectDownload()
    {
        await fixture.ResetAsync();
        using var client = fixture.Client([AccountingPermissions.Create, AccountingPermissions.Read,
            AccountingPermissions.FilesWrite, AccountingPermissions.FilesRead, AccountingPermissions.FilesDelete]);
        var paymentId = await CreateAsync(client);
        using var created = await client.PostAsJsonAsync("/payments/files", new { PaymentId = paymentId, Bucket = "synthetic-only", ObjectName = "proof/ใบเสร็จ.pdf" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        using var read = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var wire = (await read.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(paymentId, wire["PaymentId"]!.GetValue<int>());
        Assert.Equal("synthetic-only", wire["Bucket"]!.GetValue<string>());
        Assert.Equal("proof/ใบเสร็จ.pdf", wire["ObjectName"]!.GetValue<string>());
        Assert.False(wire.ContainsKey("Payment"));
        using var list = await client.GetAsync($"/payments/{paymentId}/files");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(wire["Id"]!.GetValue<int>(), Assert.Single((await list.Content.ReadFromJsonAsync<JsonArray>())!)!["Id"]!.GetValue<int>());
        using var deleted = await client.DeleteAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await using var database = fixture.Database();
        Assert.Empty(await database.Files.AsNoTracking().ToListAsync());
        Assert.Single(await database.Payments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PaymentFile_AccountingReadDoesNotGrantFileReadOrWrite()
    {
        await fixture.ResetAsync();
        using var owner = fixture.Client([AccountingPermissions.Create, AccountingPermissions.Read]);
        var id = await CreateAsync(owner);
        using var readOnly = fixture.Client([AccountingPermissions.Read]);
        using var list = await readOnly.GetAsync($"/payments/{id}/files");
        using var create = await readOnly.PostAsJsonAsync("/payments/files", new { PaymentId = id, Bucket = "synthetic-only", ObjectName = "proof.pdf" });
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        using var fileReader = fixture.Client([AccountingPermissions.FilesRead]);
        using var allowed = await fileReader.GetAsync($"/payments/{id}/files");
        Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.FilesRead);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.FilesWrite);
        await using var database = fixture.Database();
        Assert.Empty(await database.Files.AsNoTracking().ToListAsync());
    }
}
