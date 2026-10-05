using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceNotificationPayloadSnapshotTests
{
    [Fact]
    public void PublishedProducerFixture_HasIndependentLiteralDigest()
    {
        var payload = new InvoiceNotificationPayloadSnapshot("recipient@example.invalid", "Invoice fixture", "synthetic invoice");
        Assert.Equal("8df2575fc38b90f91c153416beb7ca539b4f41ee2c1676f993f497d583ddf741", payload.Digest());
    }

    [Fact]
    public void CallerMutation_CannotChangeOwnedDigestOrAttachmentBytes()
    {
        string[] cc = ["recipient@example.invalid"];
        byte[] bytes = [1, 2, 3];
        InvoiceNotificationAttachment[] attachments = [new("ใบแจ้งหนี้.pdf", "application/pdf", bytes)];
        var payload = new InvoiceNotificationPayloadSnapshot("recipient@example.invalid", "ใบแจ้งหนี้", "ข้อมูล", cc: cc, attachments: attachments);
        var digest = payload.Digest();
        cc[0] = "changed@example.invalid";
        bytes[0] = 99;
        attachments[0] = new("changed.pdf", "application/pdf", [9]);
        payload.Attachments![0].Content[0] = 88;
        Assert.Equal(digest, payload.Digest());
        Assert.Equal(new byte[] { 1, 2, 3 }, payload.Attachments[0].Content);
        Assert.Equal("recipient@example.invalid", Assert.Single(payload.Cc!));
    }

    [Fact]
    public void NullAndEmptyRecipients_AndRecipientOrder_RemainDistinct()
    {
        var omitted = new InvoiceNotificationPayloadSnapshot("recipient@example.invalid", "Invoice fixture", "synthetic invoice");
        var empty = new InvoiceNotificationPayloadSnapshot("recipient@example.invalid", "Invoice fixture", "synthetic invoice", cc: []);
        var first = new InvoiceNotificationPayloadSnapshot("recipient@example.invalid", "Invoice fixture", "synthetic invoice", cc: ["a@example.invalid", "b@example.invalid"]);
        var reversed = new InvoiceNotificationPayloadSnapshot("recipient@example.invalid", "Invoice fixture", "synthetic invoice", cc: ["b@example.invalid", "a@example.invalid"]);
        Assert.NotEqual(omitted.Digest(), empty.Digest());
        Assert.NotEqual(first.Digest(), reversed.Digest());
    }

    [Theory]
    [InlineData("not-an-email", "valid", "valid")]
    [InlineData("recipient@example.invalid", "", "valid")]
    [InlineData("recipient@example.invalid", "valid", "")]
    public void InvalidPayload_RefusesBeforeAdmission(string to, string subject, string body) =>
        Assert.Throws<ArgumentException>(() => new InvoiceNotificationPayloadSnapshot(to, subject, body));

    [Fact]
    public void UnpairedUtf16_RefusesBeforeAdmission() => Assert.Throws<ArgumentException>(() =>
        new InvoiceNotificationPayloadSnapshot("recipient@example.invalid", "valid", "\ud800"));
}
