using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.AccountingService.Tests.Fixtures;

public sealed class InvoiceDelegationChainFixture : IAsyncLifetime
{
    public const string Issuer = "https://delegation-proof.invalid";
    public const string Audience = "maliev-services";
    public const string Service = "service:legacy-intranet";
    public const string Permission = "legacy.accounting.create";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly RSA rsa = RSA.Create(2048);
    private Process? auth;
    private HttpClient? authClient;
    private Timer? authDeadline;
    private int disposed;
    private readonly Guid run = Guid.NewGuid();
    private readonly string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly string serviceSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly Dictionary<string, string> connections = [];
    private readonly Dictionary<string, string> employeeTokens = [];
    private string serviceToken = null!;
    public Factory Accounting { get; private set; } = null!;
    public int Effects;
    public bool AllowLive = true;
    public List<(string Subject, string Permission)> LiveChecks { get; } = [];
    public string EmployeeAccessToken(string subject) => employeeTokens[subject];
    public NpgsqlConnection OpenProofConnection(string database) => new(connections[database]);

    public async Task<System.Net.HttpStatusCode> ExchangeStatus(Guid operation, string employee)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/v1/exchange/invoice-create");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        request.Content = JsonContent.Create(new { employeeAccessToken = employeeTokens[employee], quotationId = 84, operationId = operation.ToString("D") });
        using var response = await authClient!.SendAsync(request);
        return response.StatusCode;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await postgres.StartAsync().WaitAsync(TimeSpan.FromMinutes(2));
            // The authority comes only from this running Testcontainer, never ambient configuration.
            if (string.IsNullOrWhiteSpace(postgres.Id)) throw new InvalidOperationException("Disposable container identity is missing.");
            var authority = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString());
            if (authority.Port != postgres.GetMappedPublicPort(5432) || authority.Host is not ("127.0.0.1" or "localhost"))
                throw new InvalidOperationException("Disposable container mapping is inconsistent.");
            await using (var control = new NpgsqlConnection(authority.ConnectionString))
            {
                await control.OpenAsync();
                foreach (var name in new[] { "invoice", "customer", "employee", "state" })
                {
                    var databaseName = $"accounting_chain_{run:N}_{name}";
                    await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", control);
                    await command.ExecuteNonQueryAsync();
                    var connection = new NpgsqlConnectionStringBuilder(authority.ConnectionString) { Database = databaseName, Pooling = false };
                    connections.Add(name, connection.ConnectionString);
                }
            }
            await using (var database = Database()) await database.Database.MigrateAsync();
            var root = Root();
            await SeedOwnerIdentities(root, authority.ConnectionString);
            var dll = Path.Combine(root, ".dependencies", "delegation-chain", "auth-51afbbd", "Legacy.Maliev.AuthService.Api", "bin", "Release", "net10.0", "Legacy.Maliev.AuthService.Api.dll");
            if (!File.Exists(dll)) throw new InvalidOperationException("Build must prepare the exact-pinned Auth API before executing chain tests.");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(dll)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var inherited = new Dictionary<string, string?>();
            foreach (var name in new[] { "PATH", "SystemRoot", "TEMP", "TMP", "HOME", "USERPROFILE", "DOTNET_ROOT" })
                inherited[name] = Environment.GetEnvironmentVariable(name);
            start.Environment.Clear();
            foreach (var (name, value) in inherited)
                if (value is not null) start.Environment[name] = value;
            start.ArgumentList.Add(dll);
            start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            start.Environment["DOTNET_ENVIRONMENT"] = "Production";
            start.Environment["Jwt__Issuer"] = Issuer;
            start.Environment["Jwt__Audience"] = Audience;
            start.Environment["Jwt__PrivateKeyPem"] = rsa.ExportPkcs8PrivateKeyPem();
            start.Environment["Jwt__KeyId"] = "ephemeral-chain-proof";
            start.Environment["CORS__AllowedOrigins__0"] = "https://delegation-proof.invalid";
            start.Environment["ConnectionStrings__CustomerIdentity"] = connections["customer"];
            start.Environment["ConnectionStrings__EmployeeIdentity"] = connections["employee"];
            start.Environment["ConnectionStrings__RefreshSessions"] = connections["state"];
            start.Environment["ServiceClients__Clients__legacy-intranet__SecretSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serviceSecret))).ToLowerInvariant();
            start.Environment["ServiceClients__Clients__legacy-intranet__Permissions__0"] = "legacy-auth.invoice-delegation.issue";
            start.Environment["ServiceClients__Clients__legacy-intranet__Permissions__1"] = Permission;
            start.Environment["Logging__LogLevel__Default"] = "Warning";
            start.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
            start.Environment["Logging__Console__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
            var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            auth = new Process { StartInfo = start, EnableRaisingEvents = true };
            auth.OutputDataReceived += (_, args) =>
            {
                const string marker = "Now listening on: ";
                if (args.Data is not { } line) return;
                if (line.StartsWith('{'))
                {
                    using var json = JsonDocument.Parse(line);
                    if (json.RootElement.TryGetProperty("Message", out var message)) line = message.GetString() ?? "";
                }
                if (line.IndexOf(marker, StringComparison.Ordinal) is var index && index >= 0)
                    listening.TrySetResult(line[(index + marker.Length)..].Trim());
            };
            // Drain both pipes without exposing private key/configuration or employee tokens in test output.
            auth.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is { } line && line.StartsWith("Unhandled exception.", StringComparison.Ordinal))
                    listening.TrySetException(new InvalidOperationException(line));
            };
            auth.Exited += (_, _) => listening.TrySetException(new InvalidOperationException("Pinned Auth API exited before listening."));
            auth.Start();
            authDeadline = new Timer(_ =>
            {
                try { if (!auth.HasExited) auth.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }, null, TimeSpan.FromMinutes(3), Timeout.InfiniteTimeSpan);
            auth.BeginOutputReadLine();
            auth.BeginErrorReadLine();
            var address = await listening.Task.WaitAsync(TimeSpan.FromSeconds(45));
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Host != "127.0.0.1")
                throw new InvalidOperationException("Auth proof must bind loopback only.");
            authClient = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15) };
            serviceToken = await Login("/auth/v1/service/login", new { clientId = "legacy-intranet", clientSecret = serviceSecret });
            foreach (var id in new[] { "42", "99" })
                employeeTokens.Add("employee:" + id, await Login("/auth/v1/login", new { userName = "chain-employee-" + id + "@example.invalid", password, identityKind = 1 }));
            Accounting = new Factory(this);
        }
        catch { await DisposeAsync(); throw; }
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (authDeadline is not null) await authDeadline.DisposeAsync();
        if (Accounting is not null) await Accounting.DisposeAsync();
        authClient?.Dispose();
        if (auth is not null)
        {
            if (!auth.HasExited) { auth.Kill(entireProcessTree: true); await auth.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            auth.Dispose();
        }
        rsa.Dispose();
        await postgres.DisposeAsync();
    }

    public InvoiceDbContext Database() => new(new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql(connections["invoice"]).Options);

    public string Ordinary(string subject, string kind, string permission) => subject == Service && kind == "service"
        && permission is Permission or "legacy-auth.invoice-delegation.issue" ? serviceToken : Sign([
        new("sub", subject), new("identity_kind", kind), new("permissions", permission),
        new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        new("jti", Guid.NewGuid().ToString("D")),
    ], Audience, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10));

    public string Sign(IEnumerable<Claim> claims, string audience, DateTime nbf, DateTime exp, string algorithm = SecurityAlgorithms.RsaSha256, RSA? key = null) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Issuer, audience, claims, nbf, exp,
            new SigningCredentials(new RsaSecurityKey(key ?? rsa), algorithm)));

    public async Task<JsonElement> Exchange(Guid operation, string employee = "employee:42", int quotation = 84)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/v1/exchange/invoice-create");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Ordinary(Service, "service", "legacy-auth.invoice-delegation.issue"));
        request.Content = JsonContent.Create(new { employeeAccessToken = employeeTokens[employee], quotationId = quotation, operationId = operation.ToString("D") });
        using var response = await authClient!.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    public static CreateInvoiceFromQuotationRequest Intent(string number = "INV-chain") => new(number, null, null, null, null, null, null,
        new(null, null, null, null, null, null, null, null, null), new(null, null, null, null, null, null, null, null, null), null, null, false, false);

    private async Task<string> Login(string route, object body)
    {
        using var response = await authClient!.PostAsJsonAsync(route, body);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("accessToken").GetString()!;
    }

    private async Task SeedOwnerIdentities(string root, string authority)
    {
        if (await RunOwnerUtility(root, authority) != 0) throw new InvalidOperationException("Private disposable owner-migration utility failed.");
    }

    public Task<int> ValidateUtilityGuard(string? mutation) => RunOwnerUtility(Root(), postgres.GetConnectionString(), mutation, validateOnly: true);

    private async Task<int> RunOwnerUtility(string root, string authority, string? mutation = null, bool validateOnly = false)
    {
        var dll = Path.Combine(root, ".dependencies", "delegation-chain", "seed-51afbbd-v5", "bin", "Release", "net10.0", "Seed.dll");
        if (!File.Exists(dll)) throw new InvalidOperationException("Build must prepare the private owner-migration utility.");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        var inherited = new Dictionary<string, string?>();
        foreach (var name in new[] { "PATH", "SystemRoot", "TEMP", "TMP", "HOME", "USERPROFILE", "DOTNET_ROOT" })
            inherited[name] = Environment.GetEnvironmentVariable(name);
        start.Environment.Clear();
        foreach (var (name, value) in inherited) if (value is not null) start.Environment[name] = value;
        start.ArgumentList.Add(dll);
        start.Environment["CHAIN_RUN"] = run.ToString("N");
        start.Environment["CHAIN_CONTAINER"] = authority;
        start.Environment["CHAIN_PASSWORD"] = password;
        foreach (var (name, connection) in connections) start.Environment["CHAIN_" + name.ToUpperInvariant()] = connection;
        if (validateOnly) start.Environment["CHAIN_VALIDATE_ONLY"] = "1";
        if (mutation is not null)
        {
            var changed = new NpgsqlConnectionStringBuilder(connections["employee"]);
            switch (mutation)
            {
                case "database": changed.Database = "postgres"; break;
                case "host": changed.Host = "example.invalid"; break;
                case "pooling": changed.Pooling = true; break;
                case "authority": changed.Port++; break;
                default: throw new ArgumentException("Unknown utility guard mutation.", nameof(mutation));
            }
            start.Environment["CHAIN_EMPLOYEE"] = changed.ConnectionString;
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(75)); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); throw; }
        await Task.WhenAll(output, errors);
        return process.ExitCode;
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.AccountingService.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Accounting proof repository not found.");
    }

    public sealed class Factory(InvoiceDelegationChainFixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:Issuer", Issuer);
            builder.UseSetting("Jwt:Audience", Audience);
            builder.UseSetting("Jwt:PublicKey", Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.rsa.ExportSubjectPublicKeyInfoPem())));
            builder.UseSetting("Cache:RedisEnabled", "false");
            foreach (var name in new[] { "PaymentDbContext", "InvoiceDbContext", "ReceiptDbContext" })
                builder.UseSetting($"ConnectionStrings:{name}", fixture.connections["invoice"]);
            builder.ConfigureTestServices(services =>
            {
                var iam = new Mock<IIamServiceClient>(MockBehavior.Strict);
                iam.Setup(value => value.CheckPermissionLiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .Returns((string subject, string permission, string? _, CancellationToken _) =>
                    {
                        fixture.LiveChecks.Add((subject, permission));
                        return Task.FromResult(fixture.AllowLive && subject == Service && permission == Permission);
                    });
                services.RemoveAll<IIamServiceClient>();
                services.AddSingleton(iam.Object);
                var workflow = new Mock<IInvoiceCreationWorkflow>(MockBehavior.Strict);
                workflow.Setup(value => value.CreateAsync(It.IsAny<int>(), It.IsAny<CreateInvoiceFromQuotationRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Interlocked.Increment(ref fixture.Effects);
                        return Task.FromResult(new InvoiceCreationResult(902, InvoiceCreationState.Completed,
                            InvoiceCreationEmailState.NotRequested, null, new("proof.invalid", "invoice.pdf")));
                    });
                services.RemoveAll<IInvoiceCreationWorkflow>();
                services.AddSingleton(workflow.Object);
            });
        }
    }
}
