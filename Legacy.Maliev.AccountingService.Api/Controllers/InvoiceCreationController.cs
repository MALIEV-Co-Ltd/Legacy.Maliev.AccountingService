using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.AccountingService.Api.Controllers.Invoice;

/// <summary>Server-owned quotation-to-invoice preview and creation workflow.</summary>
[ApiController, Route("invoices/from-quotation"), Authorize]
public sealed class InvoiceCreationController(IInvoiceCreationWorkflow workflow, InvoiceCreationDelegationVerifier verifier,
    InvoiceCreationAdmissionStore admissions, InvoiceNotificationIntentOptions? notificationOptions = null) : ControllerBase
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
        if (!Request.Headers.TryGetValue(InvoiceCreationDelegationVerifier.HeaderName, out var delegation))
            return await ExecuteAsync(() => workflow.CreateAsync(quotationId, request, operationId, cancellationToken));
        if (delegation.Count != 1 || operationKey != operationId.ToString("D"))
            return Unauthorized(Problem(title: "Invalid invoice-create delegation.", statusCode: StatusCodes.Status401Unauthorized));
        var origin = verifier.VerifyOrigin(delegation[0], User, quotationId, operationId);
        if (origin is null)
            return Unauthorized(Problem(title: "Invalid invoice-create delegation.", statusCode: StatusCodes.Status401Unauthorized));
        return await ExecuteAsync(async () =>
        {
            var fingerprint = InvoiceCreationAdmissionStore.Fingerprint(request);
            if (notificationOptions?.Enabled != true)
            {
                var legacy = await admissions.AdmitAsync(operationId, quotationId, origin.EmployeeSubject,
                    origin.ServiceSubject, fingerprint, cancellationToken);
                if (!legacy.IsNew) return legacy.Completed!;
            }
            else
            {
                var admission = await admissions.AdmitAsync(operationId, quotationId, origin, fingerprint, cancellationToken);
                if (admission.Completed is not null)
                    return admission.Completed.EmailState == InvoiceCreationEmailState.ProviderAccepted
                        ? await workflow.ReplayCompletedAsync(quotationId, operationId, origin, admission.Completed, cancellationToken)
                        : admission.Completed;
                if (admission.NeedsReconciliation) return await workflow.ReconcileAsync(quotationId, operationId, origin, cancellationToken);
            }
            try
            {
                var result = notificationOptions?.Enabled == true
                    ? await workflow.CreateAsync(quotationId, request, operationId, origin, cancellationToken)
                    : await workflow.CreateAsync(quotationId, request, operationId, cancellationToken);
                if (notificationOptions?.Enabled == true && request.SendEmail && result.EmailState == InvoiceCreationEmailState.ExplicitRetryRequired)
                    await admissions.MarkUncertainAsync(operationId, cancellationToken);
                else await admissions.CompleteAsync(operationId, result, cancellationToken);
                return result;
            }
            catch
            {
                try { await admissions.MarkUncertainAsync(operationId, CancellationToken.None); }
                catch { /* The pending admission itself remains fail-closed. */ }
                throw;
            }
        });
    }

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> execute)
    {
        try { return Ok(await execute()); }
        catch (ArgumentException exception) { return BadRequest(Problem(title: exception.Message, statusCode: StatusCodes.Status400BadRequest)); }
        catch (InvoiceCreationNotFoundException exception) { return NotFound(Problem(title: exception.Message, statusCode: StatusCodes.Status404NotFound)); }
        catch (InvoiceCreationConflictException exception) { return Conflict(Problem(title: exception.Message, statusCode: StatusCodes.Status409Conflict)); }
        catch (InvoiceCreationUnavailableException exception) { return StatusCode(StatusCodes.Status503ServiceUnavailable, Problem(title: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable)); }
        catch (InvoiceCreationDependencyException exception) { return StatusCode(StatusCodes.Status502BadGateway, Problem(title: exception.Message, statusCode: StatusCodes.Status502BadGateway)); }
        catch (InvoiceNotificationCorrelationConflictException) { return Conflict(Problem(title: "Invoice notification requires reconciliation.", statusCode: StatusCodes.Status409Conflict)); }
        catch (InvoiceNotificationCorrelationUnavailableException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, Problem(title: "Invoice notification is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable)); }
        catch (HttpRequestException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, Problem(title: "Invoice dependency is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable)); }
        catch (TaskCanceledException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, Problem(title: "Invoice dependency timed out.", statusCode: StatusCodes.Status503ServiceUnavailable)); }
    }
}
