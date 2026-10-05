using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Prospective parent-version and atomic decision wire contracts; no joined or aggregate-snapshot acceptance.</summary>
public sealed class QuotationAtomicCompletionContractTests
{
    private static readonly Guid Operation = Guid.Parse("4f7870e2-d349-41bb-b4cf-567450f261e9");
    private static readonly DateTime OriginalVersion = new DateTime(2030, 7, 18, 12, 34, 56, DateTimeKind.Unspecified).AddTicks(1234560);

    [Fact]
    public async Task Completion_UsesOnlyLookupAndOneAtomicDecisionWithoutFinancialPut()
    {
        using var handler = new CompletionTransport();
        using var http = Http(handler);
        await CompleteWithCurrentApiAsync(new InvoiceQuotationCompletionClient(http));

        Assert.Equal(["GET /quotations/84", "PUT /quotations/84/decision"], handler.Requests.Select(value => value.Route));
    }

    [Fact]
    public async Task Completion_DecisionHasExactlyPascalCaseCustomerInvoiceIntent()
    {
        using var handler = new CompletionTransport();
        using var http = Http(handler);
        await CompleteWithCurrentApiAsync(new InvoiceQuotationCompletionClient(http));

        var decision = Assert.Single(handler.Requests, value => value.Route == "PUT /quotations/84/decision");
        using var json = JsonDocument.Parse(decision.Body!);
        Assert.Equal(["Accepted", "EmployeeInitiated", "InvoiceId"], json.RootElement.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.True(json.RootElement.GetProperty("Accepted").GetBoolean());
        Assert.False(json.RootElement.GetProperty("EmployeeInitiated").GetBoolean());
        Assert.Equal(901, json.RootElement.GetProperty("InvoiceId").GetInt32());
    }

    [Fact]
    public async Task Completion_DecisionCarriesStableOperationAndOriginalVersionRatherThanLaterLookupVersion()
    {
        using var handler = new CompletionTransport { LookupVersion = OriginalVersion.AddMinutes(1) };
        using var http = Http(handler);
        await CompleteWithCurrentApiAsync(new InvoiceQuotationCompletionClient(http));

        var decision = Assert.Single(handler.Requests, value => value.Route == "PUT /quotations/84/decision");
        Assert.Equal(Operation.ToString("D"), decision.Operation);
        var expected = new DateTimeOffset(DateTime.SpecifyKind(OriginalVersion, DateTimeKind.Utc));
        Assert.Equal(expected.ToString("O", CultureInfo.InvariantCulture), decision.ExpectedVersion);
        Assert.NotEqual(new DateTimeOffset(DateTime.SpecifyKind(handler.LookupVersion, DateTimeKind.Utc)).ToString("O", CultureInfo.InvariantCulture), decision.ExpectedVersion);
    }

    [Theory]
    [InlineData("2030-07-18T12:34:56.123456", DateTimeKind.Unspecified)]
    [InlineData("2030-07-18T12:34:56.123456Z", DateTimeKind.Utc)]
    public async Task SourceSnapshot_PreservesOriginalModifiedDateTicksAndKind(string wireVersion, DateTimeKind kind)
    {
        var source = new InvoiceCreationSourceClient(new SourceTransport(wireVersion));
        var snapshot = await source.GetAsync(84, CancellationToken.None);

        // The public snapshot contract is deliberately absent today; reflection keeps this first RED buildable.
        var property = typeof(InvoiceCreationQuotation).GetProperty("ModifiedDate");
        Assert.NotNull(property);
        Assert.Equal(typeof(DateTime?), property.PropertyType);
        var actual = Assert.IsType<DateTime>(property.GetValue(snapshot.Quotation));
        Assert.Equal(OriginalVersion.Ticks, actual.Ticks);
        Assert.Equal(kind, actual.Kind);
    }

    [Fact]
    public async Task CreateAsync_MissingSourceVersionConflictsBeforeInvoiceOrDownstreamWrites()
    {
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(SnapshotWithoutVersion());
        var store = new Mock<IInvoiceCreationStore>();
        store.Setup(value => value.CreateAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice invoice, IReadOnlyList<InvoiceOrderItem> _, CancellationToken _) => { invoice.Id = 901; return invoice; });
        var completion = new Mock<IInvoiceQuotationCompletionClient>();
        completion.Setup(value => value.CompleteAsync(84, 901, Operation, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var documents = new Mock<IInvoiceCreationDocumentClient>();
        documents.Setup(value => value.RenderAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        var files = new Mock<IInvoiceCreationFileClient>();
        files.Setup(value => value.ExistsAsync("maliev.com", "invoices/901/invoice_inv-84.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(value => value.LinkFileAsync(901, "maliev.com", "invoices/901/invoice_inv-84.pdf", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var notifications = new Mock<IInvoiceCreationNotificationClient>();
        notifications.Setup(value => value.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Invoice>(), It.IsAny<byte[]>(), Operation, It.IsAny<CancellationToken>())).ReturnsAsync("synthetic-provider-message");
        var workflow = Workflow(source.Object, store.Object, completion.Object, documents.Object, files.Object, notifications.Object, EmptyJournal().Object);

        var failure = await Record.ExceptionAsync(() => workflow.CreateAsync(84, Request(), Operation, CancellationToken.None));

        Assert.IsType<InvoiceCreationConflictException>(failure);
        store.Verify(value => value.CreateAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(completion.Invocations);
        Assert.Empty(documents.Invocations);
        Assert.Empty(files.Invocations);
        Assert.Empty(notifications.Invocations);
    }

    [Fact]
    public async Task CreateAsync_CompletedJournalReplayReturnsBeforeSourceVersionOrAnyEffects()
    {
        var replay = new InvoiceCreationResult(901, InvoiceCreationState.Completed, InvoiceCreationEmailState.NotRequested, null, new("maliev.com", "invoices/901/invoice_inv-84.pdf"));
        var journal = new Mock<IInvoiceCreationJournal>(MockBehavior.Strict);
        journal.Setup(value => value.GetAsync("create:84", Operation, It.IsAny<CancellationToken>())).ReturnsAsync(replay);
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        var store = new Mock<IInvoiceCreationStore>(MockBehavior.Strict);
        var completion = new Mock<IInvoiceQuotationCompletionClient>(MockBehavior.Strict);
        var documents = new Mock<IInvoiceCreationDocumentClient>(MockBehavior.Strict);
        var files = new Mock<IInvoiceCreationFileClient>(MockBehavior.Strict);
        var notifications = new Mock<IInvoiceCreationNotificationClient>(MockBehavior.Strict);
        var workflow = Workflow(source.Object, store.Object, completion.Object, documents.Object, files.Object, notifications.Object, journal.Object);

        var result = await workflow.CreateAsync(84, Request(), Operation, CancellationToken.None);

        Assert.Same(replay, result);
        Assert.Empty(source.Invocations);
        Assert.Empty(store.Invocations);
        Assert.Empty(completion.Invocations);
        Assert.Empty(documents.Invocations);
        Assert.Empty(files.Invocations);
        Assert.Empty(notifications.Invocations);
        journal.VerifyAll();
        journal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Completion_VersionlessCompatibilityOverloadConflictsBeforeHttp()
    {
        using var handler = new CompletionTransport();
        using var http = Http(handler);
        var client = new InvoiceQuotationCompletionClient(http);
        var failure = await Record.ExceptionAsync(() => client.CompleteAsync(84, 901, Operation, CancellationToken.None));

        Assert.Empty(handler.Requests);
        Assert.IsType<InvoiceCreationConflictException>(failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Completion_FailedLookupNeverIssuesMutation(HttpStatusCode status)
    {
        using var handler = new CompletionTransport { LookupStatus = status };
        using var http = Http(handler);
        await Assert.ThrowsAsync<InvoiceCreationDependencyException>(() => CompleteWithCurrentApiAsync(new InvoiceQuotationCompletionClient(http)));

        Assert.Equal("GET /quotations/84", Assert.Single(handler.Requests).Route);
    }

    [Fact]
    public async Task Completion_ExistingDifferentInvoiceCannotBeRebound()
    {
        using var handler = new CompletionTransport { InvoiceId = 902 };
        using var http = Http(handler);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => CompleteWithCurrentApiAsync(new InvoiceQuotationCompletionClient(http)));

        Assert.Equal("GET /quotations/84", Assert.Single(handler.Requests).Route);
    }

    [Fact]
    public async Task Completion_LookupForAnotherQuotationNeverIssuesMutation()
    {
        using var handler = new CompletionTransport { QuotationId = 85 };
        using var http = Http(handler);
        await Assert.ThrowsAsync<InvoiceCreationDependencyException>(() => CompleteWithCurrentApiAsync(new InvoiceQuotationCompletionClient(http)));

        Assert.Equal("GET /quotations/84", Assert.Single(handler.Requests).Route);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Completion_InvalidLookupCannotAuthorizeMutation(string body)
    {
        using var handler = new CompletionTransport { LookupBody = body };
        using var http = Http(handler);
        var failure = await Record.ExceptionAsync(() => CompleteWithCurrentApiAsync(new InvoiceQuotationCompletionClient(http)));

        Assert.NotNull(failure);
        Assert.Equal("GET /quotations/84", Assert.Single(handler.Requests).Route);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Completion_RejectedDecisionIsNeverReportedSuccessful(HttpStatusCode status)
    {
        using var handler = new CompletionTransport { DecisionStatus = status };
        using var http = Http(handler);
        await Assert.ThrowsAsync<InvoiceCreationDependencyException>(() => CompleteWithCurrentApiAsync(new InvoiceQuotationCompletionClient(http)));

        Assert.Equal(["GET /quotations/84", "PUT /quotations/84/decision"], handler.Requests.Select(value => value.Route));
    }

    [Fact]
    public async Task Completion_ConflictingDecisionIsNeverReportedSuccessful()
    {
        using var handler = new CompletionTransport { DecisionStatus = HttpStatusCode.Conflict };
        using var http = Http(handler);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => CompleteWithCurrentApiAsync(new InvoiceQuotationCompletionClient(http)));

        Assert.Equal(["GET /quotations/84", "PUT /quotations/84/decision"], handler.Requests.Select(value => value.Route));
    }

    // Explicit first-RED invocation seam, not conditional behavior: replace this call with
    // CompleteAsync(84, 901, Operation, OriginalVersion, CancellationToken.None) when that public API exists.
    // Keep the separate versionless compatibility test on the four-argument overload.
    private static Task CompleteWithCurrentApiAsync(InvoiceQuotationCompletionClient client)
        => client.CompleteAsync(84, 901, Operation, CancellationToken.None);

    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new("https://quotation-contract.invalid/") };

    private static InvoiceCreationWorkflowService Workflow(IInvoiceCreationSource source, IInvoiceCreationStore store,
        IInvoiceQuotationCompletionClient completion, IInvoiceCreationDocumentClient documents,
        IInvoiceCreationFileClient files, IInvoiceCreationNotificationClient notifications, IInvoiceCreationJournal journal)
        => new(source, store, completion, documents, files, notifications, journal, new NoopLock(),
            new FakeTimeProvider(new DateTimeOffset(2030, 7, 18, 12, 0, 0, TimeSpan.Zero)));

    private static Mock<IInvoiceCreationJournal> EmptyJournal()
    {
        var journal = new Mock<IInvoiceCreationJournal>();
        journal.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((InvoiceCreationResult?)null);
        journal.Setup(value => value.SetAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<InvoiceCreationResult>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return journal;
    }

    private static CreateInvoiceFromQuotationRequest Request() => new("INV-84", null, null, null, null, null, null,
        new(null, null, null, null, null, null, null, null, null), new(null, null, null, null, null, null, null, null, null), null, null, false, true);

    private static InvoiceCreationSourceSnapshot SnapshotWithoutVersion() => new(
        new(84, 42, 7, 1, 100m, 7m, 107m, 3m, null, null, null, null, null),
        new(42, "Synthetic Thai customer", "fixture@example.invalid", null, null, null, null, null, null),
        new(7, "Synthetic Thai employee"), new(1, "THB", "Thai baht"),
        [new(1, 84, null, "Synthetic part", 1, 100m, 100m)]);

    private sealed class NoopLock : IInvoiceCreationLock
    {
        public ValueTask<IAsyncDisposable> AcquireAsync(int quotationId, CancellationToken cancellationToken) => ValueTask.FromResult<IAsyncDisposable>(new Lease());
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }

    private sealed record CapturedRequest(string Route, string? Body, string? Operation, string? ExpectedVersion);

    private sealed class CompletionTransport : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];
        public DateTime LookupVersion { get; init; } = OriginalVersion;
        public int QuotationId { get; init; } = 84;
        public string? LookupBody { get; init; }
        public int? InvoiceId { get; init; }
        public HttpStatusCode LookupStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode DecisionStatus { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var route = request.Method + " " + request.RequestUri!.AbsolutePath;
            Requests.Add(new(route, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                Header(request, "Idempotency-Key"), Header(request, "X-Expected-Modified-Date")));
            if (route == "GET /quotations/84")
                return Json(LookupBody ?? JsonSerializer.Serialize(new { Id = QuotationId, CustomerId = 42, EmployeeId = 7, InvoiceId, Period = 14,
                    ExpirationDate = "2030-08-01T00:00:00", Subtotal = 100m, Vat = 7m, Total = 107m,
                    WithholdingTax = 3m, CurrencyId = 1, Accepted = (bool?)null, ModifiedDate = LookupVersion }), LookupStatus);
            // Permit the current financial PUT only to capture the real old sequence. Assertions prohibit it.
            if (route == "PUT /quotations/84") return new(HttpStatusCode.NoContent);
            if (route == "PUT /quotations/84/decision") return Json("{}", DecisionStatus);
            throw new InvalidOperationException("Unexpected contract transport route.");
        }

        private static string? Header(HttpRequestMessage request, string name)
            => request.Headers.TryGetValues(name, out var values) ? Assert.Single(values) : null;
    }

    private sealed class SourceTransport(string wireVersion) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => Http(new SourceHandler(wireVersion));
    }

    private sealed class SourceHandler(string wireVersion) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/quotations/84" => JsonSerializer.Serialize(new { Id = 84, CustomerId = 42, EmployeeId = 7, CurrencyId = 1,
                    Subtotal = 100m, Vat = 7m, Total = 107m, ModifiedDate = wireVersion }),
                "/quotations/84/orderitems" => """[{"Id":1,"QuotationId":84,"Description":"Synthetic part","Quantity":1,"UnitPrice":100,"Subtotal":100}]""",
                "/customers/42" => """{"Id":42,"FullName":"Synthetic Thai customer","Email":"fixture@example.invalid"}""",
                "/employees/7" => """{"Id":7,"FullName":"Synthetic Thai employee"}""",
                "/currencies/1" => """{"Id":1,"ShortName":"THB","LongName":"Thai baht"}""",
                _ => throw new InvalidOperationException("Unexpected source contract route."),
            };
            return Task.FromResult(Json(body));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
