extern alias auth_api;
using AuthProgram = auth_api::Program;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AuthService.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Commerce.JoinedAuth.Tests;

public sealed class FrozenAuthHost : IAsyncDisposable
{
    private readonly QuotationNormalIamFixture authority;
    private readonly IReadOnlyDictionary<string, string> connections;
    private readonly Dictionary<string, string> secrets = new(StringComparer.Ordinal);
    private readonly string employeePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly Factory app;
    private string employeeToken = null!;
    public ConcurrentQueue<(string Path, int Status)> Responses { get; } = new();
    public int SuccessfulIamResponses;
    public int IamTransportFailures;
    public int UnmatchedTransportCalls;
    public string IntranetToken { get; private set; } = null!;
    public string EmployeeToken => employeeToken;

    public FrozenAuthHost(QuotationNormalIamFixture authority, IReadOnlyDictionary<string, string> connections, string ownedAuthority)
    {
        this.authority = authority;
        this.connections = connections;
        var owner = new NpgsqlConnectionStringBuilder(ownedAuthority);
        foreach (var key in new[] { "CustomerIdentity", "EmployeeIdentity", "RefreshSessions" })
        {
            var selected = new NpgsqlConnectionStringBuilder(connections[key]);
            if (selected.Host != owner.Host || selected.Port != owner.Port || selected.Username != owner.Username
                || selected.Password != owner.Password || selected.Pooling || selected.Database is not { } databaseName
                || !databaseName.StartsWith("joined_auth_", StringComparison.Ordinal))
                throw new InvalidOperationException("Auth proof connection must belong to the fresh owned container.");
        }
        foreach (var client in new[] { "legacy-accounting", "legacy-quotation", "legacy-intranet", "legacy-auth" })
            secrets.Add(client, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        app = new Factory(this);
    }

    public string ClientSecret(string client) => secrets[client];
    public RefreshSessionDbContext State() => new(new DbContextOptionsBuilder<RefreshSessionDbContext>().UseNpgsql(connections["RefreshSessions"]).Options);

    public async Task InitializeAsync()
    {
        await using (var customer = new CustomerIdentityDbContext(new DbContextOptionsBuilder<CustomerIdentityDbContext>().UseNpgsql(connections["CustomerIdentity"]).Options))
            await customer.Database.MigrateAsync();
        await using (var state = State()) await state.Database.MigrateAsync();
        await using (var employee = new EmployeeIdentityDbContext(new DbContextOptionsBuilder<EmployeeIdentityDbContext>().UseNpgsql(connections["EmployeeIdentity"]).Options))
        {
            await employee.Database.MigrateAsync();
            var row = new LegacyIdentityRow
            {
                Id = "employee:42", DatabaseID = 42, UserName = "joined-employee-42@example.invalid",
                NormalizedUserName = "JOINED-EMPLOYEE-42@EXAMPLE.INVALID", Email = "joined-employee-42@example.invalid",
                NormalizedEmail = "JOINED-EMPLOYEE-42@EXAMPLE.INVALID", EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("D"), ConcurrencyStamp = Guid.NewGuid().ToString("D"), LockoutEnabled = true,
            };
            row.PasswordHash = new PasswordHasher<LegacyIdentityRow>().HashPassword(row, employeePassword);
            employee.Users.Add(row);
            await employee.SaveChangesAsync();
        }
        IntranetToken = await ServiceLoginAsync("legacy-intranet");
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/auth/v1/login", new { userName = "joined-employee-42@example.invalid", password = employeePassword, identityKind = 1 });
        Observe("/auth/v1/login", response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        employeeToken = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    public async Task<string> ServiceLoginAsync(string clientId)
    {
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/auth/v1/service/login", new { clientId, clientSecret = secrets[clientId] });
        Observe("/auth/v1/service/login", response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    public async Task<(HttpStatusCode Status, string? Token)> ExchangeAsync(int quotationId, Guid operation)
    {
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/v1/exchange/invoice-create");
        request.Headers.Authorization = new("Bearer", IntranetToken);
        request.Content = JsonContent.Create(new { employeeAccessToken = employeeToken, quotationId, operationId = operation.ToString("D") });
        using var response = await client.SendAsync(request);
        Observe("/auth/v1/exchange/invoice-create", response);
        return (response.StatusCode, response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString() : null);
    }

    public HttpMessageHandler CreateHandler() => new ObservedHandler(this, app.Server.CreateHandler());
    private void Observe(string path, HttpResponseMessage response) => Responses.Enqueue((path, (int)response.StatusCode));
    public ValueTask DisposeAsync() => app.DisposeAsync();

    private sealed class Factory(FrozenAuthHost fixture) : WebApplicationFactory<AuthProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.joined-public/Legacy.Maliev.AuthService/Legacy.Maliev.AuthService.Api")));
            var configuration = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "https://quotation95-auth.invalid", ["Jwt:Audience"] = "quotation95-services",
                ["Jwt:PrivateKeyPem"] = fixture.authority.AuthAuthorityPrivateKeyPem, ["Jwt:KeyId"] = "owned-joined-auth-proof",
                ["ConnectionStrings:CustomerIdentity"] = fixture.connections["CustomerIdentity"],
                ["ConnectionStrings:EmployeeIdentity"] = fixture.connections["EmployeeIdentity"],
                ["ConnectionStrings:RefreshSessions"] = fixture.connections["RefreshSessions"],
                ["Services:IAM:BaseUrl"] = "https://joined-auth-iam.invalid", ["Services:Auth:BaseUrl"] = "https://joined-auth.invalid",
                ["ServiceAuthentication:ClientId"] = "legacy-auth", ["ServiceAuthentication:ClientSecret"] = fixture.secrets["legacy-auth"],
                ["EmployeeRecovery:Enabled"] = "false", ["QualificationIntrospection:Enabled"] = "false",
                ["CORS:AllowedOrigins:0"] = "https://joined-auth-proof.invalid", ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
                ["Observability:RuntimeMetricsEnabled"] = "false",
            };
            foreach (var (client, secret) in fixture.secrets)
                configuration[$"ServiceClients:Clients:{client}:SecretSha256"] = ServiceClientCredential.HashSecret(secret);
            var grants = new Dictionary<string, string[]>
            {
                ["legacy-intranet"] = ["legacy-auth.invoice-delegation.issue", "legacy.accounting.create"],
                ["legacy-accounting"] = ["legacy.quotations.read", "legacy.customer-quotations.read", "legacy.quotation-lines.read", "legacy.quotations.update"],
                ["legacy-quotation"] = [], ["legacy-auth"] = [],
            };
            foreach (var (client, permissions) in grants)
                for (var index = 0; index < permissions.Length; index++) configuration[$"ServiceClients:Clients:{client}:Permissions:{index}"] = permissions[index];
            foreach (var setting in configuration) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services => services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(http => http.PrimaryHandler = new ExternalTransport(fixture))));
        }
    }

