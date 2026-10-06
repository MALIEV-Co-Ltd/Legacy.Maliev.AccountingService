using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

// Off-repository draft: normal Program HTTP, controlled signed workload/live IAM, owned PostgreSQL/Redis.
// Requires the model/migration bundle; no native execution or acceptance is represented here.
[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaymentFileMetadataHttpTests(AccountingBoundaryHttpFixture fixture)
{
    private const int ParentId = 91;
    private const string ObjectName = " folder/ใบเสร็จ sample.pdf ";

    [Theory]
    [InlineData("bmp")]
    [InlineData("supplementary")]
    [InlineData("mixed")]
    public async Task Source50UnitLiteralCreateReadAndReplayKeepsOneOwnedRow(string variant)
    {
        await SeedAsync();
        var bucket = Bucket(variant, true);
        Assert.Equal(50, bucket.Length);
        using var client = await ClientAsync();
        using var created = await PostAsync(client, bucket);
        await fixture.AssertStatusAsync(created, HttpStatusCode.Created);
        var createdBody = await created.Content.ReadAsStringAsync();
        var wire = JsonNode.Parse(createdBody) as JsonObject;
        Assert.NotNull(wire);
        Assert.Equal(bucket, wire["Bucket"]!.GetValue<string>());
        Assert.Equal(ObjectName, wire["ObjectName"]!.GetValue<string>());
        var id = wire["Id"]!.GetValue<int>();
        using var read = await client.GetAsync(FilePath(id));
        await fixture.AssertStatusAsync(read, HttpStatusCode.OK);
        var readBody = await read.Content.ReadAsStringAsync();
        var readWire = JsonNode.Parse(readBody)!.AsObject();
        Assert.Equal(id, readWire["Id"]!.GetValue<int>());
        Assert.Equal(bucket, readWire["Bucket"]!.GetValue<string>());
        Assert.Equal(ObjectName, readWire["ObjectName"]!.GetValue<string>());
        var before = await fixture.ReceiptSnapshotAsync();
        var redis = await fixture.ReceiptRedisSnapshotAsync();
        using var replay = await PostAsync(client, bucket);
        await fixture.AssertStatusAsync(replay, HttpStatusCode.Created);
        Assert.Equal(createdBody, await replay.Content.ReadAsStringAsync());
        using var readAfterReplay = await client.GetAsync(FilePath(id));
        await fixture.AssertStatusAsync(readAfterReplay, HttpStatusCode.OK);
        Assert.Equal(readBody, await readAfterReplay.Content.ReadAsStringAsync());
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(redis.OrderBy(pair => pair.Key, StringComparer.Ordinal), (await fixture.ReceiptRedisSnapshotAsync()).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData("bmp")]
    [InlineData("supplementary")]
    public async Task OversizedCreateKeepsPinnedOpaqueProviderFailureAndCannotSeedSuccessMemo(string variant)
    {
        await SeedAsync();
        using var client = await ClientAsync();
        var before = await fixture.ReceiptSnapshotAsync();
        var redis = await fixture.ReceiptRedisSnapshotAsync();
        using var failed = await PostAsync(client, Bucket(variant, false));
        await AssertOpaqueProviderFailureAsync(failed);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(redis.OrderBy(pair => pair.Key, StringComparer.Ordinal), (await fixture.ReceiptRedisSnapshotAsync()).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        // The same operation key remains free to record one actual valid result after definite rejection.
        using var valid = await PostAsync(client, " bucket ");
        await fixture.AssertStatusAsync(valid, HttpStatusCode.Created);
        var validBody = await valid.Content.ReadAsStringAsync();
        var result = JsonNode.Parse(validBody) as JsonObject;
        Assert.Equal(" bucket ", result!["Bucket"]!.GetValue<string>());
        using var repeated = await PostAsync(client, " bucket ");
        await fixture.AssertStatusAsync(repeated, HttpStatusCode.Created);
        Assert.Equal(validBody, await repeated.Content.ReadAsStringAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData("null-bucket")]
    [InlineData("null-object")]
    [InlineData("oversized")]
    public async Task RejectedFullPutPreservesRowsParentAndPrimedFileCache(string field)
    {
        await SeedAsync();
        using var client = await ClientAsync();
        using var created = await PostAsync(client, " bucket ");
        await fixture.AssertStatusAsync(created, HttpStatusCode.Created);
        var original = await created.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(original);
        var id = original["Id"]!.GetValue<int>();
        using var read = await client.GetAsync(FilePath(id));
        await fixture.AssertStatusAsync(read, HttpStatusCode.OK);
        var readBody = await read.Content.ReadAsStringAsync();
        var supplied = (JsonObject)original.DeepClone();
        if (field == "null-bucket") supplied["Bucket"] = null;
        else if (field == "null-object") supplied["ObjectName"] = null;
        else supplied["Bucket"] = Bucket("supplementary", false);
        var before = await fixture.ReceiptSnapshotAsync();
        var redis = await fixture.ReceiptRedisSnapshotAsync();
        using var response = await client.PutAsJsonAsync(FilePath(id), supplied);
        await AssertOpaqueProviderFailureAsync(response);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(redis.OrderBy(pair => pair.Key, StringComparer.Ordinal), (await fixture.ReceiptRedisSnapshotAsync()).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        using var unchanged = await client.GetAsync(FilePath(id));
        await fixture.AssertStatusAsync(unchanged, HttpStatusCode.OK);
        Assert.Equal(readBody, await unchanged.Content.ReadAsStringAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    private async Task AssertOpaqueProviderFailureAsync(HttpResponseMessage response)
    {
        // Pinned7ed ExceptionHandlingMiddleware keeps nonunique DbUpdate/Postgres failures at500.
        // This cohort preserves that existing category; changing public error policy needs its own scope.
        await fixture.AssertStatusAsync(response, HttpStatusCode.InternalServerError);
        var envelope = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(envelope);
        Assert.Equal(["details", "error", "statusCode", "traceId"], envelope.Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Null(envelope["details"]);
        Assert.Equal(500, envelope["statusCode"]!.GetValue<int>());
        Assert.Equal("An internal server error occurred", envelope["error"]!.GetValue<string>());
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = await fixture.ReceiptClientAsync([AccountingPermissions.FilesWrite, AccountingPermissions.FilesRead]);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return client;
    }

    private async Task SeedAsync()
    {
        await fixture.ResetAsync();
        await using var payment = fixture.Database();
        payment.Payments.Add(new Payment
        {
            Id = ParentId,
            PaymentDirectionId = 100000,
            PaymentMethodId = 100000,
            PaymentTypeId = 100000,
            Amount = 107m,
            EmployeeId = 42,
            CurrencyId = 42,
        });
        await payment.SaveChangesAsync();
    }

    private static string Bucket(string variant, bool valid) => variant switch
    {
        "bmp" => new string('ก', valid ? 50 : 51),
        "supplementary" => string.Concat(Enumerable.Repeat("😀", valid ? 25 : 26)),
        "mixed" => string.Concat(Enumerable.Repeat("😀", 24)) + " ก",
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };
    private static string FilePath(int id) => $"/payments/files/{id}";
    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string bucket) => client.PostAsJsonAsync("/payments/files",
        new { PaymentId = ParentId, Bucket = bucket, ObjectName });
}
