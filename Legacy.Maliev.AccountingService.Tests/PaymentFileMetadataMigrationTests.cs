using System.Diagnostics;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;

namespace Legacy.Maliev.AccountingService.Tests;

// Off-repository draft. Requires the coordinated model/migrations/designers/snapshots bundle.
// No native execution or acceptance is represented by this source file.
public sealed class PaymentFileMetadataMigrationTests(InvoiceNotificationPhaseFencePostgresFixture fixture)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    private const string Target = "20261006180000_RequirePaymentFileMetadata";
    private const string SourceLengthSql = """
        char_length("Bucket") + char_length(regexp_replace("Bucket" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50
        """;
    private static readonly DateTime RetainedDate = new(2026, 7, 1, 2, 3, 4, DateTimeKind.Unspecified);
    private static readonly DateTime PaymentDateUtc = new(2026, 7, 1, 2, 3, 4, DateTimeKind.Utc);

    [Fact]
    public void RuntimeModelMatchesSnapshotAndExactSourceFileMetadata()
    {
        using var context = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql("Host=127.0.0.1;Database=model_only;Username=synthetic").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PaymentFile));
        Assert.NotNull(entity);
        var bucket = entity.FindProperty("Bucket");
        var objectName = entity.FindProperty("ObjectName");
        Assert.NotNull(bucket);
        Assert.NotNull(objectName);
        Assert.False(bucket.IsNullable);
        Assert.False(objectName.IsNullable);
        Assert.Equal(50, bucket.GetMaxLength());
        Assert.Equal("text", bucket.GetColumnType());
        Assert.Equal("text", objectName.GetColumnType());
        var check = Assert.Single(entity.GetCheckConstraints());
        Assert.Equal($"CK_{Table()}_BucketLength", check.Name);
        Assert.Equal(SourceLengthSql, check.Sql);
    }

    [Theory]
    [InlineData("bmp50", true)]
    [InlineData("bmp51", false)]
    [InlineData("supplementary50", true)]
    [InlineData("supplementary52", false)]
    [InlineData("mixed50", true)]
    [InlineData("mixed51", false)]
    public async Task UpgradeUsesUtf16BoundaryAndNeverChangesRetainedRows(string variant, bool allowed)
    {
        await using var context = await PreimageAsync();
        var bucket = variant switch
        {
            "bmp50" => new string('ก', 50),
            "bmp51" => new string('ก', 51),
            "supplementary50" => string.Concat(Enumerable.Repeat("😀", 25)),
            "supplementary52" => string.Concat(Enumerable.Repeat("😀", 26)),
            "mixed50" => string.Concat(Enumerable.Repeat("😀", 24)) + " ก",
            "mixed51" => string.Concat(Enumerable.Repeat("😀", 24)) + " ก ",
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        Assert.Equal(allowed, bucket.Length <= 50);
        await SeedAsync(context, bucket, " folder/ใบเสร็จ sample.pdf ");
        var before = await SnapshotAsync(context);
        if (allowed)
        {
            await MigrateAsync(context, Target);
            Assert.Equal(before.Rows, (await SnapshotAsync(context)).Rows);
            await AssertInstalledAsync(context);
            var after = await SnapshotAsync(context);
            var invalid = string.Concat(Enumerable.Repeat("😀", 26));
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => InsertFileAsync(context, 8, invalid, "object"));
            Assert.Equal("23514", rejected.SqlState);
            Assert.Equal(after, await SnapshotAsync(context));
            await MigrateAsync(context, Target);
            Assert.Equal(after, await SnapshotAsync(context));
        }
        else
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target));
            Assert.Equal("P0001", failure.SqlState);
            Assert.Equal(before, await SnapshotAsync(context));
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HistoricalNullsBlockAtomicallyWithoutReconciliation(bool nullBucket, bool nullObject)
    {
        await using var context = await PreimageAsync();
        await SeedAsync(context, nullBucket ? null : " bucket ", nullObject ? null : " path ");
        var before = await SnapshotAsync(context);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target));
        Assert.Equal("P0001", failure.SqlState);
        Assert.Equal(before, await SnapshotAsync(context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedPreimageOrConflictingCheckRefusesChanges(bool conflictingCheck)
    {
        await using var context = await PreimageAsync();
        await SeedAsync(context, "bucket", "object");
        var table = QuoteIdentifier(Table());
        var check = QuoteIdentifier($"CK_{Table()}_BucketLength");
        var mutation = conflictingCheck
            ? $"ALTER TABLE {table} ADD CONSTRAINT {check} CHECK (true)"
            : $"ALTER TABLE {table} ALTER COLUMN \"Bucket\" SET NOT NULL";
        await ExecuteOwnedSqlAsync(context, mutation);
        var before = await SnapshotAsync(context);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target));
        Assert.Equal("P0001", failure.SqlState);
        Assert.Equal(before, await SnapshotAsync(context));
    }

    [Fact]
    public async Task WriterFenceTimesOutAndReleasesItsFailedMigrationTransaction()
    {
        await using var context = await PreimageAsync();
        await SeedAsync(context, "bucket", "object");
        var before = await SnapshotAsync(context);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var writer = new NpgsqlConnection(context.Database.GetConnectionString());
        await writer.OpenAsync(lifetime.Token);
        await using var transaction = await writer.BeginTransactionAsync(lifetime.Token);
        try
        {
            await using var command = new NpgsqlCommand($"LOCK TABLE {QuoteIdentifier(Table())} IN ROW EXCLUSIVE MODE", writer, transaction)
            {
                CommandTimeout = 10,
            };
            await command.ExecuteNonQueryAsync(lifetime.Token);
            var clock = Stopwatch.StartNew();
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => MigrateAsync(context, Target));
            var provider = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal("55P03", provider.SqlState);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(15));
            Assert.Equal(before, await SnapshotAsync(context));
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await MigrateAsync(context, Target);
        Assert.Equal(before.Rows, (await SnapshotAsync(context)).Rows);
        await AssertInstalledAsync(context);
    }

    [Fact]
    public async Task EmptyMetadataRemainsValidInStorageAndDownOnlyRelaxesNewConstraints()
    {
        await using var context = await PreimageAsync();
        await SeedAsync(context, "", "");
        var before = await SnapshotAsync(context);
        await MigrateAsync(context, Target);
        await AssertInstalledAsync(context);
        await MigrateAsync(context, Previous());
        Assert.Equal(before, await SnapshotAsync(context));
    }

    [Fact]
    public async Task StatementDeadlineRollsBackOwnedDdlAndRetainsHistory()
    {
        await using var context = await PreimageAsync();
        await SeedAsync(context, "bucket", "object");
        var before = await SnapshotAsync(context);
        var suffix = Guid.NewGuid().ToString("N");
        var function = "meta_sleep_" + suffix;
        var trigger = "meta_delay_" + suffix;
        var functionIdentifier = QuoteIdentifier(function);
        var triggerIdentifier = QuoteIdentifier(trigger);
        try
        {
            // Exact disposable-database helpers induce a server deadline without a large stress dataset.
            await ExecuteOwnedSqlAsync(context, $"CREATE FUNCTION {functionIdentifier}() RETURNS event_trigger LANGUAGE plpgsql AS $$ BEGIN PERFORM pg_sleep(31); END; $$");
            await ExecuteOwnedSqlAsync(context, $"CREATE EVENT TRIGGER {triggerIdentifier} ON ddl_command_start WHEN TAG IN ('ALTER TABLE') EXECUTE FUNCTION {functionIdentifier}()");
            var clock = Stopwatch.StartNew();
            var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target));
            Assert.Equal("57014", failure.SqlState);
            Assert.Contains("statement timeout", failure.MessageText, StringComparison.Ordinal);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(28), TimeSpan.FromSeconds(40));
            Assert.Equal(before, await SnapshotAsync(context));
        }
        finally
        {
            try
            {
                await ExecuteOwnedSqlAsync(context, $"DROP EVENT TRIGGER IF EXISTS {triggerIdentifier}");
            }
            finally
            {
                await ExecuteOwnedSqlAsync(context, $"DROP FUNCTION IF EXISTS {functionIdentifier}()");
            }
        }
        Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_event_trigger WHERE evtname={trigger}").SingleAsync());
        Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_proc WHERE proname={function}").SingleAsync());
        await MigrateAsync(context, Target);
        Assert.Equal(before.Rows, (await SnapshotAsync(context)).Rows);
        await AssertInstalledAsync(context);
    }

    private async Task<DbContext> PreimageAsync()
    {
        // Reuse the fixture's exact owned PostgreSQL container; each case owns a new database.
        var database = await fixture.NewDatabaseAsync("0");
        DbContext context = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql(database.Database.GetConnectionString()).Options);
        await database.DisposeAsync();
        try
        {
            context.Database.SetCommandTimeout(40);
            await MigrateAsync(context, Previous());
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private static async Task SeedAsync(DbContext context, string? bucket, string? objectName)
    {
        context.AddRange(new PaymentDirection { Id = 91, Name = "Synthetic direction" },
            new PaymentMethod { Id = 91, Name = "Synthetic method" }, new PaymentType { Id = 91, Name = "Synthetic type" });
        context.Add(new Payment
        {
            Id = 91,
            PaymentDirectionId = 91,
            PaymentMethodId = 91,
            PaymentTypeId = 91,
            Amount = 1m,
            EmployeeId = 42,
            CurrencyId = 42,
            PaymentDate = PaymentDateUtc,
        });
        await context.SaveChangesAsync();
        await InsertFileAsync(context, 7, bucket, objectName);
        context.ChangeTracker.Clear();
    }

    private static async Task InsertFileAsync(DbContext context, int id, string? bucket, string? objectName)
    {
        // Bypass the new EF maximum-length mapping when arranging historical invalid rows.
        // Explicit unrestricted text parameters must preserve the original UTF-16 input.
        const string parent = "PaymentID";
        await ExecuteOwnedSqlAsync(context,
            $"INSERT INTO {QuoteIdentifier(Table())} (\"ID\",{QuoteIdentifier(parent)},\"Bucket\",\"ObjectName\",\"CreatedDate\",\"ModifiedDate\") VALUES (@id,91,@bucket,@object,@retained,@retained)",
            new NpgsqlParameter[]
            {
                new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = id },
                new NpgsqlParameter("bucket", NpgsqlDbType.Text) { Value = (object?)bucket ?? DBNull.Value, Size = 0 },
                new NpgsqlParameter("object", NpgsqlDbType.Text) { Value = (object?)objectName ?? DBNull.Value, Size = 0 },
                new NpgsqlParameter("retained", NpgsqlDbType.Timestamp) { Value = RetainedDate },
            });
    }

    private static async Task<Snapshot> SnapshotAsync(DbContext context)
    {
        var table = Table();
        const string rowsSql = "SELECT jsonb_agg(to_jsonb(f) ORDER BY f.\"ID\")::text AS \"Value\" FROM \"PaymentFile\" f";
        var relation = QuoteIdentifier(table);
        var rows = await context.Database.SqlQueryRaw<string>(rowsSql).SingleAsync();
        var schema = await context.Database.SqlQuery<string>($"SELECT jsonb_agg(to_jsonb(c) ORDER BY c.ordinal_position)::text AS \"Value\" FROM information_schema.columns c WHERE c.table_schema='public' AND c.table_name={table}").SingleAsync();
        var constraints = await context.Database.SqlQuery<string>($"SELECT COALESCE(jsonb_agg(pg_get_constraintdef(c.oid) ORDER BY c.conname)::text,'[]') AS \"Value\" FROM pg_constraint c WHERE c.conrelid={relation}::regclass").SingleAsync();
        var history = string.Join("\n", (await context.Database.GetAppliedMigrationsAsync()).Order(StringComparer.Ordinal));
        return new Snapshot(rows, schema, constraints, history);
    }

    private static async Task AssertInstalledAsync(DbContext context)
    {
        Assert.Contains(Target, await context.Database.GetAppliedMigrationsAsync());
        var table = Table();
        var relation = QuoteIdentifier(table);
        var check = $"CK_{table}_BucketLength";
        Assert.Equal(2, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_attribute WHERE attrelid={relation}::regclass AND attname IN ('Bucket','ObjectName') AND attnotnull AND atttypid='text'::regtype AND NOT attisdropped").SingleAsync());
        Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conrelid={relation}::regclass AND conname={check} AND contype='c' AND convalidated").SingleAsync());
    }

    private static async Task MigrateAsync(DbContext context, string target)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await context.GetService<IMigrator>().MigrateAsync(target, lifetime.Token);
    }

    private static async Task ExecuteOwnedSqlAsync(DbContext context, string sql, params NpgsqlParameter[] parameters)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(lifetime.Token);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 40 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(lifetime.Token);
    }

    private static string QuoteIdentifier(string name)
    {
        var known = name is "PaymentFile" or "PaymentID" or "CK_PaymentFile_BucketLength";
        var prefix = name.StartsWith("meta_sleep_", StringComparison.Ordinal) ? "meta_sleep_" : "meta_delay_";
        var generated = name.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(name[prefix.Length..], "N", out var identity)
            && identity != Guid.Empty && name == prefix + identity.ToString("N");
        if (!known && !generated) throw new ArgumentException("Unexpected test-owned SQL identifier.", nameof(name));
        using var builder = new NpgsqlCommandBuilder();
        return builder.QuoteIdentifier(name);
    }

    private static string Table() => "PaymentFile";
    private static string Previous() => "20260721024322_FixTimestampColumnType";
    private sealed record Snapshot(string Rows, string Schema, string Constraints, string History);
}
