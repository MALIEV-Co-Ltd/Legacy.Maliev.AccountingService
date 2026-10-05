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
    TimeProvider timeProvider,
    IInvoiceNotificationWorkflow? invoiceNotifications = null) : IInvoiceCreationWorkflow
{
    private const string Bucket = "maliev.com";

    public async Task<InvoiceCreationPreview> PreviewAsync(int quotationId, CancellationToken cancellationToken)
    {
        ValidateQuotation(quotationId);
        return Preview(await source.GetAsync(quotationId, cancellationToken), timeProvider.GetUtcNow());
    }

    public Task<InvoiceCreationResult> CreateAsync(int quotationId, CreateInvoiceFromQuotationRequest request, Guid operationId, CancellationToken cancellationToken) =>
        CreateCoreAsync(quotationId, request, operationId, null, cancellationToken);

    public Task<InvoiceCreationResult> CreateAsync(int quotationId, CreateInvoiceFromQuotationRequest request, Guid operationId,
        InvoiceNotificationOrigin origin, CancellationToken cancellationToken) => CreateCoreAsync(quotationId, request, operationId, origin, cancellationToken);

    public Task<InvoiceCreationResult> ReconcileAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        CancellationToken cancellationToken) => invoiceNotifications?.ReconcileAsync(quotationId, operationId, origin, cancellationToken)
        ?? throw new InvoiceCreationUnavailableException("Invoice notification reconciliation is unavailable.");

    public async Task<InvoiceCreationResult> ReplayCompletedAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        InvoiceCreationResult result, CancellationToken cancellationToken)
    {
        if (invoiceNotifications is null) throw new InvoiceCreationUnavailableException("Invoice notification reconciliation is unavailable.");
        await invoiceNotifications.ValidateReplayAsync(quotationId, operationId, origin, result, cancellationToken);
        return result;
    }

    private async Task<InvoiceCreationResult> CreateCoreAsync(int quotationId, CreateInvoiceFromQuotationRequest request, Guid operationId,
        InvoiceNotificationOrigin? origin, CancellationToken cancellationToken)
    {
        ValidateQuotation(quotationId);
        ArgumentNullException.ThrowIfNull(request);
        if (operationId == Guid.Empty) throw new ArgumentException("A stable operation UUID is required.", nameof(operationId));
        if (string.IsNullOrWhiteSpace(request.InvoiceNumber)) throw new ArgumentException("Invoice number is required.", nameof(request));

        var useV2 = request.SendEmail && invoiceNotifications is { Enabled: true };
        if (useV2)
        {
            if (origin is null) throw new InvoiceCreationConflictException("Verified originating admission is required for invoice notification.");
            await invoiceNotifications!.ValidateOriginAsync(quotationId, operationId, origin, false, cancellationToken);
        }

        var scope = $"create:{quotationId}";
        var replay = await journal.GetAsync(scope, operationId, cancellationToken);
        if (replay is not null) return await ReplayAsync(replay);

        await using var lease = await operationLock.AcquireAsync(quotationId, cancellationToken);
        replay = await journal.GetAsync(scope, operationId, cancellationToken);
        if (replay is not null) return await ReplayAsync(replay);

        if (useV2) await invoiceNotifications!.ValidateOriginAsync(quotationId, operationId, origin!, true, cancellationToken);

        var snapshot = await source.GetAsync(quotationId, cancellationToken);
        if (snapshot.Quotation.ModifiedDate is null || snapshot.Quotation.ModifiedDate.Value.Kind == DateTimeKind.Local)
            throw new InvoiceCreationConflictException("Quotation has no valid authoritative version. Reconcile the quotation before creating an invoice.");
        var preview = Preview(snapshot, timeProvider.GetUtcNow());
        var invoiceNumber = request.InvoiceNumber.Trim();
        var existing = await store.FindByNumberAsync(invoiceNumber, cancellationToken);
        if (existing is not null && invoiceNotifications is not null && await invoiceNotifications.HasFenceAsync(existing.Id, cancellationToken))
        {
            if (!useV2 || origin is null) throw new InvoiceCreationConflictException("Retained invoice notification requires authenticated reconciliation.");
            return await invoiceNotifications.ReconcileAsync(quotationId, operationId, origin, cancellationToken);
        }
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
            items = MapItems(snapshot.OrderItems, existing.Id, Now());
        }
        else
        {
            invoice = MapInvoice(preview, request, Now());
            items = MapItems(snapshot.OrderItems, null, invoice.CreatedDate!.Value);
            invoice = await store.CreateAsync(invoice, items, cancellationToken);
            foreach (var item in items) item.InvoiceId = invoice.Id;
        }

        await quotations.CompleteAsync(quotationId, invoice.Id, operationId, snapshot.Quotation.ModifiedDate, cancellationToken);
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
                if (useV2)
                {
                    var financial = new InvoiceCreationResult(invoice.Id, InvoiceCreationState.Completed, InvoiceCreationEmailState.NotRequested, null, stored);
                    await invoiceNotifications!.PrepareFinancialAsync(quotationId, operationId, origin!, financial, cancellationToken);
                    var notification = await invoiceNotifications.SendAsync(quotationId, operationId, origin!, snapshot.Customer.Email,
                        snapshot.Customer.FullName, invoice, pdf, cancellationToken);
                    messageId = notification.ProviderMessageId;
                    emailState = notification.ProviderAccepted ? InvoiceCreationEmailState.ProviderAccepted : InvoiceCreationEmailState.ExplicitRetryRequired;
                }
                else
                {
                    messageId = await notifications.SendAsync(snapshot.Customer.Email, snapshot.Customer.FullName, invoice, pdf, operationId, cancellationToken);
                    emailState = InvoiceCreationEmailState.Delivered;
                }
            }
        }

        var result = new InvoiceCreationResult(invoice.Id, reconciled ? InvoiceCreationState.Reconciled : InvoiceCreationState.Completed, emailState, messageId, stored);
        if (!useV2 || emailState != InvoiceCreationEmailState.ExplicitRetryRequired)
            await journal.SetAsync(scope, operationId, result, cancellationToken);
        return result;

        async Task<InvoiceCreationResult> ReplayAsync(InvoiceCreationResult completed)
        {
            if (invoiceNotifications is not null && await invoiceNotifications.HasFenceAsync(completed.InvoiceId, cancellationToken))
            {
                if (!useV2 || origin is null) throw new InvoiceCreationConflictException("Retained invoice notification requires verified originating authority.");
                await invoiceNotifications.ValidateReplayAsync(quotationId, operationId, origin, completed, cancellationToken);
            }
            return completed;
        }
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
            q.Subtotal, q.Vat, q.Total, withholding, q.Total - withholding, value.OrderItems,
            q.SourceRequestId, q.SourceJourneyId);
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
        SourceRequestId = p.SourceRequestId,
        SourceJourneyId = p.SourceJourneyId,
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

    // CreatedDate/ModifiedDate are "timestamp without time zone" wall-clock columns storing the
    // UTC instant with Kind stripped; Npgsql rejects Kind=Utc values for that column type.
    private DateTime Now() => DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);

    private static string? First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static string SafeFilePart(string value) => string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
    private static void ValidateQuotation(int quotationId) { if (quotationId <= 0) throw new ArgumentOutOfRangeException(nameof(quotationId)); }
}
