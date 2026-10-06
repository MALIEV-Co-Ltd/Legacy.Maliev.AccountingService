using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Uses the exact owning operation receipt before/recovering one employee decision request.</summary>
public sealed class InvoiceEmployeeQuotationCompletionClient(HttpClient http, TimeProvider? clock = null) : IInvoiceEmployeeQuotationCompletionClient, IDisposable
{
    public const string CapabilityHeader = "X-Maliev-Quotation-Invoice-Capability";
    public static readonly TimeSpan InvocationDeadline = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions PascalCase = new() { PropertyNamingPolicy = null };
    private static readonly HashSet<string> Fields = ["ContractVersion", "OperationId", "QuotationId", "InvoiceId", "OriginIssuer",
        "EmployeeSubject", "RequesterSubject", "ExecutorSubject", "OriginalQuotationVersion", "FinancialBinding", "FinancialBindingVersion",
        "State", "DecisionOrderVersion", "CompletedOrders", "TotalOrders", "ModifiedDate"];

    public async Task<InvoiceQuotationOperationReceipt> CompleteAsync(InvoiceFinancialOwnership ownership, string freshCapability,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(freshCapability) || freshCapability.Length > 16384 || freshCapability.Any(char.IsWhiteSpace)) throw Unavailable();
        var parts = freshCapability.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty)) throw Unavailable();
        using var deadline = new CancellationTokenSource(InvocationDeadline, clock ?? TimeProvider.System);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        cancellationToken = bounded.Token;
        var existing = await ReadAsync(ownership, freshCapability, cancellationToken);
        if (existing?.State == "Completed") return existing;
        using var decision = Request(HttpMethod.Put, $"/quotations/{ownership.QuotationId}/decision", ownership, freshCapability);
        decision.Content = JsonContent.Create(new { Accepted = true, EmployeeInitiated = true, InvoiceId = ownership.InvoiceId }, options: PascalCase);
        try
        {
            // At most one PUT per explicit invocation; no transport retry or generic quote GET.
            using var response = await http.SendAsync(decision, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw Unavailable();
        }
        catch (HttpRequestException) { /* Owning readback alone may resolve a lost acknowledgment. */ }
        catch (IOException) { /* Transport acknowledgment loss is not permission to repeat the PUT. */ }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { /* HttpClient timeout does not authorize a repeated decision. */ }
        var result = await ReadAsync(ownership, freshCapability, cancellationToken) ?? throw Unavailable();
        if (existing is not null && (result.DecisionOrderVersion != existing.DecisionOrderVersion ||
            result.TotalOrders != existing.TotalOrders || result.CompletedOrders < existing.CompletedOrders)) throw Unavailable();
        return result;
    }

    private async Task<InvoiceQuotationOperationReceipt?> ReadAsync(InvoiceFinancialOwnership ownership, string capability,
        CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get,
            $"/quotations/{ownership.QuotationId}/invoice-completion/operations/{ownership.OperationId:D}?invoiceId={ownership.InvoiceId}", ownership, capability);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.Conflict) throw new InvoiceCreationConflictException("Quotation operation ownership conflicted.");
        if (!response.IsSuccessStatusCode) throw Unavailable();
        byte[] bytes;
        try { bytes = await ReceiptDocumentClient.ReadBoundedAsync(response.Content, 16384, "QuotationService", cancellationToken); }
        catch (ReceiptWorkflowDependencyException) { throw Unavailable(); }
        catch (IOException) { throw Unavailable(); }
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw Unavailable();
            var properties = json.RootElement.EnumerateObject().ToArray();
            if (properties.Length != Fields.Count || properties.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != Fields.Count ||
                properties.Any(value => !Fields.Contains(value.Name))) throw Unavailable();
            string Text(string name) => json.RootElement.GetProperty(name).ValueKind == JsonValueKind.String
                ? json.RootElement.GetProperty(name).GetString()! : throw Unavailable();
            int Number(string name)
            {
                var value = json.RootElement.GetProperty(name);
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) ||
                    value.GetRawText() != number.ToString(CultureInfo.InvariantCulture)) throw Unavailable();
                return number;
            }
            var result = new InvoiceQuotationOperationReceipt(Number("ContractVersion"), Text("OperationId"), Number("QuotationId"), Number("InvoiceId"),
                Text("OriginIssuer"), Text("EmployeeSubject"), Text("RequesterSubject"), Text("ExecutorSubject"), Text("OriginalQuotationVersion"),
                Text("FinancialBinding"), Text("FinancialBindingVersion"), Text("State"), Text("DecisionOrderVersion"), Number("CompletedOrders"),
                Number("TotalOrders"), Text("ModifiedDate"));
            if (result.ContractVersion != 1 || result.OperationId != ownership.OperationId.ToString("D") ||
                result.QuotationId != ownership.QuotationId || result.InvoiceId != ownership.InvoiceId || result.OriginIssuer != ownership.OriginIssuer ||
                result.EmployeeSubject != ownership.EmployeeSubject || result.RequesterSubject != ownership.RequesterSubject ||
                result.ExecutorSubject != "service:legacy-accounting" || result.OriginalQuotationVersion != ownership.OriginalQuotationVersion ||
                result.FinancialBinding != ownership.FinancialBinding || result.FinancialBindingVersion != "invoice-creation-financial-v1" ||
                result.State is not ("QuotationCommitted" or "OrdersPartial" or "OrdersConflict" or "Completed") ||
                result.CompletedOrders < 0 || result.TotalOrders < 0 || result.CompletedOrders > result.TotalOrders ||
                result.State == "Completed" && result.CompletedOrders != result.TotalOrders ||
                !CanonicalUtc(result.OriginalQuotationVersion) || !CanonicalUtc(result.DecisionOrderVersion) || !CanonicalUtc(result.ModifiedDate)) throw Unavailable();
            return result;
        }
        catch (JsonException) { throw Unavailable(); }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, InvoiceFinancialOwnership ownership, string capability)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(CapabilityHeader, "Bearer " + capability);
        request.Headers.TryAddWithoutValidation("X-Expected-Modified-Date", ownership.OriginalQuotationVersion);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", ownership.OperationId.ToString("D"));
        return request;
    }

    private static bool CanonicalUtc(string value) => DateTime.TryParseExact(value, "O", CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind, out var parsed) && parsed.Kind == DateTimeKind.Utc && parsed.Ticks > 0 &&
        parsed.ToString("O", CultureInfo.InvariantCulture) == value;
    private static InvoiceCreationUnavailableException Unavailable() => new("Quotation operation completion requires verified reconciliation.");
    public void Dispose() => http.Dispose();
}
