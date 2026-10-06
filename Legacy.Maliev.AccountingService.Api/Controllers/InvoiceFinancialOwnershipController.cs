using System.Security.Claims;
using System.Data.Common;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;

namespace Legacy.Maliev.AccountingService.Api.Controllers.Invoice;

/// <summary>Fixed uncached Auth/Quotation readback; permissions never substitute for the requesting workload.</summary>
[ApiController, Authorize, Route("internal/invoice-creation/operations")]
public sealed class InvoiceFinancialOwnershipController(InvoiceFinancialOwnershipStore ownership) : ControllerBase
{
    /// <summary>Proposed granular grant; its runtime configuration is independently owned, not installed by this route.</summary>
    public const string ReadPermission = "legacy.accounting.invoice-financial-ownership.read";

    [HttpGet("{operationId:guid}/financial-ownership"), RequirePermission(ReadPermission, RequireLiveCheck = true)]
    public async Task<ActionResult<InvoiceFinancialOwnership>> ReadAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var subjects = User.FindAll("sub").Select(value => value.Value).ToArray();
        var kinds = User.FindAll("identity_kind").Select(value => value.Value).ToArray();
        if (subjects is not ["service:legacy-auth"] and not ["service:legacy-quotation"] || kinds is not ["service"] ||
            !User.HasClaim("permissions", ReadPermission) || User.FindAll("permissions").Any(value => value.Value.Contains('*')) ||
            User.HasClaim(value => value.Type is "executor" or "sid" or ClaimTypes.Role)) return Forbid();
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await ownership.ReadAsync(operationId, cancellationToken)); }
        catch (InvoiceCreationConflictException)
        { return Conflict(new ProblemDetails { Title = "Committed financial ownership is unavailable or requires reconciliation.", Status = 409 }); }
        catch (DbException)
        { return StatusCode(503, new ProblemDetails { Title = "Financial ownership readback is unavailable.", Status = 503 }); }
        catch (RetryLimitExceededException)
        { return StatusCode(503, new ProblemDetails { Title = "Financial ownership readback is unavailable.", Status = 503 }); }
    }
}
