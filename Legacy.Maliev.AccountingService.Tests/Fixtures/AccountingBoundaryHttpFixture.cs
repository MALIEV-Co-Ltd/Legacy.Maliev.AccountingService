using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.AccountingService.Tests.Fixtures;

[CollectionDefinition(AccountingBoundaryHttpCollection.Name, DisableParallelization = true)]
public sealed class AccountingBoundaryHttpCollection : ICollectionFixture<AccountingBoundaryHttpFixture>
{
    public const string Name = "Accounting dedicated HTTP boundary";
}

/// <summary>Separate disposable contexts, real JWT/live permissions, and two actual API environments.</summary>
public sealed class AccountingBoundaryHttpFixture : IAsyncLifetime
{
    private const string Issuer = "https://accounting-boundary-proof.invalid";
    private const string Audience = "accounting-boundary-proof";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly RSA rsa = RSA.Create(2048);
    private readonly Dictionary<string, string> connections = [];
    private readonly ConcurrentDictionary<string, HashSet<string>> grants = new();
    private Factory? production;
    private Factory? development;
    private Factory? summary;
    private Task<ReceiptHost>? receiptHost;
    private int disposed;

    public ConcurrentQueue<(string Subject, string Permission)> LiveChecks { get; } = new();
    public ConcurrentQueue<string> FailureMetadata { get; } = new();
    private SummaryCacheObserver? summaryCache;

    public async Task<string> SummaryCacheSnapshotAsync()
    {
        _ = summary!.Services.GetRequiredService<IDistributedCache>();
        var observer = summaryCache ?? throw new InvalidOperationException("Summary cache observer is not initialized.");
        return await observer.SnapshotAsync();
    }

