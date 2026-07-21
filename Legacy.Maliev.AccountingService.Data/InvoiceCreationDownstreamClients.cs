using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;

namespace Legacy.Maliev.AccountingService.Data;

public sealed class InvoiceCreationSourceClient(IHttpClientFactory clients) : IInvoiceCreationSource
{
    public const string QuotationClient = "InvoiceCreationQuotation";
    public const string CustomerClient = "InvoiceCreationCustomer";
    public const string EmployeeClient = "InvoiceCreationEmployee";
    public const string CatalogClient = "InvoiceCreationCatalog";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<InvoiceCreationSourceSnapshot> GetAsync(int quotationId, CancellationToken cancellationToken)
    {
        var quotations = clients.CreateClient(QuotationClient);
        var quotation = await GetAsync<QuotationResponse>(quotations, $"/quotations/{quotationId}", "quotation", cancellationToken);
        if (quotation.CustomerId is null || quotation.EmployeeId is null) throw new InvoiceCreationConflictException("Quotation has no customer or employee owner.");
        var itemsTask = GetAsync<IReadOnlyList<OrderItemResponse>>(quotations, $"/quotations/{quotationId}/orderitems", "quotation order items", cancellationToken);
        var customerTask = GetAsync<CustomerResponse>(clients.CreateClient(CustomerClient), $"/customers/{quotation.CustomerId}", "customer", cancellationToken);
        var employeeTask = GetAsync<EmployeeResponse>(clients.CreateClient(EmployeeClient), $"/employees/{quotation.EmployeeId}", "employee", cancellationToken);
        var currencyTask = GetAsync<CurrencyResponse>(clients.CreateClient(CatalogClient), $"/currencies/{quotation.CurrencyId}", "currency", cancellationToken);
        await Task.WhenAll(itemsTask, customerTask, employeeTask, currencyTask);
        var customer = await customerTask;
        var catalog = clients.CreateClient(CatalogClient);
        var billingCountryTask = CountryAsync(catalog, customer.BillingAddress?.CountryId, cancellationToken);
        var shippingCountryTask = customer.ShippingAddress?.CountryId == customer.BillingAddress?.CountryId
            ? billingCountryTask
            : CountryAsync(catalog, customer.ShippingAddress?.CountryId, cancellationToken);
        await Task.WhenAll(billingCountryTask, shippingCountryTask);
        return new(
            new(quotation.Id, quotation.CustomerId, quotation.EmployeeId, quotation.CurrencyId, quotation.Subtotal, quotation.Vat, quotation.Total, quotation.WithholdingTax, quotation.Comment, quotation.Fob, quotation.ShippedVia, quotation.Terms, quotation.InvoiceId),
            new(customer.Id, customer.FullName, customer.Email, customer.Mobile, customer.Telephone, customer.Fax,
                customer.Company is null ? null : new(customer.Company.Name, customer.Company.TaxNumber, customer.Company.Registrar),
                Address(customer.BillingAddress, await billingCountryTask), Address(customer.ShippingAddress, await shippingCountryTask)),
            new((await employeeTask).Id, (await employeeTask).FullName),
            new((await currencyTask).Id, (await currencyTask).ShortName, (await currencyTask).LongName),
            (await itemsTask).Select(value => new InvoiceCreationOrderItem(value.Id, value.QuotationId, value.OrderId, value.Description, value.Quantity, value.UnitPrice, value.Subtotal)).ToArray());
    }

    private static InvoiceCreationAddress? Address(AddressResponse? value, string? country) => value is null ? null : new(value.Building, value.AddressLine1, value.AddressLine2, value.City, value.State, value.PostalCode, country);
    private static async Task<string?> CountryAsync(HttpClient client, int? id, CancellationToken cancellationToken) => id is null ? null : (await GetAsync<CountryResponse>(client, $"/countries/{id}", "country", cancellationToken)).Name;
    private static async Task<T> GetAsync<T>(HttpClient client, string path, string name, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvoiceCreationNotFoundException($"Invoice {name} dependency was not found.");
        if (!response.IsSuccessStatusCode) throw new InvoiceCreationDependencyException($"{name} service rejected invoice creation with status {(int)response.StatusCode}.");
        var bytes = await ReceiptDocumentClient.ReadBoundedAsync(response.Content, 256 * 1024, name, cancellationToken);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvoiceCreationDependencyException($"{name} service returned invalid data.");
    }

    private sealed record QuotationResponse(int Id, int? CustomerId, int? EmployeeId, int? InvoiceId, int Period, DateTime ExpirationDate, decimal Subtotal, decimal Vat, decimal Total, decimal? WithholdingTax, decimal? QuotedAmount, int CurrencyId, string? Comment, string? Fob, string? ShippedVia, string? Terms, bool? Accepted, DateTime? CreatedDate, DateTime? ModifiedDate);
    private sealed record OrderItemResponse(int Id, int QuotationId, int? OrderId, string? Description, int? Quantity, decimal? UnitPrice, decimal? Subtotal);
    private sealed record CustomerResponse(int Id, string FullName, string? Telephone, string? Mobile, string? Fax, string Email, CompanyResponse? Company, AddressResponse? BillingAddress, AddressResponse? ShippingAddress);
    private sealed record CompanyResponse(string? Name, string? TaxNumber, string? Registrar);
    private sealed record AddressResponse(string? Building, string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode, int CountryId);
    private sealed record EmployeeResponse(int Id, string FullName);
    private sealed record CurrencyResponse(int Id, string ShortName, string LongName);
    private sealed record CountryResponse(int Id, string Name);
}

