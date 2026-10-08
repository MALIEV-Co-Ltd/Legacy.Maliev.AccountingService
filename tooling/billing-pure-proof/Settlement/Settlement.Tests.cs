namespace BillingSettlementCandidate;

public sealed class CashAllocationPlannerTests
{
    private static readonly Guid AccountId = Guid.Parse("52b37dc0-79e1-4aec-9b3d-7420b76a25a5");
    private static readonly Guid StageId = Guid.Parse("73c0fc0b-60b0-40c4-a9a0-3fbf136c5a7d");
    private static readonly Guid OperationId = Guid.Parse("d892e158-5e79-44a4-86c4-11a550cb525d");
    private static Stage Target => new(AccountId, StageId, 7, "THB", 2, 200m);
    private static VerifiedCash Source => new(31, 7, "THB", "verified-r1", 100m, "synthetic-owner-receipt");
    private static Request Intent => new(OperationId, "verified-r1", 60m);
    private static Allocation Previous => new(Guid.Parse("925f990d-3974-4a37-b902-4646066b56a2"),
        AccountId, Guid.Parse("536c2689-7844-4cbe-914a-0c9b9c10910b"), 7, "THB", 31, "verified-r1", 60m,
        "synthetic-owner-receipt", 100m);

    [Fact]
    public void VerifiedCashCanBeAllocatedSixtyThenFortyAcrossStages()
    {
        var first = CashAllocationPlanner.Plan(Target, Source, [], Intent);
        Assert.Equal(40m, first.SourceUnapplied);
        Assert.Equal(140m, first.StageOutstanding);
        var second = CashAllocationPlanner.Plan(Target with { StageId = Previous.StageId }, Source,
            [first.Allocation], Intent with { OperationId = Previous.OperationId, Cash = 40m });
        Assert.Equal(0m, second.SourceUnapplied);
        Assert.Equal(160m, second.StageOutstanding);
    }

    [Fact]
    public void ExactReplayDoesNotConsumeCashOrDebtAgain()
    {
        var first = CashAllocationPlanner.Plan(Target, Source, [], Intent);
        var replay = CashAllocationPlanner.Plan(Target with { Outstanding = 140m }, Source,
            [first.Allocation], Intent);
        Assert.True(replay.Replay);
        Assert.Equal(first.Allocation, replay.Allocation);
        Assert.Equal(40m, replay.SourceUnapplied);
        Assert.Equal(140m, replay.StageOutstanding);
    }

    [Fact]
    public void ChangedAmountCannotReuseAcknowledgedOperation()
    {
        var first = CashAllocationPlanner.Plan(Target, Source, [], Intent);
        Assert.Throws<InvalidOperationException>(() => CashAllocationPlanner.Plan(Target, Source,
            [first.Allocation], Intent with { Cash = 59m }));
    }

    [Fact]
    public void ChangedStageCannotReuseAcknowledgedOperation()
    {
        var first = CashAllocationPlanner.Plan(Target, Source, [], Intent);
        Assert.Throws<InvalidOperationException>(() => CashAllocationPlanner.Plan(Target with { StageId = Previous.StageId },
            Source, [first.Allocation], Intent));
    }

    [Fact]
    public void WrongCustomerRefusesCashAttribution() => Assert.Throws<ArgumentException>(() =>
        CashAllocationPlanner.Plan(Target, Source with { CustomerId = 8 }, [], Intent));

    [Fact]
    public void WrongCurrencyRefusesConversion() => Assert.Throws<ArgumentException>(() =>
        CashAllocationPlanner.Plan(Target, Source with { Currency = "USD" }, [], Intent));

    [Fact]
    public void StalePaymentRevisionRefuses() => Assert.Throws<InvalidOperationException>(() =>
        CashAllocationPlanner.Plan(Target, Source with { Revision = "verified-r2" }, [], Intent));

    [Fact]
    public void MissingOwnerReceiptRefuses() => Assert.Throws<ArgumentException>(() =>
        CashAllocationPlanner.Plan(Target, Source with { AuthorityReceipt = "" }, [], Intent));

    [Fact]
    public void GlobalAcknowledgedCashConsumptionCannotBeExceeded() => Assert.Throws<InvalidOperationException>(() =>
        CashAllocationPlanner.Plan(Target, Source, [Previous], Intent with { Cash = 41m }));

