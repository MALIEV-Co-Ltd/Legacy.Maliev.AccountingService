using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Actual normal Program registrations exercise the enabled provider retry boundary.</summary>
public sealed class InvoiceFinancialOwnershipRegisteredOptionsTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    [Fact]
    public async Task RegisteredRetryStrategyReadsWholeFinancialSnapshotInsideItsOwnedAttempt()
    {
        await using var scope = await fixture.ReceiptScopeAsync();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        Assert.True(database.Database.CreateExecutionStrategy().RetriesOnFailure);
        var operation = Guid.NewGuid();
        var origin = new InvoiceNotificationOrigin("https://auth.example.invalid", "employee:42", "service:legacy-intranet");
        _ = await scope.ServiceProvider.GetRequiredService<InvoiceCreationAdmissionStore>().AdmitAsync(operation, 84, origin,
            new string('A', 64), CancellationToken.None);
        var invoice = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
            new Invoice { Number = "SYNTHETIC-REGISTERED-" + operation.ToString("N"), CustomerId = 42, Total = 12m },
            [new() { Description = "owned", Quantity = 3, UnitPrice = 4m }],
            new(operation, 84, origin, new DateTime(2026, 10, 6, 1, 2, 3, DateTimeKind.Unspecified)), CancellationToken.None);
        var receipt = await scope.ServiceProvider.GetRequiredService<InvoiceFinancialOwnershipStore>().ReadAsync(operation, CancellationToken.None);
        Assert.Equal(invoice.Id, receipt.InvoiceId);
        Assert.Equal(operation, receipt.OperationId);
        Assert.Equal("2026-10-06T01:02:03.0000000Z", receipt.OriginalQuotationVersion);
        Assert.Single(await database.Items.AsNoTracking().Where(value => value.InvoiceId == invoice.Id).ToListAsync());
        Assert.Null((await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == operation)).FinancialResultJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredProviderReadRetryRecoversOrReturnsGeneric503WithoutFinancialMutation(bool exhaust)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var scope = await fixture.ReceiptScopeAsync();
        var database = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
        var registered = (DbContextOptions<InvoiceDbContext>)database.GetService<IDbContextOptions>();
        Assert.True(database.Database.CreateExecutionStrategy().RetriesOnFailure);
        var operation = Guid.NewGuid();
        var origin = new InvoiceNotificationOrigin("https://auth.example.invalid", "employee:42", "service:legacy-intranet");
        _ = await scope.ServiceProvider.GetRequiredService<InvoiceCreationAdmissionStore>().AdmitAsync(operation, 84, origin,
            new string('B', 64), budget.Token);
        var invoice = await new InvoiceCreationStore(database, TimeProvider.System).CreateAsync(
            new Invoice { Number = "SYNTHETIC-RETRY-" + operation.ToString("N"), CustomerId = 42, Total = 12m },
            [new() { Description = "owned", Quantity = 3, UnitPrice = 4m }],
            new(operation, 84, origin, new DateTime(2026, 10, 6, 1, 2, 3, DateTimeKind.Unspecified)), budget.Token);
        var before = await FinancialStateAsync(database, operation, invoice.Id, budget.Token);
        var expected = await scope.ServiceProvider.GetRequiredService<InvoiceFinancialOwnershipStore>().ReadAsync(operation, budget.Token);
        var fault = new ReadOnlySnapshotFault(exhaust);
        // Keep the real registered connection/model/provider and fresh-context copying. Only
        // the retry count/delay are bounded for deterministic fault injection (three attempts).
        var controlled = new DbContextOptionsBuilder<InvoiceDbContext>(registered)
            .UseNpgsql(options => options.EnableRetryOnFailure(2, TimeSpan.Zero, null))
            .AddInterceptors(fault).Options;
        await using var readContext = new InvoiceDbContext(controlled);
        Assert.IsType<Npgsql.EntityFrameworkCore.PostgreSQL.NpgsqlRetryingExecutionStrategy>(readContext.Database.CreateExecutionStrategy());
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", "service:legacy-auth"),
                new Claim("identity_kind", "service"),
                new Claim("permissions", InvoiceFinancialOwnershipController.ReadPermission)], "synthetic"))
        };
        var controller = new InvoiceFinancialOwnershipController(new InvoiceFinancialOwnershipStore(readContext))
        { ControllerContext = new ControllerContext { HttpContext = http } };
        var result = await controller.ReadAsync(operation, budget.Token);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        Assert.Equal(exhaust ? 3 : 2, fault.Attempts);
        Assert.Equal(fault.Attempts, fault.Contexts.Count);
        Assert.DoesNotContain(readContext.ContextId.InstanceId, fault.Contexts);
        if (exhaust)
        {
            var unavailable = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(503, unavailable.StatusCode);
            var problem = Assert.IsType<ProblemDetails>(unavailable.Value);
            Assert.Equal(503, problem.Status);
            Assert.Equal("Financial ownership readback is unavailable.", problem.Title);
            Assert.Null(problem.Detail);
            Assert.DoesNotContain("Synthetic", JsonSerializer.Serialize(problem), StringComparison.Ordinal);
        }
        else
        {
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(expected, Assert.IsType<InvoiceFinancialOwnership>(ok.Value));
        }
        Assert.Equal(before, await FinancialStateAsync(database, operation, invoice.Id, budget.Token));
    }

    private static async Task<string> FinancialStateAsync(InvoiceDbContext database, Guid operation, int invoiceId,
        CancellationToken cancellationToken) => JsonSerializer.Serialize(new
        {
            Admission = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == operation, cancellationToken),
            Invoice = await database.Invoices.AsNoTracking().SingleAsync(value => value.Id == invoiceId, cancellationToken),
            Items = await database.Items.AsNoTracking().Where(value => value.InvoiceId == invoiceId).OrderBy(value => value.Id).ToArrayAsync(cancellationToken)
        });

    private sealed class ReadOnlySnapshotFault(bool exhaust) : DbCommandInterceptor
    {
        public int Attempts { get; private set; }
        public HashSet<Guid> Contexts { get; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText == "SET TRANSACTION READ ONLY")
            {
                Attempts++;
                Contexts.Add(eventData.Context!.ContextId.InstanceId);
                if (exhaust || Attempts == 1)
                    throw new NpgsqlException("Synthetic financial read transient failure.", new IOException());
            }
            return ValueTask.FromResult(result);
        }
    }
}
