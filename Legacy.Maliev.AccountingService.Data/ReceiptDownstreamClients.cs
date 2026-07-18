using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Receipt;

namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Authenticated client for QuestPDF receipt rendering.</summary>
public sealed class ReceiptDocumentClient(HttpClient httpClient) : IReceiptDocumentClient
{
    private const int MaximumPdfBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
    };

    /// <inheritdoc />
    public async Task<byte[]> RenderAsync(
        Receipt receipt,
        IReadOnlyList<ReceiptOrderItem> items,
        byte[]? signature,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/Pdfs/receipt",
            new
            {
                receipt.AmountPaid,
                receipt.BillingAddressBuilding,
                receipt.BillingAddressCity,
                receipt.BillingAddressCompany,
                receipt.BillingAddressCountry,
                receipt.BillingAddressLine1,
                receipt.BillingAddressLine2,
                receipt.BillingAddressPostalCode,
                receipt.BillingAddressRecipient,
                receipt.BillingAddressState,
                receipt.CommercialRegistration,
                receipt.CreatedDate,
                receipt.Currency,
                receipt.CustomerId,
                receipt.Id,
                receipt.InvoiceNumber,
                receipt.ModifiedDate,
                OrderItems = items.Select(item => new
                {
                    item.Description,
                    Quantity = item.Quantity ?? 0,
                    Subtotal = item.Subtotal ?? 0m,
                    UnitPrice = item.UnitPrice ?? 0m,
                }),
                receipt.PaymentDate,
                Remark = receipt.Comment,
                receipt.Subtotal,
                receipt.TaxIdentification,
                receipt.Total,
                receipt.Vat,
                receipt.WithholdingTax,
                Signature = signature,
            },
            DocumentJson,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw Dependency("DocumentService", response.StatusCode);
        }

        return await ReadBoundedAsync(response.Content, MaximumPdfBytes, "DocumentService", cancellationToken);
    }

    private static ReceiptWorkflowDependencyException Dependency(string service, HttpStatusCode status) =>
        new($"{service} rejected the receipt operation with status {(int)status}.");

    internal static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        string service,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maximumBytes)
        {
            throw new ReceiptWorkflowDependencyException($"{service} returned an oversized response.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return result.ToArray();
            }

            if (result.Length + read > maximumBytes)
            {
                throw new ReceiptWorkflowDependencyException($"{service} returned an oversized response.");
            }

            await result.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}

