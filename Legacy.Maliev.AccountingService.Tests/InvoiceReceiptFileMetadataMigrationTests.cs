using System.Diagnostics;
using Legacy.Maliev.AccountingService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;
using InvoiceRecord = Legacy.Maliev.AccountingService.Domain.Invoice.Invoice;
using InvoiceFile = Legacy.Maliev.AccountingService.Domain.Invoice.InvoiceFile;
using ReceiptRecord = Legacy.Maliev.AccountingService.Domain.Receipt.Receipt;
using ReceiptFile = Legacy.Maliev.AccountingService.Domain.Receipt.ReceiptFile;

namespace Legacy.Maliev.AccountingService.Tests;

// Off-repository draft. Requires the coordinated model/migrations/designers/snapshots bundle.
// No native execution or acceptance is represented by this source file.
public sealed class InvoiceReceiptFileMetadataMigrationTests(InvoiceNotificationPhaseFencePostgresFixture fixture)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    private const string Target = "20261006160000_RequireFileMetadata";
    private const string SourceLengthSql = """
        char_length("Bucket") + char_length(regexp_replace("Bucket" COLLATE "C", U&'[\0001-\FFFF]', '', 'g')) <= 50
        """;
    private static readonly DateTime RetainedDate = new(2026, 7, 1, 2, 3, 4, DateTimeKind.Unspecified);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RuntimeModelMatchesSnapshotAndExactSourceFileMetadata(bool invoice)
    {
        using DbContext context = invoice
            ? new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql("Host=127.0.0.1;Database=model_only;Username=synthetic").Options)
            : new ReceiptDbContext(new DbContextOptionsBuilder<ReceiptDbContext>().UseNpgsql("Host=127.0.0.1;Database=model_only;Username=synthetic").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(invoice ? typeof(InvoiceFile) : typeof(ReceiptFile));
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
        Assert.Equal($"CK_{Table(invoice)}_BucketLength", check.Name);
        Assert.Equal(SourceLengthSql, check.Sql);
    }

    [Theory]
    [InlineData(true, "bmp50", true)]
    [InlineData(false, "bmp50", true)]
    [InlineData(true, "bmp51", false)]
    [InlineData(false, "bmp51", false)]
    [InlineData(true, "supplementary50", true)]
    [InlineData(false, "supplementary50", true)]
    [InlineData(true, "supplementary52", false)]
    [InlineData(false, "supplementary52", false)]
    [InlineData(true, "mixed50", true)]
    [InlineData(false, "mixed50", true)]
    [InlineData(true, "mixed51", false)]
    [InlineData(false, "mixed51", false)]
    public async Task UpgradeUsesUtf16BoundaryAndNeverChangesRetainedRows(bool invoice, string variant, bool allowed)
    {
        await using var context = await PreimageAsync(invoice);
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
        await SeedAsync(context, invoice, bucket, " folder/ใบเสร็จ sample.pdf ");
        var before = await SnapshotAsync(context, invoice);
        if (allowed)
        {
            await MigrateAsync(context, Target);
            Assert.Equal(before.Rows, (await SnapshotAsync(context, invoice)).Rows);
            await AssertInstalledAsync(context, invoice);
            var after = await SnapshotAsync(context, invoice);
            var invalid = string.Concat(Enumerable.Repeat("😀", 26));
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => InsertFileAsync(context, invoice, 8, invalid, "object"));
            Assert.Equal("23514", rejected.SqlState);
            Assert.Equal(after, await SnapshotAsync(context, invoice));
            await MigrateAsync(context, Target);
            Assert.Equal(after, await SnapshotAsync(context, invoice));
        }
        else
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target));
            Assert.Equal("P0001", failure.SqlState);
            Assert.Equal(before, await SnapshotAsync(context, invoice));
        }
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public async Task HistoricalNullsBlockAtomicallyWithoutReconciliation(bool invoice, bool nullBucket, bool nullObject)
    {
        await using var context = await PreimageAsync(invoice);
        await SeedAsync(context, invoice, nullBucket ? null : " bucket ", nullObject ? null : " path ");
        var before = await SnapshotAsync(context, invoice);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target));
        Assert.Equal("P0001", failure.SqlState);
        Assert.Equal(before, await SnapshotAsync(context, invoice));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task UnexpectedPreimageOrConflictingCheckRefusesChanges(bool invoice, bool conflictingCheck)
    {
        await using var context = await PreimageAsync(invoice);
        await SeedAsync(context, invoice, "bucket", "object");
        var table = Table(invoice);
        var mutation = conflictingCheck
            ? $"ALTER TABLE \"{table}\" ADD CONSTRAINT \"CK_{table}_BucketLength\" CHECK (true)"
            : $"ALTER TABLE \"{table}\" ALTER COLUMN \"Bucket\" SET NOT NULL";
        await context.Database.ExecuteSqlRawAsync(mutation);
        var before = await SnapshotAsync(context, invoice);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target));
        Assert.Equal("P0001", failure.SqlState);
        Assert.Equal(before, await SnapshotAsync(context, invoice));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriterFenceTimesOutAndReleasesItsFailedMigrationTransaction(bool invoice)
    {
        await using var context = await PreimageAsync(invoice);
        await SeedAsync(context, invoice, "bucket", "object");
        var before = await SnapshotAsync(context, invoice);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var writer = new NpgsqlConnection(context.Database.GetConnectionString());
        await writer.OpenAsync(lifetime.Token);
        await using var transaction = await writer.BeginTransactionAsync(lifetime.Token);
        try
        {
            await using var command = new NpgsqlCommand($"LOCK TABLE \"{Table(invoice)}\" IN ROW EXCLUSIVE MODE", writer, transaction)
            {
                CommandTimeout = 10,
            };
            await command.ExecuteNonQueryAsync(lifetime.Token);
            var clock = Stopwatch.StartNew();
            var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target));
            Assert.Equal("55P03", failure.SqlState);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(15));
            Assert.Equal(before, await SnapshotAsync(context, invoice));
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await MigrateAsync(context, Target);
        Assert.Equal(before.Rows, (await SnapshotAsync(context, invoice)).Rows);
        await AssertInstalledAsync(context, invoice);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyMetadataRemainsValidInStorageAndDownOnlyRelaxesNewConstraints(bool invoice)
    {
        await using var context = await PreimageAsync(invoice);
        await SeedAsync(context, invoice, "", "");
        var before = await SnapshotAsync(context, invoice);
        await MigrateAsync(context, Target);
        await AssertInstalledAsync(context, invoice);
        await MigrateAsync(context, Previous(invoice));
        Assert.Equal(before, await SnapshotAsync(context, invoice));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StatementDeadlineRollsBackOwnedDdlAndRetainsHistory(bool invoice)
    {
        await using var context = await PreimageAsync(invoice);
        await SeedAsync(context, invoice, "bucket", "object");
        var before = await SnapshotAsync(context, invoice);
        var suffix = Guid.NewGuid().ToString("N");
        var function = "meta_sleep_" + suffix;
        var trigger = "meta_delay_" + suffix;
        try
        {
            // Exact disposable-database helpers induce a server deadline without a large stress dataset.
            await context.Database.ExecuteSqlRawAsync($"CREATE FUNCTION {function}() RETURNS event_trigger LANGUAGE plpgsql AS $$ BEGIN PERFORM pg_sleep(31); END; $$");
            await context.Database.ExecuteSqlRawAsync($"CREATE EVENT TRIGGER {trigger} ON ddl_command_start WHEN TAG IN ('ALTER TABLE') EXECUTE FUNCTION {function}()");
            var clock = Stopwatch.StartNew();
            var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target));
            Assert.Equal("57014", failure.SqlState);
            Assert.Contains("statement timeout", failure.MessageText, StringComparison.Ordinal);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(28), TimeSpan.FromSeconds(40));
            Assert.Equal(before, await SnapshotAsync(context, invoice));
        }
        finally
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync($"DROP EVENT TRIGGER IF EXISTS {trigger}");
            }
            finally
            {
                await context.Database.ExecuteSqlRawAsync($"DROP FUNCTION IF EXISTS {function}()");
            }
        }
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM pg_event_trigger WHERE evtname='{trigger}'").SingleAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM pg_proc WHERE proname='{function}'").SingleAsync());
        await MigrateAsync(context, Target);
        Assert.Equal(before.Rows, (await SnapshotAsync(context, invoice)).Rows);
        await AssertInstalledAsync(context, invoice);
    }

    private async Task<DbContext> PreimageAsync(bool invoice)
    {
        // Reuse the fixture's exact owned PostgreSQL container; each case owns a new database.
        var database = await fixture.NewDatabaseAsync("0");
        DbContext context = database;
        try
        {
            if (!invoice)
            {
                context = new ReceiptDbContext(new DbContextOptionsBuilder<ReceiptDbContext>().UseNpgsql(database.Database.GetConnectionString()).Options);
                await database.DisposeAsync();
            }
            context.Database.SetCommandTimeout(40);
            await MigrateAsync(context, Previous(invoice));
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private static async Task SeedAsync(DbContext context, bool invoice, string? bucket, string? objectName)
    {
        if (invoice)
        {
            context.Add(new InvoiceRecord { Id = 91, CustomerId = 42, Number = "META-SOURCE", Currency = "THB", Total = 1m });
        }
        else
        {
            context.Add(new ReceiptRecord { Id = 91, InvoiceNumber = "META-SOURCE", CustomerId = 42, Currency = "THB", PaymentDate = DateTime.SpecifyKind(RetainedDate, DateTimeKind.Utc), Total = 1m });
        }
        await context.SaveChangesAsync();
        await InsertFileAsync(context, invoice, 7, bucket, objectName);
        context.ChangeTracker.Clear();
    }

    private static async Task InsertFileAsync(DbContext context, bool invoice, int id, string? bucket, string? objectName)
    {
        // Bypass the new EF maximum-length mapping when arranging historical invalid rows.
        // Explicit unrestricted text parameters must preserve the original UTF-16 input.
        var parent = invoice ? "InvoiceID" : "ReceiptID";
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO \"{Table(invoice)}\" (\"ID\",\"{parent}\",\"Bucket\",\"ObjectName\",\"CreatedDate\",\"ModifiedDate\") VALUES (@id,91,@bucket,@object,@retained,@retained)",
            new object[]
            {
                new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = id },
                new NpgsqlParameter("bucket", NpgsqlDbType.Text) { Value = (object?)bucket ?? DBNull.Value, Size = 0 },
                new NpgsqlParameter("object", NpgsqlDbType.Text) { Value = (object?)objectName ?? DBNull.Value, Size = 0 },
                new NpgsqlParameter("retained", NpgsqlDbType.Timestamp) { Value = RetainedDate },
            });
    }

    private static async Task<Snapshot> SnapshotAsync(DbContext context, bool invoice)
    {
        var table = Table(invoice);
        var rows = await context.Database.SqlQueryRaw<string>($"SELECT jsonb_agg(to_jsonb(f) ORDER BY f.\"ID\")::text AS \"Value\" FROM \"{table}\" f").SingleAsync();
        var schema = await context.Database.SqlQueryRaw<string>($"SELECT jsonb_agg(to_jsonb(c) ORDER BY c.ordinal_position)::text AS \"Value\" FROM information_schema.columns c WHERE c.table_schema='public' AND c.table_name='{table}'").SingleAsync();
        var constraints = await context.Database.SqlQueryRaw<string>($"SELECT COALESCE(jsonb_agg(pg_get_constraintdef(c.oid) ORDER BY c.conname)::text,'[]') AS \"Value\" FROM pg_constraint c WHERE c.conrelid='\"{table}\"'::regclass").SingleAsync();
        var history = string.Join("\n", (await context.Database.GetAppliedMigrationsAsync()).Order(StringComparer.Ordinal));
        return new Snapshot(rows, schema, constraints, history);
    }

    private static async Task AssertInstalledAsync(DbContext context, bool invoice)
    {
        Assert.Contains(Target, await context.Database.GetAppliedMigrationsAsync());
        var table = Table(invoice);
        Assert.Equal(2, await context.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM pg_attribute WHERE attrelid='\"{table}\"'::regclass AND attname IN ('Bucket','ObjectName') AND attnotnull AND atttypid='text'::regtype AND NOT attisdropped").SingleAsync());
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conrelid='\"{table}\"'::regclass AND conname='CK_{table}_BucketLength' AND contype='c' AND convalidated").SingleAsync());
        if (invoice)
            Assert.Equal(2, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_attribute WHERE attrelid='\"InvoiceCreationAdmission\"'::regclass AND attname IN ('FinancialOwnershipJson','EmployeeCompletionJson') AND NOT attnotnull AND atttypid='text'::regtype AND NOT attisdropped").SingleAsync());
    }

    private static async Task MigrateAsync(DbContext context, string target)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await context.GetService<IMigrator>().MigrateAsync(target, lifetime.Token);
    }

    private static string Table(bool invoice) => invoice ? "InvoiceFile" : "ReceiptFile";
    private static string Previous(bool invoice) => invoice ? "20261006140000_RetainInvoiceFinancialOwnership" : "20260927184934_PreserveNullableAmountPaid";
    private sealed record Snapshot(string Rows, string Schema, string Constraints, string History);
}
