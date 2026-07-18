using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.AccountingService.Api.Controllers.Invoice;

/// <summary>Server-owned receipt workflow for one invoice.</summary>
[ApiController]
[Route("invoices/{invoiceId:int}/receipt")]
[Authorize]
public sealed class InvoiceReceiptWorkflowController(IReceiptWorkflow workflow) : ControllerBase
{
    /// <summary>Creates or reconciles a receipt, its PDF, storage link, and optional email.</summary>
    [HttpPost]
    [RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    [ProducesResponseType<ReceiptWorkflowResult>(StatusCodes.Status200OK)]
    public Task<ActionResult<ReceiptWorkflowResult>> CreateAsync(
        int invoiceId,
        [FromBody] ReceiptWorkflowApiRequest request,
        [FromHeader(Name = "X-Legacy-Employee-Id")] int employeeId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) => ExecuteAsync(
            idempotencyKey,
            operationId => workflow.CreateAsync(
                invoiceId,
                new CreateReceiptRequest(
                    request.Comment,
                    request.SendEmail,
                    employeeId),
                operationId,
                cancellationToken),
            cancellationToken,
            employeeId);

    /// <summary>Removes receipt storage and accounting state idempotently.</summary>
    [HttpDelete]
    [RequirePermission(AccountingPermissions.Delete, RequireLiveCheck = true)]
    [ProducesResponseType<ReceiptWorkflowResult>(StatusCodes.Status200OK)]
    public Task<ActionResult<ReceiptWorkflowResult>> RemoveAsync(
        int invoiceId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) => ExecuteAsync(
            idempotencyKey,
            operationId => workflow.RemoveAsync(invoiceId, operationId, cancellationToken),
            cancellationToken,
            null);

    /// <summary>Explicitly resends an existing receipt with an operator-approved operation UUID.</summary>
    [HttpPost("email")]
    [RequirePermission(AccountingPermissions.Update, RequireLiveCheck = true)]
    [ProducesResponseType<ReceiptWorkflowResult>(StatusCodes.Status200OK)]
    public Task<ActionResult<ReceiptWorkflowResult>> SendEmailAsync(
        int invoiceId,
        [FromHeader(Name = "X-Legacy-Employee-Id")] int employeeId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) => ExecuteAsync(
            idempotencyKey,
            operationId => workflow.SendEmailAsync(
                invoiceId,
                new SendReceiptEmailRequest(employeeId),
                operationId,
                cancellationToken),
            cancellationToken,
            employeeId);

    private async Task<ActionResult<ReceiptWorkflowResult>> ExecuteAsync(
        string? idempotencyKey,
        Func<Guid, Task<ReceiptWorkflowResult>> execute,
        CancellationToken cancellationToken,
        int? employeeId)
    {
        if (employeeId is <= 0)
        {
            return this.BadRequest(Problem(
                StatusCodes.Status400BadRequest,
                "Trusted employee identity required",
                "X-Legacy-Employee-Id must identify the authenticated legacy employee."));
        }

        if (!Guid.TryParseExact(idempotencyKey, "D", out var operationId) || operationId == Guid.Empty)
        {
            return this.BadRequest(Problem(
                StatusCodes.Status400BadRequest,
                "Stable operation identity required",
                "Idempotency-Key must be a non-empty UUID in canonical D format."));
        }

        try
        {
            return this.Ok(await execute(operationId));
        }
        catch (ReceiptWorkflowNotFoundException exception)
        {
            return this.NotFound(Problem(StatusCodes.Status404NotFound, "Receipt workflow state not found", exception.Message));
        }
        catch (ReceiptWorkflowUnavailableException exception)
        {
            return this.StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                Problem(StatusCodes.Status503ServiceUnavailable, "Receipt workflow unavailable", exception.Message));
        }
        catch (HttpRequestException)
        {
            return this.StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                Problem(StatusCodes.Status503ServiceUnavailable, "Receipt workflow unavailable", "A required service is unavailable."));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return this.StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                Problem(StatusCodes.Status503ServiceUnavailable, "Receipt workflow unavailable", "A required service timed out."));
        }
        catch (ReceiptWorkflowDependencyException exception)
        {
            return this.StatusCode(
                StatusCodes.Status502BadGateway,
                Problem(StatusCodes.Status502BadGateway, "Receipt dependency failed", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return this.BadRequest(Problem(StatusCodes.Status400BadRequest, "Invalid receipt request", exception.Message));
        }
    }

    private static ProblemDetails Problem(int status, string title, string detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
    };
}

/// <summary>API input for receipt creation and optional delivery.</summary>
public sealed record ReceiptWorkflowApiRequest(
    [param: StringLength(1000)] string? Comment,
    bool SendEmail);
