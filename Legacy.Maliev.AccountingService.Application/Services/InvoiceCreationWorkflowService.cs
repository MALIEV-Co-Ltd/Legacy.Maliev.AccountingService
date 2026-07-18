using System.Globalization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;

namespace Legacy.Maliev.AccountingService.Application.Services;

/// <summary>Owns authoritative invoice creation and retry reconciliation for a quotation.</summary>
public sealed class InvoiceCreationWorkflowService(
    IInvoiceCreationSource source,
    IInvoiceCreationStore store,
    IInvoiceQuotationCompletionClient quotations,
    IInvoiceCreationDocumentClient documents,
    IInvoiceCreationFileClient files,
    IInvoiceCreationNotificationClient notifications,
    IInvoiceCreationJournal journal,
    IInvoiceCreationLock operationLock,
    TimeProvider timeProvider) : IInvoiceCreationWorkflow
{
    private const string Bucket = "maliev.com";

    public async Task<InvoiceCreationPreview> PreviewAsync(int quotationId, CancellationToken cancellationToken)
    {
        ValidateQuotation(quotationId);
        return Preview(await source.GetAsync(quotationId, cancellationToken), timeProvider.GetUtcNow());
    }

    public async Task<InvoiceCreationResult> CreateAsync(int quotationId, CreateInvoiceFromQuotationRequest request, Guid operationId, CancellationToken cancellationToken)
    {
        ValidateQuotation(quotationId);
        ArgumentNullException.ThrowIfNull(request);
        if (operationId == Guid.Empty) throw new ArgumentException("A stable operation UUID is required.", nameof(operationId));
        if (string.IsNullOrWhiteSpace(request.InvoiceNumber)) throw new ArgumentException("Invoice number is required.", nameof(request));

        var scope = $"create:{quotationId}";
        var replay = await journal.GetAsync(scope, operationId, cancellationToken);
        if (replay is not null) return replay;

        await using var lease = await operationLock.AcquireAsync(quotationId, cancellationToken);
        replay = await journal.GetAsync(scope, operationId, cancellationToken);
        if (replay is not null) return replay;

        var snapshot = await source.GetAsync(quotationId, cancellationToken);
        var preview = Preview(snapshot, timeProvider.GetUtcNow());
        var invoiceNumber = request.InvoiceNumber.Trim();
        var existing = await store.FindByNumberAsync(invoiceNumber, cancellationToken);
        var reconciled = existing is not null;
        Invoice invoice;
        IReadOnlyList<InvoiceOrderItem> items;
        if (existing is not null)
        {
            if (existing.CustomerId != preview.CustomerId || existing.Total != preview.Total)
            {
                throw new InvoiceCreationConflictException("Invoice number already belongs to different authoritative quotation data.");
            }

            invoice = existing;
            items = MapItems(snapshot.OrderItems, existing.Id, timeProvider.GetUtcNow().UtcDateTime);
        }
        else
        {
            invoice = MapInvoice(preview, request, timeProvider.GetUtcNow().UtcDateTime);
            items = MapItems(snapshot.OrderItems, null, invoice.CreatedDate!.Value);
            invoice = await store.CreateAsync(invoice, items, cancellationToken);
            foreach (var item in items) item.InvoiceId = invoice.Id;
        }

        await quotations.CompleteAsync(quotationId, invoice.Id, operationId, cancellationToken);
        var path = $"invoices/{invoice.Id}";
        var fileName = $"invoice_{SafeFilePart(invoice.Number)}.pdf";
        var objectName = $"{path}/{fileName}".ToLowerInvariant();
        var pdf = await documents.RenderAsync(invoice, items, cancellationToken);
        InvoiceCreationStoredFile stored;
        if (await files.ExistsAsync(Bucket, objectName, cancellationToken))
        {
            stored = new(Bucket, objectName);
        }
        else
        {
            stored = await files.UploadAsync(Bucket, path, fileName, pdf, operationId, cancellationToken);
            if (!string.Equals(stored.Bucket, Bucket, StringComparison.Ordinal) || !string.Equals(stored.ObjectName, objectName, StringComparison.Ordinal))
                throw new InvoiceCreationDependencyException("FileService returned an unexpected invoice object identity.");
        }

        await store.LinkFileAsync(invoice.Id, stored.Bucket, stored.ObjectName, cancellationToken);
        var emailState = InvoiceCreationEmailState.NotRequested;
        string? messageId = null;
        if (request.SendEmail)
        {
            if (reconciled) emailState = InvoiceCreationEmailState.ExplicitRetryRequired;
            else
            {
                messageId = await notifications.SendAsync(snapshot.Customer.Email, snapshot.Customer.FullName, invoice, pdf, operationId, cancellationToken);
                emailState = InvoiceCreationEmailState.Delivered;
            }
        }

        var result = new InvoiceCreationResult(invoice.Id, reconciled ? InvoiceCreationState.Reconciled : InvoiceCreationState.Completed, emailState, messageId, stored);
        await journal.SetAsync(scope, operationId, result, cancellationToken);
        return result;
    }

    private static InvoiceCreationPreview Preview(InvoiceCreationSourceSnapshot value, DateTimeOffset now)
    {
        var q = value.Quotation;
        if (q.CustomerId is null || q.CustomerId <= 0 || q.EmployeeId is null || q.EmployeeId <= 0)
            throw new InvoiceCreationConflictException("Quotation has no valid customer or employee owner.");
        if (value.Customer.Id != q.CustomerId || value.Employee.Id != q.EmployeeId || value.Currency.Id != q.CurrencyId)
            throw new InvoiceCreationDependencyException("Quotation dependencies returned mismatched identities.");
        if (value.OrderItems.Count == 0) throw new InvoiceCreationConflictException("Quotation has no invoiceable order items.");

        var customer = value.Customer;
        var telephone = First(customer.Mobile, customer.Telephone, customer.Fax);
        var billing = Address(customer, customer.BillingAddress, telephone: null);
        var shipping = Address(customer, customer.ShippingAddress, telephone);
        var withholding = q.WithholdingTax ?? 0m;
        return new(q.Id, customer.Id, $"{now:ddMMyy}-{customer.Id}-{q.Id}", value.Employee.FullName, value.Currency.ShortName,
            q.Comment, q.ShippedVia, q.Fob, q.Terms, billing, shipping, customer.Company?.TaxNumber, customer.Company?.Registrar,
            q.Subtotal, q.Vat, q.Total, withholding, q.Total - withholding, value.OrderItems);
    }

    private static InvoiceAddressInput Address(InvoiceCreationCustomer customer, InvoiceCreationAddress? address, string? telephone)
    {
        var missing = "(no address given / ไม่มีข้อมูลที่อยู่)";
        return new(customer.FullName, customer.Company?.Name, address?.Building, address?.Line1 ?? missing, address?.Line2,
            address?.City, address?.State, address?.PostalCode, address?.Country ?? "-", telephone);
    }

    private static Invoice MapInvoice(InvoiceCreationPreview p, CreateInvoiceFromQuotationRequest r, DateTime now) => new()
    {
        Number = r.InvoiceNumber.Trim(),
        CustomerId = p.CustomerId,
        Comment = r.Comment,
        SalesPerson = p.SalesPerson,
        Currency = p.Currency,
        PurchaseOrderNumber = r.PurchaseOrderNumber,
        Requisitioner = r.Requisitioner,
        ShippedVia = r.ShippedVia,
        Fob = r.Fob,
        Terms = r.Terms,
        BillingAddressRecipient = r.BillingAddress.Recipient,
        BillingAddressCompany = r.BillingAddress.Company,
        BillingAddressBuilding = r.BillingAddress.Building,
        BillingAddressLine1 = r.BillingAddress.Line1,
        BillingAddressLine2 = r.BillingAddress.Line2,
        BillingAddressCity = r.BillingAddress.City,
        BillingAddressState = r.BillingAddress.State,
        BillingAddressPostalCode = r.BillingAddress.PostalCode,
        BillingAddressCountry = r.BillingAddress.Country,
        ShippingAddressRecipient = r.ShippingAddress.Recipient,
        ShippingAddressRecipientTelephone = r.ShippingAddress.Telephone,
        ShippingAddressCompany = r.ShippingAddress.Company,
        ShippingAddressBuilding = r.ShippingAddress.Building,
        ShippingAddressLine1 = r.ShippingAddress.Line1,
        ShippingAddressLine2 = r.ShippingAddress.Line2,
        ShippingAddressCity = r.ShippingAddress.City,
        ShippingAddressState = r.ShippingAddress.State,
        ShippingAddressPostalCode = r.ShippingAddress.PostalCode,
        ShippingAddressCountry = r.ShippingAddress.Country,
        TaxIdentification = r.TaxIdentification,
        CommercialRegistration = r.CommercialRegistration,
        Subtotal = p.Subtotal,
        Vat = p.Vat,
        Total = p.Total,
        WithholdingTax = r.DeductWithholdingTax ? p.AvailableWithholdingTax : 0m,
        Outstanding = p.Total - (r.DeductWithholdingTax ? p.AvailableWithholdingTax : 0m),
        IsPaid = false,
        CreatedDate = now,
        ModifiedDate = now,
    };

    private static IReadOnlyList<InvoiceOrderItem> MapItems(IReadOnlyList<InvoiceCreationOrderItem> values, int? invoiceId, DateTime now) => values.Select(value => new InvoiceOrderItem
    {
        InvoiceId = invoiceId,
        Description = value.Description,
        Quantity = value.Quantity,
        UnitPrice = value.UnitPrice,
        Subtotal = value.Subtotal,
        CreatedDate = now,
        ModifiedDate = now,
    }).ToArray();

    private static string? First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static string SafeFilePart(string value) => string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
    private static void ValidateQuotation(int quotationId) { if (quotationId <= 0) throw new ArgumentOutOfRangeException(nameof(quotationId)); }
}