    [Fact]
    public void StageDebtCannotBecomeNegative() => Assert.Throws<InvalidOperationException>(() =>
        CashAllocationPlanner.Plan(Target with { Outstanding = 59m }, Source, [], Intent));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    public void InvalidCashRefuses(string amount) => Assert.Throws<ArgumentException>(() =>
        CashAllocationPlanner.Plan(Target, Source, [], Intent with { Cash = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) }));

    [Fact]
    public void MissingOperationRefuses() => Assert.Throws<ArgumentException>(() =>
        CashAllocationPlanner.Plan(Target, Source, [], Intent with { OperationId = Guid.Empty }));

    [Fact]
    public void DuplicateHistoryOperationsRefuse() => Assert.Throws<ArgumentException>(() =>
        CashAllocationPlanner.Plan(Target, Source, [Previous, Previous], Intent));

    [Fact]
    public void MalformedAcknowledgedAmountsRefuse() => Assert.Throws<ArgumentException>(() =>
        CashAllocationPlanner.Plan(Target, Source, [Previous with { Cash = -1m }], Intent));

    [Fact]
    public void OtherPaymentDoesNotConsumeThisCash()
    {
        var proposal = CashAllocationPlanner.Plan(Target, Source, [Previous with { PaymentId = 32 }], Intent);
        Assert.Equal(40m, proposal.SourceUnapplied);
    }

    [Fact]
    public void AnotherAccountStillConsumesGlobalPaymentAvailability() => Assert.Throws<InvalidOperationException>(() =>
        CashAllocationPlanner.Plan(Target, Source, [Previous with { AccountId = Guid.NewGuid() }], Intent with { Cash = 41m }));

    [Theory]
    [InlineData("revision")]
    [InlineData("customer")]
    [InlineData("currency")]
    [InlineData("receipt")]
    [InlineData("sourceCash")]
    public void IncoherentSamePaymentHistoryCannotBeFilteredAway(string changed)
    {
        var prior = changed switch
        {
            "revision" => Previous with { PaymentRevision = "old-r0" },
            "customer" => Previous with { CustomerId = 8 },
            "currency" => Previous with { Currency = "USD" },
            "receipt" => Previous with { PaymentAuthorityReceipt = "other-receipt" },
            _ => Previous with { CashAtVerification = 99m },
        };
        Assert.Throws<InvalidOperationException>(() => CashAllocationPlanner.Plan(Target, Source, [prior], Intent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverspentAcknowledgedHistoryRefusesEvenDuringReplay(bool replay)
    {
        var prior = Previous with { Cash = 101m };
        Assert.Throws<InvalidOperationException>(() => CashAllocationPlanner.Plan(Target, Source, [prior],
            replay ? Intent with { OperationId = prior.OperationId, Cash = 101m } : Intent));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("payment")]
    [InlineData("revision")]
    public void ChangedIdentityCannotReuseAcknowledgedOperation(string changed)
    {
        var first = CashAllocationPlanner.Plan(Target, Source, [], Intent);
        var target = changed == "account" ? Target with { AccountId = Guid.NewGuid() } : Target;
        var source = changed == "payment" ? Source with { PaymentId = 32 } : Source;
        var request = changed == "revision" ? Intent with { ExpectedPaymentRevision = "old-r0" } : Intent;
        Assert.Throws<InvalidOperationException>(() => CashAllocationPlanner.Plan(target, source, [first.Allocation], request));
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("amount")]
    public void ChangedVerifiedSourceCannotReplayOldAttribution(string changed)
    {
        var first = CashAllocationPlanner.Plan(Target, Source, [], Intent);
        var source = changed == "receipt" ? Source with { AuthorityReceipt = "new-owner-receipt" } : Source with { Cash = 110m };
        Assert.Throws<InvalidOperationException>(() => CashAllocationPlanner.Plan(Target, source, [first.Allocation], Intent));
    }

    [Fact]
    public void NullHistoryRefuses() => Assert.Throws<ArgumentException>(() =>
        CashAllocationPlanner.Plan(Target, Source, [null!], Intent));

    [Theory]
    [InlineData("account")]
    [InlineData("stage")]
    [InlineData("customer")]
    [InlineData("payment")]
    [InlineData("precisionLow")]
    [InlineData("precisionHigh")]
    [InlineData("cashNegative")]
    [InlineData("cashPrecision")]
    [InlineData("debtNegative")]
    [InlineData("debtPrecision")]
    public void InvalidAuthoritativeFactsRefuse(string changed)
    {
        var target = changed switch
        {
            "account" => Target with { AccountId = Guid.Empty },
            "stage" => Target with { StageId = Guid.Empty },
            "customer" => Target with { CustomerId = 0 },
            "precisionLow" => Target with { Precision = -1 },
            "precisionHigh" => Target with { Precision = 5 },
            "debtNegative" => Target with { Outstanding = -1m },
            "debtPrecision" => Target with { Outstanding = 0.001m },
            _ => Target,
        };
        var source = changed switch
        {
            "payment" => Source with { PaymentId = 0 },
            "cashNegative" => Source with { Cash = -1m },
            "cashPrecision" => Source with { Cash = 0.001m },
            _ => Source,
        };
        Assert.Throws<ArgumentException>(() => CashAllocationPlanner.Plan(target, source, [], Intent));
    }
}