public sealed class InvoiceQuotationCompletionClient(HttpClient httpClient) : IInvoiceQuotationCompletionClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public async Task CompleteAsync(int quotationId, int invoiceId, Guid operationId, CancellationToken cancellationToken)
    {
        using var get = await httpClient.GetAsync($"/quotations/{quotationId}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!get.IsSuccessStatusCode) throw new InvoiceCreationDependencyException($"QuotationService rejected invoice completion lookup with status {(int)get.StatusCode}.");
        var bytes = await ReceiptDocumentClient.ReadBoundedAsync(get.Content, 64 * 1024, "QuotationService", cancellationToken);
        var q = JsonSerializer.Deserialize<QuotationResponse>(bytes, Json) ?? throw new InvoiceCreationDependencyException("QuotationService returned invalid completion data.");
        if (q.InvoiceId is not null && q.InvoiceId != invoiceId) throw new InvoiceCreationConflictException("Quotation is already linked to another invoice.");
        if (q.InvoiceId != invoiceId || q.Accepted != true)
        {
            using var update = new HttpRequestMessage(HttpMethod.Put, $"/quotations/{quotationId}")
            {
                Content = JsonContent.Create(new { q.CustomerId, q.EmployeeId, InvoiceId = invoiceId, q.Period, q.ExpirationDate, q.Subtotal, q.Vat, q.Total, q.WithholdingTax, q.CurrencyId, q.Comment, q.Fob, q.ShippedVia, q.Terms, Accepted = true }),
            };
            if (q.ModifiedDate is not null) update.Headers.TryAddWithoutValidation("X-Expected-Modified-Date", new DateTimeOffset(DateTime.SpecifyKind(q.ModifiedDate.Value, DateTimeKind.Utc)).ToString("O"));
            using var response = await httpClient.SendAsync(update, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict) throw new InvoiceCreationConflictException("Quotation changed while the invoice was being created.");
            if (!response.IsSuccessStatusCode) throw new InvoiceCreationDependencyException($"QuotationService rejected invoice linking with status {(int)response.StatusCode}.");
        }
        using var decision = new HttpRequestMessage(HttpMethod.Put, $"/quotations/{quotationId}/decision") { Content = JsonContent.Create(new { Accepted = true }) };
        decision.Headers.TryAddWithoutValidation("Idempotency-Key", operationId.ToString("D"));
        using var decided = await httpClient.SendAsync(decision, cancellationToken);
        if (decided.StatusCode == HttpStatusCode.Conflict) throw new InvoiceCreationConflictException("Linked orders could not be transitioned to accepted.");
        if (!decided.IsSuccessStatusCode) throw new InvoiceCreationDependencyException($"QuotationService rejected linked-order acceptance with status {(int)decided.StatusCode}.");
    }
    private sealed record QuotationResponse(int? CustomerId, int? EmployeeId, int? InvoiceId, int Period, DateTime ExpirationDate, decimal Subtotal, decimal Vat, decimal Total, decimal? WithholdingTax, int CurrencyId, string? Comment, string? Fob, string? ShippedVia, string? Terms, bool? Accepted, DateTime? ModifiedDate);
}

public sealed class InvoiceCreationDocumentClient(HttpClient httpClient) : IInvoiceCreationDocumentClient
{
    public async Task<byte[]> RenderAsync(Invoice invoice, IReadOnlyList<InvoiceOrderItem> items, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("/pdfs/invoice", new
        {
            invoice.BillingAddressBuilding,
            invoice.BillingAddressCity,
            invoice.BillingAddressCompany,
            invoice.BillingAddressCountry,
            invoice.BillingAddressLine1,
            invoice.BillingAddressLine2,
            invoice.BillingAddressPostalCode,
            invoice.BillingAddressRecipient,
            invoice.BillingAddressState,
            invoice.CommercialRegistration,
            invoice.CreatedDate,
            invoice.Currency,
            invoice.CustomerId,
            invoice.Fob,
            invoice.Number,
            OrderItems = items.Select(value => new { value.Description, Quantity = value.Quantity ?? 0, UnitPrice = value.UnitPrice ?? 0m, Subtotal = value.Subtotal ?? 0m }),
            invoice.Outstanding,
            invoice.PurchaseOrderNumber,
            Remark = invoice.Comment,
            invoice.Requisitioner,
            invoice.SalesPerson,
            invoice.ShippedVia,
            invoice.ShippingAddressBuilding,
            invoice.ShippingAddressCity,
            invoice.ShippingAddressCompany,
            invoice.ShippingAddressCountry,
            invoice.ShippingAddressLine1,
            invoice.ShippingAddressLine2,
            invoice.ShippingAddressPostalCode,
            invoice.ShippingAddressRecipient,
            invoice.ShippingAddressRecipientTelephone,
            invoice.ShippingAddressState,
            invoice.Subtotal,
            invoice.TaxIdentification,
            invoice.Terms,
            invoice.Total,
            invoice.Vat,
            invoice.WithholdingTax,
        }, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvoiceCreationDependencyException($"DocumentService rejected invoice rendering with status {(int)response.StatusCode}.");
        return await ReceiptDocumentClient.ReadBoundedAsync(response.Content, 10 * 1024 * 1024, "DocumentService", cancellationToken);
    }
}
