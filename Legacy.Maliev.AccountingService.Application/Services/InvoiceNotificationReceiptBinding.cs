using Legacy.Maliev.AccountingService.Application.Models;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Legacy.Maliev.AccountingService.Application.Services;

/// <summary>Strict domain-separated receipt continuity; does not authenticate transport or grant execution.</summary>
public static class InvoiceNotificationReceiptBinding
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] Frame(InvoiceNotificationCorrelationIdentity identity, ReadOnlySpan<byte> payloadBinding,
        string keyId, InvoiceNotificationReceiptObservation receipt)
    {
        if (payloadBinding.Length != 32 || receipt is null) throw InvalidInput();
        try
        {
            var validation = InvoiceNotificationCorrelationBinding.Frame(identity, keyId, new string('0', 64));
            CryptographicOperations.ZeroMemory(validation);
        }
        catch (ArgumentException) { throw InvalidInput(); }
        ValidateContent(receipt);
        if (receipt.IntentId != identity.IntentId.ToString("D", CultureInfo.InvariantCulture) ||
            receipt.WorkflowOperationId != identity.WorkflowOperationId.ToString("D", CultureInfo.InvariantCulture) ||
            receipt.ResourceId != identity.InvoiceId.ToString(CultureInfo.InvariantCulture) ||
            receipt.Purpose != identity.Purpose || receipt.ResourceType != "invoice") throw InvalidInput();

        using var frame = new MemoryStream();
        Write(frame, "receipt-binding-version", "accounting-notification-receipt-hmac-v1");
        Write(frame, "producer-contract", "delivery-intents-v2");
        Write(frame, "local-payload-binding", Convert.ToHexString(payloadBinding).ToLowerInvariant());
        Write(frame, "binding-key-id", keyId);
        Write(frame, "sender-issuer", identity.SenderIssuer);
        Write(frame, "sender-service-subject", identity.SenderServiceSubject);
        Write(frame, "intent-id", receipt.IntentId);
        Write(frame, "invoice-id", receipt.ResourceId);
        Write(frame, "purpose", receipt.Purpose);
        Write(frame, "resource-type", receipt.ResourceType);
        Write(frame, "workflow-operation-id", receipt.WorkflowOperationId);
        Write(frame, "remote-state", receipt.State);
        Write(frame, "remote-version", receipt.Version.ToString(CultureInfo.InvariantCulture));
        Write(frame, "remote-admitted-utc-ticks", receipt.AdmittedAt!.Value.UtcTicks.ToString(CultureInfo.InvariantCulture));
        Write(frame, "remote-updated-utc-ticks", receipt.UpdatedAt!.Value.UtcTicks.ToString(CultureInfo.InvariantCulture));
        Write(frame, "provider-message-id", receipt.ProviderMessageId);
        return frame.ToArray();
    }

    public static byte[] Compute(InvoiceNotificationCorrelationIdentity identity, ReadOnlySpan<byte> payloadBinding,
        string keyId, ReadOnlySpan<byte> key, InvoiceNotificationReceiptObservation receipt)
    {
        if (key.Length != 32) throw InvalidInput();
        var frame = Frame(identity, payloadBinding, keyId, receipt);
        try { return HMACSHA256.HashData(key, frame); }
        finally { CryptographicOperations.ZeroMemory(frame); }
    }

    public static InvoiceNotificationReceiptProgression ClassifyProgression(InvoiceNotificationRetainedReceipt? previous,
        InvoiceNotificationReceiptObservation incoming, ReadOnlySpan<byte> binding)
    {
        if (incoming is null || binding.Length != 32) throw InvalidInput();
        ValidateContent(incoming);
        if (previous is null) return incoming.State == "admitted" ? InvoiceNotificationReceiptProgression.First : InvoiceNotificationReceiptProgression.Conflict;
        if (!ValidStateVersion(previous.State, previous.Version) || !ValidTime(previous.AdmittedAt) ||
            !ValidTime(previous.UpdatedAt) || previous.UpdatedAt < previous.AdmittedAt || previous.Binding.Length != 32) throw InvalidInput();
        if (incoming.AdmittedAt != previous.AdmittedAt || incoming.UpdatedAt < previous.UpdatedAt || incoming.Version < previous.Version)
            return InvoiceNotificationReceiptProgression.Conflict;
        if (incoming.Version == previous.Version)
            return incoming.State == previous.State && incoming.UpdatedAt == previous.UpdatedAt &&
                CryptographicOperations.FixedTimeEquals(binding, previous.Binding)
                ? InvoiceNotificationReceiptProgression.Duplicate : InvoiceNotificationReceiptProgression.Conflict;
        var allowed = previous.State switch
        {
            "admitted" => incoming.State is "submitting" or "providerAccepted" or "outcomeUnknown",
            "submitting" => incoming.State is "providerAccepted" or "outcomeUnknown",
            _ => false
        };
        return allowed ? InvoiceNotificationReceiptProgression.Advance : InvoiceNotificationReceiptProgression.Conflict;
    }

    private static void ValidateContent(InvoiceNotificationReceiptObservation receipt)
    {
        if (!Guid.TryParseExact(receipt.IntentId, "D", out var intent) || intent == Guid.Empty || receipt.IntentId != intent.ToString("D") ||
            !Guid.TryParseExact(receipt.WorkflowOperationId, "D", out var workflow) || workflow == Guid.Empty || workflow == intent ||
            receipt.WorkflowOperationId != workflow.ToString("D") || receipt.Purpose != "invoice-issued" || receipt.ResourceType != "invoice" ||
            !int.TryParse(receipt.ResourceId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
            receipt.ResourceId != id.ToString(CultureInfo.InvariantCulture) || !ValidStateVersion(receipt.State, receipt.Version) ||
            !ValidTime(receipt.AdmittedAt) || !ValidTime(receipt.UpdatedAt) || receipt.UpdatedAt < receipt.AdmittedAt) throw InvalidInput();
        if (receipt.State == "providerAccepted")
        {
            if (string.IsNullOrWhiteSpace(receipt.ProviderMessageId) || receipt.ProviderMessageId.Length > 256 ||
                receipt.ProviderMessageId.Contains('\0', StringComparison.Ordinal)) throw InvalidInput();
            try { _ = StrictUtf8.GetByteCount(receipt.ProviderMessageId); }
            catch (EncoderFallbackException) { throw InvalidInput(); }
        }
        else if (receipt.ProviderMessageId is not null) throw InvalidInput();
    }

    private static bool ValidStateVersion(string state, long version) => (state, version) is
        ("admitted", 1) or ("submitting", 2) or ("providerAccepted", 3) or ("outcomeUnknown", 3);

    private static bool ValidTime(DateTimeOffset? time) => time is { } value && value.UtcTicks % 10 == 0;

    private static void Write(Stream frame, string tag, string? value)
    {
        frame.Write(Encoding.ASCII.GetBytes(tag));
        frame.WriteByte(0);
        frame.WriteByte(value is null ? (byte)1 : (byte)0);
        var bytes = value is null ? [] : StrictUtf8.GetBytes(value);
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)bytes.Length);
        frame.Write(length);
        frame.Write(bytes);
    }

    private static ArgumentException InvalidInput() => new("Invalid invoice notification receipt input.");
}
