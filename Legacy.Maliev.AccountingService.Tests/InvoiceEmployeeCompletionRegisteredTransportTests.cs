using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Maliev.Aspire.ServiceDefaults.LegacyAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Time.Testing;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Polly.Timeout;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Actual Program/Defaults client pipeline with controlled transport/workload token; no producer IAM or joined acceptance claim.</summary>
public sealed class InvoiceEmployeeCompletionRegisteredTransportTests
{
    private static readonly InvoiceFinancialOwnership Ownership = new(1, Guid.Parse("dc904058-4d1b-4eab-b8b2-a505700b0419"), 84, 901,
        "https://auth.example.invalid", "employee:42", "service:legacy-intranet", "2026-10-06T01:02:03.0000000Z", new string('A', 64));
    private const string Proof = "synthetic.transient.capability";

    [Theory]
    [InlineData("503")]
    [InlineData("io-loss")]
    [InlineData("timeout")]
    public async Task ActualRegisteredLegacyNotificationPostIsOneAttemptDespiteInheritedResilience(string scenario)
    {
        using var rsa = RSA.Create(2048);
        var calls = 0;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var transport = new Transport((request, _) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            return scenario switch
            {
                "io-loss" => Task.FromException<HttpResponseMessage>(new IOException("Synthetic lost legacy send acknowledgment")),
                "timeout" => Task.FromException<HttpResponseMessage>(new TimeoutRejectedException("Synthetic classified timeout")),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            };
        });
        using var factory = new Factory(rsa, transport, legacyNotification: true);
        var client = factory.Services.GetRequiredService<IInvoiceCreationNotificationClient>();
        var error = await Record.ExceptionAsync(() => client.SendAsync("synthetic@example.invalid", "Synthetic", new Invoice { Id = 901, Number = "SYNTHETIC" },
            [1, 2, 3], Guid.NewGuid(), budget.Token));
        Assert.NotNull(error);
        Assert.Equal(1, calls);
        factory.AssertRegisteredPipeline(true);
    }

    [Theory]
    [InlineData("get-503")]
    [InlineData("put-503")]
    [InlineData("put-io-loss")]
    public async Task NormalRegisteredPipelineNeverRetriesIndividualRequests(string scenario)
    {
        using var rsa = RSA.Create(2048);
        var calls = new List<HttpMethod>();
        using var transport = new Transport((request, _) =>
        {
            calls.Add(request.Method);
            Assert.Equal("Bearer synthetic-accounting-workload", request.Headers.Authorization!.ToString());
            Assert.Equal("Bearer " + Proof, request.Headers.GetValues(InvoiceEmployeeQuotationCompletionClient.CapabilityHeader).Single());
            if (scenario == "get-503") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (calls.Count == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (request.Method == HttpMethod.Put)
                return scenario == "put-io-loss" ? Task.FromException<HttpResponseMessage>(new IOException("Synthetic lost PUT acknowledgment"))
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(Completed());
        });
        using var factory = new Factory(rsa, transport);
        var client = factory.Services.GetRequiredService<IInvoiceEmployeeQuotationCompletionClient>();
        if (scenario == "get-503")
            await Assert.ThrowsAsync<InvoiceCreationUnavailableException>(() => client.CompleteAsync(Ownership, Proof, CancellationToken.None));
        else Assert.Equal("Completed", (await client.CompleteAsync(Ownership, Proof, CancellationToken.None)).State);
        Assert.Equal(scenario == "get-503" ? new[] { HttpMethod.Get } : new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Get }, calls);
        factory.AssertRegisteredPipeline();
    }

    [Fact]
    public async Task NormalRegisteredRedirectPolicyDoesNotAcceptForeignReceipt()
    {
        using var rsa = RSA.Create(2048);
        var calls = 0;
        using var transport = new Transport((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://foreign.invalid/receipt");
            return Task.FromResult(response);
        });
        using var factory = new Factory(rsa, transport);
        await Assert.ThrowsAsync<InvoiceCreationUnavailableException>(() => factory.Services.GetRequiredService<IInvoiceEmployeeQuotationCompletionClient>()
            .CompleteAsync(Ownership, Proof, CancellationToken.None));
        Assert.Equal(1, calls);
        // Primary settings observed before the test transport replacement; transport itself does not emulate redirect following.
        factory.AssertRegisteredPipeline();
    }

    [Fact]
    public async Task NormalRegisteredHeaderFirstReadRejectsUnknownLengthOversizedBodyAndDisposesIt()
    {
        using var rsa = RSA.Create(2048);
        var stream = new CountedStream(new byte[1024 * 1024]);
        using var transport = new Transport((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        using var factory = new Factory(rsa, transport);
        await Assert.ThrowsAsync<InvoiceCreationUnavailableException>(() => factory.Services.GetRequiredService<IInvoiceEmployeeQuotationCompletionClient>()
            .CompleteAsync(Ownership, Proof, CancellationToken.None));
        Assert.InRange(stream.BytesRead, 16385, 24576);
        Assert.True(stream.Disposed);
        factory.AssertRegisteredPipeline();
    }

    [Fact]
    public async Task NormalRegisteredSlowBodySharesFiniteInvocationDeadlineAndDisposesIt()
    {
        using var rsa = RSA.Create(2048);
        var clock = new FakeTimeProvider();
        var stream = new BlockedStream();
        using var transport = new Transport((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        using var factory = new Factory(rsa, transport, clock);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var completion = factory.Services.GetRequiredService<IInvoiceEmployeeQuotationCompletionClient>().CompleteAsync(Ownership, Proof, budget.Token);
        try
        {
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(InvoiceEmployeeQuotationCompletionClient.InvocationDeadline);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(stream.Disposed);
            factory.AssertRegisteredPipeline();
        }
        finally
        {
            budget.Cancel();
            clock.Advance(InvoiceEmployeeQuotationCompletionClient.InvocationDeadline);
            try { await completion.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
        }
    }

    private static HttpResponseMessage Completed() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new InvoiceQuotationOperationReceipt(1, Ownership.OperationId.ToString("D"), 84, 901,
            Ownership.OriginIssuer, Ownership.EmployeeSubject, Ownership.RequesterSubject, "service:legacy-accounting", Ownership.OriginalQuotationVersion,
            Ownership.FinancialBinding, "invoice-creation-financial-v1", "Completed", "2026-10-06T01:02:04.0000000Z", 2, 2,
            "2026-10-06T01:02:05.0000000Z")), Encoding.UTF8, "application/json")
    };

    private sealed class Factory(RSA rsa, HttpMessageHandler transport, TimeProvider? clock = null, bool legacyNotification = false) : WebApplicationFactory<Program>
    {
        private string[]? handlerTypes;
        private bool? redirectsEnabled;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:Issuer", "https://auth.example.invalid");
            builder.UseSetting("Jwt:Audience", "synthetic-accounting");
            builder.UseSetting("Jwt:PublicKey", Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())));
            builder.UseSetting("Cache:RedisEnabled", "false");
            builder.UseSetting("Cache:AllowInMemoryFallback", "true");
            builder.UseSetting("Services:Quotation", "http://quotation.invalid/");
            builder.UseSetting("Services:Notification", "http://notification.invalid/");
            foreach (var context in new[] { "InvoiceDbContext", "ReceiptDbContext", "PaymentDbContext" })
                builder.UseSetting($"ConnectionStrings:{context}", "Host=127.0.0.1;Database=unused_transport_only;Username=synthetic");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILegacyServiceAccessTokenProvider>();
                services.AddSingleton<ILegacyServiceAccessTokenProvider, SyntheticWorkloadToken>();
                if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); }
                services.PostConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder =>
                {
                    var clientName = legacyNotification ? nameof(IInvoiceCreationNotificationClient) : nameof(IInvoiceEmployeeQuotationCompletionClient);
                    var registeredName = builder.Name;
                    if (registeredName is null || !registeredName.Contains(clientName, StringComparison.Ordinal)) return;
                    redirectsEnabled = builder.PrimaryHandler switch
                    {
                        HttpClientHandler handler => handler.AllowAutoRedirect,
                        SocketsHttpHandler handler => handler.AllowAutoRedirect,
                        _ => throw new InvalidOperationException("Expected actual registered primary settings before controlled replacement.")
                    };
                    handlerTypes = builder.AdditionalHandlers.Select(value => value.GetType().FullName!).ToArray();
                    builder.PrimaryHandler = transport;
                }));
            });
        }
        public void AssertRegisteredPipeline(bool inheritedResilience = false)
        {
            if (!inheritedResilience) Assert.False(redirectsEnabled);
            Assert.NotNull(handlerTypes);
            Assert.Contains(typeof(LegacyServiceAuthenticationHandler).FullName!, handlerTypes);
            if (inheritedResilience) Assert.Contains(handlerTypes, value => value.Contains("Resilience", StringComparison.Ordinal));
            else Assert.DoesNotContain(handlerTypes, value => value.Contains("Resilience", StringComparison.Ordinal));
        }
    }
    private sealed class SyntheticWorkloadToken : ILegacyServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("synthetic-accounting-workload");
        public void Invalidate(string token) { }
    }
    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class CountedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { var count = await base.ReadAsync(buffer, cancellationToken); BytesRead += count; return count; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class BlockedStream : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { Entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
