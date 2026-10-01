using Legacy.Maliev.AccountingService.Application.Models;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Legacy.Maliev.AccountingService.Application.Services;

/// <summary>Strict domain-separated local binding; never persists or reconstructs raw notification payload.</summary>
public static class InvoiceNotificationCorrelationBinding
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Returns an independently owned, exact tagged frame for every immutable identity field.</summary>
    public static byte[] Frame(InvoiceNotificationCorrelationIdentity identity, string keyId, string payloadDigest)
    {
        Validate(identity, keyId, payloadDigest);
        using var frame = new MemoryStream();
        Write(frame, "binding-version", identity.BindingVersion);
        Write(frame, "intent-id", identity.IntentId.ToString("D", CultureInfo.InvariantCulture));
        Write(frame, "invoice-id", identity.InvoiceId.ToString(CultureInfo.InvariantCulture));
        Write(frame, "purpose", identity.Purpose);
        Write(frame, "quotation-id", identity.QuotationId.ToString(CultureInfo.InvariantCulture));
        Write(frame, "workflow-operation-id", identity.WorkflowOperationId.ToString("D", CultureInfo.InvariantCulture));
        Write(frame, "origin-issuer", identity.Origin.Issuer);
        Write(frame, "origin-employee-subject", identity.Origin.EmployeeSubject);
        Write(frame, "origin-service-subject", identity.Origin.ServiceSubject);
        Write(frame, "sender-issuer", identity.SenderIssuer);
        Write(frame, "sender-service-subject", identity.SenderServiceSubject);
        Write(frame, "payload-frame-version", identity.PayloadFrameVersion);
        Write(frame, "binding-key-id", keyId);
        Write(frame, "payload-digest", payloadDigest);
        return frame.ToArray();
    }

    /// <summary>HMACs the retained-key frame with a required 32-byte local owner key.</summary>
    public static byte[] Compute(InvoiceNotificationCorrelationIdentity identity, string keyId,
        ReadOnlySpan<byte> key, string payloadDigest)
    {
        if (key.Length != 32) throw InvalidInput();
        var frame = Frame(identity, keyId, payloadDigest);
        try { return HMACSHA256.HashData(key, frame); }
        finally { CryptographicOperations.ZeroMemory(frame); }
    }

    private static void Validate(InvoiceNotificationCorrelationIdentity identity, string keyId, string payloadDigest)
    {
        if (identity is null || identity.Origin is null || identity.IntentId == Guid.Empty ||
            identity.WorkflowOperationId == Guid.Empty || identity.IntentId == identity.WorkflowOperationId ||
            identity.InvoiceId <= 0 || identity.QuotationId <= 0 || identity.Purpose != "invoice-issued" ||
            identity.SenderServiceSubject != "service:legacy-accounting" ||
            identity.PayloadFrameVersion != "notification-payload-v1" ||
            identity.BindingVersion != "accounting-invoice-notification-hmac-v1") throw InvalidInput();

        Text(identity.Origin.Issuer, 512);
        Text(identity.Origin.EmployeeSubject, 256);
        Text(identity.Origin.ServiceSubject, 128);
        Text(identity.SenderIssuer, 512);
        Text(identity.SenderServiceSubject, 128);
        Text(keyId, 64);
        if (keyId.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_' and not '.') ||
            payloadDigest is null || payloadDigest.Length != 64 ||
            payloadDigest.Any(value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) throw InvalidInput();
    }

    private static void Text(string value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0', StringComparison.Ordinal)) throw InvalidInput();
        try { if (StrictUtf8.GetByteCount(value) > maximumBytes) throw InvalidInput(); }
        catch (EncoderFallbackException) { throw InvalidInput(); }
    }

    private static void Write(Stream frame, string tag, string value)
    {
        frame.Write(Encoding.ASCII.GetBytes(tag));
        frame.WriteByte(0);
        frame.WriteByte(0);
        var bytes = StrictUtf8.GetBytes(value);
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)bytes.Length);
        frame.Write(length);
        frame.Write(bytes);
    }

    private static ArgumentException InvalidInput() => new("Invalid invoice notification binding input.");
}
