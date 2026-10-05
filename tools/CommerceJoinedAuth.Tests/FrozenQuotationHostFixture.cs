extern alias quotation_api;
using QuotationProgram = quotation_api::Program;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Legacy.Maliev.QuotationService.Data;
using Legacy.Maliev.QuotationService.Domain;
using Maliev.Aspire.ServiceDefaults;
using Maliev.Aspire.ServiceDefaults.IAM;
using Maliev.Aspire.ServiceDefaults.LegacyAuth;
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
public sealed class QuotationNormalIamFixture : IAsyncLifetime
{
    private const string Issuer = "https://quotation95-auth.invalid";
    private const string Audience = "quotation95-services";
    private readonly RSA key = RSA.Create(2048);
    private readonly string run = Guid.NewGuid().ToString("N");
    private readonly string liveCredential = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public string DockerEndpoint { get; private set; } = "";
    private PostgreSqlContainer? postgres;
    private IContainer? redis;
    private string Requests => new NpgsqlConnectionStringBuilder(postgres!.GetConnectionString()) { Database = "quotation95_requests", Pooling = false }.ConnectionString;
    public string PublicKey => Convert.ToBase64String(Encoding.UTF8.GetBytes(key.ExportSubjectPublicKeyInfoPem()));
    public string RedisConnection => $"127.0.0.1:{redis!.GetMappedPublicPort(6379)}";
    public string AuthAuthorityPrivateKeyPem => key.ExportPkcs8PrivateKeyPem();
    public string AccountingWorkloadToken { get; set; } = null!;
    public string AccountingIncomingToken { get; set; } = null!;
    public string AuthClientSecret { get; set; } = null!;
    public Func<HttpMessageHandler> AuthHandlerFactory { get; set; } = null!;
    public QuotationDbContext Context() => new(new DbContextOptionsBuilder<QuotationDbContext>()
        .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres!.GetConnectionString()) { Pooling = false }.ConnectionString).Options);

    public async Task InitializeAsync()
    {
        // Pre-create fail-closed local authority check, not post-create remote cleanup.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_CONTEXT")))
            throw new InvalidOperationException("Proof requires no ambient Docker override.");
        var name = (await DockerReadAsync("context", "show")).Trim();
        if (name.Length is < 1 or > 128) throw new InvalidOperationException("Invalid local Docker context.");
        using var context = JsonDocument.Parse(await DockerReadAsync("context", "inspect", name));
        if (context.RootElement.GetArrayLength() != 1) throw new InvalidOperationException("Ambiguous Docker context.");
        var endpoint = context.RootElement[0].GetProperty("Endpoints").GetProperty("docker").GetProperty("Host").GetString()!;
        if (endpoint.StartsWith("npipe:////./pipe/", StringComparison.Ordinal))
        {
            var pipe = endpoint["npipe:////./pipe/".Length..];
            if (pipe.Length == 0 || pipe.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '_' and not '-'))
                throw new InvalidOperationException("Invalid local Docker pipe.");
            endpoint = "npipe://./pipe/" + pipe;
        }
        else if (endpoint != "unix:///var/run/docker.sock") throw new InvalidOperationException("Remote Docker authority refused.");
        DockerEndpoint = endpoint;
        postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDockerEndpoint(endpoint)
            .WithName($"quotation95-pg-{run}").WithLabel("maliev.proof.owner", "quotation95").WithLabel("maliev.proof.run", run)
            .WithDatabase("quotation95").WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
            .WithCreateParameterModifier(parameters => ConfigureOwnedStorage(parameters, "5432/tcp", "/var/lib/postgresql", 268435456)).Build();
        redis = new ContainerBuilder("redis:7-alpine").WithDockerEndpoint(endpoint)
            .WithName($"quotation95-redis-{run}").WithLabel("maliev.proof.owner", "quotation95").WithLabel("maliev.proof.run", run)
            .WithPortBinding(6379, true).WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379))
            .WithCreateParameterModifier(parameters => ConfigureOwnedStorage(parameters, "6379/tcp", "/data", 16777216)).Build();
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE DATABASE quotation95_requests", connection);
            await command.ExecuteNonQueryAsync();
        }
        await using var db = Context();
        await db.Database.MigrateAsync();
        await using var requests = new QuotationRequestDbContext(new DbContextOptionsBuilder<QuotationRequestDbContext>().UseNpgsql(Requests).Options);
        await requests.Database.MigrateAsync();
    }

    private static void ConfigureOwnedStorage(CreateContainerParameters parameters, string port, string path, int bytes)
    {
        parameters.HostConfig ??= new HostConfig();
        parameters.HostConfig.PortBindings ??= new Dictionary<string, IList<PortBinding>>();
        parameters.HostConfig.PortBindings[port] = [new PortBinding { HostIP = "127.0.0.1", HostPort = "" }];
        parameters.HostConfig.Tmpfs = new Dictionary<string, string> { [path] = $"rw,noexec,nosuid,size={bytes}" };
    }

    public async Task<Quotation> SeedAsync(bool? accepted = null)
    {
        await using var db = Context();
        var row = new Quotation
        {
            CustomerId = 42,
            EmployeeId = 17,
            CurrencyId = 764,
            Period = 30,
            ExpirationDate = new DateTime(2035, 1, 1),
            Subtotal = 100m,
            Vat = 7m,
            Total = 107m,
            WithholdingTax = 3m,
            Accepted = accepted,
            Comment = "synthetic quotation95 note",
            CreatedDate = new DateTime(2026, 9, 29, 3, 4, 5),
            ModifiedDate = new DateTime(2026, 9, 29, 4, 4, 5),
            SourceRequestId = 17,
            SourceJourneyId = Guid.NewGuid()
        };
        db.Quotations.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    public WebApplicationFactory<QuotationProgram> App(QuotationNormalIamBoundary boundary, string? credential = null,
        string? origin = "https://quotation95-iam.invalid", string environment = "Production")
    {
        boundary.ExpectedCredential = credential ?? liveCredential;
        return new Factory(this, boundary, boundary.ExpectedCredential, origin, environment);
    }

    public async Task DisposeAsync()
    {
        try { if (postgres is not null) await postgres.DisposeAsync(); }
        finally
        {
            try { if (redis is not null) await redis.DisposeAsync(); }
            finally { key.Dispose(); }
        }
    }

    private static async Task<string> DockerReadAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker inspection unavailable.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new InvalidOperationException("Docker inspection timed out."); }
        var value = await output;
        _ = await error;
        if (process.ExitCode != 0 || value.Length > 16384) throw new InvalidOperationException("Docker inspection refused.");
        return value;
    }

    private sealed class Factory(QuotationNormalIamFixture fixture, QuotationNormalIamBoundary boundary,
        string credential, string? origin, string environment) : WebApplicationFactory<QuotationProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseContentRoot(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../.joined-public/Legacy.Maliev.QuotationService/Legacy.Maliev.QuotationService.Api")));
            foreach (var setting in new Dictionary<string, string?>
            {
                ["ConnectionStrings:QuotationDbContext"] = new NpgsqlConnectionStringBuilder(fixture.postgres!.GetConnectionString()) { Pooling = false }.ConnectionString,
                ["ConnectionStrings:QuotationRequestDbContext"] = fixture.Requests,
                ["ConnectionStrings:redis"] = $"127.0.0.1:{fixture.redis!.GetMappedPublicPort(6379)}",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.key.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Services:Auth:BaseUrl"] = Issuer,
                ["Services:IAM:BaseUrl"] = origin,
                ["Services:Order:BaseUrl"] = "https://quotation95-order.invalid",
                ["ServiceAuthentication:ClientId"] = "legacy-quotation",
                ["ServiceAuthentication:ClientSecret"] = fixture.AuthClientSecret,
                ["IAM:LivePermissionChecks:Credential"] = credential,
                ["QualificationAuthority:Enabled"] = "false",
                ["Features:ResourceScopedAuthEnabled"] = "true",
                ["GoogleAnalyticsMeasurementProtocol:Enabled"] = "false",
                ["GoogleAnalyticsMeasurementProtocol:MeasurementId"] = "",
                ["GoogleAnalyticsMeasurementProtocol:ApiSecret"] = "",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
                ["Observability:RuntimeMetricsEnabled"] = "false"
            })
            {
                // UseSetting(null) represents an explicit blank; omission must be genuinely absent.
                if (setting.Key != "Services:IAM:BaseUrl" || setting.Value is not null)
                    builder.UseSetting(setting.Key, setting.Value);
            }
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(http =>
                {
                    if (http.Name != "IAMService" && http.Name != LegacyServiceAccessTokenProvider.HttpClientName)
                        http.PrimaryHandler = new RejectTransport(boundary);
                }));
                // Normal JWT/IAM registration remains; controlled transports only.
                services.Configure<HttpClientFactoryOptions>("IAMService", options => options.HttpMessageHandlerBuilderActions.Add(http =>
                {
                    if (boundary.InspectPrimaryOnly)
                    {
                        boundary.PrimaryRedirectsDisabled = http.PrimaryHandler switch
                        {
                            SocketsHttpHandler sockets => !sockets.AllowAutoRedirect,
                            HttpClientHandler handler => !handler.AllowAutoRedirect,
                            _ => false
                        };
                    }
                    else http.PrimaryHandler = new IamTransport(boundary);
                }));
                services.Configure<HttpClientFactoryOptions>(LegacyServiceAccessTokenProvider.HttpClientName,
                    options => options.HttpMessageHandlerBuilderActions.Add(http => http.PrimaryHandler = fixture.AuthHandlerFactory()));
            });
        }
    }

    private sealed class RejectTransport(QuotationNormalIamBoundary boundary) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref boundary.UnmatchedTransportCalls);
            throw new InvalidOperationException("Unmatched outbound transport refused before network send.");
        }
    }

    private sealed class IamTransport(QuotationNormalIamBoundary boundary) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref boundary.IamCalls);
            try
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://quotation95-iam.invalid/iam/v1/auth/check-permission", request.RequestUri!.AbsoluteUri);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                var workload = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter);
                Assert.Equal("service:legacy-quotation", workload.Subject);
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal(new[] { "bypassCache", "permissionId", "principalId", "resourcePath" }, json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
                var principal = json.RootElement.GetProperty("principalId").GetString();
                var permission = json.RootElement.GetProperty("permissionId").GetString();
                var resource = json.RootElement.GetProperty("resourcePath").GetString()
                    ?? throw new InvalidOperationException("IAM proof requires a concrete resource path.");
                var live = json.RootElement.GetProperty("bypassCache").GetBoolean();
                Assert.Equal("service:legacy-accounting", principal);
                Assert.Contains(permission, new[] { "legacy.quotations.update", "legacy.quotations.read", "legacy.customer-quotations.read", "legacy.quotation-lines.read" });
                Assert.NotNull(resource);
                Assert.StartsWith("/quotations/", resource, StringComparison.Ordinal);
                Assert.True(int.TryParse(resource["/quotations/".Length..], out var resourceId) && resourceId > 0);
                if (live)
                    Assert.Equal(boundary.ExpectedCredential, Assert.Single(request.Headers.GetValues("X-Maliev-IAM-Live-Check-Key")));
                else
                    Assert.False(request.Headers.Contains("X-Maliev-IAM-Live-Check-Key"));
                boundary.Entered.TrySetResult();
                if (boundary.Mode == "wait") await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                if (boundary.Mode == "network") throw new HttpRequestException("Synthetic dependency unavailable.");
                var status = boundary.Mode == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
                var allowed = boundary.Mode != "denied";
                var response = new HttpResponseMessage(status)
                { Content = JsonContent.Create(new { principalId = principal, permissionId = permission, resourcePath = resource, allowed, fromCache = false, latencyMs = 0 }) };
                boundary.IamResponses.Enqueue(new(permission!, resource, live, (int)status, allowed));
                if (status == HttpStatusCode.OK)
                {
                    if (live) Interlocked.Increment(ref boundary.SuccessfulLiveResponses);
                    else Interlocked.Increment(ref boundary.SuccessfulStandardResponses);
                }
                return response;
            }
            catch
            {
                Interlocked.Increment(ref boundary.IamTransportFailures);
                throw;
            }
        }
    }

}

/// <summary>Per-host external boundary observation; never production grant evidence.</summary>
public sealed class QuotationNormalIamBoundary
{
    public sealed record IamResponse(string Permission, string Resource, bool Live, int Status, bool Allowed);
    public ConcurrentQueue<IamResponse> IamResponses { get; } = new();
    public int SuccessfulStandardResponses;
    public int SuccessfulLiveResponses;
    public int IamTransportFailures;
    public string Mode { get; init; } = "allowed";
    public bool InspectPrimaryOnly { get; init; }
    public bool PrimaryRedirectsDisabled { get; set; }
    public string ExpectedCredential { get; set; } = "";
    public int UnmatchedTransportCalls;
    public int IamCalls;
    public int LoginCalls;
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

///