/// <summary>Authenticated client for malware-scanned receipt storage.</summary>
public sealed class ReceiptFileClient(HttpClient httpClient, IHttpClientFactory httpClientFactory) : IReceiptFileClient
{
    public const string ObjectDownloadClientName = "ReceiptObjectDownload";
    private const int MaximumMetadataBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string bucket, string objectName, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"/uploads/SignedUrl?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => true,
            HttpStatusCode.NotFound => false,
            _ => throw Dependency(response.StatusCode),
        };
    }

    /// <inheritdoc />
    public async Task<ReceiptStoredFile> UploadAsync(
        string bucket,
        string path,
        string fileName,
        byte[] content,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(content);
        file.Headers.ContentType = new(MediaTypeNames.Application.Pdf);
        form.Add(file, "files", fileName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/Uploads?bucket={Uri.EscapeDataString(bucket)}&path={Uri.EscapeDataString(path)}")
        {
            Content = form,
        };
        request.Headers.Add("Idempotency-Key", operationId.ToString("D"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw Dependency(response.StatusCode);
        }

        var bytes = await ReceiptDocumentClient.ReadBoundedAsync(
            response.Content,
            MaximumMetadataBytes,
            "FileService",
            cancellationToken);
        var result = JsonSerializer.Deserialize<UploadResult>(bytes, Json);
        var stored = result?.Object?.SingleOrDefault()
            ?? throw new ReceiptWorkflowDependencyException("FileService returned an invalid upload response.");
        return new ReceiptStoredFile(stored.Bucket, stored.ObjectName);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string bucket, string objectName, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/Uploads?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.BadRequest &&
            !await ExistsAsync(bucket, objectName, cancellationToken))
        {
            return;
        }

        throw Dependency(response.StatusCode);
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadAsync(
        string bucket,
        string objectName,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        using var signedUrlResponse = await httpClient.GetAsync(
            $"/uploads/SignedUrl?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (signedUrlResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ReceiptWorkflowNotFoundException("Signature object was not found.");
        }

        if (!signedUrlResponse.IsSuccessStatusCode)
        {
            throw Dependency(signedUrlResponse.StatusCode);
        }

        var uriBytes = await ReceiptDocumentClient.ReadBoundedAsync(
            signedUrlResponse.Content,
            MaximumMetadataBytes,
            "FileService",
            cancellationToken);
        var signedUri = JsonSerializer.Deserialize<Uri>(uriBytes, Json);
        if (signedUri is null ||
            !string.Equals(signedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !(string.Equals(signedUri.Host, "storage.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
              signedUri.Host.EndsWith(".storage.googleapis.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ReceiptWorkflowDependencyException("FileService returned an invalid signed object URL.");
        }

        using var response = await httpClientFactory.CreateClient(ObjectDownloadClientName).GetAsync(
            signedUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ReceiptWorkflowDependencyException(
                $"Object storage rejected the signature download with status {(int)response.StatusCode}.");
        }

        return await ReceiptDocumentClient.ReadBoundedAsync(
            response.Content,
            maximumBytes,
            "Object storage",
            cancellationToken);
    }

    private static ReceiptWorkflowDependencyException Dependency(HttpStatusCode status) =>
        new($"FileService rejected the receipt operation with status {(int)status}.");

    private sealed record UploadResult(IReadOnlyList<UploadObject>? Object);
    private sealed record UploadObject(string Bucket, string ObjectName);
}

/// <summary>Authenticated client for invoice-owned customer delivery data.</summary>
public sealed class ReceiptCustomerClient(HttpClient httpClient) : IReceiptCustomerClient
{
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public async Task<ReceiptCustomerContact> GetAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"/customers/{customerId}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ReceiptWorkflowNotFoundException("Invoice customer was not found.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ReceiptWorkflowDependencyException(
                $"CustomerService rejected the receipt operation with status {(int)response.StatusCode}.");
        }

        var bytes = await ReceiptDocumentClient.ReadBoundedAsync(
            response.Content,
            MaximumResponseBytes,
            "CustomerService",
            cancellationToken);
        var customer = JsonSerializer.Deserialize<CustomerResponse>(bytes, Json);
        if (customer is null ||
            string.IsNullOrWhiteSpace(customer.Email) ||
            string.IsNullOrWhiteSpace(customer.FullName) ||
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(customer.Email))
        {
            throw new ReceiptWorkflowDependencyException("CustomerService returned invalid receipt contact data.");
        }

        return new ReceiptCustomerContact(customer.Email, customer.FullName);
    }

    private sealed record CustomerResponse(string Email, string FullName);
}

/// <summary>Authenticated client for employee-owned signature metadata and clean object bytes.</summary>
public sealed class ReceiptSignatureClient(HttpClient httpClient, IReceiptFileClient files) : IReceiptSignatureClient
{
    private const int MaximumResponseBytes = 64 * 1024;
    private const int MaximumSignatureBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(int employeeId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"/employees/Signatures/{employeeId}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ReceiptWorkflowDependencyException(
                $"EmployeeService rejected the receipt operation with status {(int)response.StatusCode}.");
        }

        var bytes = await ReceiptDocumentClient.ReadBoundedAsync(
            response.Content,
            MaximumResponseBytes,
            "EmployeeService",
            cancellationToken);
        var signature = JsonSerializer.Deserialize<SignatureResponse>(bytes, Json);
        if (signature is null || string.IsNullOrWhiteSpace(signature.Bucket) || string.IsNullOrWhiteSpace(signature.ObjectName))
        {
            throw new ReceiptWorkflowDependencyException("EmployeeService returned invalid signature metadata.");
        }

        return await files.DownloadAsync(
            signature.Bucket,
            signature.ObjectName,
            MaximumSignatureBytes,
            cancellationToken);
    }

    private sealed record SignatureResponse(string Bucket, string ObjectName);
}

/// <summary>Authenticated client for idempotent receipt email delivery.</summary>
public sealed class ReceiptNotificationClient(HttpClient httpClient) : IReceiptNotificationClient
{
    private const int MaximumResponseBytes = 32 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public async Task<string?> SendAsync(
        string customerEmail,
        string customerName,
        Receipt receipt,
        byte[] pdf,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/notifications/v1/email/Info")
        {
            Content = JsonContent.Create(new
            {
                to = customerEmail,
                subject = $"Receipt for your payment to invoice #{receipt.InvoiceNumber}",
                body = Body(customerName),
                bcc = new[] { "mail-tracking@maliev.com" },
                attachments = new[]
                {
                    new
                    {
                        fileName = $"receipt_{receipt.Id}.pdf",
                        contentType = MediaTypeNames.Application.Pdf,
                        content = pdf,
                    },
                },
            }),
        };
        request.Headers.Add("Idempotency-Key", operationId.ToString("D"));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ReceiptWorkflowDependencyException(
                $"NotificationService rejected the receipt operation with status {(int)response.StatusCode}.");
        }

        var bytes = await ReceiptDocumentClient.ReadBoundedAsync(
            response.Content,
            MaximumResponseBytes,
            "NotificationService",
            cancellationToken);
        return JsonSerializer.Deserialize<NotificationResult>(bytes, Json)?.ProviderMessageId;
    }

    private static string Body(string customerName)
    {
        var name = WebUtility.HtmlEncode(customerName);
        return $"<div>Hello {name},</div><div>&nbsp;</div>" +
            "<div>Here is confirmation that we have received your payment.</div>" +
            "<div>The receipt for your payment is attached.</div><div>&nbsp;</div>" +
            "<div>Thank you for your business.</div><div>&nbsp;</div><div>Best regards,</div>" +
            "<div>Maliev Co., Ltd.</div>";
    }

    private sealed record NotificationResult(string? ProviderMessageId);
}
