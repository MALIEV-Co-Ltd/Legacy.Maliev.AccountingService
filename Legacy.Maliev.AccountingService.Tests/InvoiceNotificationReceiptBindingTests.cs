using System.Globalization;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceNotificationReceiptBindingTests
{
    private const string LiteralFrameHex = "726563656970742D62696E64696E672D76657273696F6E000000000000000000276163636F756E74696E672D6E6F74696669636174696F6E2D726563656970742D686D61632D763170726F64756365722D636F6E74726163740000000000000000001364656C69766572792D696E74656E74732D76326C6F63616C2D7061796C6F61642D62696E64696E67000000000000000000406161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616162696E64696E672D6B65792D696400000000000000000009666978747572652D3173656E6465722D6973737565720000000000000000001968747470733A2F2F617574682E6578616D706C652E7465737473656E6465722D736572766963652D7375626A65637400000000000000000019736572766963653A6C65676163792D6163636F756E74696E67696E74656E742D69640000000000000000002431303030303030302D303030302D303030302D303030302D303030303030303030303031696E766F6963652D6964000000000000000000023432707572706F73650000000000000000000E696E766F6963652D6973737565647265736F757263652D7479706500000000000000000007696E766F696365776F726B666C6F772D6F7065726174696F6E2D69640000000000000000002432303030303030302D303030302D303030302D303030302D30303030303030303030303272656D6F74652D73746174650000000000000000001070726F7669646572416363657074656472656D6F74652D76657273696F6E000000000000000000013372656D6F74652D61646D69747465642D7574632D7469636B730000000000000000001236333932363430393630303030303030303072656D6F74652D757064617465642D7574632D7469636B730000000000000000001236333932363430393630313030303030303070726F76696465722D6D6573736167652D696400000000000000000012666978747572652D616363657074616E6365";
    private static readonly DateTimeOffset Admitted = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] PayloadBinding = Enumerable.Repeat((byte)0xaa, 32).ToArray();
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    private static InvoiceNotificationCorrelationIdentity Identity => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"), 42, "invoice-issued", 7,
        Guid.Parse("20000000-0000-0000-0000-000000000002"),
        new("https://origin.example.test", "employee:fixture", "service:legacy-intranet"),
        "https://auth.example.test", "service:legacy-accounting", "notification-payload-v1",
        "accounting-invoice-notification-hmac-v1");

    private static InvoiceNotificationReceiptObservation Accepted => new(
        "10000000-0000-0000-0000-000000000001", "invoice-issued", "invoice", "42",
        "20000000-0000-0000-0000-000000000002", "providerAccepted", 3,
        Admitted, Admitted.AddSeconds(1), "fixture-acceptance");

    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void IndependentlyFramedLiteral_HasExactLengthAndHmac(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(Convert.FromHexString(LiteralFrameHex), InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1", Accepted));
            Assert.Equal(771, InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1", Accepted).Length);
            Assert.Equal("9982DC0BF49D41109E8F1B5BEE4DB1883E91368E0F1D21C8A93535D0A9DD42DB",
                Convert.ToHexString(InvoiceNotificationReceiptBinding.Compute(Identity, PayloadBinding, "fixture-1", Key, Accepted)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void SubMicrosecondReceipt_RefusesRatherThanRounding(int ticks) => Assert.Throws<ArgumentException>(() =>
        InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1", Accepted with { UpdatedAt = Admitted.AddTicks(ticks) }));

    [Fact]
    public void ExactMicrosecondAndEquivalentOffset_PutGetFramesAreIdentical()
    {
        var receipt = Accepted with { UpdatedAt = Admitted.AddTicks(10) };
        var frame = InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1", receipt);
        Assert.NotEmpty(frame);
        Assert.Equal(frame, InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1",
            receipt with { AdmittedAt = Admitted.ToOffset(TimeSpan.FromHours(7)), UpdatedAt = receipt.UpdatedAt!.Value.ToOffset(TimeSpan.FromHours(7)) }));
    }

    [Theory]
    [InlineData("timestamp")]
    [InlineData("backwards")]
    [InlineData("id")]
    [InlineData("resource")]
    [InlineData("version")]
    [InlineData("rejected")]
    [InlineData("empty-provider")]
    [InlineData("long-provider")]
    [InlineData("surrogate")]
    [InlineData("updated-null")]
    [InlineData("purpose")]
    [InlineData("resource-type")]
    [InlineData("workflow")]
    [InlineData("provider-null")]
    [InlineData("provider-nul")]
    public void InvalidReceipt_IsFixedMessageRefusal(string field)
    {
        var receipt = field switch
        {
            "timestamp" => Accepted with { AdmittedAt = null },
            "backwards" => Accepted with { UpdatedAt = Admitted.AddSeconds(-1) },
            "id" => Accepted with { IntentId = "10000000-0000-0000-0000-00000000000A" },
            "resource" => Accepted with { ResourceId = "042" },
            "version" => Accepted with { Version = 4 },
            "rejected" => Accepted with { State = "rejectedBeforeSubmission" },
            "empty-provider" => Accepted with { ProviderMessageId = "" },
            "long-provider" => Accepted with { ProviderMessageId = new string('x', 257) },
            "updated-null" => Accepted with { UpdatedAt = null },
            "purpose" => Accepted with { Purpose = "other" },
            "resource-type" => Accepted with { ResourceType = "other" },
            "workflow" => Accepted with { WorkflowOperationId = "30000000-0000-0000-0000-000000000003" },
            "provider-null" => Accepted with { ProviderMessageId = null },
            "provider-nul" => Accepted with { ProviderMessageId = "fixture\0hidden" },
            _ => Accepted with { ProviderMessageId = "\ud800" }
        };
        var error = Assert.Throws<ArgumentException>(() => InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1", receipt));
        Assert.Equal("Invalid invoice notification receipt input.", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void MissingOrWrongKeyLength_Refuses(int length) => Assert.Throws<ArgumentException>(() =>
        InvoiceNotificationReceiptBinding.Compute(Identity, PayloadBinding, "fixture-1", new byte[length], Accepted));

    [Theory]
    [InlineData("admitted", 1, "submitting", 2, InvoiceNotificationReceiptProgression.Advance)]
    [InlineData("admitted", 1, "providerAccepted", 3, InvoiceNotificationReceiptProgression.Advance)]
    [InlineData("submitting", 2, "outcomeUnknown", 3, InvoiceNotificationReceiptProgression.Advance)]
    [InlineData("outcomeUnknown", 3, "providerAccepted", 3, InvoiceNotificationReceiptProgression.Conflict)]
    [InlineData("providerAccepted", 3, "admitted", 1, InvoiceNotificationReceiptProgression.Conflict)]
    public void CurrentProducerGraph_IsMonotonicNotInvented(string oldState, long oldVersion, string newState, long newVersion,
        InvoiceNotificationReceiptProgression expected)
    {
        var previous = new InvoiceNotificationRetainedReceipt(oldState, oldVersion, Admitted, Admitted, new byte[32]);
        var receipt = Accepted with { State = newState, Version = newVersion, ProviderMessageId = newState == "providerAccepted" ? "fixture-acceptance" : null };
        Assert.Equal(expected, InvoiceNotificationReceiptBinding.ClassifyProgression(previous, receipt, PayloadBinding));
    }

    [Fact]
    public void RetainedBinding_CopiesInputAndOutput()
    {
        var bytes = new byte[32];
        var retained = new InvoiceNotificationRetainedReceipt("admitted", 1, Admitted, Admitted, bytes);
        bytes[0] = 1;
        var output = retained.Binding;
        output[1] = 2;
        Assert.Equal(new byte[32], retained.Binding);
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("invoice")]
    [InlineData("purpose")]
    [InlineData("workflow")]
    [InlineData("sender")]
    [InlineData("payload-version")]
    [InlineData("binding-version")]
    public void IndependentlyBoundContext_MismatchRefuses(string field)
    {
        var identity = field switch
        {
            "intent" => Identity with { IntentId = Guid.Parse("30000000-0000-0000-0000-000000000003") },
            "invoice" => Identity with { InvoiceId = 43 },
            "purpose" => Identity with { Purpose = "other" },
            "workflow" => Identity with { WorkflowOperationId = Guid.Parse("30000000-0000-0000-0000-000000000003") },
            "sender" => Identity with { SenderServiceSubject = "service:other" },
            "payload-version" => Identity with { PayloadFrameVersion = "other" },
            _ => Identity with { BindingVersion = "other" }
        };
        Assert.Throws<ArgumentException>(() => InvoiceNotificationReceiptBinding.Frame(identity, PayloadBinding, "fixture-1", Accepted));
    }

    [Theory]
    [InlineData("sender-issuer")]
    [InlineData("key-id")]
    [InlineData("key")]
    [InlineData("payload")]
    [InlineData("provider")]
    [InlineData("admitted")]
    [InlineData("updated")]
    public void BoundFrameOrKeyMutation_ChangesLiteralHmac(string field)
    {
        var identity = field == "sender-issuer" ? Identity with { SenderIssuer = "https://other.example.test" } : Identity;
        var payload = PayloadBinding.ToArray();
        var key = Key.ToArray();
        if (field == "payload") payload[0] ^= 1;
        if (field == "key") key[0] ^= 1;
        var receipt = field switch
        {
            "provider" => Accepted with { ProviderMessageId = "fixture-other" },
            "admitted" => Accepted with { AdmittedAt = Admitted.AddTicks(10) },
            "updated" => Accepted with { UpdatedAt = Admitted.AddSeconds(2) },
            _ => Accepted
        };
        Assert.NotEqual("9982DC0BF49D41109E8F1B5BEE4DB1883E91368E0F1D21C8A93535D0A9DD42DB",
            Convert.ToHexString(InvoiceNotificationReceiptBinding.Compute(identity, payload, field == "key-id" ? "fixture-2" : "fixture-1", key, receipt)));
    }

    [Fact]
    public void ReceiptDomain_IsNotPayloadDomain_AndCopiesOutputs()
    {
        var receipt = InvoiceNotificationReceiptBinding.Compute(Identity, PayloadBinding, "fixture-1", Key, Accepted);
        Assert.NotEqual(InvoiceNotificationCorrelationBinding.Compute(Identity, "fixture-1", Key, new string('a', 64)), receipt);
        receipt[0] ^= 1;
        Assert.Equal("9982DC0BF49D41109E8F1B5BEE4DB1883E91368E0F1D21C8A93535D0A9DD42DB",
            Convert.ToHexString(InvoiceNotificationReceiptBinding.Compute(Identity, PayloadBinding, "fixture-1", Key, Accepted)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid/kid")]
    public void MissingOrMalformedRetainedKeyId_Refuses(string kid) => Assert.Throws<ArgumentException>(() =>
        InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, kid, Accepted));

    [Theory]
    [InlineData("state")]
    [InlineData("version")]
    [InlineData("length")]
    [InlineData("precision")]
    [InlineData("backwards")]
    public void MalformedRetainedEvidence_Refuses(string field)
    {
        var previous = new InvoiceNotificationRetainedReceipt(field == "state" ? "invented" : "admitted",
            field == "version" ? 0 : 1, Admitted,
            field == "precision" ? Admitted.AddTicks(1) : field == "backwards" ? Admitted.AddSeconds(-1) : Admitted,
            new byte[field == "length" ? 31 : 32]);
        Assert.Throws<ArgumentException>(() => InvoiceNotificationReceiptBinding.ClassifyProgression(previous, Accepted, PayloadBinding));
    }

    [Theory]
    [InlineData("same")]
    [InlineData("binding")]
    [InlineData("timestamp")]
    [InlineData("admitted")]
    public void EqualVersion_IsExactDuplicateOnly(string field)
    {
        var binding = PayloadBinding.ToArray();
        if (field == "binding") binding[0] ^= 1;
        var previous = new InvoiceNotificationRetainedReceipt("providerAccepted", 3, Admitted, Admitted.AddSeconds(1), PayloadBinding);
        var receipt = field switch
        {
            "timestamp" => Accepted with { UpdatedAt = Admitted.AddSeconds(2) },
            "admitted" => Accepted with { AdmittedAt = Admitted.AddTicks(10) },
            _ => Accepted
        };
        Assert.Equal(field == "same" ? InvoiceNotificationReceiptProgression.Duplicate : InvoiceNotificationReceiptProgression.Conflict,
            InvoiceNotificationReceiptBinding.ClassifyProgression(previous, receipt, binding));
    }

    [Fact]
    public void SameVersionProviderMutation_ConflictsWithRetainedReceipt()
    {
        var oldBinding = InvoiceNotificationReceiptBinding.Compute(Identity, PayloadBinding, "fixture-1", Key, Accepted);
        var changed = Accepted with { ProviderMessageId = "fixture-other" };
        var newBinding = InvoiceNotificationReceiptBinding.Compute(Identity, PayloadBinding, "fixture-1", Key, changed);
        Assert.Equal(InvoiceNotificationReceiptProgression.Conflict, InvoiceNotificationReceiptBinding.ClassifyProgression(
            new("providerAccepted", 3, Admitted, Admitted.AddSeconds(1), oldBinding), changed, newBinding));
    }

    [Fact]
    public void MaximalMicrosecondTimestamp_IsValid_ButMaximumResidualRefuses()
    {
        var maximum = DateTimeOffset.MaxValue.AddTicks(-9);
        Assert.NotEmpty(InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1", Accepted with { UpdatedAt = maximum }));
        Assert.Throws<ArgumentException>(() => InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1",
            Accepted with { UpdatedAt = DateTimeOffset.MaxValue }));
    }

    [Fact]
    public void ThaiProviderIdentifier_UsesCharacterBound_NotGuessedByteBound()
    {
        Assert.NotEmpty(InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1", Accepted with { ProviderMessageId = new string('ก', 256) }));
        Assert.Throws<ArgumentException>(() => InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1",
            Accepted with { ProviderMessageId = new string('ก', 257) }));
    }

    [Fact]
    public void ThaiProviderIdentifier_IndependentLiteralUtf8AndHmac()
    {
        // Independent fixture vector: replace only the final18 ASCII provider bytes
        // and its length in LiteralFrameHex with the literal30 UTF8 bytes below.
        var receipt = Accepted with { ProviderMessageId = "ใบแจ้งหนี้" };
        var frame = InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1", receipt);
        Assert.Equal(783, frame.Length);
        Assert.EndsWith("000000000000001EE0B983E0B89AE0B981E0B888E0B989E0B887E0B8ABE0B899E0B8B5E0B989", Convert.ToHexString(frame));
        Assert.Equal("94F1ED3FDB90E50D4C1B0430D15F0A3291F7D67CD7597DAB7EE92051E4768F13",
            Convert.ToHexString(InvoiceNotificationReceiptBinding.Compute(Identity, PayloadBinding, "fixture-1", Key, receipt)));
    }

    [Fact]
    public void NullProvider_UsesIndependentNullMarker_NotEmptyPresent()
    {
        var admitted = Accepted with { State = "admitted", Version = 1, UpdatedAt = Admitted, ProviderMessageId = null };
        Assert.EndsWith("70726F76696465722D6D6573736167652D696400010000000000000000",
            Convert.ToHexString(InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1", admitted)));
        Assert.Throws<ArgumentException>(() => InvoiceNotificationReceiptBinding.Frame(Identity, PayloadBinding, "fixture-1",
            admitted with { ProviderMessageId = "" }));
    }

    [Theory]
    [InlineData("admitted", 1, InvoiceNotificationReceiptProgression.First)]
    [InlineData("submitting", 2, InvoiceNotificationReceiptProgression.Conflict)]
    [InlineData("outcomeUnknown", 3, InvoiceNotificationReceiptProgression.Conflict)]
    [InlineData("providerAccepted", 3, InvoiceNotificationReceiptProgression.Conflict)]
    public void FirstObservation_DoesNotInventLocalExecutionLineage(string state, long version, InvoiceNotificationReceiptProgression expected)
    {
        Assert.Equal(expected, InvoiceNotificationReceiptBinding.ClassifyProgression(null,
            Accepted with { State = state, Version = version, ProviderMessageId = state == "providerAccepted" ? "fixture-acceptance" : null }, PayloadBinding));
    }

    [Fact]
    public void UnknownTerminal_CannotInventAcceptedVersionFour()
    {
        var retained = new InvoiceNotificationRetainedReceipt("outcomeUnknown", 3, Admitted, Admitted.AddSeconds(1), PayloadBinding);
        Assert.Throws<ArgumentException>(() => InvoiceNotificationReceiptBinding.ClassifyProgression(retained, Accepted with { Version = 4 }, PayloadBinding));
    }
}
