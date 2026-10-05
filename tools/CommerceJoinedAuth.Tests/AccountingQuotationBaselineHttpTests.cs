extern alias accounting_api;
extern alias quotation_api;
using AccountingProgram = accounting_api::Program;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Docker.DotNet.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.QuotationService.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Commerce.JoinedAuth.Tests;

[CollectionDefinition("joined-auth", DisableParallelization = true)]
public sealed class JoinedBaselineCollection : ICollectionFixture<AccountingQuotationBaselineFixture>
{
}

[Collection("joined-auth")]
public sealed class AccountingQuotationBaselineHttpTests(AccountingQuotationBaselineFixture fixture)
{
    [Fact]
    public async Task RequiredCompletion_PositivePersistedInvoice_MustReachSingleAtomicCustomerDecision()
    {
        fixture.ResetObservations();
        var row = await fixture.Quotation.SeedAsync();
        await using var database = fixture.InvoiceDatabase();
        var invoice = new Legacy.Maliev.AccountingService.Domain.Invoice.Invoice
        { Number = $"ATOMIC-REQUIRED-{Guid.NewGuid():N}", CustomerId = 42, Currency = "THB", Total = 107m };
        database.Invoices.Add(invoice);
        await database.SaveChangesAsync();
        // Actual Accounting host registration, actual completion HTTP transport and actual producer.
        // Desired contract should RED at the current protected full-PUT409, without needing Web's
        // future context shape. Context-absent callers still require positive atomic invoice linkage.
        using var bootstrap = fixture.Accounting.CreateClient();
        using var scope = fixture.Accounting.Services.CreateScope();
        var completion = scope.ServiceProvider.GetRequiredService<IInvoiceQuotationCompletionClient>();
        var error = await Record.ExceptionAsync(() => completion.CompleteAsync(row.Id, invoice.Id, Guid.NewGuid(), CancellationToken.None));
        foreach (var call in fixture.Trace) Console.WriteLine($"Joined observed boundary: {call}");
        Assert.Null(error);
        Assert.Equal($"PUT /quotations/{row.Id}/decision =>200", Assert.Single(fixture.Trace, call => call.StartsWith($"PUT /quotations/{row.Id}/decision =>", StringComparison.Ordinal)));
        Assert.DoesNotContain(fixture.Trace, call => call.StartsWith($"PUT /quotations/{row.Id} =>", StringComparison.Ordinal));
        var sent = Assert.Single(fixture.DecisionRequests, value => value.Path == $"/quotations/{row.Id}/decision");
        Assert.Equal(new[] { "Accepted", "EmployeeInitiated", "InvoiceId" }, sent.Body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.True(sent.Body.GetProperty("Accepted").GetBoolean());
        Assert.False(sent.Body.GetProperty("EmployeeInitiated").GetBoolean());
        Assert.Equal(invoice.Id, sent.Body.GetProperty("InvoiceId").GetInt32());
        await using var quotations = fixture.Quotation.Context();
        var stored = await quotations.Quotations.AsNoTracking().SingleAsync(value => value.Id == row.Id);
        Assert.True(stored.Accepted);
        Assert.Equal(invoice.Id, stored.InvoiceId);
        Assert.Equal("customer", stored.AcceptanceOrigin);
        var outcome = Assert.Single(await quotations.AcceptedOutcomes.Where(value => value.QuotationId == row.Id).ToArrayAsync());
        Assert.Equal("customer", outcome.AcceptanceOrigin);
        Assert.Empty(await quotations.GoogleAnalyticsOutbox.Where(value => value.QuotationId == row.Id).ToArrayAsync());
        Assert.Equal(0, fixture.ExternalProviderCalls);
        Assert.Equal(0, fixture.QuotationBoundary.UnmatchedTransportCalls);
        fixture.AssertSuccessfulQuotationIam(row.Id);
    }

    [Fact]
    public async Task CurrentAccountingCompletion_ObservesProtected409WithPersistedInvoiceAndNoQuotationIntent()
    {
        fixture.ResetObservations();
        var row = await fixture.Quotation.SeedAsync();
        await using (var database = fixture.Quotation.Context())
        {
            database.OrderItems.Add(new QuotationOrderItem { QuotationId = row.Id, Description = "Synthetic part", Quantity = 1, UnitPrice = 100m });
            await database.SaveChangesAsync();
        }
        var quotationBefore = await fixture.QuotationScalarSnapshotAsync(row.Id);
        using var client = fixture.Accounting.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Quotation.AccountingIncomingToken);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var number = $"JOINED-{row.Id}-{Guid.NewGuid():N}";
        using var response = await client.PostAsJsonAsync($"/invoices/from-quotation/{row.Id}", new
        {
            InvoiceNumber = number, Comment = "Synthetic joined baseline", SendEmail = false,
            BillingAddress = new { Recipient = "Synthetic Thai customer", Line1 = "Synthetic address", Country = "Thailand" },
            ShippingAddress = new { Recipient = "Synthetic Thai customer", Line1 = "Synthetic address", Country = "Thailand" },
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(fixture.Trace, call => call == $"PUT /quotations/{row.Id} =>409");
        Assert.DoesNotContain(fixture.Trace, call => call.Contains("/decision", StringComparison.Ordinal));
        await using var invoices = fixture.InvoiceDatabase();
        var invoice = await invoices.Invoices.AsNoTracking().SingleAsync(value => value.Number == number);
        Assert.True(invoice.Id > 0);
        Assert.Equal(42, invoice.CustomerId);
        Assert.Equal(107m, invoice.Total);
        Assert.Single(await invoices.Items.Where(value => value.InvoiceId == invoice.Id).ToArrayAsync());
        await using var quotations = fixture.Quotation.Context();
        var stored = await quotations.Quotations.AsNoTracking().SingleAsync(value => value.Id == row.Id);
        Assert.Equal(row.Accepted, stored.Accepted);
        Assert.Equal(row.InvoiceId, stored.InvoiceId);
        Assert.Equal(row.ModifiedDate, stored.ModifiedDate);
        Assert.Null(stored.AcceptedUtc);
        Assert.Empty(await quotations.AcceptedOutcomes.Where(value => value.QuotationId == row.Id).ToArrayAsync());
        Assert.Empty(await quotations.GoogleAnalyticsOutbox.Where(value => value.QuotationId == row.Id).ToArrayAsync());
        Assert.Equal(quotationBefore, await fixture.QuotationScalarSnapshotAsync(row.Id));
        Assert.Equal(0, fixture.ExternalProviderCalls);
        Assert.Equal(0, fixture.QuotationBoundary.UnmatchedTransportCalls);
        Assert.True(fixture.AccountingLiveChecks > 0);
        fixture.AssertSuccessfulQuotationIam(row.Id);
    }
}

// Actual Accounting, Quotation and Auth Programs with their real stores and token issuers.
// IAM/provider transports remain controlled; Web ownership and antiforgery are separate.
public sealed class AccountingQuotationBaselineFixture : IAsyncLifetime
{
    public QuotationNormalIamFixture Quotation { get; } = new();
    public QuotationNormalIamBoundary QuotationBoundary { get; } = new();
    public WebApplicationFactory<AccountingProgram> Accounting { get; private set; } = null!;
    public FrozenAuthHost Auth { get; private set; } = null!;
    public ConcurrentQueue<string> Trace { get; } = new();
    public ConcurrentQueue<(string Path, JsonElement Body)> DecisionRequests { get; } = new();
    public int ExternalProviderCalls;
    public int AccountingLiveChecks;
    private PostgreSqlContainer? postgres;
    private WebApplicationFactory<quotation_api::Program>? quotationApp;
    private readonly Dictionary<string, string> connections = [];
    private string? previousStandby;
    private int disposed;

    public void ResetObservations()
    {
        while (Trace.TryDequeue(out _)) { }
        while (DecisionRequests.TryDequeue(out _)) { }
        while (QuotationBoundary.IamResponses.TryDequeue(out _)) { }
        ExternalProviderCalls = 0;
        AccountingLiveChecks = 0;
        QuotationBoundary.UnmatchedTransportCalls = 0;
        QuotationBoundary.IamCalls = 0;
        QuotationBoundary.LoginCalls = 0;
        QuotationBoundary.SuccessfulStandardResponses = 0;
        QuotationBoundary.SuccessfulLiveResponses = 0;
        QuotationBoundary.IamTransportFailures = 0;
    }

    public void AssertSuccessfulQuotationIam(int id)
    {
        Assert.Equal(0, QuotationBoundary.IamTransportFailures);
        Assert.True(QuotationBoundary.SuccessfulStandardResponses > 0);
        Assert.True(QuotationBoundary.SuccessfulLiveResponses > 0);
        Assert.All(QuotationBoundary.IamResponses, response =>
        {
            Assert.Equal($"/quotations/{id}", response.Resource);
            Assert.Equal(200, response.Status);
            Assert.True(response.Allowed);
        });
    }

    public async Task InitializeAsync()
    {
        previousStandby = Environment.GetEnvironmentVariable("MALIEV_OBSERVABILITY_STANDBY");
        Environment.SetEnvironmentVariable("MALIEV_OBSERVABILITY_STANDBY", "true");
        try
        {
            await Quotation.InitializeAsync();
            postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDockerEndpoint(Quotation.DockerEndpoint)
                .WithName($"commerce-joined-pg-{Guid.NewGuid():N}").WithLabel("maliev.proof.owner", "commerce-joined")
                .WithDatabase("joined_invoice").WithPassword(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)))
                .WithCreateParameterModifier(parameters =>
                {
                    parameters.HostConfig ??= new HostConfig();
                    parameters.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>
                    { ["5432/tcp"] = [new PortBinding { HostIP = "127.0.0.1", HostPort = "" }] };
                    parameters.HostConfig.Tmpfs = new Dictionary<string, string> { ["/var/lib/postgresql"] = "rw,noexec,nosuid,size=268435456" };
                }).Build();
            await postgres.StartAsync().WaitAsync(TimeSpan.FromMinutes(2));
            var authority = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString());
            if (authority.Host is not ("127.0.0.1" or "localhost") || authority.Port != postgres.GetMappedPublicPort(5432))
                throw new InvalidOperationException("Joined proof requires owned loopback PostgreSQL.");
            await using (var connection = new NpgsqlConnection(authority.ConnectionString))
            {
                await connection.OpenAsync();
                foreach (var name in new[] { "joined_payment", "joined_receipt", "joined_auth_customer", "joined_auth_employee", "joined_auth_state" })
                {
                    await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
                    await command.ExecuteNonQueryAsync();
                }
            }
            foreach (var (context, name) in new[] { ("InvoiceDbContext", "joined_invoice"), ("PaymentDbContext", "joined_payment"), ("ReceiptDbContext", "joined_receipt") })
                connections[context] = new NpgsqlConnectionStringBuilder(authority.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
            var authDatabases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CustomerIdentity"] = "joined_auth_customer",
                ["EmployeeIdentity"] = "joined_auth_employee",
                ["RefreshSessions"] = "joined_auth_state",
            };
            foreach (var database in authDatabases)
                connections[database.Key] = new NpgsqlConnectionStringBuilder(authority.ConnectionString) { Database = database.Value, Pooling = false }.ConnectionString;
            await using (var invoice = InvoiceDatabase()) await invoice.Database.MigrateAsync();
            await using (var payment = new PaymentDbContext(Options<PaymentDbContext>("PaymentDbContext"))) await payment.Database.MigrateAsync();
            await using (var receipt = new ReceiptDbContext(Options<ReceiptDbContext>("ReceiptDbContext"))) await receipt.Database.MigrateAsync();
            Auth = new FrozenAuthHost(Quotation, connections, authority.ConnectionString);
            await Auth.InitializeAsync();
            Quotation.AccountingWorkloadToken = await Auth.ServiceLoginAsync("legacy-accounting");
            Quotation.AccountingIncomingToken = Auth.IntranetToken;
            Quotation.AuthClientSecret = Auth.ClientSecret("legacy-quotation");
            Quotation.AuthHandlerFactory = Auth.CreateHandler;
            quotationApp = Quotation.App(QuotationBoundary, environment: "Testing");
            using var bootstrap = quotationApp.CreateClient();
            Accounting = new AccountingFactory(this);
        }
        catch { await DisposeAsync(); throw; }
    }

    private DbContextOptions<T> Options<T>(string name) where T : DbContext => new DbContextOptionsBuilder<T>().UseNpgsql(connections[name]).Options;
    public InvoiceDbContext InvoiceDatabase() => new(Options<InvoiceDbContext>("InvoiceDbContext"));

    public async Task<string> QuotationScalarSnapshotAsync(int id)
    {
        await using var database = Quotation.Context();
        var row = await database.Quotations.AsNoTracking().SingleAsync(value => value.Id == id);
        var scalars = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in database.Entry(row).Properties) scalars.Add(property.Metadata.Name, property.CurrentValue);
        return JsonSerializer.Serialize(scalars);
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            try { if (Accounting is not null) await Accounting.DisposeAsync(); }
            finally
            {
                try { if (quotationApp is not null) await quotationApp.DisposeAsync(); }
                finally { if (Auth is not null) await Auth.DisposeAsync(); }
            }
        }
        finally
        {
            try { if (postgres is not null) await postgres.DisposeAsync(); }
            finally
            {
                try { await Quotation.DisposeAsync(); }
                finally { Environment.SetEnvironmentVariable("MALIEV_OBSERVABILITY_STANDBY", previousStandby); }
            }
        }
    }

    private sealed class AccountingFactory(AccountingQuotationBaselineFixture fixture) : WebApplicationFactory<AccountingProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../.joined-public/Legacy.Maliev.AccountingService/Legacy.Maliev.AccountingService.Api")));
            foreach (var setting in new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "https://quotation95-auth.invalid", ["Jwt:Audience"] = "quotation95-services", ["Jwt:PublicKey"] = fixture.Quotation.PublicKey,
                ["ConnectionStrings:InvoiceDbContext"] = fixture.connections["InvoiceDbContext"],
                ["ConnectionStrings:PaymentDbContext"] = fixture.connections["PaymentDbContext"],
                ["ConnectionStrings:ReceiptDbContext"] = fixture.connections["ReceiptDbContext"], ["ConnectionStrings:redis"] = fixture.Quotation.RedisConnection,
                ["Services:Auth:BaseUrl"] = "https://joined-auth.invalid", ["Services:IAM:BaseUrl"] = "https://joined-iam.invalid",
                ["Services:Quotation"] = "https://joined-quotation.invalid", ["Services:Customer"] = "https://joined-customer.invalid",
                ["Services:Employee"] = "https://joined-employee.invalid", ["Services:Catalog"] = "https://joined-catalog.invalid",
                ["Services:Document"] = "https://joined-document.invalid", ["Services:File"] = "https://joined-file.invalid", ["Services:Notification"] = "https://joined-notification.invalid",
                ["ServiceAuthentication:ClientId"] = "legacy-accounting", ["ServiceAuthentication:ClientSecret"] = fixture.Auth.ClientSecret("legacy-accounting"),
                ["IAM:LivePermissionChecks:Credential"] = "synthetic-joined-live-check", ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "", ["Observability:RuntimeMetricsEnabled"] = "false",
            }) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services => services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(http => http.PrimaryHandler = new Transport(fixture, fixture.quotationApp!.Server.CreateHandler(), fixture.Auth.CreateHandler()))));
        }
    }

    private sealed class Transport(AccountingQuotationBaselineFixture fixture, HttpMessageHandler quotationHandler, HttpMessageHandler authHandler) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker quotation = new(quotationHandler);
        private readonly HttpMessageInvoker auth = new(authHandler);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing boundary URI.");
            if (uri.Host == "joined-quotation.invalid")
            {
                if (request.Method == HttpMethod.Put && uri.AbsolutePath.EndsWith("/decision", StringComparison.Ordinal))
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    fixture.DecisionRequests.Enqueue((uri.AbsolutePath, body.RootElement.Clone()));
                }
                var response = await quotation.SendAsync(request, cancellationToken);
                fixture.Trace.Enqueue($"{request.Method} {uri.AbsolutePath} =>{(int)response.StatusCode}");
                return response;
            }
            if (uri.Host == "joined-auth.invalid" && uri.AbsolutePath == "/auth/v1/service/login" && request.Method == HttpMethod.Post)
                return await auth.SendAsync(request, cancellationToken);
            if (uri.Host == "joined-iam.invalid" && uri.AbsolutePath == "/iam/v1/auth/check-permission" && request.Method == HttpMethod.Post)
            {
                Assert.Equal("synthetic-joined-live-check", Assert.Single(request.Headers.GetValues("X-Maliev-IAM-Live-Check-Key")));
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal("service:legacy-intranet", body.RootElement.GetProperty("principalId").GetString());
                Assert.Equal("legacy.accounting.create", body.RootElement.GetProperty("permissionId").GetString());
                Assert.True(body.RootElement.GetProperty("bypassCache").GetBoolean());
                Interlocked.Increment(ref fixture.AccountingLiveChecks);
                return Json(new { principalId = "service:legacy-intranet", permissionId = "legacy.accounting.create",
                    resourcePath = body.RootElement.GetProperty("resourcePath").GetString(), allowed = true, fromCache = false, latencyMs = 0 });
            }
            if (request.Method == HttpMethod.Get)
            {
                if (uri.Host == "joined-customer.invalid" && uri.AbsolutePath == "/customers/42") return Json(new { Id = 42, FullName = "Synthetic Thai customer", Email = "fixture@example.invalid" });
                if (uri.Host == "joined-employee.invalid" && uri.AbsolutePath == "/employees/17") return Json(new { Id = 17, FullName = "Synthetic Thai employee" });
                if (uri.Host == "joined-catalog.invalid" && uri.AbsolutePath == "/currencies/764") return Json(new { Id = 764, ShortName = "THB", LongName = "Thai baht" });
            }
            Interlocked.Increment(ref fixture.ExternalProviderCalls);
            throw new InvalidOperationException("Unmatched external/provider transport refused before network send.");
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        protected override void Dispose(bool disposing) { if (disposing) { quotation.Dispose(); auth.Dispose(); } base.Dispose(disposing); }
    }
}
