using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Data.Common;
using System.Text.Json;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class BillingLedgerPostgresTests(BillingLedgerFixture fixture) : IClassFixture<BillingLedgerFixture>
{
    [Fact]
    public async Task AdditiveInvoiceMigrationPreservesLegacyRowsAndEnablesBillingLedger()
    {
        await using var empty = await fixture.NewDatabaseAsync(ensureCreated: false);
        await using var invoice = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>()
            .UseNpgsql(empty.Database.GetConnectionString()).Options);
        var migrator = invoice.GetService<IMigrator>();
        await migrator.MigrateAsync("20261007100000_RequireInvoiceMasterSourceStrings");
        await invoice.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Invoice" ("ID", "Number", "Currency", "CustomerID", "IsPaid", "Subtotal", "Vat", "Total", "CreatedDate", "ModifiedDate")
            VALUES (1,'SYNTHETIC-BILLING-UPGRADE','THB',21,false,90,10,100,TIMESTAMP '2026-10-07 00:00:00',TIMESTAMP '2026-10-07 00:00:00')
            """);
        var before = JsonSerializer.Serialize(await invoice.Invoices.AsNoTracking().SingleAsync());
        await migrator.MigrateAsync();
        Assert.Equal(before, JsonSerializer.Serialize(await invoice.Invoices.AsNoTracking().SingleAsync()));
        Assert.False(invoice.Database.HasPendingModelChanges());
        var opened = await new BillingLedger(invoice, TimeProvider.System).OpenAsync(Snapshot(), Context(0), CancellationToken.None);
        await new BillingLedger(invoice, TimeProvider.System).ExecuteAsync(opened.AccountId, Context(1),
            new IssueBillingStage(Draft(BillingStageKind.Deposit, 200m)), CancellationToken.None);
        Assert.Equal(200m, (await new BillingLedger(invoice, TimeProvider.System).GetAsync(opened.AccountId, CancellationToken.None))!.Billed.Gross);
        Assert.Single(await invoice.Invoices.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ConcurrentRemainingIssuanceCommitsOneStageAndOneIntent()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var opened = await Store(database).OpenAsync(Snapshot(), Context(0), CancellationToken.None);
        var results = await Task.WhenAll(IssueAsync(), IssueAsync());
        Assert.Single(results, result => result);
        await using var read = fixture.Context(database.Database.GetConnectionString()!);
        var state = await Store(read).GetAsync(opened.AccountId, CancellationToken.None);
        Assert.Equal(1000m, state!.Billed.Gross);
        Assert.Equal(0m, state.Unbilled);
        Assert.Single(await read.Set<BillingIntentRow>().ToListAsync());
        Assert.Equal(2, await read.Set<BillingOperationRow>().CountAsync());

        async Task<bool> IssueAsync()
        {
            await using var writer = fixture.Context(database.Database.GetConnectionString()!);
            try { await Store(writer).ExecuteAsync(opened.AccountId, Context(1), new IssueBillingStage(Draft()), CancellationToken.None); return true; }
            catch (BillingConflictException) { return false; }
        }
    }

    [Fact]
    public async Task LostResponseReplaysBeforeRevisionCheckAndChangedIntentConflicts()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var store = Store(database);
        var opened = await store.OpenAsync(Snapshot(), Context(0), CancellationToken.None);
        var context = Context(1);
        var command = new IssueBillingStage(Draft(BillingStageKind.Deposit, 200m));
        var first = await store.ExecuteAsync(opened.AccountId, context, command, CancellationToken.None);
        var replay = await store.ExecuteAsync(opened.AccountId, context, command, CancellationToken.None);
        Assert.Equal(first, replay);
        await Assert.ThrowsAsync<BillingConflictException>(() => store.ExecuteAsync(opened.AccountId, context,
            new IssueBillingStage(Draft(BillingStageKind.Deposit, 201m)), CancellationToken.None));
        Assert.Single(await database.Set<BillingIntentRow>().ToListAsync());
        Assert.Equal(200m, (await store.GetAsync(opened.AccountId, CancellationToken.None))!.Billed.Gross);
    }

    [Fact]
    public async Task FailedCommitRollsBackStageOperationAndIntent()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var opened = await Store(database).OpenAsync(Snapshot(), Context(0), CancellationToken.None);
        await using var writer = fixture.Context(database.Database.GetConnectionString()!, new FailSave());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(writer).ExecuteAsync(opened.AccountId, Context(1),
            new IssueBillingStage(Draft()), CancellationToken.None));
        await using var read = fixture.Context(database.Database.GetConnectionString()!);
        Assert.Equal(0m, (await Store(read).GetAsync(opened.AccountId, CancellationToken.None))!.Billed.Gross);
        Assert.Empty(await read.Set<BillingIntentRow>().ToListAsync());
        Assert.Equal(1, await read.Set<BillingOperationRow>().CountAsync());
    }

    [Fact]
    public async Task SubPrecisionSourceLinesNeverOpenAccount()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var snapshot = Snapshot() with
        {
            Cap = new(0.01m, 0m, 0.01m, "THB"),
            Lines =
            [new(1, new(0.005m, 0m, 0.005m, "THB"), "Exempt"), new(2, new(0.005m, 0m, 0.005m, "THB"), "Exempt")]
        };
        await Assert.ThrowsAsync<ArgumentException>(() => Store(database).OpenAsync(snapshot, Context(0), CancellationToken.None));
        Assert.Empty(await database.Set<BillingAccountRow>().ToListAsync());
    }

    [Fact]
    public async Task MissingTaxCategoryNeverOpensAccount()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var snapshot = Snapshot();
        snapshot = snapshot with { Lines = snapshot.Lines.Select(line => line with { TaxCategory = " " }).ToArray() };
        await Assert.ThrowsAsync<ArgumentException>(() => Store(database).OpenAsync(snapshot, Context(0), CancellationToken.None));
        Assert.Empty(await database.Set<BillingAccountRow>().ToListAsync());
    }

    [Fact]
    public async Task SnapshotWithoutCoherentLineTotalsNeverOpensAccount()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var snapshot = Snapshot() with { Cap = new(900m, 100m, 1001m, "THB") };
        await Assert.ThrowsAsync<ArgumentException>(() => Store(database).OpenAsync(snapshot, Context(0), CancellationToken.None));
        Assert.Empty(await database.Set<BillingAccountRow>().ToListAsync());
    }

    [Fact]
    public async Task DepositThenRemainingDoesNotDuplicateCommercialBaseOrVat()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var store = Store(database);
        var opened = await store.OpenAsync(Snapshot(), Context(0), CancellationToken.None);
        await store.ExecuteAsync(opened.AccountId, Context(1), new IssueBillingStage(Draft(BillingStageKind.Deposit, 200m)), CancellationToken.None);
        await store.ExecuteAsync(opened.AccountId, Context(2), new IssueBillingStage(Draft()), CancellationToken.None);
        var state = await store.GetAsync(opened.AccountId, CancellationToken.None);
        Assert.Equal(new BillingMoney(900m, 100m, 1000m, "THB"), state!.Billed);
        Assert.Equal(1000m, state.Outstanding);
        Assert.Equal(0m, state.Cash);
        Assert.Equal(0m, state.VatRecognized);
    }

    [Fact]
    public async Task InvalidDigestIsRejectedEvenWhenItHasTheCorrectLength()
    {
        await using var database = await fixture.NewDatabaseAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Store(database).OpenAsync(Snapshot() with { Digest = new string('Z', 64) }, Context(0), CancellationToken.None));
        Assert.Empty(await database.Set<BillingAccountRow>().ToListAsync());
    }

    [Fact]
    public async Task ConfiguredRetryStrategySupportsAtomicOpeningAndIssuance()
    {
        await using var database = await fixture.NewDatabaseAsync();
        await using var writer = fixture.Context(database.Database.GetConnectionString()!, retry: true);
        var store = Store(writer);
        var opened = await store.OpenAsync(Snapshot(), Context(0), CancellationToken.None);
        await store.ExecuteAsync(opened.AccountId, Context(1), new IssueBillingStage(Draft()), CancellationToken.None);
        Assert.Equal(1000m, (await store.GetAsync(opened.AccountId, CancellationToken.None))!.Billed.Gross);
    }

    [Fact]
    public async Task PublicCommandRejectsAmountAndPercentageTogether()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var opened = await Store(database).OpenAsync(Snapshot(), Context(0), CancellationToken.None);
        var draft = Draft(BillingStageKind.Deposit, 200m) with { Percentage = 25m };
        await Assert.ThrowsAsync<ArgumentException>(() => Store(database).ExecuteAsync(opened.AccountId, Context(1), new IssueBillingStage(draft), CancellationToken.None));
        Assert.Empty(await database.Set<BillingIntentRow>().ToListAsync());
    }

    [Fact]
    public async Task MixedCategoriesRetainEveryIssuedLineAndFinalResidual()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var snapshot = Snapshot() with
        {
            Cap = new(0.08m, 0.03m, 0.11m, "THB"),
            Lines =
            [new(1, new(0.03m, 0m, 0.03m, "THB"), "synthetic-exempt"), new(2, new(0.05m, 0.03m, 0.08m, "THB"), "synthetic-taxable")]
        };
        var store = Store(database);
        var opened = await store.OpenAsync(snapshot, Context(0), CancellationToken.None);
        for (var revision = 1; revision <= 4; revision++)
            await store.ExecuteAsync(opened.AccountId, Context(revision), new IssueBillingStage(Draft(BillingStageKind.Installment, 0.02m)), CancellationToken.None);
        await store.ExecuteAsync(opened.AccountId, Context(5), new IssueBillingStage(Draft()), CancellationToken.None);
        var stateJson = (await database.Set<BillingAccountRow>().AsNoTracking().SingleAsync()).StateJson;
        using var parsed = JsonDocument.Parse(stateJson);
        var stages = parsed.RootElement.GetProperty("Stages").EnumerateArray().ToArray();
        Assert.All(stages, stage => Assert.True(stage.TryGetProperty("Portions", out _), "Issued per-line allocations must be retained."));
        var portions = stages.SelectMany(stage => stage.GetProperty("Portions").EnumerateArray()).ToArray();
        foreach (var source in snapshot.Lines)
        {
            var line = portions.Where(portion => portion.GetProperty("SourceLineId").GetInt32() == source.SourceLineId).ToArray();
            Assert.Equal(source.Amount.Base, line.Sum(portion => portion.GetProperty("Amount").GetProperty("Base").GetDecimal()));
            Assert.Equal(source.Amount.Vat, line.Sum(portion => portion.GetProperty("Amount").GetProperty("Vat").GetDecimal()));
            Assert.All(line, portion => Assert.Equal(source.TaxCategory, portion.GetProperty("TaxCategory").GetString()));
        }
    }

    [Fact]
    public async Task CallerMutationWhileWaitingCannotChangeCommittedFingerprintContent()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var opened = await Store(database).OpenAsync(Snapshot(), Context(0), CancellationToken.None);
        var context = Context(1);
        var orders = new List<int> { 5 };
        var requirements = new List<EvidenceRequirement> { new(BillingEvidenceKind.BillingInstruction, orders) };
        var draft = Draft(BillingStageKind.Deposit, 200m) with { Requirements = requirements };
        await using var blocker = new NpgsqlConnection(database.Database.GetConnectionString());
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@operation, 0))", blocker, transaction);
        lockCommand.Parameters.AddWithValue("operation", context.OperationId.ToString("D"));
        await lockCommand.ExecuteNonQueryAsync();
        await using var writer = fixture.Context(database.Database.GetConnectionString()!);
        var pending = Store(writer).ExecuteAsync(opened.AccountId, context, new IssueBillingStage(draft), CancellationToken.None);
        orders[0] = 99;
        requirements.Add(new(BillingEvidenceKind.Acceptance, [99]));
        await transaction.CommitAsync();
        await pending;
        var stage = Assert.Single((await Store(database).GetAsync(opened.AccountId, CancellationToken.None))!.Stages);
        Assert.Equal(5, Assert.Single(Assert.Single(stage.Requirements).OrderIds));
    }

    [Fact]
    public async Task TransientReadFailureRetriesWithoutDuplicateStageOrIntent()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var opened = await Store(database).OpenAsync(Snapshot(), Context(0), CancellationToken.None);
        await using var writer = fixture.Context(database.Database.GetConnectionString()!, new FailFirstLockedRead(), retry: true);
        await Store(writer).ExecuteAsync(opened.AccountId, Context(1), new IssueBillingStage(Draft()), CancellationToken.None);
        Assert.Single((await Store(database).GetAsync(opened.AccountId, CancellationToken.None))!.Stages);
        Assert.Single(await database.Set<BillingIntentRow>().ToListAsync());
        Assert.Equal(2, await database.Set<BillingOperationRow>().CountAsync());
    }

    [Fact]
    public async Task DatabaseRejectsFinancialStateBeyondCommercialCap()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var opened = await Store(database).OpenAsync(Snapshot(), Context(0), CancellationToken.None);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"BillingAccount\" SET \"StateJson\" = jsonb_set(\"StateJson\", '{{Billed,Gross}}', '1001'::jsonb) WHERE \"Id\" = {opened.AccountId}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    private static BillingLedger Store(DbContext database) => new(database, TimeProvider.System);
    private static BillingContext Context(long revision) => new(42, Guid.NewGuid(), revision);
    private static BillingSnapshot Snapshot() => new(84, 21, "synthetic-tax-id", "revision-1", new string('A', 64),
        new(900m, 100m, 1000m, "THB"), [new(1, new(900m, 100m, 1000m, "THB"), "synthetic-category")], 2);
    private static StageDraft Draft(BillingStageKind kind = BillingStageKind.Remaining, decimal? amount = null) =>
        new(kind, amount, null, new DateOnly(2026, 11, 1), new(21, "synthetic-tax-id", "Head office", "00000", "Synthetic address"), []);
    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Controlled commit failure.");
    }
    private sealed class FailFirstLockedRead : DbCommandInterceptor
    {
        private int failed;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) && Interlocked.Exchange(ref failed, 1) == 0)
                throw new NpgsqlException("Controlled transient read failure.", new TimeoutException());
            return ValueTask.FromResult(result);
        }
    }
}

public sealed class BillingLedgerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();
    public async Task<BillingProofContext> NewDatabaseAsync(bool ensureCreated = true)
    {
        var name = "billing_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        var builder = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = name };
        var database = Context(builder.ConnectionString);
        if (ensureCreated) await database.Database.EnsureCreatedAsync();
        return database;
    }
    public BillingProofContext Context(string connection, IInterceptor? interceptor = null, bool retry = false)
    {
        var builder = new DbContextOptionsBuilder<BillingProofContext>().UseNpgsql(connection, options => { if (retry) options.EnableRetryOnFailure(); });
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new(builder.Options);
    }
}

public sealed class BillingProofContext(DbContextOptions<BillingProofContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => BillingModelConfiguration.Apply(modelBuilder);
}
