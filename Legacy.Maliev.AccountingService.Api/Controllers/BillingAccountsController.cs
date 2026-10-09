using System.Data.Common;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;

namespace Legacy.Maliev.AccountingService.Api.Controllers.Billing;

/// <summary>Uncached staff reads and nonmutating financial previews; issuance is a separate workflow.</summary>
[ApiController, Authorize, Route("v1/customers/{customerId:int}/billing/accounts")]
public sealed class BillingAccountsController(IConfiguration configuration, IBillingLedger? ledger = null) : ControllerBase
{
    [HttpGet("{accountId:guid}"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true,
        ResourcePathTemplate = "/customers/{customerId}/billing/accounts/{accountId}")]
    public async Task<ActionResult<BillingAccountView>> ReadAsync(int customerId, Guid accountId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Available()) return Unavailable();
        if (customerId <= 0 || accountId == Guid.Empty) return BadRequest();
        try
        {
            var account = await ledger!.GetAsync(accountId, cancellationToken);
            return account is null || account.Snapshot.CustomerId != customerId ? NotFound() : Ok(account);
        }
        catch (DbException) { return Unavailable(); }
        catch (RetryLimitExceededException) { return Unavailable(); }
    }

    [HttpPost("{accountId:guid}/preview"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true,
        ResourcePathTemplate = "/customers/{customerId}/billing/accounts/{accountId}")]
    public async Task<ActionResult<BillingStagePreview>> PreviewAsync(int customerId, Guid accountId,
        BillingPreviewRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Available()) return Unavailable();
        if (customerId <= 0 || accountId == Guid.Empty || request.ExpectedRevision <= 0 || request.Recipient is null
            || string.IsNullOrWhiteSpace(request.Recipient.Recipient) || string.IsNullOrWhiteSpace(request.Recipient.Address)) return BadRequest();
        try
        {
            var account = await ledger!.GetAsync(accountId, cancellationToken);
            if (account is null || account.Snapshot.CustomerId != customerId) return NotFound();
            if (account.Revision != request.ExpectedRevision) return Conflict();
            var draft = new StageDraft(request.Kind, request.Amount, request.Percentage, request.DueDate, request.Recipient, []);
            var portions = BillingAmountCalculator.AllocateLines(account.Snapshot, draft, account);
            var amount = new BillingMoney(portions.Sum(line => line.Amount.Base), portions.Sum(line => line.Amount.Vat),
                portions.Sum(line => line.Amount.Gross), account.Snapshot.Cap.Currency);
            return Ok(new BillingStagePreview(account.Id, account.Revision, amount, portions));
        }
        catch (ArgumentException) { return BadRequest(); }
        catch (OverflowException) { return BadRequest(); }
        catch (DbException) { return Unavailable(); }
        catch (RetryLimitExceededException) { return Unavailable(); }
    }

    private bool Available() => ledger is not null && bool.TryParse(configuration["Billing:ReadEnabled"], out var enabled) && enabled
        && bool.TryParse(configuration["Features:ResourceScopedAuthEnabled"], out var scoped) && scoped;
    private ObjectResult Unavailable() => StatusCode(503, new ProblemDetails { Status = 503, Title = "Billing is unavailable." });
}

public sealed record BillingPreviewRequest(BillingStageKind Kind, decimal? Amount, decimal? Percentage, DateOnly? DueDate,
    BillingRecipient Recipient, long ExpectedRevision);
public sealed record BillingStagePreview(Guid AccountId, long Revision, BillingMoney Amount, IReadOnlyList<BillingLine> Portions);
