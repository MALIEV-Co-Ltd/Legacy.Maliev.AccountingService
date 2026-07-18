using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.AccountingService.Api.Controllers.Invoice;

/// <summary>Server-owned quotation-to-invoice preview and creation workflow.</summary>
[ApiController, Route("invoices/from-quotation"), Authorize]
public sealed class InvoiceCreationController(IInvoiceCreationWorkflow workflow) : ControllerBase
{
    [HttpGet("{quotationId:int}/preview"), RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public async Task<ActionResult<InvoiceCreationPreview>> PreviewAsync(int quotationId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => workflow.PreviewAsync(quotationId, cancellationToken));

    [HttpPost("{quotationId:int}"), RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public async Task<ActionResult<InvoiceCreationResult>> CreateAsync(
        int quotationId,
        CreateInvoiceFromQuotationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? operationKey,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(operationKey, out var operationId) || operationId == Guid.Empty)
            return BadRequest(Problem(title: "A stable UUID Idempotency-Key is required.", statusCode: StatusCodes.Status400BadRequest));
        return await ExecuteAsync(() => workflow.CreateAsync(quotationId, request, operationId, cancellationToken));
    }

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> execute)
    {
        try { return Ok(await execute()); }
        catch (ArgumentException exception) { return BadRequest(Problem(title: exception.Message, statusCode: StatusCodes.Status400BadRequest)); }
        catch (InvoiceCreationNotFoundException exception) { return NotFound(Problem(title: exception.Message, statusCode: StatusCodes.Status404NotFound)); }
        catch (InvoiceCreationConflictException exception) { return Conflict(Problem(title: exception.Message, statusCode: StatusCodes.Status409Conflict)); }
        catch (InvoiceCreationUnavailableException exception) { return StatusCode(StatusCodes.Status503ServiceUnavailable, Problem(title: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable)); }
        catch (InvoiceCreationDependencyException exception) { return StatusCode(StatusCodes.Status502BadGateway, Problem(title: exception.Message, statusCode: StatusCodes.Status502BadGateway)); }
        catch (HttpRequestException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, Problem(title: "Invoice dependency is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable)); }
        catch (TaskCanceledException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, Problem(title: "Invoice dependency timed out.", statusCode: StatusCodes.Status503ServiceUnavailable)); }
    }
}
