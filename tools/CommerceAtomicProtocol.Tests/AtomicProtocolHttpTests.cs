using System.Net;
using System.Net.Http.Json;
using Commerce.JoinedAuth.Tests;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.QuotationService.Domain;
using Legacy.Maliev.QuotationService.Application.Interfaces;
using Legacy.Maliev.QuotationService.Application.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Commerce.AtomicProtocol.Tests;

[Collection("atomic-protocol")]
public sealed class AtomicProtocolHttpTests(AccountingQuotationBaselineFixture fixture)
{
    private AtomicProtocolScenario Scenario()
    {
        fixture.ResetObservations();
        return new(fixture);
    }

    [Fact]
    public async Task PersistedInvoice_ActualDiCompletionUsesExactAtomicCustomerWire()
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync();
        var invoice = await scenario.InvoiceAsync();
        var operation = Guid.NewGuid();
        await scenario.CompleteAsync(row.Id, invoice, operation, row.ModifiedDate);
        scenario.AssertWire(row.Id, invoice, operation, row.ModifiedDate!.Value);
        await scenario.AssertAcceptedAsync(row.Id, invoice);
        Assert.Equal(new[] { $"GET /quotations/{row.Id}", $"PUT /quotations/{row.Id}/decision" }, scenario.Requests);
        Assert.Equal(0, scenario.LaterEffects);
    }

    [Fact]
    public async Task OrdinaryAndRealDelegatedHttpCreationPersistInvoiceAndCustomerOutcome()
    {
        await using var scenario = Scenario();
        foreach (var delegated in new[] { false, true })
        {
            var row = await scenario.SeedAsync();
            var operation = Guid.NewGuid();
            var number = $"HTTP-{Guid.NewGuid():N}";
            using var response = await scenario.CreateAsync(row.Id, operation, number, delegated);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var invoices = fixture.InvoiceDatabase();
            var invoice = await invoices.Invoices.AsNoTracking().SingleAsync(value => value.Number == number);
            Assert.Equal(107m, invoice.Total);
            Assert.Single(await invoices.Items.Where(value => value.InvoiceId == invoice.Id).ToArrayAsync());
            scenario.AssertWire(row.Id, invoice.Id, operation, row.ModifiedDate!.Value);
            await scenario.AssertAcceptedAsync(row.Id, invoice.Id);
            if (delegated)
            {
                var admission = await invoices.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == operation);
                Assert.Equal("Completed", admission.State);
                Assert.Equal("employee:42", admission.EmployeeSubject);
                Assert.Equal("service:legacy-intranet", admission.ServiceSubject);
                Assert.NotNull(admission.ResultJson);
            }
        }
        Assert.Equal(4, scenario.LaterEffects); // Controlled PDF and existence response for each HTTP creation.
        Assert.Equal(2, fixture.AccountingLiveChecks);
    }

    [Fact]
    public async Task SameInvoiceReplayPreservesOutcomeTimestampAndOrderKeys()
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync();
        await AddLinksAsync(row.Id, 7101);
        var invoice = await scenario.InvoiceAsync();
        var operation = Guid.NewGuid();
        await scenario.CompleteAsync(row.Id, invoice, operation, row.ModifiedDate);
        var before = await fixture.QuotationScalarSnapshotAsync(row.Id);
        await scenario.CompleteAsync(row.Id, invoice, operation, row.ModifiedDate);
        Assert.Equal(before, await fixture.QuotationScalarSnapshotAsync(row.Id));
        await scenario.AssertAcceptedAsync(row.Id, invoice);
        Assert.Equal(2, scenario.Orders.Count);
        Assert.Single(scenario.Orders.Select(value => value.Key).Distinct());
        Assert.Equal(0, scenario.LaterEffects);
    }

    [Fact]
    public async Task DifferentInvoiceCannotRebindAcceptedQuotation()
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync();
        var first = await scenario.InvoiceAsync();
        var second = await scenario.InvoiceAsync();
        await scenario.CompleteAsync(row.Id, first, Guid.NewGuid(), row.ModifiedDate);
        var before = await fixture.QuotationScalarSnapshotAsync(row.Id);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => scenario.CompleteAsync(row.Id, second, Guid.NewGuid(), row.ModifiedDate));
        Assert.Single(scenario.Decisions);
        Assert.Equal(before, await fixture.QuotationScalarSnapshotAsync(row.Id));
        await scenario.AssertAcceptedAsync(row.Id, first);
    }

    [Fact]
    public Task PriorDeclineRejectsCustomerInvoiceDecision() => RejectStateAsync(false);

    [Fact]
    public Task AcceptedUnlinkedQuotationRejectsLateCustomerInvoiceAttachment() => RejectStateAsync(true);

    private async Task RejectStateAsync(bool accepted)
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync(accepted);
        var invoice = await scenario.InvoiceAsync();
        var before = await fixture.QuotationScalarSnapshotAsync(row.Id);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => scenario.CompleteAsync(row.Id, invoice, Guid.NewGuid(), row.ModifiedDate));
        Assert.Equal(409, Assert.Single(scenario.Decisions).Status);
        Assert.Equal(before, await fixture.QuotationScalarSnapshotAsync(row.Id));
        await AssertNoOutcomeAsync(row.Id);
        Assert.Equal(0, scenario.LaterEffects);
    }

    [Fact]
    public async Task ParentScalarEditAfterSourceConflictsWithOriginalVersionAndFencesDelegatedReplay()
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync();
        scenario.BeforeCompletionLookup = () => EditParentAsync(scenario, row);
        var operation = Guid.NewGuid();
        var number = $"RACE-{Guid.NewGuid():N}";
        using var response = await scenario.CreateAsync(row.Id, operation, number, delegated: true);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var invoices = fixture.InvoiceDatabase();
        var invoice = await invoices.Invoices.AsNoTracking().SingleAsync(value => value.Number == number);
        scenario.AssertWire(row.Id, invoice.Id, operation, row.ModifiedDate!.Value);
        Assert.Equal(409, Assert.Single(scenario.Decisions).Status);
        await AssertUncertainAsync(scenario, row.Id, operation, number);
        await AssertNoOutcomeAsync(row.Id);
    }

    [Fact]
    public async Task MissingSourceVersionReturns409BeforeInvoicePersistence()
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync();
        await using (var db = fixture.Quotation.Context())
            await db.Quotations.Where(value => value.Id == row.Id).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ModifiedDate, (DateTime?)null));
        var number = $"NULL-VERSION-{Guid.NewGuid():N}";
        using var response = await scenario.CreateAsync(row.Id, Guid.NewGuid(), number);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var invoices = fixture.InvoiceDatabase();
        Assert.False(await invoices.Invoices.AnyAsync(value => value.Number == number));
        Assert.Empty(scenario.Decisions);
        Assert.Equal(0, scenario.LaterEffects);
        await AssertNoOutcomeAsync(row.Id);
    }

    [Fact]
    public async Task VersionlessCompatibilityCallFailsBeforeProducerHttp()
    {
        await using var scenario = Scenario();
        using var bootstrap = scenario.Accounting.CreateClient();
        using var scope = scenario.Accounting.Services.CreateScope();
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => scope.ServiceProvider.GetRequiredService<IInvoiceQuotationCompletionClient>()
            .CompleteAsync(84, 901, Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(scenario.Requests);
        Assert.Equal(0, scenario.LaterEffects);
    }

    [Fact]
    public async Task LostProducerAcknowledgmentLeavesAcceptedInvoiceAndNeedsReconciliationFence()
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync();
        scenario.LoseDecisionResponse = true;
        var operation = Guid.NewGuid();
        var number = $"LOST-{Guid.NewGuid():N}";
        using var response = await scenario.CreateAsync(row.Id, operation, number, delegated: true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var invoices = fixture.InvoiceDatabase();
        var invoice = await invoices.Invoices.AsNoTracking().SingleAsync(value => value.Number == number);
        await scenario.AssertAcceptedAsync(row.Id, invoice.Id);
        Assert.Equal(200, Assert.Single(scenario.Decisions).Status);
        await AssertUncertainAsync(scenario, row.Id, operation, number);
        var before = await fixture.QuotationScalarSnapshotAsync(row.Id);
        // Explicit bounded reconciliation; the HTTP admission must remain fenced.
        await scenario.CompleteAsync(row.Id, invoice.Id, operation, row.ModifiedDate);
        Assert.Equal(before, await fixture.QuotationScalarSnapshotAsync(row.Id));
        await scenario.AssertAcceptedAsync(row.Id, invoice.Id);
        await AssertUncertainAsync(scenario, row.Id, operation, number);
    }

    [Fact]
    public async Task ConcurrentSameAndDifferentInvoiceIntentsRespectPersistedBinding()
    {
        await using var scenario = Scenario();
        foreach (var same in new[] { true, false })
        {
            var row = await scenario.SeedAsync();
            var first = await scenario.InvoiceAsync();
            var second = same ? first : await scenario.InvoiceAsync();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var arrived = 0;
            scenario.BeforeDecisionDispatch = async token =>
            {
                if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            };
            var errors = await Task.WhenAll(
                Record.ExceptionAsync(() => scenario.CompleteAsync(row.Id, first, Guid.NewGuid(), row.ModifiedDate)),
                Record.ExceptionAsync(() => scenario.CompleteAsync(row.Id, second, Guid.NewGuid(), row.ModifiedDate)));
            scenario.BeforeDecisionDispatch = null;
            Assert.Equal(2, arrived);
            if (same) Assert.All(errors, error => Assert.Null(error));
            else
            {
                Assert.Single(errors, error => error is null);
                Assert.IsType<InvoiceCreationConflictException>(Assert.Single(errors, error => error is not null));
            }
            await using var db = fixture.Quotation.Context();
            var stored = await db.Quotations.AsNoTracking().SingleAsync(value => value.Id == row.Id);
            Assert.NotNull(stored.InvoiceId);
            var boundInvoice = stored.InvoiceId.GetValueOrDefault();
            Assert.Contains(boundInvoice, new[] { first, second });
            await scenario.AssertAcceptedAsync(row.Id, boundInvoice);
        }
    }

    [Fact]
    public async Task MalformedCompletionResponseNeverIssuesDecision()
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync();
        scenario.CorruptCompletionLookup = "{}"; // Transport corruption control, not a claimed producer response.
        var invoice = await scenario.InvoiceAsync();
        await Assert.ThrowsAsync<InvoiceCreationDependencyException>(() => scenario.CompleteAsync(row.Id, invoice, Guid.NewGuid(), row.ModifiedDate));
        Assert.Empty(scenario.Decisions);
        Assert.Equal(0, scenario.LaterEffects);
    }

    [Fact]
    public async Task MissingProducerQuotationNeverIssuesDecision()
    {
        await using var scenario = Scenario();
        var invoice = await scenario.InvoiceAsync();
        await Assert.ThrowsAsync<InvoiceCreationDependencyException>(() => scenario.CompleteAsync(int.MaxValue, invoice, Guid.NewGuid(), DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)));
        Assert.Empty(scenario.Decisions);
        Assert.Equal(0, scenario.LaterEffects);
    }

    [Fact]
    public Task LiveIamDenialCannotUseTokenPermissionOrWrite() => IamFailureAsync("denied");

    [Fact]
    public Task LiveIamUnavailableCannotUseTokenPermissionOrWrite() => IamFailureAsync("unavailable");

    private async Task IamFailureAsync(string mode)
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync();
        scenario.AccountingIamFailure = mode;
        var number = $"IAM-{Guid.NewGuid():N}";
        using var response = await scenario.CreateAsync(row.Id, Guid.NewGuid(), number);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, scenario.IamControls);
        Assert.Empty(scenario.Requests);
        Assert.Equal(0, scenario.LaterEffects);
        await using var invoices = fixture.InvoiceDatabase();
        Assert.False(await invoices.Invoices.AnyAsync(value => value.Number == number));
        await AssertNoOutcomeAsync(row.Id);
    }

    [Fact]
    public async Task LinkedOrderPartialFailureKeepsProducerPersistenceAndDelegatedFenceWithoutLaterEffects()
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync();
        await AddLinksAsync(row.Id, 7201, 7202);
        scenario.FailedOrder = 7202;
        var operation = Guid.NewGuid();
        var number = $"PARTIAL-{Guid.NewGuid():N}";
        using var response = await scenario.CreateAsync(row.Id, operation, number, delegated: true);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        await using var invoices = fixture.InvoiceDatabase();
        var invoice = await invoices.Invoices.AsNoTracking().SingleAsync(value => value.Number == number);
        await scenario.AssertAcceptedAsync(row.Id, invoice.Id);
        Assert.Equal(503, Assert.Single(scenario.Decisions).Status);
        Assert.Contains(scenario.Orders, value => value.Id == 7201 && value.Status == 201);
        Assert.Contains(scenario.Orders, value => value.Id == 7202 && value.Status == 503);
        await AssertUncertainAsync(scenario, row.Id, operation, number);
        var before = await fixture.QuotationScalarSnapshotAsync(row.Id);
        var firstOrderKeys = scenario.Orders.ToDictionary(value => value.Id, value => value.Key);
        scenario.FailedOrder = 0;
        // Direct same-intent reconciliation does not reopen the admission or repair a saga.
        await scenario.CompleteAsync(row.Id, invoice.Id, operation, row.ModifiedDate);
        Assert.Equal(before, await fixture.QuotationScalarSnapshotAsync(row.Id));
        Assert.Equal(4, scenario.Orders.Count);
        foreach (var order in scenario.Orders)
            Assert.Equal(firstOrderKeys[order.Id], order.Key);
        await scenario.AssertAcceptedAsync(row.Id, invoice.Id);
        await AssertUncertainAsync(scenario, row.Id, operation, number);
    }

    [Fact]
    public async Task ChildItemEditDoesNotAdvanceParentVersion_CharacterizesAggregateProtectionGap()
    {
        await using var scenario = Scenario();
        var row = await scenario.SeedAsync();
        await using var db = fixture.Quotation.Context();
        var item = await db.OrderItems.SingleAsync(value => value.QuotationId == row.Id);
        scenario.BeforeCompletionLookup = async () =>
        {
            // Existing historical workload grants intentionally exclude LinesWrite. Exercise the real
            // repository through DI for this characterization; do not invent a child-write grant.
            using var scope = scenario.Quotation.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQuotationService>().UpdateOrderItemAsync(
                item.Id, new UpsertQuotationOrderItemRequest(row.Id, null, "Edited synthetic part", 2, 100m), null, CancellationToken.None);
            Assert.Equal(Legacy.Maliev.QuotationService.Application.Models.UpdateResult.Updated, result);
            await using var changedDatabase = fixture.Quotation.Context();
            var unchangedParent = await changedDatabase.Quotations.AsNoTracking().SingleAsync(value => value.Id == row.Id);
            Assert.Equal(row.ModifiedDate, unchangedParent.ModifiedDate);
        };
        var number = $"CHILD-GAP-{Guid.NewGuid():N}";
        using var created = await scenario.CreateAsync(row.Id, Guid.NewGuid(), number);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var parent = await db.Quotations.AsNoTracking().SingleAsync(value => value.Id == row.Id);
        var changed = await db.OrderItems.AsNoTracking().SingleAsync(value => value.Id == item.Id);
        Assert.Equal(2, changed.Quantity);
        await using var invoices = fixture.InvoiceDatabase();
        var invoice = await invoices.Invoices.SingleAsync(value => value.Number == number);
        var line = await invoices.Items.SingleAsync(value => value.InvoiceId == invoice.Id);
        Assert.Equal(1, line.Quantity); // Original line persisted while parent-only decision succeeds.
        Assert.True(parent.Accepted);
        await scenario.AssertAcceptedAsync(row.Id, invoice.Id);
    }

    private async Task AddLinksAsync(int quotation, params int[] orders)
    {
        await using var db = fixture.Quotation.Context();
        foreach (var order in orders) db.OrderLinks.Add(new QuotationOrderLink { QuotationId = quotation, OrderId = order });
        await db.SaveChangesAsync();
    }

    private async Task AssertNoOutcomeAsync(int quotation)
    {
        await using var db = fixture.Quotation.Context();
        Assert.Empty(await db.AcceptedOutcomes.Where(value => value.QuotationId == quotation).ToArrayAsync());
        Assert.Empty(await db.GoogleAnalyticsOutbox.Where(value => value.QuotationId == quotation).ToArrayAsync());
    }

    private async Task AssertUncertainAsync(AtomicProtocolScenario scenario, int quotation, Guid operation, string number)
    {
        await using var db = fixture.InvoiceDatabase();
        var admission = await db.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == operation);
        Assert.Equal("NeedsReconciliation", admission.State);
        Assert.Equal("employee:42", admission.EmployeeSubject);
        Assert.Null(admission.ResultJson);
        var decisions = scenario.Decisions.Count;
        var calls = scenario.Requests.Count;
        using var replay = await scenario.CreateAsync(quotation, operation, number, delegated: true);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(decisions, scenario.Decisions.Count);
        Assert.Equal(calls, scenario.Requests.Count);
        var reloaded = await db.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == operation);
        Assert.Equal("NeedsReconciliation", reloaded.State);
        Assert.Equal(admission.IntentFingerprint, reloaded.IntentFingerprint);
        Assert.Single(await db.Invoices.Where(value => value.Number == number).ToArrayAsync());
        Assert.Equal(0, scenario.LaterEffects);
    }

    private async Task EditParentAsync(AtomicProtocolScenario scenario, Quotation row)
    {
        using var client = scenario.Quotation.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Quotation.AccountingWorkloadToken);
        using var response = await client.PutAsJsonAsync($"/quotations/{row.Id}", new
        {
            row.CustomerId,
            row.EmployeeId,
            row.InvoiceId,
            row.Period,
            row.ExpirationDate,
            row.Subtotal,
            row.Vat,
            row.Total,
            row.WithholdingTax,
            row.CurrencyId,
            Comment = "Concurrent scalar edit",
            row.Fob,
            row.ShippedVia,
            row.Terms,
            row.Accepted
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
