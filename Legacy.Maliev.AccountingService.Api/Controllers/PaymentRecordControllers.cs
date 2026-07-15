using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentRecord = Legacy.Maliev.AccountingService.Domain.Payment.Payment;

namespace Legacy.Maliev.AccountingService.Api.Controllers.Payment;

[ApiController, Route("payments"), Authorize]
public sealed class PaymentsController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<PaymentRecord>(service, idempotency)
{
    [HttpPost, RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public Task<ActionResult<PaymentRecord>> CreatePaymentAsync(PaymentRecord item, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("payments", item, "GetPayment", new { paymentId = 0 }, key, cancellationToken);
    [HttpDelete("{paymentId:int}"), RequirePermission(AccountingPermissions.Delete, RequireLiveCheck = true)]
    public Task<IActionResult> DeletePaymentAsync(int paymentId, CancellationToken cancellationToken) => Delete(paymentId, cancellationToken);
    [HttpGet, RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public async Task<ActionResult<PaginatedResponse<PaymentRecord>>> GetPaginatedPaymentAsync(string? sort, string? search, int? index, int? size, CancellationToken cancellationToken)
    {
        _ = sort;
        var value = await Service.GetPaymentsAsync(search, index ?? 1, size ?? 20, cancellationToken);
        return value is null ? NotFound() : value;
    }
    [HttpGet("{paymentId:int}", Name = "GetPayment"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<PaymentRecord>> GetPaymentAsync(int paymentId, CancellationToken cancellationToken) => Get(paymentId, cancellationToken);
    [HttpPut("{paymentId:int}"), RequirePermission(AccountingPermissions.Update, RequireLiveCheck = true)]
    public Task<IActionResult> UpdatePaymentAsync(int paymentId, PaymentRecord item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(paymentId, item, expected, cancellationToken);
}

[ApiController, Route("payments/files"), Authorize]
public sealed class FilesController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<PaymentFile>(service, idempotency)
{
    [HttpPost, RequirePermission(AccountingPermissions.FilesWrite, RequireLiveCheck = true)]
    public Task<ActionResult<PaymentFile>> CreatePaymentFileAsync(PaymentFile item, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("payment-files", item, "GetPaymentFile", new { paymentFileId = 0 }, key, cancellationToken);
    [HttpDelete("{paymentFileId:int}"), RequirePermission(AccountingPermissions.FilesDelete, RequireLiveCheck = true)]
    public Task<IActionResult> DeletePaymentFileAsync(int paymentFileId, CancellationToken cancellationToken) => Delete(paymentFileId, cancellationToken);
    [HttpGet("{paymentFileId:int}", Name = "GetPaymentFile"), RequirePermission(AccountingPermissions.FilesRead, RequireLiveCheck = true)]
    public Task<ActionResult<PaymentFile>> GetPaymentFileAsync(int paymentFileId, CancellationToken cancellationToken) => Get(paymentFileId, cancellationToken);
    [HttpGet("/payments/{paymentId:int}/files"), RequirePermission(AccountingPermissions.FilesRead, RequireLiveCheck = true)]
    public async Task<ActionResult<IReadOnlyList<PaymentFile>>> GetPaymentFilesAsync(int paymentId, CancellationToken cancellationToken) => Ok(await Service.GetPaymentFilesAsync(paymentId, cancellationToken));
    [HttpPut("{paymentFileId:int}"), RequirePermission(AccountingPermissions.FilesWrite, RequireLiveCheck = true)]
    public Task<IActionResult> UpdatePaymentFileAsync(int paymentFileId, PaymentFile item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(paymentFileId, item, expected, cancellationToken);
}

[ApiController, Route("payments/summaries"), Authorize]
public sealed class SummariesController(IAccountingService service) : ControllerBase
{
    [HttpGet("monthly"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<FinancialSummary>> GetMonthlySummaryAsync(CancellationToken cancellationToken) => Summary("month", false, cancellationToken);
    [HttpGet("weekly"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<FinancialSummary>> GetWeeklySummaryAsync(CancellationToken cancellationToken) => Summary("week", false, cancellationToken);
    [HttpGet("monthly/income/job"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<FinancialSummary>> GetMonthlyJobIncomeSummaryAsync(CancellationToken cancellationToken) => Summary("month", true, cancellationToken);
    [HttpGet("yearly/income"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public async Task<ActionResult<IReadOnlyDictionary<DateTime, decimal>>> GetYearlyIncomeDetailAsync(int? year, int? currencyId, CancellationToken cancellationToken)
    {
        var value = await service.GetYearlyDetailAsync(true, year, currencyId, cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }
    [HttpGet("yearly/expense"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public async Task<ActionResult<IReadOnlyDictionary<DateTime, decimal>>> GetYearlyExpenseDetailAsync(int? currencyId, CancellationToken cancellationToken)
    {
        var value = await service.GetYearlyDetailAsync(false, null, currencyId, cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }
    [HttpGet("yearly"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<FinancialSummary>> GetYearlySummaryAsync(CancellationToken cancellationToken) => Summary("year", false, cancellationToken);

    private async Task<ActionResult<FinancialSummary>> Summary(string period, bool jobIncomeOnly, CancellationToken cancellationToken)
    {
        var value = await service.GetFinancialSummaryAsync(period, jobIncomeOnly, cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }
}
