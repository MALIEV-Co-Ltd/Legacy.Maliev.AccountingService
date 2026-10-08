using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class BillingTaxPolicyTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.NewGuid();
    private static BillingSnapshot Snapshot() => new(84, 21, "synthetic-tax", "sha256:" + new string('a', 64),
        new string('a', 64), new(1000m, 50m, 1050m, "THB"),
        [new(1, new(1000m, 50m, 1050m, "THB"), "SyntheticGoods")], 2);
    private static TaxPolicySnapshot Policy() => new("synthetic-policy-v1", "synthetic-approval-only", true,
        At.AddDays(-1), At.AddDays(1), 2, MidpointRounding.AwayFromZero,
        [new("SyntheticGoods", .05m, [TaxTriggerKind.Payment, TaxTriggerKind.Delivery]),
         new("SyntheticServices", .05m, [TaxTriggerKind.Payment, TaxTriggerKind.ServiceUse, TaxTriggerKind.EarlyTaxInvoice])]);
    private static TaxTrigger Trigger(decimal cumulative, TaxTriggerKind kind = TaxTriggerKind.Payment) =>
        new(Guid.NewGuid(), Account, kind, At, [new(1, cumulative)]);

    [Fact]
    public void DepositThenUnpaidDeliveryRecognizesOnlyTheRemainingBaseAndVat()
    {
        var deposit = BillingTaxService.Assess(Snapshot(), Trigger(200), [], Policy());
        Assert.Equal(BillingTaxAssessmentState.Ready, deposit.State);
        Assert.Equal(new BillingMoney(200, 10, 210, "THB"), Assert.Single(deposit.DeltaLines).Amount);
        var delivered = BillingTaxService.Assess(Snapshot(), Trigger(1000, TaxTriggerKind.Delivery), [deposit], Policy());
        Assert.Equal(new BillingMoney(800, 40, 840, "THB"), Assert.Single(delivered.DeltaLines).Amount);
        var secondFact = BillingTaxService.Assess(Snapshot(), Trigger(1000), [deposit, delivered], Policy());
        Assert.Empty(secondFact.DeltaLines);
    }

    [Theory]
    [InlineData(TaxTriggerKind.ServiceUse)]
    [InlineData(TaxTriggerKind.EarlyTaxInvoice)]
    [InlineData(TaxTriggerKind.Payment)]
    public void ConfirmedServicePolicyCanRecognizeWithoutCommercialReleaseEvidence(TaxTriggerKind kind)
    {
        var source = Snapshot() with { Lines = [new(1, new(1000, 50, 1050, "THB"), "SyntheticServices")] };
        var result = BillingTaxService.Assess(source, Trigger(300, kind), [], Policy());
        Assert.Equal(BillingTaxAssessmentState.Ready, result.State);
        Assert.Equal(15, Assert.Single(result.DeltaLines).Amount.Vat);
    }

    [Fact]
    public void MixedPartialDeliveryPreservesEachClassificationAndFinalRoundingResidual()
    {
        var source = Snapshot() with
        {
            Cap = new(20, .63m, 20.63m, "THB"),
            Lines =
            [new(1, new(10, .5m, 10.5m, "THB"), "SyntheticGoods"),
             new(2, new(10, .13m, 10.13m, "THB"), "SyntheticZero")]
        };
        var policy = Policy() with
        {
            Categories = [new("SyntheticGoods", .05m, [TaxTriggerKind.Delivery]),
            new("SyntheticZero", .0125m, [TaxTriggerKind.Delivery])]
        };
        var partial = Trigger(3.33m, TaxTriggerKind.Delivery) with { Portions = [new(1, 3.33m), new(2, 3.33m)] };
        var first = BillingTaxService.Assess(source, partial, [], policy);
        var full = partial with { EventId = Guid.NewGuid(), Portions = [new(1, 10), new(2, 10)] };
        var last = BillingTaxService.Assess(source, full, [first], policy);
        Assert.Equal(.63m, first.DeltaLines.Concat(last.DeltaLines).Sum(line => line.Amount.Vat));
        Assert.Equal(20, first.DeltaLines.Concat(last.DeltaLines).Sum(line => line.Amount.Base));
        Assert.Equal(new[] { "SyntheticGoods", "SyntheticZero" }, last.DeltaLines.Select(line => line.TaxCategory));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unconfirmed")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("unsupported-trigger")]
    [InlineData("missing-classification")]
    public void MissingAuthorityRetainsActualTriggerAndQueuesReviewWithoutVat(string change)
    {
        TaxPolicySnapshot? policy = change switch
        {
            "missing" => null,
            "unconfirmed" => Policy() with { Confirmed = false },
            "expired" => Policy() with { EffectiveUntil = At },
            "future" => Policy() with { EffectiveFrom = At.AddTicks(1) },
            "unsupported-trigger" => Policy() with { Categories = [new("SyntheticGoods", .05m, [TaxTriggerKind.ServiceUse])] },
            _ => Policy() with { Categories = [] }
        };
        var fact = Trigger(200);
        var result = BillingTaxService.Assess(Snapshot(), fact, [], policy);
        Assert.Equal(BillingTaxAssessmentState.NeedsPolicyReview, result.State);
        Assert.Equal(fact.EventId, result.Trigger.EventId);
        Assert.Equal(fact.OccurredAt, result.Trigger.OccurredAt);
        Assert.Empty(result.DeltaLines);
        Assert.False(string.IsNullOrWhiteSpace(result.ReviewReason));
    }

    [Fact]
    public void ExactEventReplayPreservesOriginalPolicyAndChangedEventConflicts()
    {
        var fact = Trigger(200);
        var first = BillingTaxService.Assess(Snapshot(), fact, [], Policy());
        var replay = BillingTaxService.Assess(Snapshot(), fact, [first], null);
        Assert.Equal(BillingTaxAssessmentState.AlreadyRecognized, replay.State);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first.Policy), System.Text.Json.JsonSerializer.Serialize(replay.Policy));
        Assert.Equal(first.Fingerprint, replay.Fingerprint);
        Assert.Throws<ArgumentException>(() => BillingTaxService.Assess(Snapshot(),
            fact with { Portions = [new(1, 201)] }, [first], Policy()));
    }

    [Fact]
    public void PolicyRolloverQueuesReviewWithoutReratingIssuedHistory()
    {
        var first = BillingTaxService.Assess(Snapshot(), Trigger(200), [], Policy());
        var changed = Policy() with { Revision = "synthetic-policy-v2" };
        var next = BillingTaxService.Assess(Snapshot(), Trigger(1000, TaxTriggerKind.Delivery), [first], changed);
        Assert.Equal(BillingTaxAssessmentState.NeedsPolicyReview, next.State);
        Assert.Empty(next.DeltaLines);
        Assert.Equal(10, Assert.Single(first.DeltaLines).Amount.Vat);
        Assert.Equal("synthetic-policy-v1", first.Policy!.Revision);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("amount")]
    [InlineData("cap")]
    [InlineData("duplicate")]
    [InlineData("event")]
    [InlineData("kind")]
    public void MalformedFactsCannotProposeRecognition(string change)
    {
        var fact = change switch
        {
            "line" => Trigger(1) with { Portions = [new(999, 1)] },
            "amount" => Trigger(-1),
            "cap" => Trigger(1001),
            "duplicate" => Trigger(1) with { Portions = [new(1, 1), new(1, 1)] },
            "event" => Trigger(1) with { EventId = Guid.Empty },
            _ => Trigger(1) with { Kind = (TaxTriggerKind)999 }
        };
        Assert.Throws<ArgumentException>(() => BillingTaxService.Assess(Snapshot(), fact, [], Policy()));
    }

    [Fact]
    public void AssessmentFreezesFactsAndPolicyCollections()
    {
        var portions = new List<TaxPortion> { new(1, 200) };
        var triggers = new List<TaxTriggerKind> { TaxTriggerKind.Payment };
        var categories = new List<TaxCategoryPolicy> { new("SyntheticGoods", .05m, triggers) };
        var result = BillingTaxService.Assess(Snapshot(), Trigger(200) with { Portions = portions }, [],
            Policy() with { Categories = categories });
        portions.Clear(); triggers.Clear(); categories.Clear();
        Assert.Single(result.Trigger.Portions);
        Assert.Single(result.Policy!.Categories);
        Assert.Single(result.Policy.Categories[0].Triggers);
        Assert.Single(result.DeltaLines);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(60)]
    public void FinalTaxObligationMustReconcileRetainedVatOrQueueReview(decimal retainedVat)
    {
        var source = Snapshot() with
        {
            Cap = new(1000, retainedVat, 1000 + retainedVat, "THB"),
            Lines = [new(1, new(1000, retainedVat, 1000 + retainedVat, "THB"), "SyntheticGoods")]
        };
        var result = BillingTaxService.Assess(source, Trigger(1000, TaxTriggerKind.Delivery), [], Policy());
        Assert.Equal(BillingTaxAssessmentState.NeedsPolicyReview, result.State);
        Assert.Empty(result.DeltaLines);
    }

    [Theory]
    [InlineData("null-trigger")]
    [InlineData("fingerprint")]
    [InlineData("unconfirmed-policy")]
    [InlineData("duplicate-line")]
    [InlineData("amount")]
    [InlineData("precision")]
    [InlineData("overshoot")]
    public void ForgedHistoryCannotSuppressOrReplayRecognition(string change)
    {
        var first = BillingTaxService.Assess(Snapshot(), Trigger(200), [], Policy());
        var bad = change switch
        {
            "null-trigger" => first with { Trigger = null! },
            "fingerprint" => first with { Fingerprint = new string('b', 64) },
            "unconfirmed-policy" => first with { Policy = first.Policy! with { Confirmed = false } },
            "duplicate-line" => first with { DeltaLines = [first.DeltaLines[0], first.DeltaLines[0]] },
            "precision" => first with { DeltaLines = [new(1, new(200.001m, 10, 210.001m, "THB"), "SyntheticGoods")] },
            "overshoot" => first with { DeltaLines = [new(1, new(1001, 50, 1051, "THB"), "SyntheticGoods")] },
            _ => first with { DeltaLines = [new(1, new(199, 10, 209, "THB"), "SyntheticGoods")] }
        };
        Assert.Throws<ArgumentException>(() => BillingTaxService.Assess(Snapshot(), first.Trigger, [bad], Policy()));
        Assert.Throws<ArgumentException>(() => BillingTaxService.Assess(Snapshot(), Trigger(1000, TaxTriggerKind.Delivery), [bad], Policy()));
    }

    [Fact]
    public void ZeroBaseWithPositiveVatCannotBecomeAReadyZeroRecognition()
    {
        var source = Snapshot() with
        {
            Cap = new(0, 10, 10, "THB"),
            Lines = [new(1, new(0, 10, 10, "THB"), "SyntheticGoods")]
        };
        Assert.Throws<ArgumentException>(() => BillingTaxService.Assess(source, Trigger(0), [], Policy()));
    }
}
