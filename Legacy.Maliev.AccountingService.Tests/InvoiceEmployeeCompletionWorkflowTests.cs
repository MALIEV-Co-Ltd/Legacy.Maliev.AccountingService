using System.Security.Cryptography;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceEmployeeCompletionWorkflowTests
{
    private static readonly Guid Operation = Guid.Parse("dc904058-4d1b-4eab-b8b2-a505700b0419");
    private static readonly InvoiceNotificationOrigin Origin = new("https://auth.example.invalid", "employee:42", "service:legacy-intranet");
    private static readonly InvoiceFinancialOwnership Ownership = new(1, Operation, 84, 901, Origin.Issuer, Origin.EmployeeSubject, Origin.ServiceSubject,
        "2026-10-06T01:02:03.0000000Z", new string('A', 64));
    private static readonly byte[] Pdf = [1, 2, 3];
    private static readonly InvoiceCreationStoredFile Stored = new("maliev.com", "invoices/901/invoice_inv-901.pdf");
    private static readonly string PdfDigest = Convert.ToHexString(SHA256.HashData(Pdf));

    [Fact]
    public async Task FreshCompletionRendersCommittedRowsAndRetainsPostDocumentResultWithoutNotification()
    {
        var setup = new Setup();
        var result = await setup.RunAsync(false);
        Assert.Equal(InvoiceCreationEmailState.NotRequested, result.EmailState);
        Assert.Equal(Stored, result.StoredFile);
        setup.Documents.Verify(value => value.RenderAsync(setup.Invoice, setup.Items, It.IsAny<CancellationToken>()), Times.Once);
        setup.Files.Verify(value => value.UploadAsync(Stored.Bucket, "invoices/901", "invoice_INV-901.pdf", Pdf, Operation, It.IsAny<CancellationToken>()), Times.Once);
        setup.Store.Verify(value => value.LinkFileAsync(901, Stored.Bucket, Stored.ObjectName, It.IsAny<CancellationToken>()), Times.Once);
        setup.Source.VerifyNoOtherCalls();
        setup.Notifications.VerifyNoOtherCalls();
        setup.AssertNoFinancialRecreation();
    }

    [Theory]
    [InlineData("QuotationCommitted")]
    [InlineData("OrdersPartial")]
    [InlineData("OrdersConflict")]
    public async Task PartialDecisionCannotBeginDocumentOrMarkAccountingCompleted(string state)
    {
        var setup = new Setup();
        setup.Decision.Setup(value => value.CompleteAsync(Ownership, "fresh.proof.signature", It.IsAny<CancellationToken>())).ReturnsAsync(Receipt(state));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => setup.RunAsync(false));
        setup.Completion.VerifyNoOtherCalls();
        setup.Documents.VerifyNoOtherCalls();
        setup.Files.VerifyNoOtherCalls();
        setup.AssertNoFinancialRecreation();
    }

    [Fact]
    public async Task LostUploadAcknowledgmentCannotRenderOrUploadAgainOnResume()
    {
        var setup = new Setup();
        setup.Files.Setup(value => value.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), Operation, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Synthetic lost upload acknowledgment"));
        await Assert.ThrowsAsync<IOException>(() => setup.RunAsync(false));
        setup.Phase = Phase("DocumentExecuting");
        await Assert.ThrowsAsync<InvoiceCreationUnavailableException>(() => setup.RunAsync(false));
        setup.Documents.Verify(value => value.RenderAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>()), Times.Once);
        setup.Files.Verify(value => value.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), Operation, It.IsAny<CancellationToken>()), Times.Once);
        setup.Completion.Verify(value => value.RetainDocumentAsync(It.IsAny<InvoiceFinancialOwnership>(), It.IsAny<string>(), It.IsAny<InvoiceCreationStoredFile>(), It.IsAny<CancellationToken>()), Times.Never);
        setup.AssertNoFinancialRecreation();
    }

    [Fact]
    public async Task FilenameExistenceCannotAdoptAnUnboundDocument()
    {
        var setup = new Setup();
        setup.Files.Setup(value => value.ExistsAsync(Stored.Bucket, Stored.ObjectName, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => setup.RunAsync(false));
        setup.Documents.VerifyNoOtherCalls();
        setup.Files.Verify(value => value.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        setup.AssertNoFinancialRecreation();
    }

    [Fact]
    public async Task RetainedDocumentCompletionDoesNotRepeatRenderUploadOrLink()
    {
        var setup = new Setup { Phase = Phase("DocumentReady") };
        Assert.Equal(InvoiceCreationEmailState.NotRequested, (await setup.RunAsync(false)).EmailState);
        setup.Documents.VerifyNoOtherCalls();
        setup.Files.VerifyNoOtherCalls();
        setup.Store.VerifyNoOtherCalls();
        setup.Source.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompletedReplayDoesNotRepeatAnyDocumentOrSendWork()
    {
        var result = new InvoiceCreationResult(901, InvoiceCreationState.Completed, InvoiceCreationEmailState.NotRequested, null, Stored);
        var setup = new Setup { Phase = Phase("Completed") with { Result = result } };
        Assert.Equal(result, await setup.RunAsync(false));
        setup.Documents.VerifyNoOtherCalls(); setup.Files.VerifyNoOtherCalls(); setup.Store.VerifyNoOtherCalls();
        setup.Source.VerifyNoOtherCalls(); setup.Notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnknownLegacyNotificationNeverUsesAnotherSendPermit()
    {
        var setup = new Setup { Phase = Phase("NotificationExecuting") };
        Assert.Equal(InvoiceCreationEmailState.ExplicitRetryRequired, (await setup.RunAsync(true)).EmailState);
        setup.Completion.Verify(value => value.BeginNotificationAsync(It.IsAny<InvoiceFinancialOwnership>(), It.IsAny<CancellationToken>()), Times.Never);
        setup.Source.VerifyNoOtherCalls(); setup.Files.VerifyNoOtherCalls(); setup.Notifications.VerifyNoOtherCalls();
        setup.Completion.Verify(value => value.RetainCompletedAsync(It.IsAny<InvoiceFinancialOwnership>(), It.IsAny<InvoiceCreationResult>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangedRecipientCustomerCannotSendOriginalInvoice()
    {
        var setup = new Setup { Phase = Phase("DocumentReady") };
        setup.Source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(Contact(43));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => setup.RunAsync(true));
        setup.Notifications.VerifyNoOtherCalls();
        setup.Files.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChangedRetainedPdfCannotBeSentOrRerendered()
    {
        var setup = new Setup { Phase = Phase("DocumentReady") };
        setup.Files.Setup(value => value.DownloadAsync(Stored.Bucket, Stored.ObjectName, 10 * 1024 * 1024, It.IsAny<CancellationToken>())).ReturnsAsync([4, 5, 6]);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => setup.RunAsync(true));
        setup.Notifications.VerifyNoOtherCalls(); setup.Documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FirstLegacySendHasOneDurablePermitAndPreservesExistingResultSemantics()
    {
        var setup = new Setup { Phase = Phase("DocumentReady") };
        setup.Notifications.Setup(value => value.SendAsync("synthetic@example.invalid", "Synthetic customer", setup.Invoice, Pdf, Operation, It.IsAny<CancellationToken>())).ReturnsAsync("legacy-message");
        var result = await setup.RunAsync(true);
        Assert.Equal(InvoiceCreationEmailState.Delivered, result.EmailState);
        setup.Notifications.Verify(value => value.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Invoice>(), It.IsAny<byte[]>(), Operation, It.IsAny<CancellationToken>()), Times.Once);
        setup.Completion.Verify(value => value.BeginNotificationAsync(Ownership, It.IsAny<CancellationToken>()), Times.Once);
        setup.AssertNoFinancialRecreation();
    }

    [Fact]
    public async Task FirstV2SendUsesExistingPhaseWorkflowAndNeverLegacySendOrFinancialReprepare()
    {
        var setup = new Setup { Phase = Phase("DocumentReady") };
        var v2 = new Mock<IInvoiceNotificationWorkflow>(MockBehavior.Strict);
        v2.SetupGet(value => value.Enabled).Returns(true);
        v2.Setup(value => value.HasFenceAsync(901, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        v2.Setup(value => value.SendAsync(84, Operation, Origin, "synthetic@example.invalid", "Synthetic customer", setup.Invoice, Pdf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceNotificationDeliveryResult(true, "provider-message"));
        setup.V2 = v2.Object;
        Assert.Equal(InvoiceCreationEmailState.ProviderAccepted, (await setup.RunAsync(true)).EmailState);
        setup.Completion.Verify(value => value.BeginNotificationAsync(Ownership, It.IsAny<CancellationToken>()), Times.Once);
        v2.Verify(value => value.SendAsync(84, Operation, Origin, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Invoice>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
        setup.Notifications.VerifyNoOtherCalls();
        setup.AssertNoFinancialRecreation();
    }

    [Fact]
    public async Task ExistingV2CorrelationUsesReadOnlyReconciliationWithoutResettingPending()
    {
        var setup = new Setup { Phase = Phase("NotificationExecuting") };
        var v2 = new Mock<IInvoiceNotificationWorkflow>(MockBehavior.Strict);
        v2.SetupGet(value => value.Enabled).Returns(true);
        v2.Setup(value => value.HasFenceAsync(901, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        v2.Setup(value => value.ReconcileAsync(84, Operation, Origin, It.IsAny<CancellationToken>())).ReturnsAsync(
            new InvoiceCreationResult(901, InvoiceCreationState.Completed, InvoiceCreationEmailState.ProviderAccepted, "provider-message", Stored));
        setup.V2 = v2.Object;
        Assert.Equal(InvoiceCreationEmailState.ProviderAccepted, (await setup.RunAsync(true)).EmailState);
        setup.Completion.Verify(value => value.BeginNotificationAsync(It.IsAny<InvoiceFinancialOwnership>(), It.IsAny<CancellationToken>()), Times.Never);
        setup.Source.VerifyNoOtherCalls(); setup.Notifications.VerifyNoOtherCalls(); setup.Files.VerifyNoOtherCalls();
    }

    private static InvoiceEmployeeCompletionPhase Phase(string state) => new(1, Operation, 84, 901, Ownership.FinancialBinding,
        "2026-10-06T01:02:04.0000000Z", 2, state, state == "DocumentExecuting" ? null : PdfDigest,
        state == "DocumentExecuting" ? null : Stored, null);
    private static InvoiceQuotationOperationReceipt Receipt(string state = "Completed") => new(1, Operation.ToString("D"), 84, 901,
        Origin.Issuer, Origin.EmployeeSubject, Origin.ServiceSubject, "service:legacy-accounting", Ownership.OriginalQuotationVersion,
        Ownership.FinancialBinding, "invoice-creation-financial-v1", state, "2026-10-06T01:02:04.0000000Z", state == "Completed" ? 2 : 1, 2,
        "2026-10-06T01:02:05.0000000Z");
    private static InvoiceCreationSourceSnapshot Contact(int id = 42) => new(new(84, id, 7, 1, 999m, 0m, 999m, null, null, null, null, null, 901),
        new(id, "Synthetic customer", "synthetic@example.invalid", null, null, null, null, null, null), new(7, "Synthetic employee"), new(1, "THB", "Thai baht"), []);
    private static CreateInvoiceFromQuotationRequest Request(bool sendEmail) => new("INV-901", null, null, null, null, null, null,
        new(null, null, null, null, null, null, null, null, null), new(null, null, null, null, null, null, null, null, null), null, null, false, sendEmail);
    private sealed class Setup
    {
        public Mock<IInvoiceCreationSource> Source { get; } = new(MockBehavior.Strict);
        public Mock<IInvoiceCreationStore> Store { get; } = new(MockBehavior.Strict);
        private readonly Mock<IInvoiceQuotationCompletionClient> legacy = new(MockBehavior.Strict);
        public Mock<IInvoiceCreationDocumentClient> Documents { get; } = new(MockBehavior.Strict);
        public Mock<IInvoiceCreationFileClient> Files { get; } = new(MockBehavior.Strict);
        public Mock<IInvoiceCreationNotificationClient> Notifications { get; } = new(MockBehavior.Strict);
        private readonly Mock<IInvoiceCreationJournal> journal = new(MockBehavior.Strict);
        private readonly Mock<IInvoiceFinancialOwnershipReader> financial = new(MockBehavior.Strict);
        public Mock<IInvoiceEmployeeQuotationCompletionClient> Decision { get; } = new(MockBehavior.Strict);
        public Mock<IInvoiceEmployeeCompletionStore> Completion { get; } = new(MockBehavior.Strict);
        public InvoiceEmployeeCompletionPhase? Phase { get; set; }
        public IInvoiceNotificationWorkflow? V2 { get; set; }
        public Invoice Invoice { get; } = new() { Id = 901, Number = "INV-901", CustomerId = 42, Total = 12m };
        public IReadOnlyList<InvoiceOrderItem> Items { get; } = [new() { InvoiceId = 901, Description = "Committed", Quantity = 3, UnitPrice = 4m, Subtotal = 12m }];
        public Setup()
        {
            financial.Setup(value => value.ReadForOriginAsync(Operation, 84, Origin, It.IsAny<CancellationToken>())).ReturnsAsync(Ownership);
            financial.Setup(value => value.ReadCommittedAsync(Operation, It.IsAny<CancellationToken>())).ReturnsAsync(new InvoiceCommittedFinancialSnapshot(Ownership, Invoice, Items));
            financial.Setup(value => value.MarkDecisionUncertainAsync(Operation, 84, Origin, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Decision.Setup(value => value.CompleteAsync(Ownership, "fresh.proof.signature", It.IsAny<CancellationToken>())).ReturnsAsync(Receipt());
            Completion.Setup(value => value.ReadAsync(Ownership, It.IsAny<CancellationToken>())).ReturnsAsync(() => Phase);
            Completion.Setup(value => value.BeginDocumentAsync(Ownership, It.IsAny<InvoiceQuotationOperationReceipt>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Completion.Setup(value => value.RetainDocumentAsync(Ownership, PdfDigest, Stored, It.IsAny<CancellationToken>())).ReturnsAsync(InvoiceEmployeeCompletionWorkflowTests.Phase("DocumentReady"));
            Completion.Setup(value => value.BeginNotificationAsync(Ownership, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Completion.Setup(value => value.RetainCompletedAsync(Ownership, It.IsAny<InvoiceCreationResult>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Documents.Setup(value => value.RenderAsync(Invoice, Items, It.IsAny<CancellationToken>())).ReturnsAsync(Pdf);
            Files.Setup(value => value.ExistsAsync(Stored.Bucket, Stored.ObjectName, It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Files.Setup(value => value.UploadAsync(Stored.Bucket, "invoices/901", "invoice_INV-901.pdf", Pdf, Operation, It.IsAny<CancellationToken>())).ReturnsAsync(Stored);
            Files.Setup(value => value.DownloadAsync(Stored.Bucket, Stored.ObjectName, 10 * 1024 * 1024, It.IsAny<CancellationToken>())).ReturnsAsync(Pdf);
            Store.Setup(value => value.LinkFileAsync(901, Stored.Bucket, Stored.ObjectName, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(Contact());
        }
        public Task<InvoiceCreationResult> RunAsync(bool sendEmail) => new InvoiceCreationWorkflowService(Source.Object, Store.Object, legacy.Object,
            Documents.Object, Files.Object, Notifications.Object, journal.Object, new NoopLock(), TimeProvider.System, V2, financial.Object,
            Decision.Object, Completion.Object).CompleteEmployeeAsync(84, Operation, Origin, Request(sendEmail), "fresh.proof.signature", CancellationToken.None);
        public void AssertNoFinancialRecreation()
        {
            Store.Verify(value => value.FindByNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Store.Verify(value => value.CreateAsync(It.IsAny<Invoice>(), It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), It.IsAny<CancellationToken>()), Times.Never);
            legacy.VerifyNoOtherCalls(); journal.VerifyNoOtherCalls();
        }
    }
    private sealed class NoopLock : IInvoiceCreationLock
    {
        public ValueTask<IAsyncDisposable> AcquireAsync(int quotationId, CancellationToken cancellationToken) => ValueTask.FromResult<IAsyncDisposable>(new Lease());
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
