using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.AccountingService.Application.Models;
using Microsoft.EntityFrameworkCore;
using IntentFixture = Legacy.Maliev.AccountingService.Tests.InvoiceNotificationIntentAcceptanceTests.IntentFixture;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Actual Accounting middleware/controller/durable recovery with controlled Notification transport.</summary>
public sealed class InvoiceNotificationControllerRecoveryTests
{
    [Fact]
    public async Task DisabledRestart_RetainedOriginCannotSelectLegacyAdmissionOrSend()
    {
        await using var fixture = await IntentFixture.StartAsync();
        fixture.EnableNotificationV2 = true;
        var transport = new NotificationTransport();
        fixture.NotificationV2Transport = transport.SendAsync;
        using var created = await fixture.CreateAsync(delegation: fixture.Delegation());
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var effects = fixture.DownstreamCalls;
        fixture.EnableNotificationV2 = false;
        await fixture.RestartHostAsync();
        using var disabled = await fixture.CreateAsync(delegation: fixture.Delegation());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
        Assert.Equal(effects, fixture.DownstreamCalls);
        Assert.Equal(new[] { "PUT", "POST" }, transport.Methods);
        fixture.EnableNotificationV2 = true;
        await fixture.RestartHostAsync();
        using var replay = await fixture.CreateAsync(delegation: fixture.Delegation());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(effects, fixture.DownstreamCalls);
        Assert.Equal(new[] { "PUT", "POST" }, transport.Methods);
    }

