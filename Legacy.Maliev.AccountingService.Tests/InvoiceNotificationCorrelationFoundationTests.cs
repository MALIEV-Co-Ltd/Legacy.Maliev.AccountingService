using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Services;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Npgsql;
using System.Data.Common;
using Xunit.Abstractions;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>
/// Child37 stronger-contract foundation evidence, not historical send/provider acceptance.
/// Disposable PostgreSQL and the registered production database options remain real.
/// </summary>
public sealed class InvoiceNotificationCorrelationFoundationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private IHost host = null!;
    private readonly StoreFault fault = new();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:InvoiceDbContext"] = postgres.GetConnectionString(),
        });
        builder.AddPostgresDbContext<InvoiceDbContext>(connectionName: "InvoiceDbContext", configureOptions: (_, options) =>
        {
            options.AddInterceptors(fault, new SaveFault(fault));
            options.LogTo((eventId, _) => eventId == RelationalEventId.TransactionDisposed || eventId == CoreEventId.ContextDisposed, eventData =>
            {
                if (fault.Armed && !fault.Reached &&
                    (fault.Stage == "transaction-dispose" && eventData.EventId == RelationalEventId.TransactionDisposed ||
                     fault.Stage == "context-dispose" && eventData.EventId == CoreEventId.ContextDisposed))
                { fault.Reached = true; throw fault.Cause; }
            });
        });
        host = builder.Build();
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        Assert.True(database.Database.CreateExecutionStrategy().RetriesOnFailure);
        await database.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        host?.Dispose();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task ReviewedUp_IndexCollationAndOperatorClassMetadata_IsConcreteBaseline()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var values = await database.Database.SqlQueryRaw<string>("""
            SELECT c.conname || ':' || array_to_string(ARRAY(
              SELECT n.nspname || '.' || p.opcname FROM unnest(i.indclass::oid[]) WITH ORDINALITY k(oid,ordinal)
              JOIN pg_opclass p ON p.oid=k.oid JOIN pg_namespace n ON n.oid=p.opcnamespace ORDER BY k.ordinal),',') || ':' ||
              array_to_string(ARRAY(SELECT COALESCE(n.nspname || '.' || p.collname,'none')
                FROM unnest(i.indcollation::oid[]) WITH ORDINALITY k(oid,ordinal)
                LEFT JOIN pg_collation p ON p.oid=k.oid LEFT JOIN pg_namespace n ON n.oid=p.collnamespace ORDER BY k.ordinal),',') AS "Value"
            FROM pg_constraint c JOIN pg_index i ON i.indexrelid=c.conindid
            WHERE c.conrelid='public."InvoiceNotificationCorrelation"'::regclass AND c.contype IN ('p','u') ORDER BY c.conname
            """).ToListAsync();
        foreach (var value in values) output.WriteLine("Reviewed Up index authority " + value);
        Assert.Equal(new[]
        {
            "PK_InvoiceNotificationCorrelation:pg_catalog.uuid_ops:none",
            "UQ_InvoiceNotificationCorrelation_InvoicePurpose:pg_catalog.int4_ops,pg_catalog.text_ops:none,pg_catalog.default",
        }, values);
        Assert.False(database.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("Purpose")]
    [InlineData("OriginEmployeeSubject")]
    public async Task TypedRead_NondeterministicColumnCollationDriftCannotPassSameNamedShape(string column)
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var row = Row(901);
        database.InvoiceNotificationCorrelations.Add(row);
        await database.SaveChangesAsync();
        await database.Database.ExecuteSqlRawAsync("""CREATE COLLATION public."case_insensitive_authority" (provider=icu,locale='und-u-ks-level2',deterministic=false)""");
        // Only fixed literal fixture columns are permitted; no caller-supplied SQL identifier.
        await database.Database.ExecuteSqlRawAsync(column == "Purpose"
            ? """ALTER TABLE public."InvoiceNotificationCorrelation" ALTER COLUMN "Purpose" TYPE varchar(32) COLLATE public."case_insensitive_authority";"""
            : """ALTER TABLE public."InvoiceNotificationCorrelation" ALTER COLUMN "OriginEmployeeSubject" TYPE varchar(256) COLLATE public."case_insensitive_authority";""");
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationUnavailableException>(() =>
            new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys()).ReadAsync(row.IntentId, CancellationToken.None));
        Assert.Equal(row.IntentId, (await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync()).IntentId);
    }

    [Fact]
    public async Task TypedRead_NondefaultConstraintOperatorClassIsRefusedByPostgreSqlAndStorageGuard()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var row = Row(901);
        database.InvoiceNotificationCorrelations.Add(row);
        await database.SaveChangesAsync();
        await database.Database.ExecuteSqlRawAsync("""
            ALTER TABLE public."InvoiceNotificationCorrelation" DROP CONSTRAINT "UQ_InvoiceNotificationCorrelation_InvoicePurpose"
            """);
        await database.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX "UQ_InvoiceNotificationCorrelation_InvoicePurpose" ON public."InvoiceNotificationCorrelation"
            USING btree ("InvoiceID", "Purpose" text_pattern_ops)
            """);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlRawAsync("""
            ALTER TABLE public."InvoiceNotificationCorrelation" ADD CONSTRAINT "UQ_InvoiceNotificationCorrelation_InvoicePurpose"
            UNIQUE USING INDEX "UQ_InvoiceNotificationCorrelation_InvoicePurpose"
            """));
        Assert.Equal("42809", refusal.SqlState);
        // PostgreSQL itself refuses this operator class as a unique constraint. The remaining
        // standalone index cannot substitute for the exact reviewed constraint authority.
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationUnavailableException>(() =>
            new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys()).ReadAsync(row.IntentId, CancellationToken.None));
    }

    [Fact]
    public async Task TypedRead_InvalidStoredPhaseVersionTimeCannotBypassValidatedStateAuthority()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var row = Row(901);
        database.InvoiceNotificationCorrelations.Add(row);
        await database.SaveChangesAsync();
        await database.Database.ExecuteSqlRawAsync("""
            ALTER TABLE public."InvoiceNotificationCorrelation" DROP CONSTRAINT "CK_InvoiceNotificationCorrelation_State";
            ALTER TABLE public."InvoiceNotificationCorrelation" DROP CONSTRAINT "CK_InvoiceNotificationCorrelation_Receipt";
            ALTER TABLE public."InvoiceNotificationCorrelation" DROP CONSTRAINT "CK_InvoiceNotificationCorrelation_ReceiptPhase";
            UPDATE public."InvoiceNotificationCorrelation" SET "Phase"='ProviderAccepted', "Version"=0,
              "RemoteVersion"=0,"UpdatedAt"='1960-01-01T00:00:00Z';
            ALTER TABLE public."InvoiceNotificationCorrelation" ADD CONSTRAINT "CK_InvoiceNotificationCorrelation_State" CHECK ("Version">0) NOT VALID
            """);
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationUnavailableException>(() =>
            new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys()).ReadAsync(row.IntentId, CancellationToken.None));
        var stored = await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
        Assert.Equal(0, stored.Version);
        Assert.Equal("ProviderAccepted", stored.Phase);
        Assert.Null(stored.ExecutionIssuedAt);
        Assert.True(stored.UpdatedAt < stored.CreatedAt);
    }

    [Fact]
    public async Task TypedRead_RetainedKeyRemainsUsableWhenNewActiveKeyIsMissing()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var row = Row(901);
        database.InvoiceNotificationCorrelations.Add(row);
        await database.SaveChangesAsync();
        var store = new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys { ActiveKeyId = "absent-active" });
        InvoiceNotificationCorrelation? result = null;
        var failure = await Record.ExceptionAsync(async () => result = await store.ReadAsync(row.IntentId, CancellationToken.None));
        Assert.Null(failure);
        Assert.NotNull(result);
        Assert.Equal("fixture-1", result.BindingKeyId);
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("extra-generated")]
    [InlineData("extra-identity")]
    [InlineData("unvalidated-check")]
    public async Task TypedRead_UnreadyRetainedAuthorityIsUnavailable_NotAbsenceOrRehash(string boundary)
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var row = Row(901);
        database.InvoiceNotificationCorrelations.Add(row);
        await database.SaveChangesAsync();
        if (boundary == "extra-generated") await database.Database.ExecuteSqlRawAsync("""ALTER TABLE public."InvoiceNotificationCorrelation" ADD "Unexpected" int GENERATED ALWAYS AS (1) STORED""");
        if (boundary == "extra-identity") await database.Database.ExecuteSqlRawAsync("""ALTER TABLE public."InvoiceNotificationCorrelation" ADD "Unexpected" int GENERATED ALWAYS AS IDENTITY""");
        if (boundary == "unvalidated-check")
        {
            await database.Database.ExecuteSqlRawAsync("""
                ALTER TABLE public."InvoiceNotificationCorrelation" DROP CONSTRAINT "CK_InvoiceNotificationCorrelation_Identity"
                """);
            await database.Database.ExecuteSqlRawAsync("""ALTER TABLE public."InvoiceNotificationCorrelation" ADD CONSTRAINT "CK_InvoiceNotificationCorrelation_Identity" CHECK ("InvoiceID">0) NOT VALID""");
        }
        var keys = new SyntheticKeys { MissingRetainedKey = boundary == "missing-key" };
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationUnavailableException>(() =>
            new InvoiceNotificationCorrelationStore(database, TimeProvider.System, keys).ReadAsync(row.IntentId, CancellationToken.None));
        Assert.Equal(row.PayloadBinding, (await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync()).PayloadBinding);
        // At the inert stage these refusal controls are vacuous GREEN, not readiness acceptance.
    }

    [Theory]
    [InlineData("quotation")]
    [InlineData("employee")]
    [InlineData("service")]
    [InlineData("origin-missing")]
    [InlineData("origin-uncertain")]
    [InlineData("invoice-missing")]
    public async Task TypedNewAdmission_MissingOrChangedPendingOriginCannotCreateAuthority(string boundary)
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var identity = await FreshIdentity(database);
        identity = boundary switch
        {
            "quotation" => identity with { QuotationId = 85 },
            "employee" => identity with { Origin = identity.Origin with { EmployeeSubject = "employee:other" } },
            "service" => identity with { Origin = identity.Origin with { ServiceSubject = "service:other" } },
            "origin-missing" => identity with { WorkflowOperationId = Guid.Parse("44444444-4444-4444-8444-444444444444") },
            "invoice-missing" => identity with { InvoiceId = int.MaxValue },
            _ => identity,
        };
        if (boundary == "origin-uncertain") await new InvoiceCreationAdmissionStore(database).MarkUncertainAsync(identity.WorkflowOperationId, CancellationToken.None);
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() =>
            new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys()).AdmitAsync(identity, new string('a', 64), CancellationToken.None));
        Assert.Empty(await database.InvoiceNotificationCorrelations.ToListAsync());
    }

    [Theory]
    [InlineData("save-transient", 0)]
    [InlineData("commit-before", 0)]
    [InlineData("commit-after", 1)]
    [InlineData("commit-after-cancel", 1)]
    [InlineData("transaction-dispose", 1)]
    [InlineData("context-dispose", 1)]
    [InlineData("rollback-failure", 0)]
    public async Task TypedAdmission_RegisteredOptionsFaultMustActuallyReachSingleAttemptBoundary(string stage, int rows)
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var identity = await FreshIdentity(database);
        using var caller = new CancellationTokenSource();
        fault.Stage = stage;
        fault.Caller = caller;
        fault.Armed = true;
        Exception? failure;
        try
        {
            failure = await Record.ExceptionAsync(() => new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys())
                .AdmitAsync(identity, new string('a', 64), caller.Token));
        }
        finally { fault.Armed = false; }
        Assert.True(fault.Reached, "UNEXECUTED at inert seam: required actual SQL/COMMIT/disposal boundary has not been reached.");
        Assert.Equal(1, fault.SaveCalls);
        Assert.Equal(stage is "save-transient" or "rollback-failure" ? 0 : 1, fault.CommitCalls);
        if (stage == "save-transient") Assert.Same(fault.Cause, failure);
        else
        {
            var unavailable = Assert.IsType<InvoiceNotificationCorrelationUnavailableException>(failure);
            Assert.True(HasCause(unavailable, fault.Cause));
            if (stage == "rollback-failure") Assert.True(HasCause(unavailable, fault.RollbackCause));
        }
        Assert.Equal(rows, await database.InvoiceNotificationCorrelations.AsNoTracking().CountAsync());
    }

    private static bool HasCause(Exception? value, Exception cause) => value is not null &&
        (ReferenceEquals(value, cause) || HasCause(value.InnerException, cause) ||
         value is AggregateException aggregate && aggregate.InnerExceptions.Any(inner => HasCause(inner, cause)));

    private static async Task<InvoiceNotificationCorrelationIdentity> FreshIdentity(InvoiceDbContext database)
    {
        var invoice = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
            new Invoice { Number = "INV-new-authority", CustomerId = 42 }, [], CancellationToken.None);
        var identity = Identity(Row(invoice.Id));
        await new InvoiceCreationAdmissionStore(database).AdmitAsync(identity.WorkflowOperationId, 84,
            "employee:42", "service:legacy-intranet", new string('A', 64), CancellationToken.None);
        return identity;
    }

    [Theory]
    [InlineData("fixture-2")]
    [InlineData("absent-active")]
    public async Task TypedReplay_RotationUsesRetainedKeyIdAndExactImmutableBinding_NotActiveKey(string activeKeyId)
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var row = Row(901);
        var identity = Identity(row);
        row.PayloadBinding = InvoiceNotificationCorrelationBinding.Compute(identity, "fixture-1", Enumerable.Range(0, 32).Select(value => (byte)value).ToArray(), new string('a', 64));
        database.InvoiceNotificationCorrelations.Add(row);
        await database.SaveChangesAsync();
        var store = new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys { ActiveKeyId = activeKeyId });
        InvoiceNotificationCorrelation? replay = null;
        var failure = await Record.ExceptionAsync(async () => replay = await store.AdmitAsync(identity, new string('a', 64), CancellationToken.None));
        Assert.Null(failure);
        Assert.NotNull(replay);
        Assert.Equal("fixture-1", replay.BindingKeyId);
        Assert.Equal(row.PayloadBinding, replay.PayloadBinding);
        Assert.Equal(1, replay.Version);
        Assert.Equal("fixture-1", (await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync()).BindingKeyId);
    }

    [Fact]
    public async Task TypedReplay_ChangedPayloadCannotReplaceRetainedBinding()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var row = Row(901);
        var identity = Identity(row);
        row.PayloadBinding = InvoiceNotificationCorrelationBinding.Compute(identity, "fixture-1", Enumerable.Range(0, 32).Select(value => (byte)value).ToArray(), new string('a', 64));
        database.InvoiceNotificationCorrelations.Add(row);
        await database.SaveChangesAsync();
        var store = new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys());
        await Assert.ThrowsAsync<InvoiceNotificationCorrelationConflictException>(() => store.AdmitAsync(identity, new string('b', 64), CancellationToken.None));
        Assert.Equal(row.PayloadBinding, (await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync()).PayloadBinding);
    }

    [Fact]
    public async Task TypedAdmission_TwoContextDifferentUuidWorkflowSameInvoicePurpose_HasOneWinner()
    {
        int invoiceId;
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
            invoiceId = (await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
                new Invoice { Number = "INV-two-context", CustomerId = 42 }, [], CancellationToken.None)).Id;
            var admissions = new InvoiceCreationAdmissionStore(database);
            foreach (var workflow in new[] { Guid.Parse("22222222-2222-4222-8222-222222222222"), Guid.Parse("44444444-4444-4444-8444-444444444444") })
                await admissions.AdmitAsync(workflow, 84, "employee:42", "service:legacy-intranet", new string('A', 64), CancellationToken.None);
        }
        async Task<Exception?> Attempt(Guid intent, Guid workflow)
        {
            using var scope = host.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
            var identity = Identity(Row(invoiceId)) with { IntentId = intent, WorkflowOperationId = workflow };
            return await Record.ExceptionAsync(() => new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys())
                .AdmitAsync(identity, new string('a', 64), CancellationToken.None));
        }
        var results = await Task.WhenAll(
            Attempt(Guid.Parse("11111111-1111-4111-8111-111111111111"), Guid.Parse("22222222-2222-4222-8222-222222222222")),
            Attempt(Guid.Parse("33333333-3333-4333-8333-333333333333"), Guid.Parse("44444444-4444-4444-8444-444444444444")));
        Assert.Single(results, value => value is null);
        Assert.Single(results, value => value is InvoiceNotificationCorrelationConflictException);
        using var readScope = host.Services.CreateScope();
        Assert.Single(await readScope.ServiceProvider.GetRequiredService<InvoiceDbContext>().InvoiceNotificationCorrelations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task TypedRead_CanceledCallerPropagatesActualToken_NotUnavailableOrTimeout()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var store = new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys());
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadAsync(Guid.NewGuid(), caller.Token));
        Assert.Equal(caller.Token, failure.CancellationToken);
        Assert.Empty(await database.InvoiceNotificationCorrelations.ToListAsync());
    }

    [Fact]
    public async Task DiscoveredMigration_ExactPhysicalColumnsAndValidatedUniqueAuthority_ModelRerunAgree()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var shape = await database.Database.SqlQueryRaw<string>("""
            SELECT a.attname || ':' || format_type(a.atttypid,a.atttypmod) || ':' ||
              CASE WHEN a.attnotnull THEN 'required' ELSE 'optional' END || ':' ||
              CASE WHEN a.attgenerated = '' AND a.attidentity = '' AND NOT a.atthasdef THEN 'plain' ELSE 'unexpected' END AS "Value"
            FROM pg_attribute a WHERE a.attrelid = 'public."InvoiceNotificationCorrelation"'::regclass
              AND a.attnum > 0 AND NOT a.attisdropped ORDER BY a.attname
            """).ToListAsync();
        Assert.Equal(new[]
        {
            "AdmissionIssuedAt:timestamp with time zone:optional:plain", "BindingKeyID:character varying(64):required:plain",
            "BindingVersion:character varying(64):required:plain", "CreatedAt:timestamp with time zone:required:plain",
            "ExecutionIssuedAt:timestamp with time zone:optional:plain", "IntentID:uuid:required:plain", "InvoiceID:integer:required:plain",
            "OriginEmployeeSubject:character varying(256):required:plain", "OriginIssuer:character varying(512):required:plain",
            "OriginServiceSubject:character varying(128):required:plain", "PayloadBinding:bytea:required:plain",
            "PayloadFrameVersion:character varying(64):required:plain", "Phase:character varying(32):required:plain",
            "Purpose:character varying(32):required:plain", "QuotationID:integer:required:plain",
            "RemoteAdmittedAt:timestamp with time zone:optional:plain", "RemoteReceiptBinding:bytea:optional:plain",
            "RemoteState:character varying(32):optional:plain", "RemoteUpdatedAt:timestamp with time zone:optional:plain",
            "RemoteVersion:bigint:optional:plain",
            "SenderIssuer:character varying(512):required:plain", "SenderServiceSubject:character varying(128):required:plain",
            "UpdatedAt:timestamp with time zone:required:plain", "Version:bigint:required:plain", "WorkflowOperationID:uuid:required:plain",
        }, shape);
        var constraints = await database.Database.SqlQueryRaw<string>("""
            SELECT conname || ':' || contype::text || ':' || convalidated::text AS "Value"
            FROM pg_constraint WHERE conrelid = 'public."InvoiceNotificationCorrelation"'::regclass
              AND contype <> 'n' ORDER BY conname
            """).ToListAsync();
        Assert.Equal(new[]
        {
            "CK_InvoiceNotificationCorrelation_Identity:c:true", "CK_InvoiceNotificationCorrelation_Receipt:c:true",
            "CK_InvoiceNotificationCorrelation_ReceiptPhase:c:true", "CK_InvoiceNotificationCorrelation_State:c:true",
            "PK_InvoiceNotificationCorrelation:p:true", "UQ_InvoiceNotificationCorrelation_InvoicePurpose:u:true",
        }, constraints);
        foreach (var check in await database.Database.SqlQueryRaw<string>("""
            SELECT conname || ':' || pg_get_expr(conbin,conrelid,true) AS "Value" FROM pg_constraint
            WHERE conrelid='public."InvoiceNotificationCorrelation"'::regclass AND contype='c' ORDER BY conname
            """).ToListAsync())
        {
            var separator = check.IndexOf(':');
            output.WriteLine(check[..separator] + " canonical SHA256 " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(check[(separator + 1)..]))));
        }
        var kind = await database.Database.SqlQueryRaw<string>("""
            SELECT relkind::text AS "Value" FROM pg_class WHERE oid='public."InvoiceNotificationCorrelation"'::regclass
            """).SingleAsync();
        Assert.Equal("r", kind);
        await database.Database.MigrateAsync();
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task DiscoveredMigration_DownRefusalRetainsAuthorityAndHistory_ExistingInvoiceDeleteIsUnchanged()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var invoice = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
            new Invoice { Number = "INV-retained", CustomerId = 42 }, [], CancellationToken.None);
        var row = Row(invoice.Id);
        database.InvoiceNotificationCorrelations.Add(row);
        await database.SaveChangesAsync();
        await Assert.ThrowsAsync<NotSupportedException>(() => database.GetService<IMigrator>()
            .MigrateAsync("20260928094829_AddInvoiceCreationAdmission"));
        Assert.Contains("20261001105037_AddInvoiceNotificationCorrelation", await database.Database.GetAppliedMigrationsAsync());
        Assert.Equal(row.IntentId, (await database.InvoiceNotificationCorrelations.AsNoTracking().SingleAsync()).IntentId);
        // Deliberate scalar/no-cascade choice retains authority without changing invoice DELETE.
        var persistedInvoice = await database.Invoices.SingleAsync();
        database.Invoices.Remove(persistedInvoice);
        await database.SaveChangesAsync();
        Assert.Empty(await database.Invoices.ToListAsync());
        Assert.Single(await database.InvoiceNotificationCorrelations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PhysicalUniqueInvoicePurpose_RejectsDifferentWorkflowAndIntentAcrossContexts()
    {
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
            database.InvoiceNotificationCorrelations.Add(Row(901));
            await database.SaveChangesAsync();
        }
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
            var collision = Row(901);
            collision.IntentId = Guid.Parse("33333333-3333-4333-8333-333333333333");
            collision.WorkflowOperationId = Guid.Parse("44444444-4444-4444-8444-444444444444");
            database.InvoiceNotificationCorrelations.Add(collision);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }
        using (var scope = host.Services.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<InvoiceDbContext>().InvoiceNotificationCorrelations.AsNoTracking().SingleAsync();
            Assert.Equal(Guid.Parse("11111111-1111-4111-8111-111111111111"), row.IntentId);
            Assert.Equal(Guid.Parse("22222222-2222-4222-8222-222222222222"), row.WorkflowOperationId);
        }
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("AdmissionIssued")]
    [InlineData("Admitted")]
    [InlineData("ExecutionIssued")]
    [InlineData("OutcomeUnknown")]
    [InlineData("ProviderAccepted")]
    public async Task PhysicalPhaseChecks_RejectMissingOrInventedExecutionLineage(string phase)
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var row = Row(901);
        row.Phase = phase;
        if (phase is "Prepared" or "AdmissionIssued" or "Admitted")
        {
            row.AdmissionIssuedAt = row.CreatedAt;
            row.ExecutionIssuedAt = row.CreatedAt;
        }
        // Keep this original State-specific bad-local-timestamp proof independently reachable.
        // New tests separately retain missing RemoteVersion / partial-quartet rejection.
        if (phase is not ("Prepared" or "AdmissionIssued"))
        {
            row.RemoteState = phase == "ProviderAccepted" ? "providerAccepted" : phase == "OutcomeUnknown" ? "submitting" : "admitted";
            row.RemoteVersion = phase == "ProviderAccepted" ? 3 : phase == "OutcomeUnknown" ? 2 : 1;
            row.RemoteAdmittedAt = row.CreatedAt;
            row.RemoteUpdatedAt = row.CreatedAt;
            row.RemoteReceiptBinding = new byte[32];
        }
        // Execution/unknown/accepted still have the exact original missing local timestamps.
        database.InvoiceNotificationCorrelations.Add(row);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        var postgresFailure = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgresFailure.SqlState);
        Assert.Equal("CK_InvoiceNotificationCorrelation_State", postgresFailure.ConstraintName);
        Assert.Empty(await database.InvoiceNotificationCorrelations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task TypedRead_RetainedPreparedAuthorityIsReturnedWithoutInventingExecutionPermit()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var row = Row(901);
        database.InvoiceNotificationCorrelations.Add(row);
        await database.SaveChangesAsync();
        var store = new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys());
        InvoiceNotificationCorrelation? result = null;
        var failure = await Record.ExceptionAsync(async () => result = await store.ReadAsync(row.IntentId, CancellationToken.None));
        Assert.Null(failure);
        Assert.NotNull(result);
        Assert.Equal(row.IntentId, result.Identity.IntentId);
        Assert.Equal("Prepared", result.Phase);
        Assert.Null(result.ExecutionIssuedAt);
        Assert.Equal(row.PayloadBinding, result.PayloadBinding);
    }

    [Fact]
    public async Task TypedRead_MissingIntentOnVerifiedReadyShapeIsNotUnavailableOrNoSendProof()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var store = new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys());
        InvoiceNotificationCorrelation? result = null;
        var failure = await Record.ExceptionAsync(async () => result = await store.ReadAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(failure);
        Assert.Null(result);
    }

    [Fact]
    public async Task TypedStore_AcknowledgedFreshInvoiceAndPendingOrigin_AdmitsImmutablePreparedAuthority()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var invoice = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
            new Invoice { Number = "INV-typed-correlation", CustomerId = 42, Total = 107m }, [], CancellationToken.None);
        var workflow = Guid.Parse("22222222-2222-4222-8222-222222222222");
        await new InvoiceCreationAdmissionStore(database).AdmitAsync(workflow, 84, "employee:42",
            "service:legacy-intranet", new string('A', 64), CancellationToken.None);
        var identity = new InvoiceNotificationCorrelationIdentity(
            Guid.Parse("11111111-1111-4111-8111-111111111111"), invoice.Id, "invoice-issued", 84, workflow,
            new("https://auth.example.invalid", "employee:42", "service:legacy-intranet"),
            "https://auth.example.invalid", "service:legacy-accounting", "notification-payload-v1",
            "accounting-invoice-notification-hmac-v1");
        IInvoiceNotificationCorrelationStore store = new InvoiceNotificationCorrelationStore(database, TimeProvider.System, new SyntheticKeys());
        InvoiceNotificationCorrelation? result = null;
        var failure = await Record.ExceptionAsync(async () => result = await store.AdmitAsync(identity, new string('a', 64), CancellationToken.None));

        // At the seam phase this is explicitly unimplemented-feature RED. It does
        // not claim that commit/lineage/CAS branches have already been reached.
        Assert.Null(failure);
        Assert.NotNull(result);
        Assert.Equal(identity, result.Identity);
        Assert.Equal("Prepared", result.Phase);
        Assert.Equal(1, result.Version);
        Assert.Null(result.AdmissionIssuedAt);
        Assert.Null(result.ExecutionIssuedAt);
    }

    [Fact]
    public async Task ActualDiscoveredMigrations_ProvideDurableInvoicePurposeAuthorityBeforeAnyNotificationRpc()
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());

        // This deliberately establishes only the missing physical-contract prerequisite.
        // It does not pretend that an absent store executed CAS/commit fault scenarios.
        var exists = await database.Database.SqlQueryRaw<bool>("""
            SELECT to_regclass('public."InvoiceNotificationCorrelation"') IS NOT NULL AS "Value"
            """).SingleAsync();

        Assert.True(exists, "Stronger child37 requirement: no public durable invoice-purpose correlation authority exists after actual migrations.");
    }

    [Fact]
    public async Task ExistingWorkflowAdmissions_CanNameSameInvoiceAcrossOperations_AndAreNotNotificationAuthority()
    {
        int invoiceId;
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
            var invoice = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
                new Invoice { Number = "INV-correlation-control", CustomerId = 42, Total = 107m }, [], CancellationToken.None);
            invoiceId = invoice.Id;
            Assert.True(invoiceId > 0);
        }

        var first = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var second = Guid.Parse("22222222-2222-4222-8222-222222222222");
        foreach (var operation in new[] { first, second })
        {
            using var scope = host.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
            var admissions = new InvoiceCreationAdmissionStore(database);
            Assert.True((await admissions.AdmitAsync(operation, 84, "employee:42", "service:legacy-intranet",
                new string('A', 64), CancellationToken.None)).IsNew);
            await admissions.CompleteAsync(operation, new InvoiceCreationResult(invoiceId,
                InvoiceCreationState.Completed, InvoiceCreationEmailState.NotRequested, null,
                new("synthetic-bucket", "synthetic-invoice.pdf")), CancellationToken.None);
        }

        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
            var admissions = new InvoiceCreationAdmissionStore(database);
            Assert.Equal(2, await database.InvoiceCreationAdmissions.CountAsync());
            Assert.Single(await database.Invoices.ToListAsync());
            foreach (var operation in new[] { first, second })
                Assert.Equal(invoiceId, (await admissions.AdmitAsync(operation, 84, "employee:42",
                    "service:legacy-intranet", new string('A', 64), CancellationToken.None)).Completed?.InvoiceId);
        }
    }

    private sealed class SyntheticKeys : IInvoiceNotificationBindingKeyring
    {
        public string ActiveKeyId { get; init; } = "fixture-1";
        public bool MissingRetainedKey { get; init; }
        public ReadOnlyMemory<byte>? Find(string keyId) => keyId switch
        {
            "fixture-1" when !MissingRetainedKey => Enumerable.Range(0, 32).Select(value => (byte)value).ToArray(),
            "fixture-2" => Enumerable.Range(32, 32).Select(value => (byte)value).ToArray(),
            _ => null,
        };
    }

    private static InvoiceNotificationCorrelationIdentity Identity(InvoiceNotificationCorrelationRow row) => new(
        row.IntentId, row.InvoiceId, row.Purpose, row.QuotationId, row.WorkflowOperationId,
        new(row.OriginIssuer, row.OriginEmployeeSubject, row.OriginServiceSubject), row.SenderIssuer,
        row.SenderServiceSubject, row.PayloadFrameVersion, row.BindingVersion);

    private static InvoiceNotificationCorrelationRow Row(int invoiceId) => new()
    {
        IntentId = Guid.Parse("11111111-1111-4111-8111-111111111111"),
        InvoiceId = invoiceId,
        Purpose = "invoice-issued",
        QuotationId = 84,
        WorkflowOperationId = Guid.Parse("22222222-2222-4222-8222-222222222222"),
        OriginIssuer = "https://auth.example.invalid",
        OriginEmployeeSubject = "employee:42",
        OriginServiceSubject = "service:legacy-intranet",
        SenderIssuer = "https://auth.example.invalid",
        SenderServiceSubject = "service:legacy-accounting",
        PayloadFrameVersion = "notification-payload-v1",
        BindingVersion = "accounting-invoice-notification-hmac-v1",
        BindingKeyId = "fixture-1",
        PayloadBinding = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray(),
        Phase = "Prepared",
        Version = 1,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class StoreFault : DbTransactionInterceptor
    {
        public bool Armed;
        public bool Reached;
        public string Stage = "";
        public int SaveCalls;
        public int CommitCalls;
        public CancellationTokenSource? Caller;
        public Exception Cause = new NpgsqlException("Synthetic correlation boundary fault.", new IOException());
        public Exception RollbackCause = new IOException("Synthetic rollback acknowledgment failure.");

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!Armed) return ValueTask.FromResult(result);
            CommitCalls++;
            if (Stage == "commit-before") { Reached = true; throw Cause; }
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed && Stage is "commit-after" or "commit-after-cancel")
            {
                Reached = true;
                if (Stage == "commit-after-cancel") { Caller!.Cancel(); Cause = new OperationCanceledException(Caller.Token); }
                throw Cause;
            }
            return Task.CompletedTask;
        }

        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed && Stage == "rollback-failure") throw RollbackCause;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SaveFault(StoreFault fault) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!fault.Armed) return ValueTask.FromResult(result);
            fault.SaveCalls++;
            if (fault.Stage is "save-transient" or "rollback-failure") { fault.Reached = true; throw fault.Cause; }
            return ValueTask.FromResult(result);
        }
    }
}