    public async Task InitializeAsync()
    {
        try
        {
            await postgres.StartAsync().WaitAsync(TimeSpan.FromMinutes(2));
            Console.WriteLine($"AccountingBoundaryFixture owned container started: {postgres.Id}");
            var authority = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString());
            if (string.IsNullOrWhiteSpace(postgres.Id) || authority.Host is not ("127.0.0.1" or "localhost")
                || authority.Port != postgres.GetMappedPublicPort(5432))
            {
                throw new InvalidOperationException("Boundary proof requires its owned loopback Testcontainer.");
            }

            await using var control = new NpgsqlConnection(authority.ConnectionString);
            await control.OpenAsync();
            var run = Guid.NewGuid().ToString("N");
            foreach (var context in new[] { "PaymentDbContext", "InvoiceDbContext", "ReceiptDbContext" })
            {
                var name = $"accounting_boundary_{run}_{context.ToLowerInvariant()}";
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", control);
                await command.ExecuteNonQueryAsync();
                connections.Add(context, new NpgsqlConnectionStringBuilder(authority.ConnectionString)
                {
                    Database = name,
                    Pooling = false,
                }.ConnectionString);
            }

            await using (var payment = Database()) await payment.Database.MigrateAsync();
            await using (var invoice = new InvoiceDbContext(Options<InvoiceDbContext>("InvoiceDbContext")))
                await invoice.Database.MigrateAsync();
            await using (var receipt = new ReceiptDbContext(Options<ReceiptDbContext>("ReceiptDbContext")))
                await receipt.Database.MigrateAsync();
            production = new Factory(this, "Production");
            development = new Factory(this, "Development");
            summary = new Factory(this, "Production", new SummaryClock());
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private DbContextOptions<T> Options<T>(string context) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseNpgsql(connections[context]).Options;

    public PaymentDbContext Database() => new(Options<PaymentDbContext>("PaymentDbContext"));

    // Same owned loopback container and isolated database authority established during InitializeAsync.
    public InvoiceDbContext InvoiceDatabase() => new(Options<InvoiceDbContext>("InvoiceDbContext"));

    public ReceiptDbContext ReceiptDatabase() => new(Options<ReceiptDbContext>("ReceiptDbContext"));

    public int ReceiptOutboundCalls => receiptHost is { IsCompletedSuccessfully: true }
        ? receiptHost.Result.OutboundCalls : 0;

    // Test observations resolve the normal receipt Program registrations and owned Redis.
    public async Task<AsyncServiceScope> ReceiptScopeAsync()
    {
        var host = await (receiptHost ??= ReceiptHost.StartAsync(this));
        return host.Factory.Services.CreateAsyncScope();
    }

    public async Task<HttpClient> ReceiptClientAsync(string[]? permissions, bool allowLive = true)
    {
        var host = await (receiptHost ??= ReceiptHost.StartAsync(this));
        var client = host.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (permissions is null) return client;
        var subject = $"service:accounting-receipt-proof-{Guid.NewGuid():N}";
        grants[subject] = allowLive ? new HashSet<string>(permissions, StringComparer.Ordinal) : [];
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new("sub", subject), new("identity_kind", "service"),
            new("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("jti", Guid.NewGuid().ToString("D")),
        };
        claims.AddRange(permissions.Select(permission => new Claim("permissions", permission)));
        var token = new JwtSecurityToken(Issuer, Audience, claims, now.AddSeconds(-5), now.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public sealed record PhysicalState(int Tables, int Rows, string Digest);
    public sealed record ReceiptBoundaryState(PhysicalState Payment, PhysicalState Invoice, PhysicalState Receipt,
        PhysicalState Journal);

    public async Task<ReceiptBoundaryState> ReceiptSnapshotAsync()
    {
        var host = await (receiptHost ??= ReceiptHost.StartAsync(this));
        await using var payment = Database();
        await using var invoice = new InvoiceDbContext(Options<InvoiceDbContext>("InvoiceDbContext"));
        await using var receipt = new ReceiptDbContext(Options<ReceiptDbContext>("ReceiptDbContext"));
        return new(await SnapshotRowsAsync(payment, 6), await SnapshotRowsAsync(invoice, 5),
            await SnapshotRowsAsync(receipt, 3), await host.SnapshotJournalAsync());
    }

    private async Task<PhysicalState> SnapshotRowsAsync(DbContext context, int expectedTables)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var authority = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (string.IsNullOrWhiteSpace(postgres.Id) || authority.Host is not ("127.0.0.1" or "localhost")
            || authority.Port != postgres.GetMappedPublicPort(5432)
            || authority.Database?.StartsWith("accounting_boundary_", StringComparison.Ordinal) != true
            || !connections.Values.Contains(connection.ConnectionString, StringComparer.Ordinal))
            throw new InvalidOperationException("Receipt snapshots require owned disposable context authority.");
        var tables = context.Model.GetEntityTypes().Select(entity => (Schema: entity.GetSchema() ?? "public",
            Table: entity.GetTableName() ?? throw new InvalidOperationException("Snapshot table is missing.")))
            .Distinct().OrderBy(table => table.Schema, StringComparer.Ordinal)
            .ThenBy(table => table.Table, StringComparer.Ordinal).ToArray();
        if (tables.Length != expectedTables) throw new InvalidOperationException("Receipt snapshot table inventory changed.");
        await connection.OpenAsync();
        var rowCount = 0;
        var inventory = new List<object>();
        foreach (var (schema, table) in tables)
        {
            // Names come only from this context's trusted EF model; all scalar columns are captured.
            static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            await using var command = new NpgsqlCommand($"SELECT row_to_json(t)::text FROM {Quote(schema)}.{Quote(table)} t", connection);
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
            rows.Sort(StringComparer.Ordinal);
            rowCount += rows.Count;
            inventory.Add(new { schema, table, rows });
        }
        // Row content remains in process; only counts and a digest leave this method.
        return new(tables.Length, rowCount, Digest(inventory));
    }

    private static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    public async Task ResetAsync()
    {
        grants.Clear();
        LiveChecks.Clear();
        FailureMetadata.Clear();
        await using var database = Database();
        await database.Files.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Accounts.ExecuteDeleteAsync();
        await database.Directions.ExecuteDeleteAsync();
        await database.Methods.ExecuteDeleteAsync();
        await database.Types.ExecuteDeleteAsync();
        // Keep explicit dependency IDs outside the generated identity range of this bounded suite.
        database.Directions.Add(new PaymentDirection { Id = 100000, Name = "Synthetic direction", Description = "Boundary proof" });
        database.Methods.Add(new PaymentMethod { Id = 100000, Name = "Synthetic method", Description = "Boundary proof" });
        database.Types.Add(new PaymentType { Id = 100000, Name = "Synthetic type", Description = "Boundary proof" });
        await database.SaveChangesAsync();
    }

    public HttpClient Client(string[]? permissions = null, bool allowLive = true, bool developmentEnvironment = false, bool summaryClockHost = false)
    {
        var factory = summaryClockHost ? summary : developmentEnvironment ? development : production;
        var client = (factory ?? throw new InvalidOperationException("Fixture is not initialized."))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (permissions is null) return client;
        var subject = $"service:accounting-boundary-{Guid.NewGuid():N}";
        grants[subject] = allowLive ? new HashSet<string>(permissions, StringComparer.Ordinal) : [];
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new("sub", subject), new("identity_kind", "service"),
            new("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("jti", Guid.NewGuid().ToString("D")),
        };
        claims.AddRange(permissions.Select(permission => new Claim("permissions", permission)));
        var token = new JwtSecurityToken(Issuer, Audience, claims, now.AddSeconds(-5), now.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected) return;
        var fields = new List<string>();
        var envelope = new List<string>();
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Inspect(document.RootElement, 0);

            void Inspect(JsonElement element, int depth)
            {
                if (element.ValueKind != JsonValueKind.Object || depth > 3) return;
                foreach (var property in element.EnumerateObject())
                {
                    if (envelope.Count < 16) envelope.Add(property.Name);
                    if (property.Name.Equals("errors", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var field in property.Value.EnumerateObject())
                        {
                            if (field.Value.ValueKind != JsonValueKind.Array) continue;
                            foreach (var error in field.Value.EnumerateArray())
                            {
                                if (fields.Count >= 16) break;
                                var text = error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : "";
                                var category = text.Contains("required", StringComparison.OrdinalIgnoreCase) ? "required"
                                    : text.Contains("convert", StringComparison.OrdinalIgnoreCase) ? "conversion"
                                    : text.Contains("JSON", StringComparison.OrdinalIgnoreCase) ? "json" : "validation";
                                fields.Add($"{field.Name}:{category}");
                            }
                        }
                    }

                    Inspect(property.Value, depth + 1);
                }
            }
        }

        Assert.Fail($"Expected HTTP {(int)expected}; actual {(int)response.StatusCode}; owned container={postgres.Id}; "
            + $"envelope fields=[{string.Join(',', envelope)}]; validation fields/categories=[{string.Join(',', fields)}]; "
            + $"exception types/SQLSTATE=[{string.Join(',', FailureMetadata.Take(12))}]");
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            try
            {
                if (receiptHost is { IsCompletedSuccessfully: true }) await receiptHost.Result.DisposeAsync();
            }
            finally
            {
                if (production is not null) await production.DisposeAsync();
            }
        }
        finally
        {
            try
            {
                if (development is not null) await development.DisposeAsync();
            }
            finally
            {
                try
                {
                    if (summary is not null) await summary.DisposeAsync();
                }
                finally
                {
                    try { rsa.Dispose(); }
                    finally
                    {
                        var id = postgres.Id;
                        await postgres.DisposeAsync();
                        Console.WriteLine($"AccountingBoundaryFixture owned container disposed: {id}");
                    }
                }
            }
        }
    }

