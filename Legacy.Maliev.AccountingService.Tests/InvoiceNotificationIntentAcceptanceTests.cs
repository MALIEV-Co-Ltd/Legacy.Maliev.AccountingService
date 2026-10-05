using System.IdentityModel.Tokens.Jwt;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Maliev.Aspire.ServiceDefaults;
using Maliev.Aspire.ServiceDefaults.IAM;
using Maliev.Aspire.ServiceDefaults.LegacyAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>New child37 boundary tests; controlled remote transports are not provider/grant proof.</summary>
public sealed class InvoiceNotificationIntentAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalProduction_SubmittedCommitUnknown_Returns503AndDelegatedRepeatCannotReexecute(bool delegated)
    {
        await using var fixture = await IntentFixture.StartAsync();
        var auth = delegated ? new InvoiceDelegationChainFixture() : null;
        try
        {
            string? bearer = null;
            string? delegation = null;
            if (auth is not null)
            {
                await auth.InitializeAsync();
                var configuration = auth.Accounting.Services.GetRequiredService<IConfiguration>();
                fixture.ExternalIssuer = configuration["Jwt:Issuer"];
                fixture.ExternalPublicKey = configuration["Jwt:PublicKey"];
                bearer = auth.Ordinary(InvoiceDelegationChainFixture.Service, "service", InvoiceDelegationChainFixture.Permission);
                delegation = (await auth.Exchange(fixture.Operation)).GetProperty("accessToken").GetString();
            }
            fixture.Fault = "commit-after-transient";
            using var response = await fixture.CreateAsync(bearer: bearer, delegation: delegation);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            // Existing controller wraps its ObjectResult; preserve that established wire in this repair.
            Assert.Equal(503, body.RootElement.GetProperty("StatusCode").GetInt32());
            Assert.Equal("Invoice creation outcome is unavailable.", body.RootElement.GetProperty("Value").GetProperty("title").GetString());
            Assert.True(fixture.FaultReached);
            Assert.Equal(2, fixture.SaveCalls);
            Assert.Equal(1, fixture.CommitCalls);
            Assert.Equal(0, fixture.NotificationCalls);
            await using var database = fixture.Database();
            Assert.Single(await database.Invoices.ToListAsync());
            Assert.Single(await database.Items.ToListAsync());
            Assert.Empty(await database.Files.ToListAsync());
            if (delegated)
            {
                var admission = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync();
                Assert.Equal("NeedsReconciliation", admission.State);
                Assert.Equal(fixture.Operation, admission.OperationId);
                Assert.Equal("employee:42", admission.EmployeeSubject);
                var effects = fixture.DownstreamCalls;
                using var repeat = await fixture.CreateAsync(bearer: bearer, delegation: delegation);
                Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
                Assert.Equal(effects, fixture.DownstreamCalls);
                Assert.Equal(2, fixture.SaveCalls);
                Assert.Equal(1, fixture.CommitCalls);
            }
        }
        finally { if (auth is not null) await auth.DisposeAsync(); }
    }

    [Fact]
    public async Task RegisteredOptions_FreshContextPreservesInterceptorsAndOriginalAdvisoryLease()
    {
        await using var fixture = await IntentFixture.StartAsync();
        using var scope = fixture.Host.Services.CreateScope();
        var original = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var options = Assert.IsType<DbContextOptions<InvoiceDbContext>>(original.GetService<IDbContextOptions>());
        var creationLock = scope.ServiceProvider.GetRequiredService<IInvoiceCreationLock>();
        var lease = await creationLock.AcquireAsync(84, CancellationToken.None);
        try
        {
            var originalPid = await original.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
            await using (var fresh = new InvoiceDbContext(options))
            {
                Assert.True(fresh.Database.CreateExecutionStrategy().RetriesOnFailure);
                await fresh.Database.OpenConnectionAsync();
                var freshPid = await fresh.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
                Assert.NotEqual(originalPid, freshPid);
                fixture.Fault = "first-save";
                fresh.Invoices.Add(NewInvoice());
                var failure = await Record.ExceptionAsync(() => fresh.SaveChangesAsync());
                Assert.True(fixture.FaultReached);
                Assert.Same(fixture.Cause, failure);
            }
            Assert.Equal(originalPid, await original.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync());
            await using var peer = fixture.Database();
            await peer.Database.OpenConnectionAsync();
            Assert.False(await peer.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(1229870659, 84) AS \"Value\"").SingleAsync());
        }
        finally { await lease.DisposeAsync(); }
        await using var releasedPeer = fixture.Database();
        await releasedPeer.Database.OpenConnectionAsync();
        Assert.True(await releasedPeer.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(1229870659, 84) AS \"Value\"").SingleAsync());
        Assert.True(await releasedPeer.Database.SqlQueryRaw<bool>("SELECT pg_advisory_unlock(1229870659, 84) AS \"Value\"").SingleAsync());
    }

    [Theory]
    [InlineData("first-save")]
    [InlineData("second-save")]
    [InlineData("first-save-transient")]
    [InlineData("second-save-transient")]
    public async Task RegisteredStore_SaveFailure_RollsBackParentAndLines(string boundary)
    {
        await using var fixture = await IntentFixture.StartAsync();
        fixture.Fault = boundary;
        using var scope = fixture.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceCreationStore>();
        var failure = await Record.ExceptionAsync(() => store.CreateAsync(NewInvoice(), NewItems(), CancellationToken.None));
        Assert.True(fixture.FaultReached, "Required actual SaveChanges boundary was not reached.");
        Assert.Same(fixture.Cause, failure);
        await using var database = fixture.Database();
        Assert.Empty(await database.Invoices.ToListAsync());
        Assert.Empty(await database.Items.ToListAsync());
        Assert.Equal(0, fixture.CommitCalls);
        Assert.Equal(0, fixture.DownstreamCalls);
    }

    [Fact]
    public async Task RegisteredStore_RollbackFailure_RetainsBothCausesAndNeverReturnsInvoice()
    {
        await using var fixture = await IntentFixture.StartAsync();
        fixture.Fault = "rollback-failure";
        using var scope = fixture.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceCreationStore>();
        var failure = await Assert.ThrowsAsync<InvoiceCreationUnavailableException>(() => store.CreateAsync(NewInvoice(), NewItems(), CancellationToken.None));
        Assert.True(fixture.FaultReached);
        Assert.True(ContainsCause(failure, fixture.Cause));
        Assert.True(ContainsCause(failure, fixture.RollbackCause));
        Assert.Equal(1, fixture.RollbackCalls);
        Assert.Equal(2, fixture.SaveCalls);
        Assert.Equal(0, fixture.CommitCalls);
        await using var database = fixture.Database();
        Assert.Empty(await database.Invoices.ToListAsync());
        Assert.Empty(await database.Items.ToListAsync());
        Assert.Equal(0, fixture.DownstreamCalls);
    }

    [Fact]
    public async Task RegisteredStore_CallerAbortBeforeFirstSave_PropagatesActualTokenAndRollsBack()
    {
        await using var fixture = await IntentFixture.StartAsync();
        using var caller = new CancellationTokenSource();
        fixture.Caller = caller;
        fixture.Fault = "cancel-save";
        using var scope = fixture.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceCreationStore>();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateAsync(NewInvoice(), NewItems(), caller.Token));
        Assert.True(fixture.FaultReached);
        Assert.Equal(caller.Token, failure.CancellationToken);
        await using var database = fixture.Database();
        Assert.Empty(await database.Invoices.ToListAsync());
        Assert.Empty(await database.Items.ToListAsync());
        Assert.Equal(0, fixture.CommitCalls);
        Assert.Equal(0, fixture.DownstreamCalls);
    }

    [Theory]
    [InlineData("commit-before-transient", 0)]
    [InlineData("commit-after-transient", 1)]
    [InlineData("commit-after-arbitrary", 1)]
    [InlineData("commit-after-cancel", 1)]
    [InlineData("transaction-dispose", 1)]
    [InlineData("context-dispose", 1)]
    public async Task RegisteredStore_SubmittedCommitOrTeardownUncertainty_NeverReturnsOrReplays(string boundary, int expectedRows)
    {
        await using var fixture = await IntentFixture.StartAsync();
        using var caller = new CancellationTokenSource();
        fixture.Caller = caller;
        fixture.Fault = boundary;
        using var scope = fixture.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceCreationStore>();
        var failure = await Record.ExceptionAsync(() => store.CreateAsync(NewInvoice(), NewItems(), caller.Token));
        Assert.True(fixture.FaultReached, "Required actual submitted-COMMIT/teardown boundary was not reached.");
        var unknown = Assert.IsAssignableFrom<InvoiceCreationUnavailableException>(failure);
        Assert.True(ContainsCause(unknown, fixture.Cause));
        Assert.Equal(1, fixture.CommitCalls);
        Assert.Equal(2, fixture.SaveCalls);
        await using var database = fixture.Database();
        Assert.Equal(expectedRows, await database.Invoices.CountAsync());
        Assert.Equal(expectedRows, await database.Items.CountAsync());
        if (expectedRows == 1)
        {
            var parent = await database.Invoices.SingleAsync();
            var line = await database.Items.SingleAsync();
            Assert.Equal("INV-fault", parent.Number);
            Assert.Equal(107m, parent.Total);
            Assert.Equal(parent.Id, line.InvoiceId);
            Assert.Equal(100m, line.Subtotal);
        }
        Assert.Equal(0, fixture.DownstreamCalls);
    }

    private static Invoice NewInvoice() => new() { Number = "INV-fault", CustomerId = 42, Currency = "THB", Total = 107m };
    private static IReadOnlyList<InvoiceOrderItem> NewItems() => [new() { Description = "Synthetic part", Quantity = 2, UnitPrice = 50m }];
    private static bool ContainsCause(Exception? value, Exception cause) => value is not null &&
        (ReferenceEquals(value, cause) || ContainsCause(value.InnerException, cause) ||
            value is AggregateException aggregate && aggregate.InnerExceptions.Any(inner => ContainsCause(inner, cause)));

    [Fact]
    public async Task RegisteredInvoiceStore_FreshCommit_WorksWithActualConfiguredStrategy()
    {
        await using var fixture = await IntentFixture.StartAsync();
        using var scope = fixture.Host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        Assert.True(database.Database.CreateExecutionStrategy().RetriesOnFailure);
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceCreationStore>();
        var invoice = await store.CreateAsync(new Invoice { Number = "INV-direct", CustomerId = 42, Total = 107m }, [], CancellationToken.None);
        Assert.True(invoice.Id > 0);
        await using var persisted = fixture.Database();
        Assert.Single(await persisted.Invoices.ToListAsync());
    }

    [Fact]
    public async Task NormalProduction_FreshInvoice_ReachesConcreteNotificationBoundary()
    {
        await using var fixture = await IntentFixture.StartAsync();
        using var response = await fixture.CreateAsync();
        // Reachability prerequisite: a real new invoice must get past the registered retry strategy.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fixture.NotificationCalls);
        Assert.True(fixture.LiveChecks > 0);
        using var scope = fixture.Host.Services.CreateScope();
        Assert.IsType<IamServiceClient>(scope.ServiceProvider.GetRequiredService<IIamServiceClient>());
        await using var database = fixture.Database();
        Assert.Single(await database.Invoices.ToListAsync());
    }

    [Fact]
    public async Task NormalProduction_ExistingInvoice_ReconcilesWithoutResend_Control()
    {
        await using var fixture = await IntentFixture.StartAsync();
        await using (var database = fixture.Database())
        {
            database.Invoices.Add(new Invoice { Number = "INV-intent", CustomerId = 42, Total = 107m });
            await database.SaveChangesAsync();
        }
        using var response = await fixture.CreateAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("EmailState").GetInt32());
        Assert.Equal(0, fixture.NotificationCalls);
        await using var persisted = fixture.Database();
        Assert.Single(await persisted.Invoices.ToListAsync());
        Assert.Single(await persisted.Files.ToListAsync());
        Assert.True(fixture.LiveChecks > 0);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    public async Task NormalProduction_AnonymousOrLiveDenied_NoInvoiceOrRemoteEffects(bool anonymous, HttpStatusCode expected)
    {
        await using var fixture = await IntentFixture.StartAsync();
        fixture.AllowLive = false;
        using var response = await fixture.CreateAsync(anonymous);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, fixture.DownstreamCalls);
        await using var database = fixture.Database();
        Assert.Empty(await database.Invoices.ToListAsync());
    }

    [Theory]
    [InlineData("denied", 1)]
    [InlineData("unavailable", 1)]
    [InlineData("unauthorized", 1)]
    [InlineData("malformed", 1)]
    [InlineData("missing-credential", 0)]
    public async Task NormalIamComposition_LiveFailureCannotUseValidTokenPermissionOrWrite(string failure, int expectedChecks)
    {
        await using var fixture = await IntentFixture.StartAsync();
        fixture.AllowLive = failure != "denied";
        fixture.IamFailure = failure;
        fixture.MissingLiveCredential = failure == "missing-credential";
        using var response = await fixture.CreateAsync();
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(expectedChecks, fixture.LiveChecks);
        Assert.Equal(0, fixture.DownstreamCalls);
        using var scope = fixture.Host.Services.CreateScope();
        Assert.IsType<IamServiceClient>(scope.ServiceProvider.GetRequiredService<IIamServiceClient>());
        await using var database = fixture.Database();
        Assert.Empty(await database.Invoices.ToListAsync());
        Assert.Empty(await database.Items.ToListAsync());
        Assert.Empty(await database.InvoiceCreationAdmissions.ToListAsync());
    }

    [Theory]
    [InlineData("http://iam-intent.invalid/")]
    [InlineData("https://iam-intent.invalid/not-an-origin")]
    [InlineData("https://iam-intent.invalid/?query=invalid")]
    [InlineData("https://iam-intent.invalid/#fragment")]
    [InlineData(" ")]
    public async Task NormalIamComposition_ProductionRejectsInvalidOriginBeforeAnyRemoteEffects(string origin)
    {
        await using var fixture = await IntentFixture.StartAsync();
        fixture.IamOrigin = origin;
        var failure = await Record.ExceptionAsync(async () =>
        {
            using var response = await fixture.CreateAsync();
        });
        Assert.NotNull(failure);
        Assert.Contains("Live IAM requires an approved service origin.", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.LiveChecks);
        Assert.Equal(0, fixture.TokenExchangeCalls);
        Assert.Equal(0, fixture.DownstreamCalls);
        await using var database = fixture.Database();
        Assert.Empty(await database.Invoices.ToListAsync());
    }

    private sealed class IntentFixture : IAsyncDisposable
    {
        private const string Issuer = "https://accounting-intent.invalid";
        private const string Audience = "maliev-services";
        private readonly RSA rsa = RSA.Create(2048);
        private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        private readonly IContainer redis = new ContainerBuilder("redis:7.4-alpine")
            .WithPortBinding(6379, true).WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
        public Guid Operation { get; } = Guid.NewGuid();
        public Factory Host { get; private set; } = null!;
        public bool AllowLive = true;
        public string? IamFailure;
        public bool MissingLiveCredential;
        public string IamOrigin = "https://iam-intent.invalid/";
        public bool LoseNotificationResponse { get; set; }
        public string? ExternalIssuer;
        public string? ExternalPublicKey;
        public int LiveChecks;
        public int TokenExchangeCalls;
        public int DownstreamCalls;
        public int NotificationCalls;
        public string? Fault;
        public bool FaultReached;
        public int SaveCalls;
        public int CommitCalls;
        public int RollbackCalls;
        public Exception RollbackCause = new IOException("Synthetic rollback acknowledgment failure.");
        public CancellationTokenSource? Caller;
        public Exception Cause = new IOException("Synthetic transaction boundary failure.");
        public List<string> NotificationRequests { get; } = [];

        public static async Task<IntentFixture> StartAsync()
        {
            var value = new IntentFixture();
            try
            {
                await value.postgres.StartAsync();
                await value.redis.StartAsync();
                await using (var database = value.Database()) await database.Database.MigrateAsync();
                value.Host = new Factory(value);
                return value;
            }
            catch { await value.DisposeAsync(); throw; }
        }

        public InvoiceDbContext Database() => new(new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);

        public async Task<HttpResponseMessage> CreateAsync(bool anonymous = false, string? bearer = null, string? delegation = null)
        {
            using var client = Host.CreateClient(new() { AllowAutoRedirect = false });
            using var request = new HttpRequestMessage(HttpMethod.Post, "/invoices/from-quotation/84")
            {
                Content = JsonContent.Create(new CreateInvoiceFromQuotationRequest("INV-intent", null, null, null, null, null, null,
                    new(null, null, null, null, null, null, null, null, null), new(null, null, null, null, null, null, null, null, null), null, null, false, true)),
            };
            request.Headers.Add("Idempotency-Key", Operation.ToString("D"));
            if (delegation is not null) request.Headers.Add(InvoiceCreationDelegationVerifier.HeaderName, "Bearer " + delegation);
            if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
            else if (!anonymous)
            {
                var jwt = new JwtSecurityToken(Issuer, Audience,
                    [new Claim("sub", "service:legacy-intranet"), new Claim("identity_kind", "service"), new Claim("permissions", "legacy.accounting.create")],
                    DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
                request.Headers.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
            }
            return await client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            if (Host is not null) await Host.DisposeAsync();
            await redis.DisposeAsync();
            await postgres.DisposeAsync();
            rsa.Dispose();
        }

        private static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };

        private async Task<HttpResponseMessage> RemoteAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/auth/v1/service/login")
            {
                Interlocked.Increment(ref TokenExchangeCalls);
                Assert.Equal(HttpMethod.Post, request.Method);
                return Json("""{"accessToken":"synthetic-workload-token","expiresIn":300}""");
            }
            if (path == "/iam/v1/auth/check-permission")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("iam-intent.invalid", request.RequestUri.Host);
                Assert.Equal("https", request.RequestUri.Scheme);
                Assert.Equal("synthetic-live-credential", request.Headers.GetValues("X-Maliev-IAM-Live-Check-Key").Single());
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("service:legacy-intranet", body.RootElement.GetProperty("principalId").GetString());
                Assert.Equal("legacy.accounting.create", body.RootElement.GetProperty("permissionId").GetString());
                Assert.True(body.RootElement.GetProperty("bypassCache").GetBoolean());
                Assert.Equal("global", body.RootElement.GetProperty("resourcePath").GetString());
                Assert.Equal("synthetic-workload-token", request.Headers.Authorization?.Parameter);
                Interlocked.Increment(ref LiveChecks);
                if (IamFailure == "unavailable") return Json("{}", HttpStatusCode.ServiceUnavailable);
                if (IamFailure == "unauthorized") return Json("{}", HttpStatusCode.Unauthorized);
                if (IamFailure == "malformed") return Json("not-json");
                return Json(AllowLive ? "{\"allowed\":true}" : "{\"allowed\":false}");
            }
            Assert.Equal("synthetic-workload-token", request.Headers.Authorization?.Parameter);
            Interlocked.Increment(ref DownstreamCalls);
            if (path.StartsWith("/notifications/", StringComparison.Ordinal))
            {
                NotificationRequests.Add(request.Method + " " + path);
                Interlocked.Increment(ref NotificationCalls);
                if (LoseNotificationResponse) throw new HttpRequestException("Synthetic acknowledgment loss.");
                return Json("""{"providerMessageId":"synthetic-accepted"}""");
            }
            if (path == "/quotations/84/orderitems") return Json("""[{"id":1,"quotationId":84,"orderId":51,"description":"Synthetic part","quantity":1,"unitPrice":100,"subtotal":100}]""");
            if (path == "/quotations/84" && request.Method == HttpMethod.Get)
                return Json("""{"id":84,"customerId":42,"employeeId":7,"currencyId":1,"subtotal":100,"vat":7,"total":107,"period":14,"expirationDate":"2030-01-01T00:00:00Z"}""");
            if (path == "/quotations/84" || path == "/quotations/84/decision") { Assert.Equal(HttpMethod.Put, request.Method); return Json("{}"); }
            if (path == "/customers/42") return Json("""{"id":42,"fullName":"Synthetic Customer","email":"recipient@example.invalid"}""");
            if (path == "/employees/7") return Json("""{"id":7,"fullName":"Synthetic Employee"}""");
            if (path == "/currencies/1") return Json("""{"id":1,"shortName":"THB","longName":"Thai Baht"}""");
            if (path == "/pdfs/invoice") { Assert.Equal(HttpMethod.Post, request.Method); return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }; }
            if (path == "/uploads/SignedUrl") return Json("{}");
            throw new InvalidOperationException("Unexpected synthetic dependency route.");
        }

        public sealed class Factory(IntentFixture fixture) : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Jwt:Issuer", fixture.ExternalIssuer ?? Issuer);
                builder.UseSetting("Jwt:Audience", Audience);
                builder.UseSetting("Jwt:PublicKey", fixture.ExternalPublicKey ?? Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.rsa.ExportSubjectPublicKeyInfoPem())));
                builder.UseSetting("ConnectionStrings:redis", $"{fixture.redis.Hostname}:{fixture.redis.GetMappedPublicPort(6379)}");
                foreach (var name in new[] { "PaymentDbContext", "ReceiptDbContext", "InvoiceDbContext" }) builder.UseSetting("ConnectionStrings:" + name, fixture.postgres.GetConnectionString());
                builder.UseSetting("Services:Auth", "https://auth-intent.invalid/");
                builder.UseSetting("ServiceAuthentication:ClientId", "synthetic-accounting-client");
                builder.UseSetting("ServiceAuthentication:ClientSecret", "synthetic-fixture-only");
                builder.UseSetting("IAM:LivePermissionChecks:Credential", fixture.MissingLiveCredential ? "" : "synthetic-live-credential");
                builder.UseSetting("Services:IAMService:BaseUrl", fixture.IamOrigin);
                foreach (var name in new[] { "Notification", "Quotation", "Document", "File", "Customer", "Employee", "Catalog" }) builder.UseSetting("Services:" + name, "https://dependency-intent.invalid/");
                builder.ConfigureTestServices(services =>
                {
                    // Extends actual registered options; never replaces UseNpgsql or its retry strategy.
                    services.AddDbContext<InvoiceDbContext>((_, options) =>
                    {
                        options.AddInterceptors(new SaveFault(fixture), new CommitFault(fixture));
                        options.LogTo(eventId =>
                        {
                            if (!fixture.FaultReached && fixture.SaveCalls >= 2 && fixture.Fault is "transaction-dispose" or "context-dispose")
                            {
                                fixture.FaultReached = true;
                                throw fixture.Cause;
                            }
                        }, [fixture.Fault == "context-dispose" ? CoreEventId.ContextDisposed : RelationalEventId.TransactionDisposed], Microsoft.Extensions.Logging.LogLevel.Debug);
                    });
                    // Only replace the remote transport: IAM registration/authentication must come from Program.
                    services.AddHttpClient("IAMService")
                        .ConfigurePrimaryHttpMessageHandler(() => new RemoteHandler(fixture));
                    services.AddHttpClient(LegacyServiceAccessTokenProvider.HttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => new RemoteHandler(fixture));
                    foreach (var name in new[] { InvoiceCreationSourceClient.QuotationClient, InvoiceCreationSourceClient.CustomerClient, InvoiceCreationSourceClient.EmployeeClient, InvoiceCreationSourceClient.CatalogClient,
                        nameof(IInvoiceCreationDocumentClient), nameof(IInvoiceCreationFileClient), nameof(IInvoiceCreationNotificationClient), nameof(IInvoiceQuotationCompletionClient) })
                        services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new RemoteHandler(fixture));
                });
            }
        }

        private sealed class RemoteHandler(IntentFixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => fixture.RemoteAsync(request, cancellationToken);
        }

        private sealed class SaveFault(IntentFixture fixture) : SaveChangesInterceptor
        {
            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                var count = Interlocked.Increment(ref fixture.SaveCalls);
                if (fixture.Fault == "first-save-transient" && count == 1 || fixture.Fault == "second-save-transient" && count == 2)
                {
                    fixture.FaultReached = true;
                    fixture.Cause = new NpgsqlException("Synthetic SaveChanges transient failure.", new IOException());
                    throw fixture.Cause;
                }
                if (fixture.Fault == "first-save" && count == 1 || fixture.Fault is "second-save" or "rollback-failure" && count == 2)
                { fixture.FaultReached = true; throw fixture.Cause; }
                if (fixture.Fault == "cancel-save" && count == 1)
                {
                    fixture.FaultReached = true;
                    fixture.Caller!.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return ValueTask.FromResult(result);
            }
        }

        private sealed class CommitFault(IntentFixture fixture) : DbTransactionInterceptor
        {
            public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref fixture.RollbackCalls);
                if (fixture.Fault == "rollback-failure") throw fixture.RollbackCause;
                return ValueTask.FromResult(result);
            }

            public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref fixture.CommitCalls);
                if (fixture.Fault == "commit-before-transient")
                {
                    fixture.FaultReached = true;
                    fixture.Cause = new NpgsqlException("Synthetic pre-COMMIT acknowledgment failure.", new IOException());
                    throw fixture.Cause;
                }
                return ValueTask.FromResult(result);
            }

            public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
            {
                if (fixture.Fault is "commit-after-transient" or "commit-after-arbitrary" or "commit-after-cancel")
                {
                    fixture.FaultReached = true;
                    fixture.Cause = fixture.Fault switch
                    {
                        "commit-after-transient" => new NpgsqlException("Synthetic post-COMMIT acknowledgment failure.", new IOException()),
                        "commit-after-arbitrary" => new InvalidOperationException("Synthetic post-COMMIT callback failure."),
                        _ => new OperationCanceledException(cancellationToken),
                    };
                    if (fixture.Fault == "commit-after-cancel") fixture.Caller!.Cancel();
                    throw fixture.Cause;
                }
                return Task.CompletedTask;
            }
        }
    }
}
