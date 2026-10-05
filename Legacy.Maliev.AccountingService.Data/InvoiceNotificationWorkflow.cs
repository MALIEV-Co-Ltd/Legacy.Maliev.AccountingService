using System.Net;
using System.Net.Mime;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Coordinates one durable invoice-purpose intent; ambiguous outcomes permit GET reconciliation only.</summary>
public sealed class InvoiceNotificationWorkflow(InvoiceDbContext database, InvoiceCreationAdmissionStore admissions,
    IInvoiceCreationJournal journal, InvoiceNotificationIntentOptions options, IInvoiceNotificationCorrelationStore correlations,
    IInvoiceNotificationPhaseStore phases, InvoiceNotificationIntentClient client) : IInvoiceNotificationWorkflow
{
    public bool Enabled => options.Enabled;

    public Task ValidateOriginAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        bool requirePending, CancellationToken cancellationToken)
    {
        RequireEnabled();
        return admissions.ValidateOriginAsync(operationId, quotationId, origin, requirePending, cancellationToken);
    }

    public Task<bool> HasFenceAsync(int invoiceId, CancellationToken cancellationToken) =>
        database.InvoiceNotificationCorrelations.AsNoTracking().AnyAsync(value => value.InvoiceId == invoiceId && value.Purpose == "invoice-issued", cancellationToken);

    public async Task ValidateReplayAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        InvoiceCreationResult result, CancellationToken cancellationToken)
    {
        await ValidateOriginAsync(quotationId, operationId, origin, false, cancellationToken);
        var financial = await admissions.ReadFinancialResultAsync(operationId, quotationId, origin, cancellationToken);
        if (result.InvoiceId != financial.InvoiceId || result.StoredFile != financial.StoredFile || result.State != financial.State ||
            result.EmailState != InvoiceCreationEmailState.ProviderAccepted || string.IsNullOrWhiteSpace(result.ProviderMessageId))
            throw new InvoiceCreationConflictException("Retained invoice notification result requires reconciliation.");
        var retained = await FindAsync(financial.InvoiceId, quotationId, operationId, origin, cancellationToken);
        if (retained is null || retained.Phase != "ProviderAccepted") throw new InvoiceCreationConflictException("Retained invoice notification result requires reconciliation.");
        await phases.ValidateAcceptedResultAsync(retained.Identity, result.ProviderMessageId, cancellationToken);
    }

    public Task PrepareFinancialAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        InvoiceCreationResult result, CancellationToken cancellationToken)
    {
        RequireEnabled();
        return admissions.SaveFinancialResultAsync(operationId, quotationId, origin, result, cancellationToken);
    }

    public async Task<InvoiceNotificationDeliveryResult> SendAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        string email, string customerName, Invoice invoice, byte[] pdf, CancellationToken cancellationToken)
    {
        await ValidateOriginAsync(quotationId, operationId, origin, true, cancellationToken);
        var financial = await admissions.ReadFinancialResultAsync(operationId, quotationId, origin, cancellationToken);
        if (financial.InvoiceId != invoice.Id) throw new InvoiceCreationConflictException("Invoice notification financial evidence differs.");
        var retained = await FindAsync(invoice.Id, quotationId, operationId, origin, cancellationToken);
        if (retained is not null) return await ReadOnlyAsync(retained, cancellationToken);

        var payload = new InvoiceNotificationPayloadSnapshot(email, $"Invoice for your quotation [Invoice no. {invoice.Number}]",
            Body(customerName), bcc: ["mail-tracking@maliev.com"],
            attachments: [new($"invoice_{invoice.Number}.pdf", MediaTypeNames.Application.Pdf, pdf)]);
        var digest = payload.Digest();
        Guid intentId;
        do { intentId = Guid.NewGuid(); } while (intentId == operationId);
        var identity = new InvoiceNotificationCorrelationIdentity(intentId, invoice.Id, "invoice-issued", quotationId, operationId,
            origin, options.SenderIssuer, options.SenderServiceSubject, InvoiceNotificationPayloadSnapshot.PayloadVersion,
            "accounting-invoice-notification-hmac-v1");
        try
        {
            retained = await correlations.AdmitAsync(identity, digest, cancellationToken);
            var admission = await phases.IssueAdmissionAsync(identity, digest, retained.Version, cancellationToken);
            var admitted = await client.AdmitAsync(admission, identity, digest, cancellationToken);
            retained = await phases.RetainReceiptAsync(identity, digest, admission.Correlation.Version, admitted, cancellationToken);
            var execution = await phases.IssueExecutionAsync(identity, digest, retained.Version, cancellationToken);
            var executed = await client.ExecuteAsync(execution, identity, payload, cancellationToken);
            retained = await phases.RetainReceiptAsync(identity, digest, execution.Correlation.Version, executed, cancellationToken);
            return Result(retained, executed);
        }
        catch (Exception exception) when (exception is InvoiceNotificationCorrelationUnavailableException or InvoiceNotificationCorrelationConflictException)
        {
            // Neither local commit ambiguity nor a lost RPC response can authorize reissue.
            retained = await FindAsync(invoice.Id, quotationId, operationId, origin, cancellationToken);
            return retained is null ? new(false, null) : await ReadOnlyAsync(retained, cancellationToken);
        }
    }

    public async Task<InvoiceCreationResult> ReconcileAsync(int quotationId, Guid operationId, InvoiceNotificationOrigin origin,
        CancellationToken cancellationToken)
    {
        await ValidateOriginAsync(quotationId, operationId, origin, false, cancellationToken);
        var financial = await admissions.ReadFinancialResultAsync(operationId, quotationId, origin, cancellationToken);
        var retained = await FindAsync(financial.InvoiceId, quotationId, operationId, origin, cancellationToken);
        var delivery = retained is null ? new InvoiceNotificationDeliveryResult(false, null) : await ReadOnlyAsync(retained, cancellationToken);
        var result = financial with
        {
            EmailState = delivery.ProviderAccepted ? InvoiceCreationEmailState.ProviderAccepted : InvoiceCreationEmailState.ExplicitRetryRequired,
            ProviderMessageId = delivery.ProviderMessageId,
        };
        if (delivery.ProviderAccepted)
        {
            await journal.SetAsync($"create:{quotationId}", operationId, result, cancellationToken);
            await admissions.CompleteReconciledAsync(operationId, quotationId, origin, result, cancellationToken);
        }
        return result;
    }

    private async Task<InvoiceNotificationDeliveryResult> ReadOnlyAsync(InvoiceNotificationCorrelation retained,
        CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await client.ReadAsync(retained.Identity, cancellationToken);
            var observed = await phases.ObserveAsync(retained.Identity, retained.Version, receipt, cancellationToken);
            return Result(observed, receipt);
        }
        catch (Exception exception) when (exception is InvoiceNotificationCorrelationUnavailableException or InvoiceNotificationCorrelationConflictException)
        {
            return new(false, null);
        }
    }

    private Task<InvoiceNotificationCorrelation?> FindAsync(int invoiceId, int quotationId, Guid operationId,
        InvoiceNotificationOrigin origin, CancellationToken cancellationToken) => phases.FindAsync(invoiceId, "invoice-issued",
        quotationId, operationId, origin, options.SenderIssuer, options.SenderServiceSubject, cancellationToken);

    private static InvoiceNotificationDeliveryResult Result(InvoiceNotificationCorrelation retained, InvoiceNotificationReceiptObservation receipt) =>
        retained.Phase == "ProviderAccepted" && receipt.State == "providerAccepted" ? new(true, receipt.ProviderMessageId) : new(false, null);

    private void RequireEnabled()
    {
        if (!Enabled) throw new InvoiceCreationUnavailableException("Invoice notification reconciliation is unavailable.");
    }

    private static string Body(string name) => $"<div>Hello {WebUtility.HtmlEncode(name)},</div><div>&nbsp;</div>" +
        "<div>Thank you for accepting our quoted amount.</div>" +
        "<div>You'll find the payable invoice for your orders attached with this email.</div><div>&nbsp;</div>" +
        "<div>The production of your orders will start as soon as the payable amount is received.</div>" +
        "<div>&nbsp;</div><div>Best regards,</div><div>Maliev Co., Ltd.</div>";
}