    [Fact]
    public async Task AcceptedResult_UsesValueThreeAndBoundTerminalReplayHasNoNotificationRequests()
    {
        await using var fixture = await IntentFixture.StartAsync();
        fixture.EnableNotificationV2 = true;
        var transport = new NotificationTransport();
        fixture.NotificationV2Transport = transport.SendAsync;
        using var created = await fixture.CreateAsync(delegation: fixture.Delegation());
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var first = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(3, first.RootElement.GetProperty("EmailState").GetInt32());
        Assert.Equal("accepted-fixture", first.RootElement.GetProperty("ProviderMessageId").GetString());
        Assert.Equal(new[] { "PUT", "POST" }, transport.Methods);
        var effects = fixture.DownstreamCalls;
        using var replay = await fixture.CreateAsync(delegation: fixture.Delegation());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var second = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(3, second.RootElement.GetProperty("EmailState").GetInt32());
        Assert.Equal(first.RootElement.GetProperty("InvoiceId").GetInt32(), second.RootElement.GetProperty("InvoiceId").GetInt32());
        Assert.Equal(effects, fixture.DownstreamCalls);
        Assert.Equal(new[] { "PUT", "POST" }, transport.Methods);
        Assert.Equal(2, fixture.LiveChecks);
        await using var database = fixture.Database();
        Assert.Single(await database.Invoices.AsNoTracking().ToListAsync());
        Assert.Equal("Completed", (await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).State);
        Assert.Equal("ProviderAccepted", (await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync()).Phase);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("malformed")]
    [InlineData("admitted")]
    public async Task LostResponse_ReadOnlyRecoveryNeverRendersUploadsOrResubmits(string initialRead)
    {
        await using var fixture = await IntentFixture.StartAsync();
        fixture.EnableNotificationV2 = true;
        var transport = new NotificationTransport { LoseExecute = true, ReadMode = initialRead };
        fixture.NotificationV2Transport = transport.SendAsync;
        using var created = await fixture.CreateAsync(delegation: fixture.Delegation());
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var uncertain = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(2, uncertain.RootElement.GetProperty("EmailState").GetInt32());
        Assert.False(uncertain.RootElement.TryGetProperty("ProviderMessageId", out _));
        var financialCalls = fixture.DownstreamCalls - fixture.NotificationCalls;
        await using var database = fixture.Database();
        var intent = (await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync()).IntentId;
        Assert.Equal("NeedsReconciliation", (await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).State);
        transport.ReadMode = "accepted";
        using var recovered = await fixture.CreateAsync(delegation: fixture.Delegation());
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        using var accepted = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync());
        Assert.Equal(3, accepted.RootElement.GetProperty("EmailState").GetInt32());
        Assert.Equal(financialCalls, fixture.DownstreamCalls - fixture.NotificationCalls);
        Assert.Equal(new[] { "PUT", "POST", "GET", "GET" }, transport.Methods);
        Assert.Equal(1, transport.Executions);
        Assert.Equal(intent, (await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync()).IntentId);
        var before = fixture.NotificationCalls;
        using var replay = await fixture.CreateAsync(delegation: fixture.Delegation());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(before, fixture.NotificationCalls);
        Assert.Equal(3, fixture.LiveChecks);
    }

    [Fact]
    public async Task EnabledWithoutDelegation_RefusesBeforeFinancialOrNotificationEffects()
    {
        await using var fixture = await IntentFixture.StartAsync();
        fixture.EnableNotificationV2 = true;
        using var response = await fixture.CreateAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, fixture.NotificationCalls);
        Assert.Equal(0, fixture.DownstreamCalls);
        await using var database = fixture.Database();
        Assert.Empty(await database.Invoices.AsNoTracking().ToListAsync());
    }

    private sealed class NotificationTransport
    {
        public List<string> Methods { get; } = [];
        public int Executions { get; private set; }
        public bool LoseExecute { get; set; }
        public string ReadMode { get; set; } = "accepted";
        private string? workflow;
        private string? resource;
        private string? digest;

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("synthetic-workload-token", request.Headers.Authorization?.Parameter);
            Methods.Add(request.Method.Method);
            var intent = request.RequestUri!.AbsolutePath.Split('/')[4];
            if (request.Method == HttpMethod.Put)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
                var root = body.RootElement;
                Assert.Equal(new[] { "channel", "payloadDigest", "payloadDigestVersion", "purpose", "resourceId", "resourceType", "workflowOperationId" },
                    root.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
                workflow = root.GetProperty("workflowOperationId").GetString();
                resource = root.GetProperty("resourceId").GetString();
                digest = root.GetProperty("payloadDigest").GetString();
                Assert.Equal("Info", root.GetProperty("channel").GetString());
                return Receipt(intent, "admitted", 1);
            }

            if (request.Method == HttpMethod.Post)
            {
                Executions++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
                var root = body.RootElement;
                var attachment = Assert.Single(root.GetProperty("attachments").EnumerateArray());
                var snapshot = new InvoiceNotificationPayloadSnapshot(root.GetProperty("to").GetString()!, root.GetProperty("subject").GetString()!,
                    root.GetProperty("body").GetString()!, bcc: root.GetProperty("bcc").EnumerateArray().Select(value => value.GetString()!).ToArray(),
                    attachments: [new(attachment.GetProperty("fileName").GetString()!, attachment.GetProperty("contentType").GetString(),
                        attachment.GetProperty("content").GetBytesFromBase64())]);
                Assert.Equal(digest, snapshot.Digest());
                if (LoseExecute) throw new IOException("Synthetic caller response loss after provider acceptance.");
                return Receipt(intent, "providerAccepted", 3);
            }

            return ReadMode switch
            {
                "missing" => Json("{}", HttpStatusCode.NotFound),
                "denied" => Json("{}", HttpStatusCode.Forbidden),
                "malformed" => Json("{}"),
                "admitted" => Receipt(intent, "admitted", 1),
                _ => Receipt(intent, "providerAccepted", 3),
            };
        }

        private HttpResponseMessage Receipt(string intent, string state, int version) => Json(JsonSerializer.Serialize(new
        {
            intentId = intent,
            purpose = "invoice-issued",
            resourceType = "invoice",
            resourceId = resource,
            workflowOperationId = workflow,
            state,
            version,
            admittedAt = "2026-10-05T00:00:00Z",
            updatedAt = version == 1 ? "2026-10-05T00:00:00Z" : "2026-10-05T00:00:01Z",
            providerMessageId = state == "providerAccepted" ? "accepted-fixture" : null,
        }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }));

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
