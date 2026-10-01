using System.Globalization;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Independent literal frames/HMAC, initially RED against the inert compilation seam.</summary>
public sealed class InvoiceNotificationCorrelationBindingTests
{
    [Fact]
    public void BindingFrame_IsExactTagged697ByteLiteral_NotDelimiterConcatenation()
    {
        var actual = InvoiceNotificationCorrelationBinding.Frame(Identity(), "fixture-1", new string('a', 64));
        Assert.Equal(Convert.FromHexString(FrameHex), actual);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void BindingHmac_IsIndependentLiteralAcrossCulture(string culture)
    {
        var prior = CultureInfo.CurrentCulture;
        var priorUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            // Public synthetic vector key: byte values 00..1f, not an owner credential.
            var actual = InvoiceNotificationCorrelationBinding.Compute(Identity(), "fixture-1",
                Enumerable.Range(0, 32).Select(value => (byte)value).ToArray(), new string('a', 64));
            Assert.Equal("0AB8029E85942E82C06CC92CBF35AAF4DEA476FA49D29894FB4A07EF32F51FE7", Convert.ToHexString(actual));
        }
        finally
        {
            CultureInfo.CurrentCulture = prior;
            CultureInfo.CurrentUICulture = priorUi;
        }
    }

    [Fact]
    public void ThaiActorBinding_IsLiteralUtf8NotReplacementOrCharacterCount()
    {
        var identity = Identity() with { Origin = Identity().Origin with { EmployeeSubject = "employee:คนไทย" } };
        var frame = InvoiceNotificationCorrelationBinding.Frame(identity, "fixture-1", new string('a', 64));
        Assert.Equal(710, frame.Length);
        Assert.Equal("D9FDFD4BDDAA85AFC296020B1B4243F48338E9BF38633C483158E4AD4FCDE449",
            Convert.ToHexString(InvoiceNotificationCorrelationBinding.Compute(identity, "fixture-1", Key(), new string('a', 64))));
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("invoice")]
    [InlineData("quotation")]
    [InlineData("workflow")]
    [InlineData("origin-issuer")]
    [InlineData("employee")]
    [InlineData("origin-service")]
    [InlineData("sender-issuer")]
    [InlineData("key-id")]
    [InlineData("payload-digest")]
    public void EveryVariableImmutableField_ChangesBinding(string field)
    {
        var identity = Identity();
        identity = field switch
        {
            "intent" => identity with { IntentId = Guid.Parse("33333333-3333-4333-8333-333333333333") },
            "invoice" => identity with { InvoiceId = 902 },
            "quotation" => identity with { QuotationId = 85 },
            "workflow" => identity with { WorkflowOperationId = Guid.Parse("33333333-3333-4333-8333-333333333333") },
            "origin-issuer" => identity with { Origin = identity.Origin with { Issuer = "https://other.example.invalid" } },
            "employee" => identity with { Origin = identity.Origin with { EmployeeSubject = "employee:43" } },
            "origin-service" => identity with { Origin = identity.Origin with { ServiceSubject = "service:other" } },
            "sender-issuer" => identity with { SenderIssuer = "https://other.example.invalid" },
            _ => identity,
        };
        var actual = InvoiceNotificationCorrelationBinding.Compute(identity,
            field == "key-id" ? "fixture-2" : "fixture-1", Key(), new string(field == "payload-digest" ? 'b' : 'a', 64));
        Assert.Equal(32, actual.Length);
        Assert.NotEqual("0AB8029E85942E82C06CC92CBF35AAF4DEA476FA49D29894FB4A07EF32F51FE7", Convert.ToHexString(actual));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("origin-null")]
    [InlineData("intent-empty")]
    [InlineData("workflow-empty")]
    [InlineData("same-uuid")]
    [InlineData("invoice-zero")]
    [InlineData("quotation-negative")]
    [InlineData("purpose")]
    [InlineData("sender")]
    [InlineData("payload-version")]
    [InlineData("binding-version")]
    [InlineData("origin-issuer-empty")]
    [InlineData("origin-issuer-long")]
    [InlineData("employee-long")]
    [InlineData("service-long")]
    [InlineData("sender-issuer-long")]
    [InlineData("employee-null-byte")]
    public void MissingOrInvalidImmutableIdentity_RejectsFixedCategory(string field)
    {
        var identity = Identity();
        identity = field switch
        {
            "null" => null!,
            "origin-null" => identity with { Origin = null! },
            "intent-empty" => identity with { IntentId = Guid.Empty },
            "workflow-empty" => identity with { WorkflowOperationId = Guid.Empty },
            "same-uuid" => identity with { WorkflowOperationId = identity.IntentId },
            "invoice-zero" => identity with { InvoiceId = 0 },
            "quotation-negative" => identity with { QuotationId = -1 },
            "purpose" => identity with { Purpose = "invoice-paid" },
            "sender" => identity with { SenderServiceSubject = "service:other" },
            "payload-version" => identity with { PayloadFrameVersion = "notification-payload-v2" },
            "binding-version" => identity with { BindingVersion = "unreviewed-v2" },
            "origin-issuer-empty" => identity with { Origin = identity.Origin with { Issuer = " " } },
            "origin-issuer-long" => identity with { Origin = identity.Origin with { Issuer = new string('x', 513) } },
            "employee-long" => identity with { Origin = identity.Origin with { EmployeeSubject = new string('ก', 86) } },
            "service-long" => identity with { Origin = identity.Origin with { ServiceSubject = new string('x', 129) } },
            "sender-issuer-long" => identity with { SenderIssuer = new string('x', 513) },
            "employee-null-byte" => identity with { Origin = identity.Origin with { EmployeeSubject = "employee:\0hidden" } },
            _ => throw new InvalidOperationException("Unknown test input."),
        };
        var error = Assert.Throws<ArgumentException>(() => InvoiceNotificationCorrelationBinding.Frame(identity, "fixture-1", new string('a', 64)));
        Assert.Equal("Invalid invoice notification binding input.", error.Message);
    }

