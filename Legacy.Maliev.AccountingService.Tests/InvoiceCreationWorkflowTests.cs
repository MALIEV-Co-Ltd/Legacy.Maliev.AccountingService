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
    private static readonly InvoiceNotificationOrigin Origin = new("https://auth.example.invalid", "employee:42", "service:legacy-intranet");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EmployeePrepareRetainsAuthorityWithoutDecisionDocumentOrNotification(bool enabled, bool sendEmail)
    {
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot());
        var store = new Mock<IInvoiceCreationStore>(MockBehavior.Strict);
        store.Setup(value => value.FindByNumberAsync("INV-84", It.IsAny<CancellationToken>())).ReturnsAsync((Invoice?)null);
        store.Setup(value => value.CreateAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(),
            It.Is<InvoiceFinancialCommitContext>(authority => authority.OperationId == OperationId && authority.QuotationId == 84 &&
                authority.Origin == Origin && authority.OriginalQuotationVersion == Snapshot().Quotation.ModifiedDate), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice invoice, IReadOnlyList<InvoiceOrderItem> _, InvoiceFinancialCommitContext _, CancellationToken _) => invoice);
        var receipt = FinancialReceipt();
        var reader = new Mock<IInvoiceFinancialOwnershipReader>(MockBehavior.Strict);
        reader.SetupSequence(value => value.ReadForOriginAsync(OperationId, 84, Origin, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvoiceFinancialOwnership?)null).ReturnsAsync(receipt);
        reader.Setup(value => value.ValidatePendingAsync(OperationId, 84, Origin, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var quotations = new Mock<IInvoiceQuotationCompletionClient>(MockBehavior.Strict);
        var documents = new Mock<IInvoiceCreationDocumentClient>(MockBehavior.Strict);
        var notifications = new Mock<IInvoiceCreationNotificationClient>(MockBehavior.Strict);
        var coordinator = new Mock<IInvoiceNotificationWorkflow>(MockBehavior.Strict);
        coordinator.SetupGet(value => value.Enabled).Returns(enabled);
        var workflow = Create(source, store, quotations, documents, notifications: notifications,
            invoiceNotifications: coordinator.Object, financialOwnership: reader.Object);
        Assert.Equal(receipt, await workflow.PrepareFinancialAsync(84, Request(false) with { SendEmail = sendEmail }, OperationId, Origin, CancellationToken.None));
        store.VerifyAll();
        reader.VerifyAll();
        quotations.VerifyNoOtherCalls();
        documents.VerifyNoOtherCalls();
        notifications.VerifyNoOtherCalls();
        coordinator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmployeePrepareUnknownAcknowledgementReadsCommittedOwnershipBeforeAnyRecreation()
    {
        var reader = new Mock<IInvoiceFinancialOwnershipReader>(MockBehavior.Strict);
        reader.Setup(value => value.ReadForOriginAsync(OperationId, 84, Origin, It.IsAny<CancellationToken>())).ReturnsAsync(FinancialReceipt());
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        var store = new Mock<IInvoiceCreationStore>(MockBehavior.Strict);
        Assert.Equal(FinancialReceipt(), await Create(source, store, financialOwnership: reader.Object)
            .PrepareFinancialAsync(84, Request(false), OperationId, Origin, CancellationToken.None));
        source.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
        reader.Verify(value => value.ReadForOriginAsync(OperationId, 84, Origin, It.IsAny<CancellationToken>()), Times.Once);
        reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmployeePrepareUnboundSameNumberRefusesDespiteMatchingCustomerAndTotal()
    {
        var reader = new Mock<IInvoiceFinancialOwnershipReader>();
        reader.Setup(value => value.ReadForOriginAsync(OperationId, 84, Origin, It.IsAny<CancellationToken>())).ReturnsAsync((InvoiceFinancialOwnership?)null);
        var source = new Mock<IInvoiceCreationSource>();
        source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot());
        var store = new Mock<IInvoiceCreationStore>(MockBehavior.Strict);
        store.Setup(value => value.FindByNumberAsync("INV-84", It.IsAny<CancellationToken>())).ReturnsAsync(new Invoice { Id = 901, CustomerId = 42, Total = 1070.27m });
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => Create(source, store, financialOwnership: reader.Object)
            .PrepareFinancialAsync(84, Request(false), OperationId, Origin, CancellationToken.None));
        store.Verify(value => value.FindByNumberAsync("INV-84", It.IsAny<CancellationToken>()), Times.Once);
        store.VerifyNoOtherCalls();
    }

    private static InvoiceFinancialOwnership FinancialReceipt() => new(1, OperationId, 84, 901, Origin.Issuer,
        Origin.EmployeeSubject, Origin.ServiceSubject, "2030-07-18T00:00:00.0000000Z", new string('A', 64));

    [Fact]
    public async Task LegacyCustomerByNumberCannotBypassRetainedEmployeeFinancialFence()
    {
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot());
        var store = new Mock<IInvoiceCreationStore>(MockBehavior.Strict);
        store.Setup(value => value.FindByNumberAsync("INV-84", It.IsAny<CancellationToken>())).ReturnsAsync(new Invoice { Id = 901, Number = "INV-84", CustomerId = 42, Total = 1070.27m });
        var reader = new Mock<IInvoiceFinancialOwnershipReader>(MockBehavior.Strict);
        reader.Setup(value => value.HasFenceAsync(901, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var quotations = new Mock<IInvoiceQuotationCompletionClient>(MockBehavior.Strict);
        var documents = new Mock<IInvoiceCreationDocumentClient>(MockBehavior.Strict);
        var notifications = new Mock<IInvoiceCreationNotificationClient>(MockBehavior.Strict);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => Create(source, store, quotations, documents,
            notifications: notifications, financialOwnership: reader.Object).CreateAsync(84, Request(false), OperationId, CancellationToken.None));
        quotations.VerifyNoOtherCalls(); documents.VerifyNoOtherCalls(); notifications.VerifyNoOtherCalls();
        store.Verify(value => value.FindByNumberAsync("INV-84", It.IsAny<CancellationToken>()), Times.Once);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnabledEmail_MissingOriginRefusesBeforeJournalOrFinancialEffects()
    {
        var coordinator = new Mock<IInvoiceNotificationWorkflow>();
        coordinator.SetupGet(value => value.Enabled).Returns(true);
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        var journal = new Mock<IInvoiceCreationJournal>(MockBehavior.Strict);
        var workflow = Create(source: source, journal: journal.Object, invoiceNotifications: coordinator.Object);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => workflow.CreateAsync(84, Request(false), OperationId, CancellationToken.None));
        source.VerifyNoOtherCalls();
        journal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnabledReplay_ValidatesDurableOriginBeforeJournalAndReceiptReplay()
    {
        var sequence = new MockSequence();
        var coordinator = new Mock<IInvoiceNotificationWorkflow>(MockBehavior.Strict);
        coordinator.SetupGet(value => value.Enabled).Returns(true);
        coordinator.InSequence(sequence).Setup(value => value.ValidateOriginAsync(84, OperationId, Origin, false, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var completed = new InvoiceCreationResult(901, InvoiceCreationState.Completed, InvoiceCreationEmailState.ProviderAccepted,
            "accepted", new("maliev.com", "invoice.pdf"));
        var journal = new Mock<IInvoiceCreationJournal>(MockBehavior.Strict);
        journal.InSequence(sequence).Setup(value => value.GetAsync("create:84", OperationId, It.IsAny<CancellationToken>())).ReturnsAsync(completed);
        coordinator.InSequence(sequence).Setup(value => value.HasFenceAsync(901, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        coordinator.InSequence(sequence).Setup(value => value.ValidateReplayAsync(84, OperationId, Origin, completed, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        var result = await Create(source: source, journal: journal.Object, invoiceNotifications: coordinator.Object)
            .CreateAsync(84, Request(false), OperationId, Origin, CancellationToken.None);
        Assert.Equal(completed, result);
        source.VerifyNoOtherCalls();
        coordinator.VerifyAll();
        journal.VerifyAll();
    }

    [Fact]
    public async Task InvalidRetainedOrigin_RejectsBeforeEvenCompletedJournalLookup()
    {
        var coordinator = new Mock<IInvoiceNotificationWorkflow>();
        coordinator.SetupGet(value => value.Enabled).Returns(true);
        coordinator.Setup(value => value.ValidateOriginAsync(84, OperationId, Origin, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvoiceCreationConflictException("different origin"));
        var journal = new Mock<IInvoiceCreationJournal>(MockBehavior.Strict);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => Create(journal: journal.Object, invoiceNotifications: coordinator.Object)
            .CreateAsync(84, Request(false), OperationId, Origin, CancellationToken.None));
        journal.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnabledEmail_RecordsOwnFinancialEvidenceAndNeverCallsLegacyClient(bool accepted)
    {
        var source = new Mock<IInvoiceCreationSource>();
        source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot());
        var store = new Mock<IInvoiceCreationStore>();
        store.Setup(value => value.CreateAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice invoice, IReadOnlyList<InvoiceOrderItem> _, CancellationToken _) => { invoice.Id = 901; return invoice; });
        var files = new Mock<IInvoiceCreationFileClient>();
        files.Setup(value => value.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var coordinator = new Mock<IInvoiceNotificationWorkflow>();
        coordinator.SetupGet(value => value.Enabled).Returns(true);
        coordinator.Setup(value => value.SendAsync(84, OperationId, Origin, "customer@example.com", "Customer One",
            It.IsAny<Invoice>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new InvoiceNotificationDeliveryResult(accepted, accepted ? "accepted" : null));
        var legacy = new Mock<IInvoiceCreationNotificationClient>(MockBehavior.Strict);
        var journal = Journal();
        var result = await Create(source: source, store: store, files: files, notifications: legacy,
            journal: journal.Object, invoiceNotifications: coordinator.Object).CreateAsync(84, Request(false), OperationId, Origin, CancellationToken.None);
        Assert.Equal(accepted ? InvoiceCreationEmailState.ProviderAccepted : InvoiceCreationEmailState.ExplicitRetryRequired, result.EmailState);
        coordinator.Verify(value => value.PrepareFinancialAsync(84, OperationId, Origin,
            It.Is<InvoiceCreationResult>(financial => financial.InvoiceId == 901 && financial.State == InvoiceCreationState.Completed &&
                financial.EmailState == InvoiceCreationEmailState.NotRequested && financial.ProviderMessageId == null), It.IsAny<CancellationToken>()), Times.Once);
        coordinator.Verify(value => value.ValidateOriginAsync(84, OperationId, Origin, false, It.IsAny<CancellationToken>()), Times.Once);
        coordinator.Verify(value => value.ValidateOriginAsync(84, OperationId, Origin, true, It.IsAny<CancellationToken>()), Times.Once);
        legacy.VerifyNoOtherCalls();
        journal.Verify(value => value.SetAsync("create:84", OperationId, It.IsAny<InvoiceCreationResult>(), It.IsAny<CancellationToken>()), accepted ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task DisabledWithRetainedFence_CannotReplayOrFallbackToLegacy()
    {
        var journal = Journal();
        journal.Setup(value => value.GetAsync("create:84", OperationId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new InvoiceCreationResult(901, InvoiceCreationState.Completed, InvoiceCreationEmailState.ProviderAccepted, "accepted", new("maliev.com", "invoice.pdf")));
        var coordinator = new Mock<IInvoiceNotificationWorkflow>();
        coordinator.SetupGet(value => value.Enabled).Returns(false);
        coordinator.Setup(value => value.HasFenceAsync(901, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        var legacy = new Mock<IInvoiceCreationNotificationClient>(MockBehavior.Strict);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => Create(source: source, notifications: legacy,
            journal: journal.Object, invoiceNotifications: coordinator.Object).CreateAsync(84, Request(false), OperationId, CancellationToken.None));
        source.VerifyNoOtherCalls();
        legacy.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReconciliationAndCompletedReplay_DelegateWithoutFinancialEffects()
    {
        var coordinator = new Mock<IInvoiceNotificationWorkflow>();
        var completed = new InvoiceCreationResult(901, InvoiceCreationState.Completed, InvoiceCreationEmailState.ProviderAccepted, "accepted", new("maliev.com", "invoice.pdf"));
        coordinator.Setup(value => value.ReconcileAsync(84, OperationId, Origin, It.IsAny<CancellationToken>())).ReturnsAsync(completed);
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        var workflow = Create(source: source, invoiceNotifications: coordinator.Object);
        Assert.Equal(completed, await workflow.ReconcileAsync(84, OperationId, Origin, CancellationToken.None));
        Assert.Equal(completed, await workflow.ReplayCompletedAsync(84, OperationId, Origin, completed, CancellationToken.None));
        coordinator.Verify(value => value.ValidateReplayAsync(84, OperationId, Origin, completed, It.IsAny<CancellationToken>()), Times.Once);
        source.VerifyNoOtherCalls();
    }

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
        Assert.Equal(701, preview.SourceRequestId);
        Assert.Equal(Guid.Parse("a3308993-39b9-41fc-bbfd-f3500de40f55"), preview.SourceJourneyId);
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
        quotation.Setup(value => value.CompleteAsync(84, 901, OperationId, Snapshot().Quotation.ModifiedDate, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var files = new Mock<IInvoiceCreationFileClient>();
        files.Setup(value => value.ExistsAsync("maliev.com", "invoices/901/invoice_inv-84.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var documents = new Mock<IInvoiceCreationDocumentClient>();
        documents.Setup(value => value.RenderAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        store.Setup(value => value.LinkFileAsync(901, "maliev.com", "invoices/901/invoice_inv-84.pdf", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var journal = Journal();
        var workflow = Create(source, store, quotation, documents, files, journal: journal.Object);

        var result = await workflow.CreateAsync(84, Request(deductWithholdingTax: false), OperationId, CancellationToken.None);

        quotation.Verify(value => value.CompleteAsync(84, 901, OperationId, Snapshot().Quotation.ModifiedDate, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(901, result.InvoiceId);
        Assert.Equal(InvoiceCreationState.Completed, result.State);
        store.Verify(value => value.CreateAsync(
            It.Is<Invoice>(invoice => invoice.CustomerId == 42 && invoice.SalesPerson == "Employee One" && invoice.Currency == "THB" && invoice.Subtotal == 1000.25m && invoice.Vat == 70.02m && invoice.Total == 1070.27m && invoice.WithholdingTax == 0m && invoice.Outstanding == 1070.27m && invoice.SourceRequestId == 701 && invoice.SourceJourneyId == Guid.Parse("a3308993-39b9-41fc-bbfd-f3500de40f55")),
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
        quotation.Setup(value => value.CompleteAsync(84, 901, OperationId, Snapshot().Quotation.ModifiedDate, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
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
        IInvoiceCreationJournal? journal = null,
        IInvoiceNotificationWorkflow? invoiceNotifications = null,
        IInvoiceFinancialOwnershipReader? financialOwnership = null) => new(
            (source ?? new()).Object,
            (store ?? new()).Object,
            (quotation ?? new()).Object,
            (documents ?? Document()).Object,
            (files ?? new()).Object,
            (notifications ?? new()).Object,
            journal ?? Journal().Object,
            new NoopLock(),
            new FakeTimeProvider(new DateTimeOffset(2030, 7, 18, 12, 0, 0, TimeSpan.Zero)), invoiceNotifications, financialOwnership);

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
        new(84, 42, 7, 1, 1000.25m, 70.02m, 1070.27m, 30m, "quotation", "Bangkok", "Courier", "Net 7", null, 701, Guid.Parse("a3308993-39b9-41fc-bbfd-f3500de40f55"), new DateTime(2030, 7, 18, 0, 0, 0, DateTimeKind.Unspecified)),
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
