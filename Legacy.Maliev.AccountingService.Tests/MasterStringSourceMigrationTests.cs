using System.Diagnostics;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;

namespace Legacy.Maliev.AccountingService.Tests;

// Independent committed source135e526d inventory, not production MasterStringConstraints.
// Forecast141: model3 + physical47 + historical47 + required9 + preimage13 + cap7 + cycle3 + Down6 + lock3 + statement3.
// Source-only draft; all database execution requires the coordinated hosted bundle.
public sealed class MasterStringSourceMigrationTests(InvoiceNotificationPhaseFencePostgresFixture fixture)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    private sealed record Rule(string Database, string Entity, string Field, int? Maximum, bool Required);
    private static readonly Rule[] Rules =
    [
        new("Invoice", "Invoice", "BillingAddressBuilding", 256, false),
        new("Invoice", "Invoice", "BillingAddressCity", 256, false),
        new("Invoice", "Invoice", "BillingAddressCompany", 256, false),
        new("Invoice", "Invoice", "BillingAddressCountry", 256, false),
        new("Invoice", "Invoice", "BillingAddressLine1", 256, false),
        new("Invoice", "Invoice", "BillingAddressLine2", 256, false),
        new("Invoice", "Invoice", "BillingAddressPostalCode", 256, false),
        new("Invoice", "Invoice", "BillingAddressRecipient", 256, false),
        new("Invoice", "Invoice", "BillingAddressState", 256, false),
        new("Invoice", "Invoice", "CommercialRegistration", 256, false),
        new("Invoice", "Invoice", "Currency", 50, false),
        new("Invoice", "Invoice", "Fob", 256, false),
        new("Invoice", "Invoice", "Number", 100, true),
        new("Invoice", "Invoice", "PurchaseOrderNumber", 256, false),
        new("Invoice", "Invoice", "Requisitioner", 256, false),
        new("Invoice", "Invoice", "SalesPerson", 256, false),
        new("Invoice", "Invoice", "ShippedVia", 256, false),
        new("Invoice", "Invoice", "ShippingAddressBuilding", 256, false),
        new("Invoice", "Invoice", "ShippingAddressCity", 256, false),
        new("Invoice", "Invoice", "ShippingAddressCompany", 256, false),
        new("Invoice", "Invoice", "ShippingAddressCountry", 256, false),
        new("Invoice", "Invoice", "ShippingAddressLine1", 256, false),
        new("Invoice", "Invoice", "ShippingAddressLine2", 256, false),
        new("Invoice", "Invoice", "ShippingAddressPostalCode", 256, false),
        new("Invoice", "Invoice", "ShippingAddressRecipient", 256, false),
        new("Invoice", "Invoice", "ShippingAddressRecipientTelephone", 256, false),
        new("Invoice", "Invoice", "ShippingAddressState", 256, false),
        new("Invoice", "Invoice", "TaxIdentification", 100, false),
        new("Invoice", "Invoice", "Terms", 256, false),
        new("Receipt", "Receipt", "BillingAddressBuilding", 256, false),
        new("Receipt", "Receipt", "BillingAddressCity", 256, false),
        new("Receipt", "Receipt", "BillingAddressCompany", 256, false),
        new("Receipt", "Receipt", "BillingAddressCountry", 256, false),
        new("Receipt", "Receipt", "BillingAddressPostalCode", 256, false),
        new("Receipt", "Receipt", "BillingAddressRecipient", 256, false),
        new("Receipt", "Receipt", "BillingAddressState", 256, false),
        new("Receipt", "Receipt", "CommercialRegistration", 256, false),
        new("Receipt", "Receipt", "Currency", 256, true),
        new("Receipt", "Receipt", "InvoiceNumber", 256, false),
        new("Receipt", "Receipt", "TaxIdentification", 256, false),
        new("Payment", "Account", "AccountNumber", 50, false),
        new("Payment", "Account", "Bank", 100, false),
        new("Payment", "Account", "Branch", 100, false),
        new("Payment", "Account", "Swift", 50, false),
        new("Payment", "Payment", "Description", null, true),
        new("Payment", "PaymentDirection", "Description", null, true),
        new("Payment", "PaymentDirection", "Name", 50, true),
        new("Payment", "PaymentMethod", "Description", null, true),
        new("Payment", "PaymentMethod", "Name", 50, true),
        new("Payment", "PaymentType", "Description", null, true),
        new("Payment", "PaymentType", "Name", 50, true),
    ];

    public static IEnumerable<object[]> LengthCases() => Rules.Where(rule => rule.Maximum.HasValue)
        .Select(rule => new object[] { rule.Database, rule.Entity, rule.Field, rule.Maximum.GetValueOrDefault() });

    public static IEnumerable<object[]> RequiredCases() => Rules.Where(rule => rule.Required)
        .Select(rule => new object[] { rule.Database, rule.Entity, rule.Field });

    public static IEnumerable<object[]> PreimageCases()
    {
        foreach (var group in Rules.GroupBy(rule => (rule.Database, rule.Entity)))
        {
            yield return [group.Key.Database, group.Key.Entity, group.First().Field, false];
            var bounded = group.FirstOrDefault(rule => rule.Maximum.HasValue);
            if (bounded is not null) yield return [group.Key.Database, group.Key.Entity, bounded.Field, true];
        }
    }

    public static IEnumerable<object[]> TableCases() => Rules.Select(rule => (rule.Database, rule.Entity)).Distinct()
        .Select(table => new object[] { table.Database, table.Entity });

    [Theory]
    [InlineData("Invoice")]
    [InlineData("Receipt")]
    [InlineData("Payment")]
    public void RuntimeModelAndSnapshotMatchEveryOwnedSourceRule(string database)
    {
        using var context = Context(database, "Host=127.0.0.1;Database=model_only;Username=synthetic");
        Assert.False(context.Database.HasPendingModelChanges());
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var rule in Rules.Where(rule => rule.Database == database))
        {
            var entity = model.FindEntityType(EntityType(rule.Entity));
            Assert.NotNull(entity);
            var property = entity.FindProperty(rule.Field);
            Assert.NotNull(property);
            Assert.Equal(!rule.Required, property.IsNullable);
            Assert.Equal(rule.Maximum, property.GetMaxLength());
            Assert.Equal("text", property.GetColumnType());
            if (rule.Maximum is int maximum)
            {
                var check = Assert.Single(entity.GetCheckConstraints(), constraint => constraint.Name == CheckName(rule.Entity, rule.Field));
                Assert.Equal(LengthSql(rule.Field, maximum), check.Sql);
            }
        }
        Assert.Equal(Rules.Where(rule => rule.Database == database).Select(rule => rule.Entity).Distinct().Order(StringComparer.Ordinal),
            model.GetEntityTypes().Where(entity => Rules.Any(rule => rule.Database == database && EntityType(rule.Entity) == entity.ClrType))
                .Select(entity => entity.ClrType.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(LengthCases))]
    public async Task PhysicalConstraintsEnforceExactBmpAndSupplementaryUtf16WithoutTruncation(string database, string entity, string field, int maximum)
    {
        await using var owned = await PreimageAsync(database);
        var context = owned.Context;
        await SeedAsync(context, database);
        await MigrateAsync(context, Target(database));
        foreach (var supplementary in new[] { false, true })
        {
            var exact = supplementary ? string.Concat(Enumerable.Repeat("😀", maximum / 2)) + (maximum % 2 == 1 ? "ก" : "") : new string('ก', maximum);
            Assert.Equal(maximum, exact.Length);
            await SetAsync(context, entity, field, exact);
            Assert.Equal(exact, await ReadAsync(context, entity, field));
            var before = await SnapshotAsync(context);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => SetAsync(context, entity, field, exact + "ก"));
            Assert.Equal("23514", failure.SqlState);
            Assert.Equal(CheckName(entity, field), failure.ConstraintName);
            Assert.Equal(before, await SnapshotAsync(context));
        }
    }

    [Theory]
    [MemberData(nameof(LengthCases))]
    public async Task RetainedOverflowRefusesUpgradeAtomicallyAndNeverRewritesSourceRows(string database, string entity, string field, int maximum)
    {
        await using var owned = await PreimageAsync(database);
        var context = owned.Context;
        await SeedAsync(context, database);
        var invalid = string.Concat(Enumerable.Repeat("😀", maximum / 2)) + new string('ก', maximum % 2 + 1);
        Assert.Equal(maximum + 1, invalid.Length);
        await SetAsync(context, entity, field, invalid);
        Assert.Equal(invalid, await ReadAsync(context, entity, field));
        var before = await SnapshotAsync(context);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target(database)));
        Assert.Equal("P0001", failure.SqlState);
        Assert.Equal(before, await SnapshotAsync(context));
        Assert.DoesNotContain(Target(database), await context.Database.GetAppliedMigrationsAsync());
    }

    [Theory]
    [MemberData(nameof(RequiredCases))]
    public async Task RequiredNullBlocksHistoricalAndPhysicalWritesButEmptyAndPaddingRemainLiteral(string database, string entity, string field)
    {
        await using var owned = await PreimageAsync(database);
        var context = owned.Context;
        await SeedAsync(context, database);
        await SetAsync(context, entity, field, null);
        var before = await SnapshotAsync(context);
        var historical = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target(database)));
        Assert.Equal("P0001", historical.SqlState);
        Assert.Equal(before, await SnapshotAsync(context));
        await SetAsync(context, entity, field, "");
        var valid = await SnapshotAsync(context);
        await MigrateAsync(context, Target(database));
        Assert.Equal(valid.Rows, (await SnapshotAsync(context)).Rows);
        Assert.Equal("", await ReadAsync(context, entity, field));
        var installed = await SnapshotAsync(context);
        var physical = await Assert.ThrowsAsync<PostgresException>(() => SetAsync(context, entity, field, null));
        Assert.Equal("23502", physical.SqlState);
        Assert.Equal(field, physical.ColumnName);
        Assert.Equal(installed, await SnapshotAsync(context));
        await SetAsync(context, entity, field, "  ");
        Assert.Equal("  ", await ReadAsync(context, entity, field));
    }

    [Theory]
    [MemberData(nameof(PreimageCases))]
    public async Task UnexpectedSchemaOrNamedConstraintCollisionRefusesWholeDatabaseUpgrade(string database, string entity, string field, bool collision)
    {
        await using var owned = await PreimageAsync(database);
        var context = owned.Context;
        await SeedAsync(context, database);
        await ExecuteAsync(context, collision
            ? $"ALTER TABLE {Quote(entity)} ADD CONSTRAINT {Quote(CheckName(entity, field))} CHECK (true)"
            : $"ALTER TABLE {Quote(entity)} ALTER COLUMN {Quote(field)} SET NOT NULL");
        var before = await SnapshotAsync(context);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target(database)));
        Assert.Equal("P0001", failure.SqlState);
        Assert.Equal(before, await SnapshotAsync(context));
    }

    [Theory]
    [MemberData(nameof(TableCases))]
    public async Task BoundedRowAdmissionRejects10001ThenAccepts10000WithoutChangingRows(string database, string entity)
    {
        await using var owned = await PreimageAsync(database);
        var context = owned.Context;
        await SeedAsync(context, database);
        var columns = await context.Database.SqlQuery<string>($"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema='public' AND table_name={entity} AND column_name <> 'ID' AND is_generated='NEVER' ORDER BY ordinal_position").ToArrayAsync();
        var names = string.Join(", ", columns.Select(Quote));
        await ExecuteAsync(context, $"INSERT INTO {Quote(entity)} (\"ID\", {names}) SELECT g, {string.Join(", ", columns.Select(column => "r." + Quote(column)))} FROM {Quote(entity)} r CROSS JOIN generate_series(2,10001) g WHERE r.\"ID\"=1");
        Assert.Equal(10001, await CountAsync(context, entity));
        var before = await SnapshotAsync(context);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Target(database)));
        Assert.Equal("P0001", failure.SqlState);
        Assert.Equal(before, await SnapshotAsync(context));
        await ExecuteAsync(context, $"DELETE FROM {Quote(entity)} WHERE \"ID\"=10001");
        Assert.Equal(10000, await CountAsync(context, entity));
        var admitted = await SnapshotAsync(context);
        await MigrateAsync(context, Target(database));
        Assert.Equal(admitted.Rows, (await SnapshotAsync(context)).Rows);
        Assert.Contains(Target(database), await context.Database.GetAppliedMigrationsAsync());
    }

    [Theory]
    [InlineData("Invoice")]
    [InlineData("Receipt")]
    [InlineData("Payment")]
    public async Task UpDownUpPreservesAllRowsRestoresPreimageAndReinstallsSameMetadata(string database)
    {
        await using var owned = await PreimageAsync(database);
        var context = owned.Context;
        await SeedAsync(context, database);
        foreach (var rule in Rules.Where(rule => rule.Database == database))
            await SetAsync(context, rule.Entity, rule.Field, rule.Required ? "" : " padded synthetic ");
        var before = await SnapshotAsync(context);
        await MigrateAsync(context, Target(database));
        var installed = await SnapshotAsync(context);
        Assert.Equal(before.Rows, installed.Rows);
        await MigrateAsync(context, Previous(database));
        Assert.Equal(before, await SnapshotAsync(context));
        await MigrateAsync(context, Target(database));
        Assert.Equal(installed, await SnapshotAsync(context));
        await MigrateAsync(context, Target(database));
        Assert.Equal(installed, await SnapshotAsync(context));
    }

    [Theory]
    [InlineData("Invoice", "Invoice", "Number", false)]
    [InlineData("Invoice", "Invoice", "Number", true)]
    [InlineData("Receipt", "Receipt", "Currency", false)]
    [InlineData("Receipt", "Receipt", "Currency", true)]
    [InlineData("Payment", "Account", "Bank", false)]
    [InlineData("Payment", "Account", "Bank", true)]
    public async Task MissingOrWrongOwnedCheckRefusesDowngradeWithoutRelaxingOtherConstraints(string database, string entity, string field, bool wrongDefinition)
    {
        await using var owned = await PreimageAsync(database);
        var context = owned.Context;
        await SeedAsync(context, database);
        await MigrateAsync(context, Target(database));
        await ExecuteAsync(context, $"ALTER TABLE {Quote(entity)} DROP CONSTRAINT {Quote(CheckName(entity, field))}");
        if (wrongDefinition)
            await ExecuteAsync(context, $"ALTER TABLE {Quote(entity)} ADD CONSTRAINT {Quote(CheckName(entity, field))} CHECK ({Quote(field)} IS NULL OR {Quote(field)} IS NOT NULL)");
        // Retain the migrator's exact session so a leaked temporary guard cannot hide behind connection disposal.
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await context.Database.OpenConnectionAsync(lifetime.Token);
        var before = await SnapshotAsync(context);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(context, Previous(database)));
        Assert.Equal("P0001", failure.SqlState);
        Assert.Equal(before, await SnapshotAsync(context));
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND relname='__AccountingSourceStringDowngradeGuard'").SingleAsync(lifetime.Token));
        Assert.Contains(Target(database), await context.Database.GetAppliedMigrationsAsync());
    }

    [Theory]
    [InlineData("Invoice", "Invoice")]
    [InlineData("Receipt", "Receipt")]
    [InlineData("Payment", "Account")]
    public async Task WriterLockDeadlineRefusesUpgradeAndReleasesMigrationTransaction(string database, string firstTable)
    {
        await using var owned = await PreimageAsync(database);
        var context = owned.Context;
        await SeedAsync(context, database);
        var before = await SnapshotAsync(context);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var writer = new NpgsqlConnection(context.Database.GetConnectionString());
        await writer.OpenAsync(lifetime.Token);
        await using var transaction = await writer.BeginTransactionAsync(lifetime.Token);
        try
        {
            await using var command = new NpgsqlCommand($"LOCK TABLE {Quote(firstTable)} IN ACCESS EXCLUSIVE MODE", writer, transaction)
            {
                CommandTimeout = 10,
            };
            await command.ExecuteNonQueryAsync(lifetime.Token);
            var clock = Stopwatch.StartNew();
            var failure = await Record.ExceptionAsync(() => MigrateAsync(context, Target(database)));
            clock.Stop();
            Assert.Equal("55P03", ProviderFailure(failure).SqlState);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(15));
        }
        finally
        {
            using var rollback = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await transaction.RollbackAsync(rollback.Token);
        }
        // An ACCESS EXCLUSIVE writer also fences reads: compare only after its acknowledged rollback.
        Assert.Equal(before, await SnapshotAsync(context));
        await MigrateAsync(context, Target(database));
        Assert.Equal(before.Rows, (await SnapshotAsync(context)).Rows);
        Assert.Contains(Target(database), await context.Database.GetAppliedMigrationsAsync());
    }

    [Theory]
    [InlineData("Invoice")]
    [InlineData("Receipt")]
    [InlineData("Payment")]
    public async Task StatementDeadlineRollsBackUpgradeAndRemovesExactOwnedDelayHelpers(string database)
    {
        await using var owned = await PreimageAsync(database);
        var context = owned.Context;
        await SeedAsync(context, database);
        var before = await SnapshotAsync(context);
        var identity = Guid.NewGuid().ToString("N");
        var function = "master_sleep_" + identity;
        var trigger = "master_delay_" + identity;
        try
        {
            await ExecuteAsync(context, $"CREATE FUNCTION {Quote(function)}() RETURNS event_trigger LANGUAGE plpgsql AS $$ BEGIN PERFORM pg_sleep(31); END; $$");
            await ExecuteAsync(context, $"CREATE EVENT TRIGGER {Quote(trigger)} ON ddl_command_start WHEN TAG IN ('ALTER TABLE') EXECUTE FUNCTION {Quote(function)}()");
            var clock = Stopwatch.StartNew();
            var failure = await Record.ExceptionAsync(() => MigrateAsync(context, Target(database)));
            clock.Stop();
            var provider = ProviderFailure(failure);
            Assert.Equal("57014", provider.SqlState);
            Assert.Contains("statement timeout", provider.MessageText, StringComparison.Ordinal);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(28), TimeSpan.FromSeconds(40));
            Assert.Equal(before, await SnapshotAsync(context));
        }
        finally
        {
            try { await ExecuteAsync(context, $"DROP EVENT TRIGGER IF EXISTS {Quote(trigger)}"); }
            finally { await ExecuteAsync(context, $"DROP FUNCTION IF EXISTS {Quote(function)}()"); }
        }
        Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_event_trigger WHERE evtname={trigger}").SingleAsync());
        Assert.Equal(0, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proname={function}").SingleAsync());
        Assert.Equal(before, await SnapshotAsync(context));
        await MigrateAsync(context, Target(database));
        Assert.Equal(before.Rows, (await SnapshotAsync(context)).Rows);
        Assert.Contains(Target(database), await context.Database.GetAppliedMigrationsAsync());
    }

    private static PostgresException ProviderFailure(Exception? failure)
    {
        Assert.NotNull(failure);
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            if (current is PostgresException provider) return provider;
        }
        // Fail closed for success or an unrelated exception, including a client-side cancellation.
        return Assert.IsType<PostgresException>(failure);
    }

    private async Task<OwnedDatabase> PreimageAsync(string database)
    {
        var allocation = await fixture.NewDatabaseAsync("0");
        var allocatedConnection = allocation.Database.GetConnectionString()!;
        await allocation.DisposeAsync();
        using (var pool = new NpgsqlConnection(allocatedConnection)) NpgsqlConnection.ClearPool(pool);
        var connectionString = new NpgsqlConnectionStringBuilder(allocatedConnection) { Pooling = false }.ConnectionString;
        var context = Context(database, connectionString);
        var owned = new OwnedDatabase(context, connectionString);
        try
        {
            context.Database.SetCommandTimeout(40);
            await MigrateAsync(context, Previous(database));
            return owned;
        }
        catch
        {
            await owned.DisposeAsync();
            throw;
        }
    }

    private static DbContext Context(string database, string connectionString) => database switch
    {
        "Invoice" => new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql(connectionString).Options),
        "Receipt" => new ReceiptDbContext(new DbContextOptionsBuilder<ReceiptDbContext>().UseNpgsql(connectionString).Options),
        "Payment" => new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql(connectionString).Options),
        _ => throw new ArgumentOutOfRangeException(nameof(database)),
    };

    private static Type EntityType(string entity) => entity switch
    {
        "Invoice" => typeof(Invoice),
        "Receipt" => typeof(Receipt),
        "Payment" => typeof(Payment),
        "Account" => typeof(Account),
        "PaymentDirection" => typeof(PaymentDirection),
        "PaymentMethod" => typeof(PaymentMethod),
        "PaymentType" => typeof(PaymentType),
        _ => throw new ArgumentOutOfRangeException(nameof(entity)),
    };

    private static string Target(string database) => $"20261007100000_Require{database}MasterSourceStrings";
    private static string Previous(string database) => database == "Payment" ? "20261006180000_RequirePaymentFileMetadata" : "20261006160000_RequireFileMetadata";
    private static string CheckName(string entity, string field) => $"CK_{entity}_{field}_SourceLength";
    private static string LengthSql(string field, int maximum) => $"char_length(\"{field}\") + char_length(regexp_replace(\"{field}\" COLLATE \"C\", U&'[\\0001-\\FFFF]', '', 'g')) <= {maximum}";

    private static async Task SeedAsync(DbContext context, string database)
    {
        var sql = database switch
        {
            "Invoice" => "INSERT INTO \"Invoice\" (\"ID\", \"Number\", \"Currency\", \"CustomerID\", \"IsPaid\", \"CreatedDate\", \"ModifiedDate\") VALUES (1,'SYNTHETIC-STRING','THB',42,false,TIMESTAMP '2026-10-07 00:00:00',TIMESTAMP '2026-10-07 00:00:00')",
            "Receipt" => "INSERT INTO \"Receipt\" (\"ID\", \"Currency\", \"InvoiceNumber\", \"PaymentDate\", \"Subtotal\", \"VAT\", \"Total\", \"CreatedDate\", \"ModifiedDate\") VALUES (1,'THB','SYNTHETIC-STRING',TIMESTAMPTZ '2026-10-07 00:00:00+00',100,7,107,TIMESTAMP '2026-10-07 00:00:00',TIMESTAMP '2026-10-07 00:00:00')",
            "Payment" => "INSERT INTO \"Account\" (\"ID\", \"Bank\") VALUES (1,'Synthetic bank'); INSERT INTO \"PaymentDirection\" (\"ID\", \"Name\", \"Description\") VALUES (1,'Income','Synthetic'); INSERT INTO \"PaymentMethod\" (\"ID\", \"Name\", \"Description\") VALUES (1,'Bank','Synthetic'); INSERT INTO \"PaymentType\" (\"ID\", \"Name\", \"Description\") VALUES (1,'Job','Synthetic'); INSERT INTO \"Payment\" (\"ID\", \"PaymentDirectionID\", \"PaymentMethodID\", \"PaymentTypeID\", \"Amount\", \"Description\") VALUES (1,1,1,1,1,'Synthetic')",
            _ => throw new ArgumentOutOfRangeException(nameof(database)),
        };
        await ExecuteAsync(context, sql);
    }

    private static Task SetAsync(DbContext context, string entity, string field, string? value) =>
        ExecuteAsync(context, $"UPDATE {Quote(entity)} SET {Quote(field)}=@value WHERE \"ID\"=1",
            new NpgsqlParameter("value", NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value, Size = 0 });

    private static Task<string> ReadAsync(DbContext context, string entity, string field) =>
        context.Database.SqlQueryRaw<string>($"SELECT {Quote(field)} AS \"Value\" FROM {Quote(entity)} WHERE \"ID\"=1").SingleAsync();

    private static Task<int> CountAsync(DbContext context, string entity) =>
        context.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM {Quote(entity)}").SingleAsync();

    private static async Task MigrateAsync(DbContext context, string target)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await context.GetService<IMigrator>().MigrateAsync(target, lifetime.Token);
    }

    private static async Task ExecuteAsync(DbContext context, string sql, params NpgsqlParameter[] parameters)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(lifetime.Token);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 40 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(lifetime.Token);
    }

    private static async Task<Snapshot> SnapshotAsync(DbContext context)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var tables = await context.Database.SqlQueryRaw<string>("SELECT tablename AS \"Value\" FROM pg_tables WHERE schemaname='public' AND tablename <> '__EFMigrationsHistory' ORDER BY tablename").ToArrayAsync(lifetime.Token);
        var rows = new List<string>();
        foreach (var table in tables)
        {
            var json = await context.Database.SqlQueryRaw<string>($"SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)::text)::text,'[]') AS \"Value\" FROM {Quote(table)} r").SingleAsync(lifetime.Token);
            rows.Add(table + ":" + json);
        }
        var schema = await context.Database.SqlQueryRaw<string>("SELECT COALESCE(jsonb_agg(to_jsonb(c) ORDER BY c.table_name,c.ordinal_position)::text,'[]') AS \"Value\" FROM information_schema.columns c WHERE c.table_schema='public'").SingleAsync(lifetime.Token);
        var checks = await context.Database.SqlQueryRaw<string>("SELECT COALESCE(jsonb_agg(jsonb_build_array(t.relname,c.conname,pg_get_constraintdef(c.oid)) ORDER BY t.relname,c.conname)::text,'[]') AS \"Value\" FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname='public'").SingleAsync(lifetime.Token);
        return new(string.Join("\n", rows), schema, checks, string.Join("\n", await context.Database.GetAppliedMigrationsAsync(lifetime.Token)));
    }

    private static string Quote(string identifier)
    {
        // Identifier inputs originate only in literal rules or the owned database metadata.
        using var builder = new NpgsqlCommandBuilder();
        return builder.QuoteIdentifier(identifier);
    }

    private sealed class OwnedDatabase(DbContext context, string connectionString) : IAsyncDisposable
    {
        public DbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            try { await Context.DisposeAsync(); }
            finally
            {
                var authority = new NpgsqlConnectionStringBuilder(connectionString);
                var name = authority.Database ?? throw new InvalidOperationException("Owned database name is missing.");
                if (!name.StartsWith("phase_", StringComparison.Ordinal)
                    || !Guid.TryParseExact(name[6..], "N", out var identity) || identity == Guid.Empty
                    || name != "phase_" + identity.ToString("N"))
                    throw new InvalidOperationException("Database cleanup lacks exact fixture ownership.");
                authority.Database = "postgres";
                authority.Pooling = false;
                using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await using var connection = new NpgsqlConnection(authority.ConnectionString);
                await connection.OpenAsync(lifetime.Token);
                await using var command = new NpgsqlCommand("DROP DATABASE " + Quote(name), connection) { CommandTimeout = 40 };
                await command.ExecuteNonQueryAsync(lifetime.Token);
                await using var verify = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname=@name", connection) { CommandTimeout = 40 };
                verify.Parameters.AddWithValue("name", NpgsqlDbType.Text, name);
                Assert.Equal(0L, (long)(await verify.ExecuteScalarAsync(lifetime.Token))!);
            }
        }
    }

    private sealed record Snapshot(string Rows, string Schema, string Constraints, string History);
}
