using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Bounded decision-only resume; even completed Order evidence cannot run later document/notification work.</summary>
public sealed class InvoiceEmployeeDecisionResumeWorkflowTests
{
    private static readonly Guid Operation = Guid.Parse("ea0f8575-2c6b-4772-9f23-bc51b1e3d72b");
    private static readonly InvoiceNotificationOrigin Origin = new("https://auth.example.invalid", "employee:42", "service:legacy-intranet");
    private static readonly InvoiceFinancialOwnership Ownership = new(1, Operation, 84, 901, Origin.Issuer, Origin.EmployeeSubject,
        Origin.ServiceSubject, "2026-10-06T01:02:03.0000000Z", new string('A', 64));

    [Fact]
    public async Task CompletedOwningReceiptDoesNotRunLaterWorkflowEffects()
    {
        var setup = new Setup();
        setup.Client.Setup(value => value.CompleteAsync(Ownership, "fresh-proof", It.IsAny<CancellationToken>())).ReturnsAsync(Receipt("Completed"));
        Assert.Equal("Completed", (await setup.Service.ResumeEmployeeDecisionAsync(84, Operation, Origin, "fresh-proof", CancellationToken.None)).State);
        setup.Financial.Verify(value => value.MarkDecisionUncertainAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<InvoiceNotificationOrigin>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(setup.Lease.Disposed);
        setup.AssertNoLaterEffects();
    }

    [Theory]
    [InlineData("QuotationCommitted")]
    [InlineData("OrdersPartial")]
    [InlineData("OrdersConflict")]
    public async Task PartialOwningReceiptRetainsUncertainAdmissionWithoutRecreatingFinancials(string state)
    {
        var setup = new Setup();
        setup.Client.Setup(value => value.CompleteAsync(Ownership, "fresh-proof", It.IsAny<CancellationToken>())).ReturnsAsync(Receipt(state));
        Assert.Equal(state, (await setup.Service.ResumeEmployeeDecisionAsync(84, Operation, Origin, "fresh-proof", CancellationToken.None)).State);
        setup.Financial.Verify(value => value.MarkDecisionUncertainAsync(Operation, 84, Origin, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(setup.Lease.Disposed);
        setup.AssertNoLaterEffects();
    }

    [Fact]
    public async Task UnknownDecisionAcknowledgmentRetainsReconciliationAndPropagatesFailure()
    {
        var setup = new Setup();
        setup.Client.Setup(value => value.CompleteAsync(Ownership, "fresh-proof", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("Synthetic lost acknowledgment"));
        await Assert.ThrowsAsync<HttpRequestException>(() => setup.Service.ResumeEmployeeDecisionAsync(84, Operation, Origin, "fresh-proof", CancellationToken.None));
        setup.Financial.Verify(value => value.MarkDecisionUncertainAsync(Operation, 84, Origin, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(setup.Lease.Disposed);
        setup.AssertNoLaterEffects();
    }

    [Fact]
    public async Task MissingFinancialReceiptCannotAdoptInvoiceByNumberOrDecision()
    {
        var setup = new Setup();
        setup.Financial.Setup(value => value.ReadForOriginAsync(Operation, 84, Origin, It.IsAny<CancellationToken>())).ReturnsAsync((InvoiceFinancialOwnership?)null);
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => setup.Service.ResumeEmployeeDecisionAsync(84, Operation, Origin, "fresh-proof", CancellationToken.None));
        setup.Client.VerifyNoOtherCalls();
        setup.AssertNoLaterEffects();
    }

    [Fact]
    public async Task ChangedFinancialSnapshotRefusesBeforeDecision()
    {
        var setup = new Setup();
        setup.Financial.Setup(value => value.ReadCommittedAsync(Operation, It.IsAny<CancellationToken>())).ReturnsAsync(
            new InvoiceCommittedFinancialSnapshot(Ownership with { InvoiceId = 902 }, new Invoice { Id = 902 }, []));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => setup.Service.ResumeEmployeeDecisionAsync(84, Operation, Origin, "fresh-proof", CancellationToken.None));
        setup.Client.VerifyNoOtherCalls();
        setup.AssertNoLaterEffects();
    }

    [Fact]
    public async Task CancelledLeaseWaiterOwnsNoPhaseAndCannotPoisonWriter()
    {
        var setup = new Setup();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        setup.Lock.Setup(value => value.AcquireAsync(84, cancelled.Token)).Throws(new OperationCanceledException(cancelled.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => setup.Service.ResumeEmployeeDecisionAsync(84, Operation, Origin, "fresh-proof", cancelled.Token));
        setup.Financial.VerifyNoOtherCalls();
        setup.Client.VerifyNoOtherCalls();
        setup.AssertNoLaterEffects();
    }

    [Fact]
    public async Task MissingFreshProofCannotAcquireLeaseOrReadAuthority()
    {
        var setup = new Setup();
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => setup.Service.ResumeEmployeeDecisionAsync(84, Operation, Origin, "", CancellationToken.None));
        setup.Lock.VerifyNoOtherCalls();
        setup.Financial.VerifyNoOtherCalls();
        setup.Client.VerifyNoOtherCalls();
        setup.AssertNoLaterEffects();
    }

    private static InvoiceQuotationOperationReceipt Receipt(string state) => new(1, Operation.ToString("D"), 84, 901,
        Origin.Issuer, Origin.EmployeeSubject, Origin.ServiceSubject, "service:legacy-accounting", Ownership.OriginalQuotationVersion,
        Ownership.FinancialBinding, "invoice-creation-financial-v1", state, "2026-10-06T01:02:04.0000000Z",
        state == "Completed" ? 2 : 1, 2, "2026-10-06T01:02:05.0000000Z");

    private sealed class Setup
    {
        private readonly Mock<IInvoiceCreationSource> source = new(MockBehavior.Strict);
        private readonly Mock<IInvoiceCreationStore> store = new(MockBehavior.Strict);
        private readonly Mock<IInvoiceQuotationCompletionClient> legacyDecision = new(MockBehavior.Strict);
        private readonly Mock<IInvoiceCreationDocumentClient> documents = new(MockBehavior.Strict);
        private readonly Mock<IInvoiceCreationFileClient> files = new(MockBehavior.Strict);
        private readonly Mock<IInvoiceCreationNotificationClient> notifications = new(MockBehavior.Strict);
        private readonly Mock<IInvoiceCreationJournal> journal = new(MockBehavior.Strict);
        public Mock<IInvoiceFinancialOwnershipReader> Financial { get; } = new(MockBehavior.Strict);
        public Mock<IInvoiceEmployeeQuotationCompletionClient> Client { get; } = new(MockBehavior.Strict);
        public Mock<IInvoiceCreationLock> Lock { get; } = new(MockBehavior.Strict);
        public Lease Lease { get; } = new();
        public InvoiceCreationWorkflowService Service { get; }
        public Setup()
        {
            Lock.Setup(value => value.AcquireAsync(84, It.IsAny<CancellationToken>())).Returns(new ValueTask<IAsyncDisposable>(Lease));
            Financial.Setup(value => value.ReadForOriginAsync(Operation, 84, Origin, It.IsAny<CancellationToken>())).ReturnsAsync(Ownership);
            Financial.Setup(value => value.ReadCommittedAsync(Operation, It.IsAny<CancellationToken>())).ReturnsAsync(
                new InvoiceCommittedFinancialSnapshot(Ownership, new Invoice { Id = 901 }, []));
            Financial.Setup(value => value.MarkDecisionUncertainAsync(Operation, 84, Origin, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Service = new(source.Object, store.Object, legacyDecision.Object, documents.Object, files.Object, notifications.Object,
                journal.Object, Lock.Object, TimeProvider.System, financialOwnership: Financial.Object, employeeQuotations: Client.Object);
        }
        public void AssertNoLaterEffects()
        {
            source.VerifyNoOtherCalls(); store.VerifyNoOtherCalls(); legacyDecision.VerifyNoOtherCalls();
            documents.VerifyNoOtherCalls(); files.VerifyNoOtherCalls(); notifications.VerifyNoOtherCalls(); journal.VerifyNoOtherCalls();
        }
    }
    private sealed class Lease : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
