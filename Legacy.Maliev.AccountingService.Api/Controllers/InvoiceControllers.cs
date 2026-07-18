using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.AccountingService.Api.Controllers.Invoice;

[ApiController, Route("invoices"), Authorize]
public sealed class InvoicesController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<Domain.Invoice.Invoice>(service, idempotency)
{
    [HttpPost, RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public Task<ActionResult<Domain.Invoice.Invoice>> CreateInvoiceAsync(Domain.Invoice.Invoice item, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("invoices", item, "GetInvoice", new { invoice = 0 }, key, cancellationToken);
    [HttpDelete("{id:int}"), RequirePermission(AccountingPermissions.Delete, RequireLiveCheck = true)]
    public Task<IActionResult> DeleteInvoiceAsync(int id, CancellationToken cancellationToken) => Delete(id, cancellationToken);
    [HttpGet("{invoice}", Name = "GetInvoice"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public async Task<ActionResult<Domain.Invoice.Invoice>> GetInvoiceAsync(string invoice, CancellationToken cancellationToken)
    {
        if (int.TryParse(invoice, out var id))
        {
            return await Get(id, cancellationToken);
        }

        var page = await Service.GetInvoicesAsync(null, null, invoice, null, 1, 2, cancellationToken);
        var match = page?.Items.SingleOrDefault(value => string.Equals(value.Number, invoice, StringComparison.OrdinalIgnoreCase));
        return match is null ? NotFound() : match;
    }
    [HttpGet, HttpGet("customers/{customerId:int}"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public async Task<ActionResult<PaginatedResponse<Domain.Invoice.Invoice>>> GetPaginatedAsync(int? customerId, InvoiceSortType? sort, string? search, int? index, int? size, bool? paid, CancellationToken cancellationToken)
    {
        var value = await Service.GetInvoicesAsync(customerId, sort, search, paid, index ?? 1, size ?? 20, cancellationToken);
        return value is null ? NotFound() : value;
    }
    [HttpPut("{id:int}"), RequirePermission(AccountingPermissions.Update, RequireLiveCheck = true)]
    public Task<IActionResult> UpdateInvoiceAsync(int id, Domain.Invoice.Invoice item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(id, item, expected, cancellationToken);
}

[ApiController, Route("invoices/orderitems"), Authorize]
public sealed class OrderItemsController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<InvoiceOrderItem>(service, idempotency)
{
    [HttpPost, RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public Task<ActionResult<InvoiceOrderItem>> CreateOrderItemAsync(InvoiceOrderItem item, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("invoice-items", item, "GetInvoiceOrderItem", new { orderItemId = 0 }, key, cancellationToken);
    [HttpDelete("{orderItemId:int}"), RequirePermission(AccountingPermissions.Delete, RequireLiveCheck = true)]
    public Task<IActionResult> DeleteOrderItemAsync(int orderItemId, CancellationToken cancellationToken) => Delete(orderItemId, cancellationToken);
    [HttpGet("{orderItemId:int}", Name = "GetInvoiceOrderItem"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<InvoiceOrderItem>> GetOrderItemAsync(int orderItemId, CancellationToken cancellationToken) => Get(orderItemId, cancellationToken);
    [HttpGet("/invoices/{invoiceId:int}/orderitems"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public async Task<ActionResult<IReadOnlyList<InvoiceOrderItem>>> GetOrderItemsAsync(int invoiceId, CancellationToken cancellationToken) => Ok(await Service.GetInvoiceItemsAsync(invoiceId, cancellationToken));
    [HttpPut("{orderItemId:int}"), RequirePermission(AccountingPermissions.Update, RequireLiveCheck = true)]
    public Task<IActionResult> UpdateOrderItemAsync(int orderItemId, InvoiceOrderItem item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(orderItemId, item, expected, cancellationToken);
}

[ApiController, Route("invoices/files"), Authorize]
public sealed class FilesController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<InvoiceFile>(service, idempotency)
{
    [HttpPost("/invoices/{invoiceId:int}/files"), RequirePermission(AccountingPermissions.FilesWrite, RequireLiveCheck = true)]
    public Task<ActionResult<InvoiceFile>> CreateInvoiceFileEntryAsync(int invoiceId, string bucket, string objectName, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("invoice-files", new InvoiceFile { InvoiceId = invoiceId, Bucket = bucket, ObjectName = objectName }, "GetInvoiceFile", new { invoiceFileId = 0 }, key, cancellationToken);
    [HttpDelete("{invoiceFileId:int}"), RequirePermission(AccountingPermissions.FilesDelete, RequireLiveCheck = true)]
    public Task<IActionResult> DeleteInvoiceFileAsync(int invoiceFileId, CancellationToken cancellationToken) => Delete(invoiceFileId, cancellationToken);
    [HttpGet("{invoiceFileId:int}", Name = "GetInvoiceFile"), RequirePermission(AccountingPermissions.FilesRead, RequireLiveCheck = true)]
    public Task<ActionResult<InvoiceFile>> GetInvoiceFileAsync(int invoiceFileId, CancellationToken cancellationToken) => Get(invoiceFileId, cancellationToken);
    [HttpGet("/invoices/{invoiceId:int}/files"), RequirePermission(AccountingPermissions.FilesRead, RequireLiveCheck = true)]
    public async Task<ActionResult<IReadOnlyList<InvoiceFile>>> GetInvoiceFilesAsync(int invoiceId, CancellationToken cancellationToken) => Ok(await Service.GetInvoiceFilesAsync(invoiceId, cancellationToken));
    [HttpPut("{invoiceFileId:int}"), RequirePermission(AccountingPermissions.FilesWrite, RequireLiveCheck = true)]
    public Task<IActionResult> UpdateInvoiceFileAsync(int invoiceFileId, InvoiceFile item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(invoiceFileId, item, expected, cancellationToken);
}