    private sealed class SummaryClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class ReceiptHost : IAsyncDisposable
    {
        private readonly IContainer redis = new ContainerBuilder("redis:7.4-alpine")
            .WithName($"legacy-accounting-receipt-proof-{Guid.NewGuid():N}")
            .WithLabel("maliev-proof", "receipt-http-rejection")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
        private IConnectionMultiplexer? connection;
        private ReceiptFactory? factory;
        private int outboundCalls;
        private int disposed;
        public ReceiptFactory Factory => factory ?? throw new InvalidOperationException("Receipt host is not initialized.");
        public int OutboundCalls => Volatile.Read(ref outboundCalls);

        public static async Task<ReceiptHost> StartAsync(AccountingBoundaryHttpFixture fixture)
        {
            var host = new ReceiptHost();
            try
            {
                if (string.IsNullOrWhiteSpace(fixture.postgres.Id) || fixture.connections.Count != 3)
                    throw new InvalidOperationException("Receipt host requires the initialized owned PostgreSQL fixture.");
                await host.redis.StartAsync().WaitAsync(TimeSpan.FromMinutes(2));
                if (string.IsNullOrWhiteSpace(host.redis.Id) || host.redis.Hostname is not ("127.0.0.1" or "localhost")
                    || host.redis.GetMappedPublicPort(6379) <= 0)
                    throw new InvalidOperationException("Receipt journal requires its owned loopback Redis container.");
                host.factory = new ReceiptFactory(fixture, host);
                using var bootstrap = host.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
                host.connection = host.Factory.Services.GetRequiredService<IConnectionMultiplexer>();
                if (!host.connection.IsConnected)
                    throw new InvalidOperationException("Receipt journal Redis connection is not ready.");
                Console.WriteLine($"Accounting receipt proof owned Redis started: {host.redis.Id}");
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }

        public async Task<PhysicalState> SnapshotJournalAsync()
        {
            var owned = connection ?? throw new InvalidOperationException("Receipt journal is not initialized.");
            var server = owned.GetServer(redis.Hostname, redis.GetMappedPublicPort(6379));
            var database = owned.GetDatabase();
            var keys = new List<RedisKey>();
            // This Redis instance belongs only to this fixture. Observe every key so a
            // changed prefix cannot turn an unexpected write into a false zero-delta proof.
            await foreach (var key in server.KeysAsync(database.Database, "*")) keys.Add(key);
            keys.Sort((left, right) => StringComparer.Ordinal.Compare(left.ToString(), right.ToString()));
            var inventory = new List<object>();
            foreach (var key in keys)
            {
                // Distributed Redis cache stores a hash; capture every field, not only key count.
                var fields = (await database.HashGetAllAsync(key)).OrderBy(field => field.Name.ToString(), StringComparer.Ordinal)
                    .Select(field => new { Name = field.Name.ToString(), Value = Convert.ToBase64String((byte[]?)field.Value ?? []) }).ToArray();
                inventory.Add(new { Key = key.ToString(), Fields = fields });
            }
            return new(1, keys.Count, Digest(inventory));
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try
            {
                if (factory is not null) await factory.DisposeAsync();
            }
            finally
            {
                try { connection?.Dispose(); }
                finally
                {
                    var id = redis.Id;
                    await redis.DisposeAsync();
                    Console.WriteLine($"Accounting receipt proof owned Redis disposed: {id}");
                }
            }
        }

        public sealed class ReceiptFactory(AccountingBoundaryHttpFixture fixture, ReceiptHost host) : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Jwt:Issuer", Issuer);
                builder.UseSetting("Jwt:Audience", Audience);
                builder.UseSetting("Jwt:PublicKey", Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.rsa.ExportSubjectPublicKeyInfoPem())));
                builder.UseSetting("Cache:RedisEnabled", "true");
                builder.UseSetting("Cache:AllowInMemoryFallback", "false");
                builder.UseSetting("ConnectionStrings:redis", $"{host.redis.Hostname}:{host.redis.GetMappedPublicPort(6379)},abortConnect=true");
                foreach (var (context, connectionString) in fixture.connections)
                    builder.UseSetting($"ConnectionStrings:{context}", connectionString);
                builder.ConfigureTestServices(services =>
                {
                    // AddStandardCache creates this owned multiplexer eagerly. Retain it even if later host initialization fails.
                    host.connection = services.Single(descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer))
                        .ImplementationInstance as IConnectionMultiplexer
                        ?? throw new InvalidOperationException("Receipt proof requires the production Redis multiplexer registration.");
                    var iam = new Mock<IIamServiceClient>(MockBehavior.Strict);
                    iam.Setup(value => value.CheckPermissionLiveAsync(It.IsAny<string>(), It.IsAny<string>(),
                            It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                        .Returns((string subject, string permission, string? _, CancellationToken _) =>
                        {
                            fixture.LiveChecks.Enqueue((subject, permission));
                            return Task.FromResult(fixture.grants.TryGetValue(subject, out var granted) && granted.Contains(permission));
                        });
                    services.RemoveAll<IIamServiceClient>();
                    services.AddSingleton(iam.Object);
                    services.AddSingleton<ILoggerProvider>(new FailureMetadataProvider(fixture));
                    // Preserve production typed clients and delegation handlers. Deny any attempted HTTP egress.
                    services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(
                        handlers => handlers.PrimaryHandler = new ReceiptOutboundHandler(host)));
                });
            }
        }

        private sealed class ReceiptOutboundHandler(ReceiptHost host) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref host.outboundCalls);
                // Never retain URLs, request bodies, credentials or response text.
                return Task.FromException<HttpResponseMessage>(new InvalidOperationException("Receipt rejection proof forbids outbound HTTP."));
            }
        }
    }

    private sealed class Factory(AccountingBoundaryHttpFixture fixture, string environment, TimeProvider? clock = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Jwt:Issuer", Issuer);
            builder.UseSetting("Jwt:Audience", Audience);
            builder.UseSetting("Jwt:PublicKey", Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.rsa.ExportSubjectPublicKeyInfoPem())));
            // No replay claim: requests omit Idempotency-Key and reads/writes go to actual PostgreSQL.
            builder.UseSetting("Cache:RedisEnabled", "false");
            foreach (var (context, connection) in fixture.connections)
                builder.UseSetting($"ConnectionStrings:{context}", connection);
            builder.ConfigureTestServices(services =>
            {
                if (clock is not null)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(clock);
                    var descriptor = services.Last(value => value.ServiceType == typeof(IDistributedCache));
                    services.RemoveAll<IDistributedCache>();
                    services.Add(new ServiceDescriptor(typeof(IDistributedCache), provider =>
                    {
                        var inner = (IDistributedCache)(descriptor.ImplementationInstance
                            ?? descriptor.ImplementationFactory?.Invoke(provider)
                            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
                        return fixture.summaryCache = new SummaryCacheObserver(inner, descriptor.ImplementationInstance is null);
                    }, descriptor.Lifetime));
                }
                var iam = new Mock<IIamServiceClient>(MockBehavior.Strict);
                iam.Setup(value => value.CheckPermissionLiveAsync(It.IsAny<string>(), It.IsAny<string>(),
                        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .Returns((string subject, string permission, string? _, CancellationToken _) =>
                    {
                        fixture.LiveChecks.Enqueue((subject, permission));
                        return Task.FromResult(fixture.grants.TryGetValue(subject, out var granted) && granted.Contains(permission));
                    });
                services.RemoveAll<IIamServiceClient>();
                services.AddSingleton(iam.Object);
                services.AddSingleton<ILoggerProvider>(new FailureMetadataProvider(fixture));
            });
        }
    }

    // Observe the actual configured cache; do not replace its provider or summary behavior.
    private sealed class SummaryCacheObserver(IDistributedCache inner, bool ownsInner) : IDistributedCache, IDisposable
    {
        private readonly ConcurrentQueue<string> operations = new();
        private const string Probe = "summary-unique-id-regression-probe";
        private bool initialized;

        public async Task<string> SnapshotAsync()
        {
            if (!initialized)
            {
                await inner.SetAsync(Probe, [11, 22, 33], new DistributedCacheEntryOptions());
                initialized = true;
            }

            return Digest(new { Operations = operations.ToArray(), Probe = await inner.GetAsync(Probe) });
        }

        private void Record(string operation, string key) => operations.Enqueue(operation + ":" + Digest(key));
        public byte[]? Get(string key) { Record("get", key); return inner.Get(key); }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) { Record("get", key); return inner.GetAsync(key, token); }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { Record("set", key); inner.Set(key, value, options); }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { Record("set", key); return inner.SetAsync(key, value, options, token); }
        public void Refresh(string key) { Record("refresh", key); inner.Refresh(key); }
        public Task RefreshAsync(string key, CancellationToken token = default) { Record("refresh", key); return inner.RefreshAsync(key, token); }
        public void Remove(string key) { Record("remove", key); inner.Remove(key); }
        public Task RemoveAsync(string key, CancellationToken token = default) { Record("remove", key); return inner.RemoveAsync(key, token); }
        public void Dispose() { if (ownsInner && inner is IDisposable disposable) disposable.Dispose(); }
    }

    private sealed class FailureMetadataProvider(AccountingBoundaryHttpFixture fixture) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new FailureMetadataLogger(fixture);
        public void Dispose() { }
    }

    private sealed class FailureMetadataLogger(AccountingBoundaryHttpFixture fixture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Never render messages, row values, connection strings, keys or tokens.
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                // Private middleware intentionally logs ExceptionType as state with no exception object.
                foreach (var entry in values.Where(entry => entry.Key == "ExceptionType"))
                    if (fixture.FailureMetadata.Count < 12 && entry.Value is string type)
                        fixture.FailureMetadata.Enqueue(type);
            }

            for (var depth = 0; exception is not null && depth < 5; depth++, exception = exception.InnerException)
            {
                if (fixture.FailureMetadata.Count >= 12) break;
                fixture.FailureMetadata.Enqueue(exception.GetType().Name
                    + (exception is PostgresException postgresException ? $":SQLSTATE={postgresException.SqlState}" : ""));
            }
        }
    }
}
