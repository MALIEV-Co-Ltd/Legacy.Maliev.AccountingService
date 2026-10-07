using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.AccountingService.Tests.Fixtures;

/// <summary>Disposable Payment database and real registered Accounting HTTP pipeline.</summary>
public sealed class PaymentListHttpFixture : IAsyncLifetime
{
    private const string Issuer = "https://payment-list-proof.invalid";
    private const string Audience = "payment-list-proof";
    private const string Subject = "service:payment-list-proof";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly RSA rsa = RSA.Create(2048);
    private readonly Dictionary<string, string> connections = [];
    private readonly HashSet<string> ownedDatabases = [];
    private IModel? historicalReadModel;
    private Factory? factory;
    private int disposed;

    public bool AllowLive { get; set; } = true;

    public async Task InitializeAsync()
    {
        try
        {
            await postgres.StartAsync().WaitAsync(TimeSpan.FromMinutes(2));
            var authority = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString());
            if (string.IsNullOrWhiteSpace(postgres.Id)
                || authority.Host is not ("127.0.0.1" or "localhost")
                || authority.Port != postgres.GetMappedPublicPort(5432))
            {
                throw new InvalidOperationException("Payment-list proof requires its own loopback Testcontainer.");
            }

            await using var control = new NpgsqlConnection(authority.ConnectionString);
            await control.OpenAsync();
            var run = Guid.NewGuid().ToString("N");
            foreach (var context in new[] { "PaymentDbContext", "InvoiceDbContext", "ReceiptDbContext" })
            {
                var name = $"payment_list_{run}_{context.ToLowerInvariant()}";
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", control);
                await command.ExecuteNonQueryAsync();
                ownedDatabases.Add(name);
                connections.Add(context, new NpgsqlConnectionStringBuilder(authority.ConnectionString)
                {
                    Database = name,
                    Pooling = false,
                }.ConnectionString);
            }

            await using (var database = Database())
            {
                await database.Database.MigrateAsync();
                database.Directions.AddRange(
                    new PaymentDirection { Id = 1, Name = "Alpha", Description = "Synthetic" },
                    new PaymentDirection { Id = 2, Name = "Bravo", Description = "Synthetic" },
                    new PaymentDirection { Id = 3, Name = "Charlie", Description = "Synthetic" });
                database.Methods.AddRange(
                    new PaymentMethod { Id = 1, Name = "Alpha", Description = "Synthetic" },
                    new PaymentMethod { Id = 2, Name = "Bravo", Description = "Synthetic" },
                    new PaymentMethod { Id = 3, Name = "Charlie", Description = "Synthetic" });
                database.Types.AddRange(
                    new PaymentType { Id = 1, Name = "Alpha", Description = "Synthetic" },
                    new PaymentType { Id = 2, Name = "Bravo", Description = "Synthetic" },
                    new PaymentType { Id = 3, Name = "Charlie", Description = "Synthetic" });
                await database.SaveChangesAsync();
            }

            factory = new Factory(this);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public PaymentDbContext Database() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(connections["PaymentDbContext"]).Options);

    public async Task SeedAsync(params Payment[] rows)
    {
        AllowLive = true;
        await using var database = Database();
        await database.Payments.ExecuteDeleteAsync();
        // Every test starts from the same lookup names, including after nullable-name regressions.
        var names = new[] { "Alpha", "Bravo", "Charlie" };
        foreach (var direction in await database.Directions.ToListAsync())
        {
            direction.Name = names[direction.Id - 1];
        }

        foreach (var method in await database.Methods.ToListAsync())
        {
            method.Name = names[method.Id - 1];
        }

        foreach (var type in await database.Types.ToListAsync())
        {
            type.Name = names[type.Id - 1];
        }

        database.Payments.AddRange(rows);
        await database.SaveChangesAsync();
    }

