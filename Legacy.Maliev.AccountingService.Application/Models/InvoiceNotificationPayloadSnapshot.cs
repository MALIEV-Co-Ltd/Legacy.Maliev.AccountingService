using System.Buffers.Binary;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace Legacy.Maliev.AccountingService.Application.Models;

/// <summary>Independently owned attachment bytes used by both digest and serialization.</summary>
public sealed class InvoiceNotificationAttachment
{
    private readonly byte[] content;

    public InvoiceNotificationAttachment(string fileName, string? contentType, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        FileName = fileName;
        ContentType = contentType;
        this.content = content.ToArray();
    }

    public string FileName { get; }
    public string? ContentType { get; }
    public byte[] Content => content.ToArray();
}

/// <summary>Exact immutable invoice payload; null arrays and recipient order are significant.</summary>
public sealed class InvoiceNotificationPayloadSnapshot
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public const string PayloadVersion = "notification-payload-v1";

    public InvoiceNotificationPayloadSnapshot(string to, string subject, string body, string? replyTo = null,
        IReadOnlyList<string>? cc = null, IReadOnlyList<string>? bcc = null,
        IReadOnlyList<InvoiceNotificationAttachment>? attachments = null)
    {
        To = to;
        Subject = subject;
        Body = body;
        ReplyTo = replyTo;
        Cc = cc is null ? null : Array.AsReadOnly(cc.ToArray());
        Bcc = bcc is null ? null : Array.AsReadOnly(bcc.ToArray());
        Attachments = attachments is null ? null : Array.AsReadOnly(attachments.Select(value =>
            new InvoiceNotificationAttachment(value.FileName, value.ContentType, value.Content)).ToArray());
        Validate();
    }

    public string Channel => "Info";
    public string To { get; }
    public string Subject { get; }
    public string Body { get; }
    public string? ReplyTo { get; }
    public IReadOnlyList<string>? Cc { get; }
    public IReadOnlyList<string>? Bcc { get; }
    public IReadOnlyList<InvoiceNotificationAttachment>? Attachments { get; }

    /// <summary>Producer-compatible tagged SHA256; no JSON normalization or shared HMAC key.</summary>
    public string Digest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Text(hash, "version", PayloadVersion);
        Text(hash, "channel", Channel);
        Text(hash, "to", To);
        Text(hash, "subject", Subject);
        Text(hash, "body", Body);
        Text(hash, "replyTo", ReplyTo);
        Recipients(hash, "cc", Cc);
        Recipients(hash, "bcc", Bcc);
        ArrayHeader(hash, "attachments", Attachments?.Count);
        if (Attachments is not null)
        {
            foreach (var item in Attachments)
            {
                Text(hash, "fileName", item.FileName);
                Text(hash, "contentType", item.ContentType);
                Field(hash, "content", item.Content);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private void Validate()
    {
        Mailbox(To);
        if (ReplyTo is not null) Mailbox(ReplyTo);
        CheckText(Subject, 998, true);
        CheckText(Body, 2 * 1024 * 1024, true);
        foreach (var recipients in new[] { Cc, Bcc })
        {
            if (recipients is { Count: > 100 }) throw Invalid();
            if (recipients is not null) foreach (var recipient in recipients) Mailbox(recipient);
        }

        if (Attachments is { Count: > 64 }) throw Invalid();
        long bytes = 0;
        if (Attachments is not null)
        {
            foreach (var item in Attachments)
            {
                CheckText(item.FileName, 255, true);
                CheckText(item.ContentType, 127, false);
                var length = item.Content.LongLength;
                bytes = checked(bytes + length);
                if (length == 0 || bytes > 200L * 1024 * 1024) throw Invalid();
            }
        }
    }

    private static void Mailbox(string value)
    {
        CheckText(value, 254, true);
        if (!MailAddress.TryCreate(value, out var address) || address.Address != value) throw Invalid();
    }

    private static void CheckText(string? value, int maximum, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value)) throw Invalid();
        try { if (value is not null && Utf8.GetByteCount(value) > maximum) throw Invalid(); }
        catch (EncoderFallbackException) { throw Invalid(); }
    }

    private static void Text(IncrementalHash hash, string tag, string? value) =>
        Field(hash, tag, value is null ? null : Utf8.GetBytes(value));

    private static void Field(IncrementalHash hash, string tag, byte[]? value)
    {
        hash.AppendData(Encoding.ASCII.GetBytes(tag));
        hash.AppendData([0]);
        hash.AppendData(value is null ? [255] : [0]);
        if (value is null) return;
        Length(hash, (ulong)value.LongLength);
        hash.AppendData(value);
    }

    private static void ArrayHeader(IncrementalHash hash, string tag, int? count)
    {
        hash.AppendData(Encoding.ASCII.GetBytes(tag));
        hash.AppendData([0]);
        hash.AppendData(count is null ? [255] : [0]);
        if (count is not null) Length(hash, (ulong)count.Value);
    }

    private static void Length(IncrementalHash hash, ulong value)
    {
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(length, value);
        hash.AppendData(length);
    }

    private static void Recipients(IncrementalHash hash, string tag, IReadOnlyList<string>? values)
    {
        ArrayHeader(hash, tag, values?.Count);
        if (values is not null) foreach (var value in values) Text(hash, "recipient", value);
    }

    private static ArgumentException Invalid() => new("Invalid invoice notification payload.");
}
