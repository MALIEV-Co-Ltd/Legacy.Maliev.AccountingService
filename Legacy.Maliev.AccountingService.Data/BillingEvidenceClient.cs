using System.Net;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Data;

public sealed class BillingEvidenceClient(HttpClient client) : IBillingEvidenceReader
{
    public Task<IReadOnlyList<EvidenceReceipt>> VerifyAsync(int customerId, int quotationId, IReadOnlyList<int> allowedOrderIds,
        IReadOnlyList<EvidenceReference> references, CancellationToken cancellationToken)
    {
        return VerifyCoreAsync(customerId, quotationId, allowedOrderIds.ToArray(), references.ToArray(), cancellationToken);
    }

    private async Task<IReadOnlyList<EvidenceReceipt>> VerifyCoreAsync(int customerId, int quotationId, int[] allowedOrderIds,
        EvidenceReference[] references, CancellationToken cancellationToken)
    {
        if (customerId <= 0 || quotationId <= 0 || allowedOrderIds.Any(id => id <= 0) ||
            allowedOrderIds.Distinct().Count() != allowedOrderIds.Length || references.Length > 32 ||
            references.Any(r => r.DocumentId == Guid.Empty || r.VersionId == Guid.Empty) ||
            references.Distinct().Count() != references.Length)
            throw new BillingDependencyException("Invalid billing evidence references.");

        var receipts = new List<EvidenceReceipt>();
        try
        {
            foreach (var reference in references)
            {
                using var response = await client.GetAsync(
                    $"customers/{customerId}/documents/{reference.DocumentId:D}/versions/{reference.VersionId:D}/receipt",
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new BillingDependencyException("Verified document evidence is unavailable.");
                if (response.Content.Headers.ContentLength > 65536)
                    throw new BillingDependencyException("Document evidence response exceeds its size limit.");
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream();
                var chunk = new byte[4096];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellationToken)) != 0)
                {
                    if (buffer.Length + read > 65536)
                        throw new BillingDependencyException("Document evidence response exceeds its size limit.");
                    buffer.Write(chunk, 0, read);
                }
                var receipt = JsonSerializer.Deserialize<EvidenceReceipt>(buffer.ToArray());
                if (receipt is null || receipt.DocumentId != reference.DocumentId || receipt.VersionId != reference.VersionId ||
                    receipt.CustomerId != customerId || receipt.Kind is not ("Shipment" or "Release" or "Acceptance" or "BillingInstruction") ||
                    receipt.VerificationStatus != "Verified" || string.IsNullOrWhiteSpace(receipt.VerifiedBySubject) ||
                    receipt.VerifiedAtUtc is null || receipt.VerifiedAtUtc.Value.Offset != TimeSpan.Zero || receipt.Revision <= 0 ||
                    receipt.ContentSha256 is null || receipt.ContentSha256.Length != 64 ||
                    receipt.ContentSha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
                    receipt.OrderIds is null || receipt.OrderIds.Any(id => id <= 0 || !allowedOrderIds.Contains(id)) ||
                    receipt.OrderIds.Distinct().Count() != receipt.OrderIds.Count ||
                    (receipt.QuotationId.HasValue ? receipt.QuotationId != quotationId : receipt.OrderIds.Count == 0))
                    throw new BillingDependencyException("Document evidence is unverified or unrelated to this billing account.");
                receipts.Add(receipt with { OrderIds = Array.AsReadOnly(receipt.OrderIds.ToArray()) });
            }
            return receipts.AsReadOnly();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new BillingDependencyException("Verified document evidence could not be read.");
        }
    }
}
