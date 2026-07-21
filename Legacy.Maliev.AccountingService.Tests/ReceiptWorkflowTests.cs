using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class ReceiptWorkflowTests
{
    private static readonly Guid OperationId = Guid.Parse("9e60b70d-21af-473e-8749-fab4993e4f4f");

    [Fact]
    public async Task CreateAsync_NewReceipt_CompletesOwnedWorkflowAndUsesStableOperationIdentity()
    {
        var invoice = Invoice(42);
        var receipt = Receipt(91, invoice.Number);
        var store = new Mock<IReceiptWorkflowStore>(MockBehavior.Strict);
        store.Setup(value => value.GetAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiptWorkflowSnapshot(invoice, [InvoiceItem(42)], null, [], []));
        store.Setup(value => value.CreateReceiptAsync(invoice, It.IsAny<IReadOnlyList<InvoiceOrderItem>>(), "paid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipt);
        store.Setup(value => value.LinkInvoiceAsync(42, 91, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        store.Setup(value => value.LinkFileAsync(91, "maliev.com", "receipts/91/receipt_91.pdf", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var documents = new Mock<IReceiptDocumentClient>(MockBehavior.Strict);
        documents.Setup(value => value.RenderAsync(receipt, It.IsAny<IReadOnlyList<ReceiptOrderItem>>(), new byte[] { 7 }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });
        var files = new Mock<IReceiptFileClient>(MockBehavior.Strict);
        files.Setup(value => value.ExistsAsync("maliev.com", "receipts/91/receipt_91.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        files.Setup(value => value.UploadAsync("maliev.com", "receipts/91", "receipt_91.pdf", new byte[] { 1, 2, 3 }, OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiptStoredFile("maliev.com", "receipts/91/receipt_91.pdf"));
        var notifications = new Mock<IReceiptNotificationClient>(MockBehavior.Strict);
        notifications.Setup(value => value.SendAsync(
                "customer@example.com", "Customer", receipt, new byte[] { 1, 2, 3 }, OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("message-id");
        var journal = Journal();
        var customers = Customer();
        var signatures = Signature();

        var result = await Workflow(store, documents, files, notifications, journal, customers, signatures).CreateAsync(
            42,
            new CreateReceiptRequest("paid", true, 12),
            OperationId,
            CancellationToken.None);

        Assert.Equal(ReceiptWorkflowState.Completed, result.State);
        Assert.Equal(ReceiptEmailState.Delivered, result.EmailState);
        Assert.Equal(91, result.ReceiptId);
        journal.Verify(value => value.SetAsync("create:42", OperationId, result, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ReconciledReceipt_NeverSilentlyResendsEmail()
    {
        var invoice = Invoice(42); invoice.ReceiptId = 91;
        var receipt = Receipt(91, invoice.Number);
        var store = new Mock<IReceiptWorkflowStore>(MockBehavior.Strict);
        store.Setup(value => value.GetAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiptWorkflowSnapshot(invoice, [InvoiceItem(42)], receipt, [ReceiptItem(91)], []));
        store.Setup(value => value.LinkInvoiceAsync(42, 91, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        store.Setup(value => value.LinkFileAsync(91, "maliev.com", "receipts/91/receipt_91.pdf", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var documents = new Mock<IReceiptDocumentClient>(MockBehavior.Strict);
        documents.Setup(value => value.RenderAsync(receipt, It.IsAny<IReadOnlyList<ReceiptOrderItem>>(), new byte[] { 7 }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });
        var files = new Mock<IReceiptFileClient>(MockBehavior.Strict);
        files.Setup(value => value.ExistsAsync("maliev.com", "receipts/91/receipt_91.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var notifications = new Mock<IReceiptNotificationClient>(MockBehavior.Strict);

        var result = await Workflow(store, documents, files, notifications, Journal(), Customer(), Signature()).CreateAsync(
            42,
            new CreateReceiptRequest("paid", true, 12),
            OperationId,
            CancellationToken.None);

        Assert.Equal(ReceiptWorkflowState.Reconciled, result.State);
        Assert.Equal(ReceiptEmailState.ExplicitRetryRequired, result.EmailState);
        notifications.VerifyNoOtherCalls();
        files.Verify(value => value.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_AlreadyMissingObject_StillRemovesDatabaseStateAndUnlinksInvoice()
    {
        var invoice = Invoice(42); invoice.ReceiptId = 91;
        var receipt = Receipt(91, invoice.Number);
        var store = new Mock<IReceiptWorkflowStore>(MockBehavior.Strict);
        store.Setup(value => value.GetAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiptWorkflowSnapshot(invoice, [], receipt, [], [new ReceiptFile { ReceiptId = 91, Bucket = "maliev.com", ObjectName = "receipts/91/receipt_91.pdf" }]));
        store.Setup(value => value.DeleteReceiptAsync(91, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        store.Setup(value => value.UnlinkInvoiceAsync(42, 91, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var files = new Mock<IReceiptFileClient>(MockBehavior.Strict);
        files.Setup(value => value.DeleteAsync("maliev.com", "receipts/91/receipt_91.pdf", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await Workflow(store, new(), files, new(), Journal(), new(), new()).RemoveAsync(42, OperationId, CancellationToken.None);

        Assert.Equal(ReceiptWorkflowState.Removed, result.State);
        store.VerifyAll();
    }

    private static ReceiptWorkflowService Workflow(
        Mock<IReceiptWorkflowStore> store,
        Mock<IReceiptDocumentClient> documents,
        Mock<IReceiptFileClient> files,
        Mock<IReceiptNotificationClient> notifications,
        Mock<IReceiptOperationJournal> journal,
        Mock<IReceiptCustomerClient> customers,
        Mock<IReceiptSignatureClient> signatures) =>
        new(store.Object, documents.Object, files.Object, notifications.Object, customers.Object, signatures.Object, journal.Object, new NoopLock());

    private static Mock<IReceiptCustomerClient> Customer()
    {
        var customer = new Mock<IReceiptCustomerClient>(MockBehavior.Strict);
        customer.Setup(value => value.GetAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiptCustomerContact("customer@example.com", "Customer"));
        return customer;
    }

    private static Mock<IReceiptSignatureClient> Signature()
    {
        var signature = new Mock<IReceiptSignatureClient>(MockBehavior.Strict);
        signature.Setup(value => value.GetAsync(12, It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 7 });
        return signature;
    }

    private static Mock<IReceiptOperationJournal> Journal()
    {
        var journal = new Mock<IReceiptOperationJournal>(MockBehavior.Strict);
        journal.Setup(value => value.GetAsync(It.IsAny<string>(), OperationId, It.IsAny<CancellationToken>())).ReturnsAsync((ReceiptWorkflowResult?)null);
        journal.Setup(value => value.SetAsync(It.IsAny<string>(), OperationId, It.IsAny<ReceiptWorkflowResult>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return journal;
    }

    private static Invoice Invoice(int id) => new()
    {
        Id = id,
        Number = "INV-42",
        CustomerId = 7,
        Currency = "THB",
        IsPaid = true,
        PaymentDate = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc),
        Subtotal = 100m,
        Vat = 7m,
        Total = 107m,
        Outstanding = 107m,
    };

    private static InvoiceOrderItem InvoiceItem(int invoiceId) => new()
    {
        Id = 1,
        InvoiceId = invoiceId,
        Description = "Print",
        Quantity = 1,
        UnitPrice = 100m,
        Subtotal = 100m,
    };

    private static Receipt Receipt(int id, string invoiceNumber) => new()
    {
        Id = id,
        InvoiceNumber = invoiceNumber,
        CustomerId = 7,
        Currency = "THB",
        PaymentDate = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc),
        Subtotal = 100m,
        Vat = 7m,
        Total = 107m,
        AmountPaid = 107m,
    };

    private static ReceiptOrderItem ReceiptItem(int receiptId) => new()
    {
        Id = 1,
        ReceiptId = receiptId,
        Description = "Print",
        Quantity = 1,
        UnitPrice = 100m,
        Subtotal = 100m,
    };

    private sealed class NoopLock : IReceiptOperationLock
    {
        public ValueTask<IAsyncDisposable> AcquireAsync(int invoiceId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable>(new Lease());

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
