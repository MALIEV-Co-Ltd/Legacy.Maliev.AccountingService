using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceCreationWorkflowTests
{
    private static readonly Guid OperationId = Guid.Parse("4f7870e2-d349-41bb-b4cf-567450f261e9");

    [Fact]
    public async Task PreviewAsync_DerivesIdentityFinancialsAndItemsFromQuotationSnapshot()
    {
        var source = new Mock<IInvoiceCreationSource>();
        source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot());
        var workflow = Create(source: source);

        var preview = await workflow.PreviewAsync(84, CancellationToken.None);

        Assert.Equal(42, preview.CustomerId);
        Assert.Equal("180730-42-84", preview.InvoiceNumber);
        Assert.Equal("Employee One", preview.SalesPerson);
        Assert.Equal("THB", preview.Currency);
        Assert.Equal(1000.25m, preview.Subtotal);
        Assert.Equal(70.02m, preview.Vat);
        Assert.Equal(1070.27m, preview.Total);
        Assert.Equal(30m, preview.AvailableWithholdingTax);
        Assert.Equal(1040.27m, preview.Outstanding);
        Assert.Equal("Customer One", preview.BillingAddress.Recipient);
        Assert.Equal("Bangkok", preview.BillingAddress.City);
        Assert.Single(preview.OrderItems);
        Assert.Equal("Part one", preview.OrderItems[0].Description);
    }

    [Fact]
    public async Task CreateAsync_IgnoresBrowserFinancialAndIdentityAuthority()
    {
        var source = new Mock<IInvoiceCreationSource>();
        source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot());
        var store = new Mock<IInvoiceCreationStore>();
        store.Setup(value => value.FindByNumberAsync("INV-84", It.IsAny<CancellationToken>())).ReturnsAsync((Invoice?)null);
        store.Setup(value => value.CreateAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice invoice, IReadOnlyList<InvoiceOrderItem> _, CancellationToken _) => { invoice.Id = 901; return invoice; });
        var quotation = new Mock<IInvoiceQuotationCompletionClient>();
        quotation.Setup(value => value.CompleteAsync(84, 901, OperationId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var files = new Mock<IInvoiceCreationFileClient>();
        files.Setup(value => value.ExistsAsync("maliev.com", "invoices/901/invoice_inv-84.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var documents = new Mock<IInvoiceCreationDocumentClient>();
        documents.Setup(value => value.RenderAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        store.Setup(value => value.LinkFileAsync(901, "maliev.com", "invoices/901/invoice_inv-84.pdf", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var journal = Journal();
        var workflow = Create(source, store, quotation, documents, files, journal: journal.Object);

        var result = await workflow.CreateAsync(84, Request(deductWithholdingTax: false), OperationId, CancellationToken.None);

        Assert.Equal(901, result.InvoiceId);
        Assert.Equal(InvoiceCreationState.Completed, result.State);
        store.Verify(value => value.CreateAsync(
            It.Is<Invoice>(invoice => invoice.CustomerId == 42 && invoice.SalesPerson == "Employee One" && invoice.Currency == "THB" && invoice.Subtotal == 1000.25m && invoice.Vat == 70.02m && invoice.Total == 1070.27m && invoice.WithholdingTax == 0m && invoice.Outstanding == 1070.27m),
            It.Is<IReadOnlyList<InvoiceOrderItem>>(items => items.Count == 1 && items[0].Description == "Part one"),
            It.IsAny<CancellationToken>()), Times.Once);
        quotation.VerifyAll();
        journal.Verify(value => value.SetAsync("create:84", OperationId, It.Is<InvoiceCreationResult>(saved => saved.InvoiceId == 901), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ReplaysSameStableOperationWithoutDownstreamWrites()
    {
        var replay = new InvoiceCreationResult(901, InvoiceCreationState.Completed, InvoiceCreationEmailState.Delivered, "provider", new("maliev.com", "invoices/901/invoice_inv-84.pdf"));
        var journal = new Mock<IInvoiceCreationJournal>();
        journal.Setup(value => value.GetAsync("create:84", OperationId, It.IsAny<CancellationToken>())).ReturnsAsync(replay);
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        var store = new Mock<IInvoiceCreationStore>(MockBehavior.Strict);
        var workflow = Create(source, store, journal: journal.Object);

        var result = await workflow.CreateAsync(84, Request(true), OperationId, CancellationToken.None);

        Assert.Same(replay, result);
    }

    [Fact]
    public async Task CreateAsync_ReconcilesExistingInvoiceWithoutAutomaticallyResendingEmail()
    {
        var snapshot = Snapshot();
        var source = new Mock<IInvoiceCreationSource>();
        source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var store = new Mock<IInvoiceCreationStore>();
        store.Setup(value => value.FindByNumberAsync("INV-84", It.IsAny<CancellationToken>())).ReturnsAsync(new Invoice { Id = 901, Number = "INV-84", CustomerId = 42, Total = 1070.27m });
        var quotation = new Mock<IInvoiceQuotationCompletionClient>();
        quotation.Setup(value => value.CompleteAsync(84, 901, OperationId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var files = new Mock<IInvoiceCreationFileClient>();
        files.Setup(value => value.ExistsAsync("maliev.com", "invoices/901/invoice_inv-84.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(value => value.LinkFileAsync(901, "maliev.com", "invoices/901/invoice_inv-84.pdf", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var notification = new Mock<IInvoiceCreationNotificationClient>(MockBehavior.Strict);
        var workflow = Create(source, store, quotation, files: files, notifications: notification);

        var result = await workflow.CreateAsync(84, Request(true), OperationId, CancellationToken.None);

        Assert.Equal(InvoiceCreationState.Reconciled, result.State);
        Assert.Equal(InvoiceCreationEmailState.ExplicitRetryRequired, result.EmailState);
        store.Verify(value => value.CreateAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static InvoiceCreationWorkflowService Create(
        Mock<IInvoiceCreationSource>? source = null,
        Mock<IInvoiceCreationStore>? store = null,
        Mock<IInvoiceQuotationCompletionClient>? quotation = null,
        Mock<IInvoiceCreationDocumentClient>? documents = null,
        Mock<IInvoiceCreationFileClient>? files = null,
        Mock<IInvoiceCreationNotificationClient>? notifications = null,
        IInvoiceCreationJournal? journal = null) => new(
            (source ?? new()).Object,
            (store ?? new()).Object,
            (quotation ?? new()).Object,
            (documents ?? Document()).Object,
            (files ?? new()).Object,
            (notifications ?? new()).Object,
            journal ?? Journal().Object,
            new NoopLock(),
            new FakeTimeProvider(new DateTimeOffset(2030, 7, 18, 12, 0, 0, TimeSpan.Zero)));

    private static Mock<IInvoiceCreationDocumentClient> Document()
    {
        var document = new Mock<IInvoiceCreationDocumentClient>();
        document.Setup(value => value.RenderAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        return document;
    }

    private static Mock<IInvoiceCreationJournal> Journal()
    {
        var journal = new Mock<IInvoiceCreationJournal>();
        journal.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((InvoiceCreationResult?)null);
        journal.Setup(value => value.SetAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<InvoiceCreationResult>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return journal;
    }

    private static CreateInvoiceFromQuotationRequest Request(bool deductWithholdingTax) => new(
        "INV-84", "customer note", "PO-1", "Req", "Courier", "Bangkok", "Net 7",
        new("Customer One", "MALIEV Customer", "Tower", "Road", null, "Bangkok", "Bangkok", "10110", "Thailand"),
        new("Customer One", "MALIEV Customer", "Tower", "Road", null, "Bangkok", "Bangkok", "10110", "Thailand", "0812345678"),
        "TAX", "REG", deductWithholdingTax, true);

    private static InvoiceCreationSourceSnapshot Snapshot() => new(
        new(84, 42, 7, 1, 1000.25m, 70.02m, 1070.27m, 30m, "quotation", "Bangkok", "Courier", "Net 7", null),
        new(42, "Customer One", "customer@example.com", "0812345678", null, null,
            new("MALIEV Customer", "TAX", "REG"),
            new("Tower", "Road", null, "Bangkok", "Bangkok", "10110", "Thailand"),
            new("Tower", "Road", null, "Bangkok", "Bangkok", "10110", "Thailand")),
        new(7, "Employee One"),
        new(1, "THB", "Thai baht"),
        [new(1, 84, 51, "Part one", 2, 500.125m, 1000.25m)]);

    private sealed class NoopLock : IInvoiceCreationLock
    {
        public ValueTask<IAsyncDisposable> AcquireAsync(int quotationId, CancellationToken cancellationToken) => ValueTask.FromResult<IAsyncDisposable>(new Lease());
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
