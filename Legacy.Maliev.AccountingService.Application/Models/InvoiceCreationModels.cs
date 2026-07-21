namespace Legacy.Maliev.AccountingService.Application.Models;

/// <summary>User-editable invoice fields. Financial, customer, currency, employee, and line-item authority is excluded.</summary>
public sealed record CreateInvoiceFromQuotationRequest(
    string InvoiceNumber,
    string? Comment,
    string? PurchaseOrderNumber,
    string? Requisitioner,
    string? ShippedVia,
    string? Fob,
    string? Terms,
    InvoiceAddressInput BillingAddress,
    InvoiceAddressInput ShippingAddress,
    string? TaxIdentification,
    string? CommercialRegistration,
    bool DeductWithholdingTax,
    bool SendEmail);

public sealed record InvoiceAddressInput(
    string? Recipient,
    string? Company,
    string? Building,
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    string? Telephone = null);

public sealed record InvoiceCreationPreview(
    int QuotationId,
    int CustomerId,
    string InvoiceNumber,
    string SalesPerson,
    string Currency,
    string? Comment,
    string? ShippedVia,
    string? Fob,
    string? Terms,
    InvoiceAddressInput BillingAddress,
    InvoiceAddressInput ShippingAddress,
    string? TaxIdentification,
    string? CommercialRegistration,
    decimal Subtotal,
    decimal Vat,
    decimal Total,
    decimal AvailableWithholdingTax,
    decimal Outstanding,
    IReadOnlyList<InvoiceCreationOrderItem> OrderItems);

public sealed record InvoiceCreationOrderItem(int Id, int QuotationId, int? OrderId, string? Description, int? Quantity, decimal? UnitPrice, decimal? Subtotal);
public sealed record InvoiceCreationQuotation(int Id, int? CustomerId, int? EmployeeId, int CurrencyId, decimal Subtotal, decimal Vat, decimal Total, decimal? WithholdingTax, string? Comment, string? Fob, string? ShippedVia, string? Terms, int? InvoiceId);
public sealed record InvoiceCreationCustomer(int Id, string FullName, string Email, string? Mobile, string? Telephone, string? Fax, InvoiceCreationCompany? Company, InvoiceCreationAddress? BillingAddress, InvoiceCreationAddress? ShippingAddress);
public sealed record InvoiceCreationCompany(string? Name, string? TaxNumber, string? Registrar);
public sealed record InvoiceCreationAddress(string? Building, string? Line1, string? Line2, string? City, string? State, string? PostalCode, string? Country);
public sealed record InvoiceCreationEmployee(int Id, string FullName);
public sealed record InvoiceCreationCurrency(int Id, string ShortName, string LongName);
public sealed record InvoiceCreationSourceSnapshot(InvoiceCreationQuotation Quotation, InvoiceCreationCustomer Customer, InvoiceCreationEmployee Employee, InvoiceCreationCurrency Currency, IReadOnlyList<InvoiceCreationOrderItem> OrderItems);
public sealed record InvoiceCreationStoredFile(string Bucket, string ObjectName);

public enum InvoiceCreationState { Completed, Reconciled }
public enum InvoiceCreationEmailState { NotRequested, Delivered, ExplicitRetryRequired }
public sealed record InvoiceCreationResult(int InvoiceId, InvoiceCreationState State, InvoiceCreationEmailState EmailState, string? ProviderMessageId, InvoiceCreationStoredFile StoredFile);

public sealed class InvoiceCreationNotFoundException(string message) : Exception(message);
public sealed class InvoiceCreationConflictException(string message) : Exception(message);
public sealed class InvoiceCreationDependencyException(string message, Exception? innerException = null) : Exception(message, innerException);
public sealed class InvoiceCreationUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);