    private sealed class ObservedHandler(FrozenAuthHost fixture, HttpMessageHandler handler) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker host = new(handler);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing Auth boundary URI.");
            if (request.Method != HttpMethod.Post || uri.AbsolutePath != "/auth/v1/service/login")
            {
                Interlocked.Increment(ref fixture.UnmatchedTransportCalls);
                throw new InvalidOperationException("Unexpected Auth transport refused before send.");
            }
            var response = await host.SendAsync(request, cancellationToken);
            fixture.Observe(uri.AbsolutePath, response);
            return response;
        }
        protected override void Dispose(bool disposing) { if (disposing) host.Dispose(); base.Dispose(disposing); }
    }

    private sealed class ExternalTransport(FrozenAuthHost fixture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing Auth external URI.");
            if (uri.Host == "joined-auth.invalid" && uri.AbsolutePath == "/auth/v1/service/login" && request.Method == HttpMethod.Post)
            {
                using var host = new HttpMessageInvoker(fixture.CreateHandler());
                return await host.SendAsync(request, cancellationToken);
            }
            if (uri.Host != "joined-auth-iam.invalid" || uri.AbsolutePath != "/iam/v1/auth/check-permission" || request.Method != HttpMethod.Post)
            {
                Interlocked.Increment(ref fixture.UnmatchedTransportCalls);
                throw new InvalidOperationException("Unmatched Auth external/provider transport refused before send.");
            }
            try
            {
                var workload = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter);
                Assert.Equal("service:legacy-auth", workload.Subject);
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var body = json.RootElement;
                Assert.Equal("service:legacy-intranet", body.GetProperty("principalId").GetString());
                Assert.Equal("legacy-auth.invoice-delegation.issue", body.GetProperty("permissionId").GetString());
                Assert.False(body.GetProperty("bypassCache").GetBoolean());
                Assert.False(request.Headers.Contains("X-Maliev-IAM-Live-Check-Key"));
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { principalId = body.GetProperty("principalId").GetString(), permissionId = body.GetProperty("permissionId").GetString(),
                        resourcePath = body.GetProperty("resourcePath").GetString(), allowed = true, fromCache = false, latencyMs = 0 }),
                };
                Interlocked.Increment(ref fixture.SuccessfulIamResponses);
                return response;
            }
            catch { Interlocked.Increment(ref fixture.IamTransportFailures); throw; }
        }
    }
}
