using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Configured sender provenance; never derives authority from a response body.</summary>
public sealed record InvoiceNotificationIntentOptions(bool Enabled, string SenderIssuer, string SenderServiceSubject);

/// <summary>Invoice-only V2 transport; mutation calls require acknowledged single-use local permits.</summary>
public sealed partial class InvoiceNotificationIntentClient(HttpClient client, InvoiceNotificationIntentOptions options,
    IInvoiceNotificationPhaseStore phases)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly HashSet<string> Required = new(StringComparer.Ordinal)
    {
        "intentId", "purpose", "resourceType", "resourceId", "workflowOperationId", "state", "version", "admittedAt", "updatedAt",
    };

    public async Task<InvoiceNotificationReceiptObservation> AdmitAsync(InvoiceNotificationAdmissionPermit permit,
        InvoiceNotificationCorrelationIdentity identity, string payloadDigest, CancellationToken cancellationToken)
    {
        ValidateSender(identity);
        if (permit.Correlation.Identity != identity || permit.Correlation.Phase != "AdmissionIssued" ||
            permit.PayloadDigest != payloadDigest || !permit.TryConsume()) throw new InvoiceNotificationCorrelationConflictException();
        using var request = new HttpRequestMessage(HttpMethod.Put, Path(identity))
        {
            Content = JsonContent.Create(new
            {
                purpose = identity.Purpose,
                resourceType = "invoice",
                resourceId = identity.InvoiceId.ToString(CultureInfo.InvariantCulture),
                workflowOperationId = identity.WorkflowOperationId.ToString("D"),
                channel = "Info",
                payloadDigestVersion = identity.PayloadFrameVersion,
                payloadDigest,
            }, options: Json),
        };
        return await SendAsync(request, identity, "admit", cancellationToken);
    }

    public async Task<InvoiceNotificationReceiptObservation> ExecuteAsync(InvoiceNotificationExecutionPermit permit,
        InvoiceNotificationCorrelationIdentity identity, InvoiceNotificationPayloadSnapshot payload, CancellationToken cancellationToken)
    {
        ValidateSender(identity);
        if (permit.Correlation.Identity != identity || permit.Correlation.Phase != "ExecutionIssued" ||
            permit.PayloadDigest != payload.Digest() || !permit.TryConsume()) throw new InvoiceNotificationCorrelationConflictException();
        using var request = new HttpRequestMessage(HttpMethod.Post, Path(identity) + "/execute")
        {
            Content = JsonContent.Create(new
            {
                channel = payload.Channel,
                to = payload.To,
                subject = payload.Subject,
                body = payload.Body,
                replyTo = payload.ReplyTo,
                cc = payload.Cc,
                bcc = payload.Bcc,
                attachments = payload.Attachments?.Select(value => new
                {
                    fileName = value.FileName,
                    contentType = value.ContentType,
                    content = value.Content,
                }).ToArray(),
            }, options: Json),
        };
        return await SendAsync(request, identity, "execute", cancellationToken);
    }

    public async Task<InvoiceNotificationReceiptObservation> ReadAsync(InvoiceNotificationCorrelationIdentity identity,
        CancellationToken cancellationToken)
    {
        ValidateSender(identity);
        using var request = new HttpRequestMessage(HttpMethod.Get, Path(identity));
        return await SendAsync(request, identity, "read", cancellationToken);
    }

    /// <summary>Retains a strict GET observation without creating or recovering mutation capability.</summary>
    public async Task<InvoiceNotificationCorrelation> ReconcileAsync(InvoiceNotificationCorrelation correlation,
        CancellationToken cancellationToken)
    {
        var receipt = await ReadAsync(correlation.Identity, cancellationToken);
        return await phases.ObserveAsync(correlation.Identity, correlation.Version, receipt, cancellationToken);
    }

    private void ValidateSender(InvoiceNotificationCorrelationIdentity identity)
    {
        if (!options.Enabled || options.SenderServiceSubject != "service:legacy-accounting" ||
            identity.SenderIssuer != options.SenderIssuer || identity.SenderServiceSubject != options.SenderServiceSubject)
            throw new InvoiceNotificationCorrelationUnavailableException();
    }

    private async Task<InvoiceNotificationReceiptObservation> SendAsync(HttpRequestMessage request,
        InvoiceNotificationCorrelationIdentity identity, string operation, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var bytes = await ReceiptDocumentClient.ReadBoundedAsync(response.Content, 64 * 1024, "NotificationService", cancellationToken);
            return ParseReceipt(bytes, identity, response.StatusCode, operation);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or ReceiptWorkflowDependencyException)
        {
            throw new InvoiceNotificationCorrelationUnavailableException();
        }
    }

    /// <summary>Strict camel-case producer receipt parser; successful parsing grants no send authority.</summary>
    public static InvoiceNotificationReceiptObservation ParseReceipt(byte[] bytes, InvoiceNotificationCorrelationIdentity identity,
        HttpStatusCode status, string operation)
    {
        try
        {
            if (bytes.Length > 64 * 1024) throw Invalid();
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!seen.Add(property.Name) || (!Required.Contains(property.Name) && property.Name != "providerMessageId")) throw Invalid();
            }

            if (!Required.IsSubsetOf(seen)) throw Invalid();
            static string Text(JsonElement root, string name) => root.GetProperty(name).GetString() ?? throw Invalid();
            var receipt = new InvoiceNotificationReceiptObservation(Text(root, "intentId"), Text(root, "purpose"),
                Text(root, "resourceType"), Text(root, "resourceId"), Text(root, "workflowOperationId"), Text(root, "state"),
                root.GetProperty("version").GetInt64(), Time(Text(root, "admittedAt")), Time(Text(root, "updatedAt")),
                seen.Contains("providerMessageId") ? Text(root, "providerMessageId") : null);
            var validStatus = operation switch
            {
                "admit" => status == HttpStatusCode.OK && receipt.State == "admitted",
                "execute" => receipt.State == "providerAccepted" ? status == HttpStatusCode.OK :
                    status == HttpStatusCode.Accepted && receipt.State is "submitting" or "outcomeUnknown",
                "read" => status == HttpStatusCode.OK,
                _ => false,
            };
            if (!validStatus) throw Invalid();
            _ = InvoiceNotificationReceiptBinding.Frame(identity, new byte[32], "validation", receipt);
            return receipt;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new InvoiceNotificationCorrelationUnavailableException();
        }
    }

    private static DateTimeOffset Time(string text)
    {
        if (!Offset().IsMatch(text) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time) || time.UtcTicks % 10 != 0) throw Invalid();
        return time.ToUniversalTime();
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex Offset();
    private static string Path(InvoiceNotificationCorrelationIdentity identity) => $"/notifications/v2/delivery-intents/{identity.IntentId:D}";
    private static ArgumentException Invalid() => new("Invalid invoice notification receipt.");
}