    [Theory]
    [InlineData(null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("bad/key", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("fixture-1", null)]
    [InlineData("fixture-1", "")]
    [InlineData("fixture-1", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("fixture-1", "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void MissingKeyIdOrNoncanonicalDigest_IsNotRehashed(string? keyId, string? digest)
    {
        Assert.Throws<ArgumentException>(() => InvoiceNotificationCorrelationBinding.Frame(Identity(), keyId!, digest!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void MissingOrWrongLengthOwnerKey_CannotProduceBinding(int length) =>
        Assert.Throws<ArgumentException>(() => InvoiceNotificationCorrelationBinding.Compute(Identity(), "fixture-1", new byte[length], new string('a', 64)));

    [Fact]
    public void BindingAndRetainedSnapshot_DefensivelyOwnByteArrays()
    {
        var frame = InvoiceNotificationCorrelationBinding.Frame(Identity(), "fixture-1", new string('a', 64));
        frame[0] = 0;
        Assert.Equal(Convert.FromHexString(FrameHex), InvoiceNotificationCorrelationBinding.Frame(Identity(), "fixture-1", new string('a', 64)));
        var bytes = Key();
        var snapshot = new InvoiceNotificationCorrelation(Identity(), "fixture-1", bytes, "Prepared", 1, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null);
        bytes[0] = 255;
        snapshot.PayloadBinding[1] = 255;
        Assert.Equal(Key(), snapshot.PayloadBinding);
    }

    private static byte[] Key() => Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void BindingFrame_UnpairedSurrogateCannotBeReplacedIntoAnEquivalentIdentity()
    {
        var identity = Identity() with { Origin = Identity().Origin with { EmployeeSubject = "employee:\ud800" } };
        Assert.Throws<ArgumentException>(() => InvoiceNotificationCorrelationBinding.Frame(identity, "fixture-1", new string('a', 64)));
    }

    private static InvoiceNotificationCorrelationIdentity Identity() => new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"), 901, "invoice-issued", 84,
        Guid.Parse("22222222-2222-4222-8222-222222222222"),
        new("https://auth.example.invalid", "employee:42", "service:legacy-intranet"),
        "https://auth.example.invalid", "service:legacy-accounting", "notification-payload-v1",
        "accounting-invoice-notification-hmac-v1");

    // Independently derived outside the codec: each literal tag + 00, presence00,
    // UInt64 big-endian length and exact UTF-8 bytes; HMAC checked with .NET's
    // standard primitive over THIS literal frame. No expected invokes production.
    private const string FrameHex =
        "62696E64696E672D76657273696F6E000000000000000000276163636F756E74696E672D696E766F6963652D6E6F74696669636174696F6E2D686D61632D7631" +
        "696E74656E742D69640000000000000000002431313131313131312D313131312D343131312D383131312D313131313131313131313131" +
        "696E766F6963652D696400000000000000000003393031707572706F73650000000000000000000E696E766F6963652D697373756564" +
        "71756F746174696F6E2D6964000000000000000000023834776F726B666C6F772D6F7065726174696F6E2D69640000000000000000002432323232323232322D323232322D343232322D383232322D323232323232323232323232" +
        "6F726967696E2D6973737565720000000000000000001C68747470733A2F2F617574682E6578616D706C652E696E76616C6964" +
        "6F726967696E2D656D706C6F7965652D7375626A6563740000000000000000000B656D706C6F7965653A3432" +
        "6F726967696E2D736572766963652D7375626A65637400000000000000000017736572766963653A6C65676163792D696E7472616E6574" +
        "73656E6465722D6973737565720000000000000000001C68747470733A2F2F617574682E6578616D706C652E696E76616C6964" +
        "73656E6465722D736572766963652D7375626A65637400000000000000000019736572766963653A6C65676163792D6163636F756E74696E67" +
        "7061796C6F61642D6672616D652D76657273696F6E000000000000000000176E6F74696669636174696F6E2D7061796C6F61642D7631" +
        "62696E64696E672D6B65792D696400000000000000000009666978747572652D31" +
        "7061796C6F61642D6469676573740000000000000000004061616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161";
}
