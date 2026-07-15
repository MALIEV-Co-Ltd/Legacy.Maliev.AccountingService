using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.AccountingService.Api.Controllers.Receipt;

[ApiController, Route("receipts"), Authorize]
public sealed class ReceiptsController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<Domain.Receipt.Receipt>(service, idempotency)
{
    [HttpPost, RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public Task<ActionResult<Domain.Receipt.Receipt>> CreateReceiptAsync(Domain.Receipt.Receipt item, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("receipts", item, "GetReceipt", new { receiptId = 0 }, key, cancellationToken);
    [HttpDelete("{id:int}"), RequirePermission(AccountingPermissions.Delete, RequireLiveCheck = true)]
    public Task<IActionResult> DeleteReceiptAsync(int id, CancellationToken cancellationToken) => Delete(id, cancellationToken);
    [HttpGet, RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public async Task<ActionResult<PaginatedResponse<Domain.Receipt.Receipt>>> GetPaginatedAsync(string? sort, string? search, int? index, int? size, CancellationToken cancellationToken)
    {
        _ = sort;
        var value = await Service.GetReceiptsAsync(search, index ?? 1, size ?? 20, cancellationToken);
        return value is null ? NotFound() : value;
    }
    [HttpGet("{receiptId:int}", Name = "GetReceipt"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<Domain.Receipt.Receipt>> GetReceiptAsync(int receiptId, CancellationToken cancellationToken) => Get(receiptId, cancellationToken);
    [HttpPut("{id:int}"), RequirePermission(AccountingPermissions.Update, RequireLiveCheck = true)]
    public Task<IActionResult> UpdateReceiptAsync(int id, Domain.Receipt.Receipt item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(id, item, expected, cancellationToken);
}

[ApiController, Route("receipts/orderitems"), Authorize]
public sealed class OrderItemsController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<ReceiptOrderItem>(service, idempotency)
{
    [HttpPost, RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public Task<ActionResult<ReceiptOrderItem>> CreateOrderItemAsync(ReceiptOrderItem item, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("receipt-items", item, "GetReceiptOrderItem", new { orderItemId = 0 }, key, cancellationToken);
    [HttpDelete("{orderItemId:int}"), RequirePermission(AccountingPermissions.Delete, RequireLiveCheck = true)]
    public Task<IActionResult> DeleteOrderItemAsync(int orderItemId, CancellationToken cancellationToken) => Delete(orderItemId, cancellationToken);
    [HttpGet("{orderItemId:int}", Name = "GetReceiptOrderItem"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<ReceiptOrderItem>> GetOrderItemAsync(int orderItemId, CancellationToken cancellationToken) => Get(orderItemId, cancellationToken);
    [HttpGet("/receipts/{receiptId:int}/orderitems"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public async Task<ActionResult<IReadOnlyList<ReceiptOrderItem>>> GetOrderItemsAsync(int receiptId, CancellationToken cancellationToken) => Ok(await Service.GetReceiptItemsAsync(receiptId, cancellationToken));
    [HttpPut("{orderItemId:int}"), RequirePermission(AccountingPermissions.Update, RequireLiveCheck = true)]
    public Task<IActionResult> UpdateOrderItemAsync(int orderItemId, ReceiptOrderItem item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(orderItemId, item, expected, cancellationToken);
}

[ApiController, Route("receipts/files"), Authorize]
public sealed class FilesController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<ReceiptFile>(service, idempotency)
{
    [HttpPost("/receipts/{receiptId:int}/files"), RequirePermission(AccountingPermissions.FilesWrite, RequireLiveCheck = true)]
    public Task<ActionResult<ReceiptFile>> CreateReceiptFileEntryAsync(int receiptId, string bucket, string objectName, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("receipt-files", new ReceiptFile { ReceiptId = receiptId, Bucket = bucket, ObjectName = objectName }, "GetReceiptFile", new { receiptFileId = 0 }, key, cancellationToken);
    [HttpDelete("{receiptFileId:int}"), RequirePermission(AccountingPermissions.FilesDelete, RequireLiveCheck = true)]
    public Task<IActionResult> DeleteReceiptFileAsync(int receiptFileId, CancellationToken cancellationToken) => Delete(receiptFileId, cancellationToken);
    [HttpGet("{receiptFileId:int}", Name = "GetReceiptFile"), RequirePermission(AccountingPermissions.FilesRead, RequireLiveCheck = true)]
    public Task<ActionResult<ReceiptFile>> GetReceiptFileAsync(int receiptFileId, CancellationToken cancellationToken) => Get(receiptFileId, cancellationToken);
    [HttpGet("/receipts/{receiptId:int}/files"), RequirePermission(AccountingPermissions.FilesRead, RequireLiveCheck = true)]
    public async Task<ActionResult<IReadOnlyList<ReceiptFile>>> GetReceiptFilesAsync(int receiptId, CancellationToken cancellationToken) => Ok(await Service.GetReceiptFilesAsync(receiptId, cancellationToken));
    [HttpPut("{receiptFileId:int}"), RequirePermission(AccountingPermissions.FilesWrite, RequireLiveCheck = true)]
    public Task<IActionResult> UpdateReceiptFileAsync(int receiptFileId, ReceiptFile item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(receiptFileId, item, expected, cancellationToken);
}
