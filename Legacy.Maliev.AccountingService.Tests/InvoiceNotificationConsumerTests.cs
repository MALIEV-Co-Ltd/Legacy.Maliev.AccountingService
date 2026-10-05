using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Real durable Accounting authority with controlled transport; real producer join is separately required.</summary>
public sealed class InvoiceNotificationConsumerTests(InvoiceNotificationPhaseFencePostgresFixture fixture)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    [Fact]
    public async Task LostExecuteResponse_ReconcilesSameIntentAndTerminalReplayHasNoTransport()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var scenario = await Scenario.CreateAsync(database);
        scenario.Transport.LoseExecute = true;
        var delivery = await scenario.SendAsync();
        Assert.True(delivery.ProviderAccepted);
        Assert.Equal("fixture-provider-acceptance", delivery.ProviderMessageId);
        Assert.Equal(new[] { "PUT", "POST", "GET" }, scenario.Transport.Methods);
        Assert.Equal(1, scenario.Transport.Executions);
        Assert.Single(scenario.Transport.Intents.Distinct());
        await scenario.Admissions.MarkUncertainAsync(scenario.Operation, CancellationToken.None);
        var reconciled = await scenario.Workflow.ReconcileAsync(84, scenario.Operation, scenario.Origin, CancellationToken.None);
        Assert.Equal(InvoiceCreationEmailState.ProviderAccepted, reconciled.EmailState);
        var before = scenario.Transport.Methods.Count;
        var replay = await scenario.Admissions.AdmitAsync(scenario.Operation, 84, scenario.Origin, new string('A', 64), CancellationToken.None);
        Assert.False(replay.IsNew);
        Assert.Equal(reconciled, replay.Completed);
        Assert.Equal(before, scenario.Transport.Methods.Count);
        Assert.Equal(reconciled, await scenario.Journal.GetAsync("create:84", scenario.Operation, CancellationToken.None));
    }

    [Theory]
    [InlineData("admitted")]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("malformed")]
    [InlineData("unavailable")]
    public async Task UncertainRead_NeverReissuesExecuteOrReplacesIntent(string mode)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var scenario = await Scenario.CreateAsync(database);
        scenario.Transport.LoseExecute = true;
        scenario.Transport.ReadMode = mode;
        Assert.False((await scenario.SendAsync()).ProviderAccepted);
        await scenario.Admissions.MarkUncertainAsync(scenario.Operation, CancellationToken.None);
        var retained = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        var result = await scenario.Workflow.ReconcileAsync(84, scenario.Operation, scenario.Origin, CancellationToken.None);
        Assert.Equal(InvoiceCreationEmailState.ExplicitRetryRequired, result.EmailState);
        Assert.Null(result.ProviderMessageId);
        Assert.Equal(1, scenario.Transport.Executions);
        Assert.Equal(retained.IntentId, (await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync()).IntentId);
        Assert.Equal(new[] { "PUT", "POST", "GET", "GET" }, scenario.Transport.Methods);
        Assert.Null(await scenario.Journal.GetAsync("create:84", scenario.Operation, CancellationToken.None));
    }

    [Fact]
    public async Task RecoveryAfterUnavailableRead_UsesOnlyGetAndOwnFinancialEvidence()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var scenario = await Scenario.CreateAsync(database);
        scenario.Transport.LoseExecute = true;
        scenario.Transport.ReadMode = "denied";
        Assert.False((await scenario.SendAsync()).ProviderAccepted);
        await scenario.Admissions.MarkUncertainAsync(scenario.Operation, CancellationToken.None);
        scenario.Transport.ReadMode = "accepted";
        var result = await scenario.Workflow.ReconcileAsync(84, scenario.Operation, scenario.Origin, CancellationToken.None);
        Assert.Equal(InvoiceCreationEmailState.ProviderAccepted, result.EmailState);
        Assert.Equal(scenario.Invoice.Id, result.InvoiceId);
        Assert.Equal(new InvoiceCreationStoredFile("fixture-bucket", "invoice.pdf"), result.StoredFile);
        Assert.Equal(1, scenario.Transport.Executions);
        Assert.Equal(new[] { "PUT", "POST", "GET", "GET" }, scenario.Transport.Methods);
    }

    [Fact]
    public async Task Restart_DoesNotRebuildOrSendChangedDocumentAndContacts()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var scenario = await Scenario.CreateAsync(database);
        Assert.True((await scenario.SendAsync()).ProviderAccepted);
        var result = await scenario.Workflow.SendAsync(84, scenario.Operation, scenario.Origin,
            "changed@example.invalid", "changed", scenario.Invoice, [99], CancellationToken.None);
        Assert.True(result.ProviderAccepted);
        Assert.Equal(new[] { "PUT", "POST", "GET" }, scenario.Transport.Methods);
        Assert.Equal(1, scenario.Transport.Executions);
    }

    [Fact]
    public async Task DisabledAfterRetainedFence_AndDifferentOrigin_CannotSendOrFallback()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var scenario = await Scenario.CreateAsync(database);
        Assert.True((await scenario.SendAsync()).ProviderAccepted);
        Assert.True(await scenario.Workflow.HasFenceAsync(scenario.Invoice.Id, CancellationToken.None));
        var disabled = scenario.CreateWorkflow(false);
        await Assert.ThrowsAsync<InvoiceCreationUnavailableException>(() => disabled.ReconcileAsync(84, scenario.Operation, scenario.Origin, CancellationToken.None));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => scenario.Workflow.ReconcileAsync(84, scenario.Operation,
            scenario.Origin with { Issuer = "https://other.example.invalid" }, CancellationToken.None));
        Assert.Equal(new[] { "PUT", "POST" }, scenario.Transport.Methods);
    }

    private sealed class Scenario(InvoiceDbContext database, Invoice invoice, Guid operation, InvoiceNotificationOrigin origin)
    {
        public Invoice Invoice { get; } = invoice;
        public Guid Operation { get; } = operation;
        public InvoiceNotificationOrigin Origin { get; } = origin;
        public Transport Transport { get; } = new();
        public Journal Journal { get; } = new();
        public InvoiceCreationAdmissionStore Admissions { get; } = new(database);
        public InvoiceNotificationWorkflow Workflow => CreateWorkflow(true);

        public InvoiceNotificationWorkflow CreateWorkflow(bool enabled)
        {
            var phases = new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new Keys());
            var options = new InvoiceNotificationIntentOptions(enabled, Origin.Issuer, "service:legacy-accounting");
            var client = new InvoiceNotificationIntentClient(new HttpClient(Transport) { BaseAddress = new("https://notification.example.invalid") }, options, phases);
            return new(database, Admissions, Journal, options, phases, phases, client);
        }

        public Task<InvoiceNotificationDeliveryResult> SendAsync() => Workflow.SendAsync(84, Operation, Origin,
            "recipient@example.invalid", "ลูกค้า", Invoice, [1, 2, 3], CancellationToken.None);

        public static async Task<Scenario> CreateAsync(InvoiceDbContext database)
        {
            var invoice = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
                new Invoice { Number = "INV-consumer", CustomerId = 42, Total = 107m }, [], CancellationToken.None);
            await new InvoiceCreationStore(database, TimeProvider.System).LinkFileAsync(invoice.Id, "fixture-bucket", "invoice.pdf", CancellationToken.None);
            var scenario = new Scenario(database, invoice, Guid.NewGuid(), new("https://auth.example.invalid", "employee:42", "service:legacy-intranet"));
            _ = await scenario.Admissions.AdmitAsync(scenario.Operation, 84, scenario.Origin, new string('A', 64), CancellationToken.None);
            await scenario.Workflow.PrepareFinancialAsync(84, scenario.Operation, scenario.Origin,
                new(invoice.Id, InvoiceCreationState.Completed, InvoiceCreationEmailState.NotRequested, null, new("fixture-bucket", "invoice.pdf")), CancellationToken.None);
            return scenario;
        }
    }

    private sealed class Keys : IInvoiceNotificationBindingKeyring
    {
        public string ActiveKeyId => "consumer-fixture";
        public ReadOnlyMemory<byte>? Find(string keyId) => keyId == ActiveKeyId ? Enumerable.Range(0, 32).Select(value => (byte)value).ToArray() : null;
    }

    private sealed class Journal : IInvoiceCreationJournal
    {
        private readonly Dictionary<(string, Guid), InvoiceCreationResult> results = [];
        public Task<InvoiceCreationResult?> GetAsync(string scope, Guid operationId, CancellationToken cancellationToken) => Task.FromResult(results.GetValueOrDefault((scope, operationId)));
        public Task SetAsync(string scope, Guid operationId, InvoiceCreationResult result, CancellationToken cancellationToken)
        { results[(scope, operationId)] = result; return Task.CompletedTask; }
    }

    private sealed class Transport : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public List<string> Intents { get; } = [];
        public int Executions { get; private set; }
        public bool LoseExecute { get; set; }
        public string ReadMode { get; set; } = "accepted";
        private string? workflow;
        private string? resource;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method.Method);
            var intent = request.RequestUri!.AbsolutePath.Split('/')[4];
            Intents.Add(intent);
            if (request.Method == HttpMethod.Put)
            {
                using var admission = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
                workflow = admission.RootElement.GetProperty("workflowOperationId").GetString();
                resource = admission.RootElement.GetProperty("resourceId").GetString();
                return Response(intent, "admitted", 1);
            }

            if (request.Method == HttpMethod.Post)
            {
                Executions++;
                if (LoseExecute) throw new IOException("Synthetic caller response loss after provider acceptance.");
                return Response(intent, "providerAccepted", 3);
            }

            return ReadMode switch
            {
                "missing" => new(HttpStatusCode.NotFound) { Content = new StringContent("{}") },
                "denied" => new(HttpStatusCode.Forbidden) { Content = new StringContent("{}") },
                "unavailable" => new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") },
                "malformed" => new(HttpStatusCode.OK) { Content = new StringContent("{}") },
                "admitted" => Response(intent, "admitted", 1),
                _ => Response(intent, "providerAccepted", 3),
            };
        }

        private HttpResponseMessage Response(string intent, string state, long version) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                intentId = intent, purpose = "invoice-issued", resourceType = "invoice", resourceId = resource,
                workflowOperationId = workflow, state, version, admittedAt = "2026-10-05T00:00:00Z",
                updatedAt = version == 1 ? "2026-10-05T00:00:00Z" : "2026-10-05T00:00:01Z",
                providerMessageId = state == "providerAccepted" ? "fixture-provider-acceptance" : null,
            }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }), Encoding.UTF8, "application/json"),
        };
    }
}
