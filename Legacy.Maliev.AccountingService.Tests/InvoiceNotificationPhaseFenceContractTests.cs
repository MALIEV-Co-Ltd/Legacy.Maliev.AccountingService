using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;
using Npgsql;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>
/// Stronger receipt-continuity contract, not a historical migration defect or provider proof.
/// These tests execute only after exact-main and root design review gates.
/// </summary>
public sealed class InvoiceNotificationPhaseFenceContractTests(InvoiceNotificationPhaseFencePostgresFixture fixture, ITestOutputHelper output)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    [Theory]
    [InlineData("RemoteState", "character varying(32)")]
    [InlineData("RemoteAdmittedAt", "timestamp with time zone")]
    [InlineData("RemoteUpdatedAt", "timestamp with time zone")]
    [InlineData("RemoteReceiptBinding", "bytea")]
    public async Task DiscoveredMigration_RetainsReceiptContinuityInsteadOfOnlyRemoteVersion(string name, string type)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        // A missing retained field cannot support replay equality or timestamp/state continuity.
        // This is executed PostgreSQL migration evidence, not reflection on a proposed API.
        var actual = await database.Database.SqlQuery<string>($"""
            SELECT pg_catalog.format_type(a.atttypid,a.atttypmod) || ':' ||
              CASE WHEN a.attnotnull THEN 'required' ELSE 'optional' END || ':' ||
              CASE WHEN a.attgenerated='' AND a.attidentity='' AND NOT a.atthasdef AND NOT a.atthasmissing
                THEN 'plain' ELSE 'unexpected' END AS "Value"
            FROM pg_catalog.pg_attribute a
            WHERE a.attrelid='public."InvoiceNotificationCorrelation"'::regclass
              AND a.attnum>0 AND NOT a.attisdropped AND a.attname={name}
            """).SingleOrDefaultAsync();
        Assert.Equal(type + ":optional:plain", actual);
    }

    [Fact]
    public async Task AcceptedFoundation_MissingReadDoesNotCreateCorrelationAuthority()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var store = new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new NoKeys());
        Assert.Null(await store.ReadAsync(Guid.Parse("10000000-0000-0000-0000-000000000001"), CancellationToken.None));
        Assert.Empty(await database.InvoiceNotificationCorrelations.AsNoTracking().ToListAsync());
        // Null is not proof that no remote submission happened and never grants a send permit.
    }

    private sealed class NoKeys : IInvoiceNotificationBindingKeyring
    {
        public string ActiveKeyId => "unconfigured";
        public ReadOnlyMemory<byte>? Find(string keyId) => null;
    }

    [Fact]
    public async Task DiscoveredReceiptMigration_HasExactValidatedChecksAndNoPendingModel()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var checks = await database.Database.SqlQueryRaw<string>("""
            SELECT conname || ':' || pg_get_expr(conbin,conrelid,true) AS "Value" FROM pg_constraint
            WHERE conrelid='public."InvoiceNotificationCorrelation"'::regclass AND contype='c' ORDER BY conname
            """).ToListAsync();
        Assert.Equal(new[] { "CK_InvoiceNotificationCorrelation_Identity", "CK_InvoiceNotificationCorrelation_Receipt",
            "CK_InvoiceNotificationCorrelation_ReceiptPhase", "CK_InvoiceNotificationCorrelation_State" }, checks.Select(value => value[..value.IndexOf(':')]));
        foreach (var check in checks)
            output.WriteLine(check[..check.IndexOf(':')] + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(check[(check.IndexOf(':') + 1)..]))));
        Assert.False(database.Database.HasPendingModelChanges());
        await database.Database.MigrateAsync();
        Assert.Contains("20261001133820_RetainInvoiceNotificationReceipt", await database.Database.GetAppliedMigrationsAsync());
    }

    [Theory]
    [InlineData("missing-state")]
    [InlineData("missing-version")]
    [InlineData("missing-admitted")]
    [InlineData("missing-updated")]
    [InlineData("missing-binding")]
    [InlineData("binding31")]
    [InlineData("backwards")]
    [InlineData("phase")]
    [InlineData("version4")]
    [InlineData("admitted2")]
    [InlineData("rejected")]
    public async Task PhysicalReceiptAuthority_RejectsMalformedOrUnsupportedEvidence(string field)
    {
        await using var database = await fixture.NewDatabaseAsync();
        var row = Row();
        row.Phase = "ProviderAccepted";
        row.AdmissionIssuedAt = row.CreatedAt;
        row.ExecutionIssuedAt = row.CreatedAt;
        row.RemoteVersion = 3;
        row.RemoteState = "providerAccepted";
        row.RemoteAdmittedAt = row.CreatedAt;
        row.RemoteUpdatedAt = row.CreatedAt;
        row.RemoteReceiptBinding = new byte[32];
        switch (field)
        {
            case "missing-state": row.RemoteState = null; break;
            case "missing-version": row.RemoteVersion = null; break;
            case "missing-admitted": row.RemoteAdmittedAt = null; break;
            case "missing-updated": row.RemoteUpdatedAt = null; break;
            case "missing-binding": row.RemoteReceiptBinding = null; break;
            case "binding31": row.RemoteReceiptBinding = new byte[31]; break;
            case "backwards": row.RemoteUpdatedAt = row.CreatedAt.AddSeconds(-1); break;
            case "phase": row.RemoteState = "admitted"; row.RemoteVersion = 1; break;
            case "version4": row.RemoteVersion = 4; break;
            case "admitted2": row.Phase = "Admitted"; row.ExecutionIssuedAt = null; row.RemoteState = "admitted"; row.RemoteVersion = 2; break;
            case "rejected": row.Phase = "RejectedBeforeSubmission"; row.RemoteState = "rejectedBeforeSubmission"; break;
        }
        database.InvoiceNotificationCorrelations.Add(row);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
        Assert.StartsWith("CK_InvoiceNotificationCorrelation_Receipt", postgres.ConstraintName);
    }

    private static InvoiceNotificationCorrelationRow Row() => new()
    {
        IntentId = Guid.NewGuid(),
        InvoiceId = 42,
        Purpose = "invoice-issued",
        QuotationId = 7,
        WorkflowOperationId = Guid.NewGuid(),
        OriginIssuer = "https://origin.example.test",
        OriginEmployeeSubject = "employee:fixture",
        OriginServiceSubject = "service:legacy-intranet",
        SenderIssuer = "https://auth.example.test",
        SenderServiceSubject = "service:legacy-accounting",
        PayloadFrameVersion = "notification-payload-v1",
        BindingVersion = "accounting-invoice-notification-hmac-v1",
        BindingKeyId = "fixture-1",
        PayloadBinding = new byte[32],
        Phase = "Prepared",
        Version = 1,
        CreatedAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)
    };

    [Theory]
    [InlineData("Prepared")]
    [InlineData("AdmissionIssued")]
    public async Task AdditiveReceiptMigration_PreservesCompatibleOldAuthorityAndRefusesDown(string phase)
    {
        await using var database = await fixture.NewDatabaseAsync("20261001105037_AddInvoiceNotificationCorrelation");
        var row = Row();
        // Old shape intentionally has no receipt columns yet: insert using its original scalar shape.
        await InsertOldRow(database, row, phase, null);
        await database.Database.MigrateAsync();
        var persisted = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        Assert.Equal(row.IntentId, persisted.IntentId);
        Assert.Equal(phase, persisted.Phase);
        Assert.Null(persisted.RemoteVersion);
        Assert.Null(persisted.RemoteState);
        Assert.Null(persisted.RemoteAdmittedAt);
        Assert.Null(persisted.RemoteUpdatedAt);
        Assert.Null(persisted.RemoteReceiptBinding);
        await Assert.ThrowsAsync<NotSupportedException>(() => database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
            .MigrateAsync("20261001105037_AddInvoiceNotificationCorrelation"));
        Assert.Contains("20261001133820_RetainInvoiceNotificationReceipt", await database.Database.GetAppliedMigrationsAsync());
        Assert.Single(await database.InvoiceNotificationCorrelations.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("ProviderAccepted")]
    [InlineData("RejectedBeforeSubmission")]
    public async Task UnsupportedOldReceiptLineage_StopsMigrationAtomicallyWithoutBackfill(string phase)
    {
        await using var database = await fixture.NewDatabaseAsync("20261001105037_AddInvoiceNotificationCorrelation");
        var row = Row();
        await InsertOldRow(database, row, phase, 3);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => database.Database.MigrateAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.DoesNotContain("20261001133820_RetainInvoiceNotificationReceipt", await database.Database.GetAppliedMigrationsAsync());
        var columns = await database.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_attribute WHERE attrelid='public."InvoiceNotificationCorrelation"'::regclass
             AND attnum>0 AND NOT attisdropped
            """).SingleAsync();
        Assert.Equal(21, columns);
        Assert.Equal(phase, await database.Database.SqlQueryRaw<string>("SELECT \"Phase\" AS \"Value\" FROM public.\"InvoiceNotificationCorrelation\"").SingleAsync());
        Assert.Equal(3, await database.Database.SqlQueryRaw<long>("SELECT \"RemoteVersion\" AS \"Value\" FROM public.\"InvoiceNotificationCorrelation\"").SingleAsync());
    }

    [Fact]
    public async Task DroppedReceiptCheckAndMalformedQuartet_ReadFailsClosedWithoutDisclosure()
    {
        await using var database = await fixture.NewDatabaseAsync();
        await database.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"InvoiceNotificationCorrelation\" DROP CONSTRAINT \"CK_InvoiceNotificationCorrelation_Receipt\"");
        var row = Row();
        row.RemoteState = "admitted"; // Deliberately corrupt authority after adversarial schema drift.
        database.InvoiceNotificationCorrelations.Add(row);
        await database.SaveChangesAsync();
        var store = new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new NoKeys());
        await Assert.ThrowsAsync<Legacy.Maliev.AccountingService.Application.Models.InvoiceNotificationCorrelationUnavailableException>(() =>
            store.ReadAsync(row.IntentId, CancellationToken.None));
        Assert.Equal("admitted", (await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync()).RemoteState);
    }

    private static Task InsertOldRow(InvoiceDbContext database, InvoiceNotificationCorrelationRow row, string phase, long? remote)
    {
        DateTimeOffset? admission = phase == "Prepared" ? null : row.CreatedAt;
        DateTimeOffset? execution = phase == "ProviderAccepted" ? row.CreatedAt : null;
        return database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."InvoiceNotificationCorrelation"
             ("IntentID","InvoiceID","Purpose","QuotationID","WorkflowOperationID","OriginIssuer","OriginEmployeeSubject",
              "OriginServiceSubject","SenderIssuer","SenderServiceSubject","PayloadFrameVersion","BindingVersion","BindingKeyID",
              "PayloadBinding","Phase","Version","RemoteVersion","CreatedAt","UpdatedAt","AdmissionIssuedAt","ExecutionIssuedAt")
            VALUES ({row.IntentId},{row.InvoiceId},{row.Purpose},{row.QuotationId},{row.WorkflowOperationId},{row.OriginIssuer},
             {row.OriginEmployeeSubject},{row.OriginServiceSubject},{row.SenderIssuer},{row.SenderServiceSubject},{row.PayloadFrameVersion},
             {row.BindingVersion},{row.BindingKeyId},{row.PayloadBinding},{phase},1,{remote},{row.CreatedAt},{row.UpdatedAt},{admission},{execution})
            """);
    }
}

/// <summary>One class-owned disposable container; all current cases are read-only after migration.</summary>
public sealed class InvoiceNotificationPhaseFencePostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    public IHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:InvoiceDbContext"] = postgres.GetConnectionString(),
        });
        builder.AddPostgresDbContext<InvoiceDbContext>(connectionName: "InvoiceDbContext");
        Host = builder.Build();
        using var scope = Host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        Assert.True(database.Database.CreateExecutionStrategy().RetriesOnFailure);
        await database.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        Host?.Dispose();
        await postgres.DisposeAsync();
    }

    public async Task<InvoiceDbContext> NewDatabaseAsync(string? targetMigration = null)
    {
        var name = "phase_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE DATABASE " + name, connection);
            await command.ExecuteNonQueryAsync();
        }
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = name }.ConnectionString;
        var database = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql(connectionString).Options);
        if (targetMigration is null) await database.Database.MigrateAsync();
        else await database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(targetMigration);
        return database;
    }
}
