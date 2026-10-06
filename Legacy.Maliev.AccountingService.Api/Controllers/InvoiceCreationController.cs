using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Polly.Timeout;

namespace Legacy.Maliev.AccountingService.Api.Controllers.Invoice;

/// <summary>Server-owned quotation-to-invoice preview and creation workflow.</summary>
[ApiController, Route("invoices/from-quotation"), Authorize]
public sealed class InvoiceCreationController(IInvoiceCreationWorkflow workflow, InvoiceCreationDelegationVerifier verifier,
    InvoiceCreationAdmissionStore admissions, InvoiceNotificationIntentOptions? notificationOptions = null,
    InvoiceCompletionCapabilityVerifier? completionVerifier = null, IInvoiceFinancialOwnershipReader? financialOwnership = null) : ControllerBase
{
    /// <summary>Explicit same-operation resume requires a fresh invoice-bound proof and the original admitted intent.</summary>
    [HttpPost("{quotationId:int}/complete"), RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public async Task<ActionResult<InvoiceCreationResult>> CompleteEmployeeAsync(int quotationId, CreateInvoiceFromQuotationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? operationKey, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(operationKey, "D", out var operationId) || operationId == Guid.Empty || operationKey != operationId.ToString("D")) return BadRequest();
        if (!Request.Headers.TryGetValue(InvoiceCompletionCapabilityVerifier.HeaderName, out var capability) || capability.Count != 1) return Unauthorized();
        if (!InvoiceCompletionCapabilityVerifier.IsRequester(User)) return Unauthorized();
        if (completionVerifier is null || financialOwnership is null)
            return StatusCode(503, new ProblemDetails { Title = "Employee invoice completion is unavailable.", Status = 503 });
        return await ExecuteEmployeeAsync(async () =>
        {
            var financial = await financialOwnership.ReadCommittedAsync(operationId, cancellationToken);
            if (financial.Ownership.QuotationId != quotationId) throw new InvoiceCreationConflictException("Retained quotation ownership differs.");
            if (!completionVerifier.Verify(capability[0], User, financial.Ownership)) throw new EmployeeAuthorityException();
            var origin = new InvoiceNotificationOrigin(financial.Ownership.OriginIssuer, financial.Ownership.EmployeeSubject, financial.Ownership.RequesterSubject);
            await admissions.ValidateEmployeeIntentAsync(operationId, quotationId, origin, request, cancellationToken);
            return await workflow.CompleteEmployeeAsync(quotationId, operationId, origin, request, capability[0]!, cancellationToken);
        });
    }

    /// <summary>Prepares verified financial ownership independently of notification activation.</summary>
    [HttpPost("{quotationId:int}/prepare"), RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public async Task<ActionResult<InvoiceFinancialOwnership>> PrepareAsync(int quotationId, CreateInvoiceFromQuotationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? operationKey, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(operationKey, "D", out var operationId) || operationId == Guid.Empty || operationKey != operationId.ToString("D")) return BadRequest();
        if (!Request.Headers.TryGetValue(InvoiceCreationDelegationVerifier.HeaderName, out var delegation) || delegation.Count != 1) return Unauthorized();
        var origin = verifier.VerifyOrigin(delegation[0], User, quotationId, operationId);
        if (origin is null) return Unauthorized();
        return await ExecuteEmployeeAsync(async () =>
        {
            var admission = await admissions.AdmitAsync(operationId, quotationId, origin, InvoiceCreationAdmissionStore.Fingerprint(request), cancellationToken);
            if (!admission.IsNew)
                return await workflow.ReadPreparedFinancialAsync(quotationId, operationId, origin, cancellationToken);
            try { return await workflow.PrepareFinancialAsync(quotationId, request, operationId, origin, cancellationToken); }
            catch
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await admissions.MarkUncertainAsync(operationId, cleanup.Token); }
                catch { /* An unacknowledged operation remains fail-closed and cannot recreate finances. */ }
                throw;
            }
        });
    }

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
            if (notificationOptions?.Enabled != true && !await admissions.RequiresOriginValidationAsync(operationId, cancellationToken))
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

    private async Task<ActionResult<T>> ExecuteEmployeeAsync<T>(Func<Task<T>> execute)
    {
        try { return Ok(await execute()); }
        catch (EmployeeAuthorityException) { return Unauthorized(); }
        catch (ArgumentException) { return Error(400, "Employee invoice request is invalid."); }
        catch (InvoiceCreationNotFoundException) { return Error(404, "Employee invoice source is unavailable."); }
        catch (InvoiceCreationConflictException) { return Error(409, "Employee invoice operation requires reconciliation."); }
        catch (InvoiceCreationDependencyException) { return Error(502, "Employee invoice dependency is unavailable."); }
        catch (InvoiceNotificationCorrelationConflictException) { return Error(409, "Invoice notification requires reconciliation."); }
        catch (InvoiceCreationUnavailableException) { return Unavailable(); }
        catch (InvoiceNotificationCorrelationUnavailableException) { return Unavailable(); }
        catch (DbException) { return Unavailable(); }
        catch (DbUpdateException) { return Unavailable(); }
        catch (RetryLimitExceededException) { return Unavailable(); }
        catch (JsonException) { return Unavailable(); }
        catch (TimeoutRejectedException) { return Unavailable(); }
        catch (ReceiptWorkflowDependencyException) { return Unavailable(); }
        catch (ReceiptWorkflowNotFoundException) { return Unavailable(); }
        catch (HttpRequestException) { return Unavailable(); }
        catch (IOException) { return Unavailable(); }
        catch (TaskCanceledException) { return Unavailable(); }
        ActionResult<T> Unavailable() => Error(503, "Employee invoice completion requires reconciliation.");
        ActionResult<T> Error(int status, string title) => StatusCode(status, new ProblemDetails { Title = title, Status = status });
    }
    private sealed class EmployeeAuthorityException : Exception { }
}