    public async Task WithHistoricalNullableLookupAsync(string sort, Func<Task> assertion)
    {
        var table = sort switch
        {
            var value when value.StartsWith("PaymentDirection_", StringComparison.Ordinal) => "PaymentDirection",
            var value when value.StartsWith("PaymentMethod_", StringComparison.Ordinal) => "PaymentMethod",
            var value when value.StartsWith("PaymentType_", StringComparison.Ordinal) => "PaymentType",
            _ => throw new ArgumentException("Require a historical lookup sort.", nameof(sort)),
        };
        await using var current = Database();
        var prior = new Legacy.Maliev.AccountingService.Data.Migrations.Payment.RequirePaymentFileMetadata().TargetModel;
        await using var historical = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(connections["PaymentDbContext"])
            .ReplaceService<IModelCustomizer, HistoricalLookupModelCustomizer>().Options);
        var readModel = historical.Model;
        foreach (var entity in new[] { typeof(PaymentDirection), typeof(PaymentMethod), typeof(PaymentType) })
        {
            if (current.Model.FindEntityType(entity)?.FindProperty("Name")?.IsNullable != false
                || readModel.FindEntityType(entity)?.FindProperty("Name")?.IsNullable != true
                || prior.FindEntityType(entity.FullName!)?.FindProperty("Name")?.IsNullable != true)
            {
                throw new InvalidOperationException("Historical read scope requires the exact nullable prior model and required modern model.");
            }
        }
        foreach (var entity in current.Model.GetEntityTypes())
        {
            var historicalEntity = readModel.FindEntityType(entity.Name)
                ?? throw new InvalidOperationException("Historical read model lost a typed entity.");
            if (historicalEntity.ClrType != entity.ClrType
                || historicalEntity.GetProperties().Count() != entity.GetProperties().Count())
            {
                throw new InvalidOperationException("Historical read model changed its typed property graph.");
            }
            foreach (var property in entity.GetProperties())
            {
                var historicalProperty = historicalEntity.FindProperty(property.Name)
                    ?? throw new InvalidOperationException("Historical read model lost a typed property.");
                var type = entity.ClrType;
                var historicalName = property.Name == "Name"
                    && (type == typeof(PaymentDirection) || type == typeof(PaymentMethod) || type == typeof(PaymentType));
                if (historicalProperty.IsNullable != (historicalName || property.IsNullable)
                    || historicalProperty.ClrType != property.ClrType
                    || historicalProperty.PropertyInfo?.DeclaringType != property.PropertyInfo?.DeclaringType
                    || historicalProperty.GetColumnType() != property.GetColumnType()
                    || historicalProperty.GetMaxLength() != property.GetMaxLength())
                {
                    throw new InvalidOperationException("Historical read model changed metadata beyond lookup Name nullability.");
                }
            }
        }
        var relaxed = false;
        try
        {
            if (factory is not null) await factory.DisposeAsync();
            historicalReadModel = readModel;
            // The CLR-bound historical read model retains SQL NULL predicates; modern write metadata stays required.
            await ChangeHistoricalNameSchemaAsync(table, restore: false);
            relaxed = true;
            factory = new Factory(this);
            await assertion();
        }
        finally
        {
            try { if (factory is not null) await factory.DisposeAsync(); }
            finally
            {
                historicalReadModel = null;
                try { if (relaxed) await ChangeHistoricalNameSchemaAsync(table, restore: true); }
                finally { factory = new Factory(this); }
            }
        }
    }

    private async Task ChangeHistoricalNameSchemaAsync(string table, bool restore)
    {
        var authority = new NpgsqlConnectionStringBuilder(connections["PaymentDbContext"]);
        if (string.IsNullOrWhiteSpace(postgres.Id) || !ownedDatabases.Contains(authority.Database)
            || authority.Host is not ("127.0.0.1" or "localhost")
            || authority.Port != postgres.GetMappedPublicPort(5432)
            || table is not ("PaymentDirection" or "PaymentMethod" or "PaymentType"))
        {
            throw new InvalidOperationException("Historical schema scope requires the exact owned disposable database.");
        }
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var connection = new NpgsqlConnection(authority.ConnectionString);
        await connection.OpenAsync(lifetime.Token);
        await using var transaction = await connection.BeginTransactionAsync(lifetime.Token);
        await using var metadata = new NpgsqlCommand("SELECT a.attnotnull FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname=@table AND a.attname='Name' AND NOT a.attisdropped", connection, transaction) { CommandTimeout = 40 };
        metadata.Parameters.AddWithValue("table", table);
        if (await metadata.ExecuteScalarAsync(lifetime.Token) is not bool required || required == restore)
        {
            throw new InvalidOperationException("Historical schema scope found an unexpected Name constraint.");
        }
        using var builder = new NpgsqlCommandBuilder();
        var quoted = builder.QuoteIdentifier(table);
        if (restore)
        {
            await using var names = new NpgsqlCommand($"UPDATE {quoted} SET \"Name\"=CASE \"ID\" WHEN 1 THEN @alpha WHEN 2 THEN @bravo WHEN 3 THEN @charlie END WHERE \"ID\" IN (1,2,3)", connection, transaction) { CommandTimeout = 40 };
            names.Parameters.AddWithValue("alpha", "Alpha");
            names.Parameters.AddWithValue("bravo", "Bravo");
            names.Parameters.AddWithValue("charlie", "Charlie");
            if (await names.ExecuteNonQueryAsync(lifetime.Token) != 3)
            {
                throw new InvalidOperationException("Historical schema scope found unexpected synthetic lookup identities.");
            }
        }
        await using var schema = new NpgsqlCommand($"ALTER TABLE {quoted} ALTER COLUMN \"Name\" {(restore ? "SET" : "DROP")} NOT NULL", connection, transaction) { CommandTimeout = 40 };
        await schema.ExecuteNonQueryAsync(lifetime.Token);
        if (await metadata.ExecuteScalarAsync(lifetime.Token) is not bool actual || actual != restore)
        {
            throw new InvalidOperationException("Historical Name constraint transition was not verified.");
        }
        await transaction.CommitAsync(lifetime.Token);
    }

    public async Task SetNullableLookupNamesAsync(string sort)
    {
        await using var database = Database();
        // The explicit historical scope makes only the selected owned Name column nullable.
        var names = new string?[] { null, "Alpha", "Charlie" };
        if (sort.StartsWith("PaymentDirection_", StringComparison.Ordinal))
        {
            foreach (var row in await database.Directions.ToListAsync())
            {
                row.Name = names[row.Id - 1]!;
            }
        }
        else if (sort.StartsWith("PaymentMethod_", StringComparison.Ordinal))
        {
            foreach (var row in await database.Methods.ToListAsync())
            {
                row.Name = names[row.Id - 1]!;
            }
        }
        else if (sort.StartsWith("PaymentType_", StringComparison.Ordinal))
        {
            foreach (var row in await database.Types.ToListAsync())
            {
                row.Name = names[row.Id - 1]!;
            }
        }
        else
        {
            throw new ArgumentException("The nullable lookup fixture requires a lookup sort.", nameof(sort));
        }

        await database.SaveChangesAsync();
    }

    public HttpClient Client(bool authenticated = true)
    {
        var client = (factory ?? throw new InvalidOperationException("Fixture has not initialized."))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (authenticated)
        {
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(Issuer, Audience,
            [
                new Claim("sub", Subject),
                new Claim("identity_kind", "service"),
                new Claim("permissions", AccountingPermissions.Read),
                new Claim("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("jti", Guid.NewGuid().ToString("D")),
            ], now.AddSeconds(-5), now.AddMinutes(5),
                new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                new JwtSecurityTokenHandler().WriteToken(token));
        }

        return client;
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (factory is not null)
            {
                await factory.DisposeAsync();
            }
        }
        finally
        {
            try
            {
                rsa.Dispose();
            }
            finally
            {
                await postgres.DisposeAsync();
            }
        }
    }

    private sealed class HistoricalLookupModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<PaymentDirection>().Property(value => value.Name).IsRequired(false);
            modelBuilder.Entity<PaymentMethod>().Property(value => value.Name).IsRequired(false);
            modelBuilder.Entity<PaymentType>().Property(value => value.Name).IsRequired(false);
        }
    }

    private sealed class Factory(PaymentListHttpFixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:Issuer", Issuer);
            builder.UseSetting("Jwt:Audience", Audience);
            builder.UseSetting("Jwt:PublicKey", Convert.ToBase64String(
                Encoding.UTF8.GetBytes(fixture.rsa.ExportSubjectPublicKeyInfoPem())));
            // Payment listing never uses the entity cache. This suite does not claim Redis acceptance.
            builder.UseSetting("Cache:RedisEnabled", "false");
            foreach (var (context, connection) in fixture.connections)
            {
                builder.UseSetting($"ConnectionStrings:{context}", connection);
            }

            builder.ConfigureTestServices(services =>
            {
                if (fixture.historicalReadModel is { } readModel)
                {
                    services.RemoveAll<PaymentDbContext>();
                    services.RemoveAll<DbContextOptions<PaymentDbContext>>();
                    services.AddDbContext<PaymentDbContext>(options => options
                        .UseNpgsql(fixture.connections["PaymentDbContext"]).UseModel(readModel));
                }
                var iam = new Mock<IIamServiceClient>(MockBehavior.Strict);
                iam.Setup(value => value.CheckPermissionLiveAsync(It.IsAny<string>(), It.IsAny<string>(),
                        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .Returns((string subject, string permission, string? _, CancellationToken _) =>
                        Task.FromResult(fixture.AllowLive && subject == Subject && permission == AccountingPermissions.Read));
                services.RemoveAll<IIamServiceClient>();
                services.AddSingleton(iam.Object);
            });
        }
    }
}
