using COOPAI.API.Services.DebtSegmentation;

namespace COOPAI.API.Tests;

public sealed class DebtSegmentationAnalyzerTests
{
    private static readonly DateOnly May = new(2026, 5, 1);
    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly July = new(2026, 7, 1);
    private static readonly DateOnly August = new(2026, 8, 1);
    private readonly DebtSegmentationAnalyzer _analyzer = new();

    [Fact]
    public void LatestPaymentEvent_AnyPositiveAmountCountsAsPaid_WithoutChangingOldExpiryBucket()
    {
        var current = History(
            expireDate: new DateOnly(2022, 7, 31),
            states: [State(July, 0.01m, 80_000m)]);

        var classification = _analyzer.Classify(current, July);

        Assert.Equal(MonthlyPaymentStatus.Paid, classification.LatestPaymentStatus);
        Assert.Equal(DebtBucket.OverThreeToFiveYears, classification.Bucket);
    }

    [Fact]
    public void ConsecutiveMisses_StartAfterMostRecentPositivePayment()
    {
        var current = History(states:
        [
            State(May, 100m, 100m),
            State(June, 0m, 90m),
            State(July, 0m, 80m)
        ]);

        var classification = _analyzer.Classify(current, July);

        Assert.Equal(DebtBucket.TwoMonthsDelinquent, classification.Bucket);
        Assert.Equal(June, classification.FirstMissedPaymentPeriod);
        Assert.Equal(2, classification.ConsecutiveMissedMonths);
    }

    [Fact]
    public void OriginationMonth_WithNoPayment_IsNotDueAndNormal()
    {
        var states = Enumerable.Range(1, 7)
            .Select(month => State(new DateOnly(2026, month, 1), 0m, month < 7 ? 0m : 100m))
            .ToArray();
        var current = History(states: states, contractDate: new DateOnly(2026, 7, 15));

        var classification = _analyzer.Classify(current, July);

        Assert.Equal(MonthlyPaymentStatus.NotDue, classification.LatestPaymentStatus);
        Assert.Equal(DebtBucket.Normal, classification.Bucket);
        Assert.Null(classification.FirstMissedPaymentPeriod);
        Assert.Equal(0, classification.ConsecutiveMissedMonths);
        Assert.Equal(DebtClassificationBasis.BeforeFirstDuePeriod, classification.CalculationBasis);
        Assert.Equal(August, classification.FirstDuePeriod);
    }

    [Theory]
    [InlineData(1, DebtBucket.OneMonthDelinquent)]
    [InlineData(2, DebtBucket.TwoMonthsDelinquent)]
    [InlineData(3, DebtBucket.ThreeToSixMonthsDelinquent)]
    [InlineData(6, DebtBucket.ThreeToSixMonthsDelinquent)]
    [InlineData(7, DebtBucket.SevenToTwelveMonthsDelinquent)]
    [InlineData(12, DebtBucket.SevenToTwelveMonthsDelinquent)]
    public void V1Policy_UsesCentralizedConsecutiveMissBoundaries(int missedMonths, DebtBucket expected)
    {
        var states = Enumerable.Range(0, missedMonths)
            .Select(offset => State(July.AddMonths(-offset), 0m, 100m))
            .ToArray();

        var classification = _analyzer.Classify(History(states: states), July);

        Assert.Equal(expected, classification.Bucket);
        Assert.Equal(missedMonths, classification.ConsecutiveMissedMonths);
        Assert.Equal(DebtClassificationBasis.ConsecutiveNoPayment, classification.CalculationBasis);
    }

    [Fact]
    public void Classification_PreservesLastPaymentPeriodAndIgnoresPartialLaterMonth()
    {
        var history = History(states:
        [
            State(May, 25m, 120m),
            State(June, 0m, 110m),
            State(July, 0m, 100m),
            State(new DateOnly(2026, 8, 1), 50m, 90m)
        ]);

        var classification = _analyzer.Classify(history, July);

        Assert.Equal(DebtBucket.TwoMonthsDelinquent, classification.Bucket);
        Assert.Equal(May, classification.LastPaymentPeriod);
        Assert.Equal(June, classification.FirstMissedPaymentPeriod);
    }

    [Theory]
    [InlineData(2025, 7, 31, DebtBucket.SevenToTwelveMonthsDelinquent)]
    [InlineData(2023, 7, 31, DebtBucket.OverOneToThreeYears)]
    [InlineData(2021, 7, 31, DebtBucket.OverThreeToFiveYears)]
    [InlineData(2016, 7, 31, DebtBucket.TenYearsAndAbove)]
    public void LongTermExpiry_UsesExactCalendarBoundaries(int year, int month, int day, DebtBucket expected)
    {
        var states = Enumerable.Range(0, 12)
            .Select(offset => State(July.AddMonths(-offset), 0m, 100m))
            .ToArray();
        var current = History(expireDate: new DateOnly(year, month, day), states: states);

        Assert.Equal(expected, _analyzer.Classify(current, July).Bucket);
    }

    [Fact]
    public void LongTermExpiry_OneDayBeyondFiveYears_IsOverFiveToTenYears()
    {
        var current = History(
            expireDate: new DateOnly(2021, 7, 30),
            states: [State(July, 0m, 100m)]);

        Assert.Equal(DebtBucket.OverFiveToTenYears, _analyzer.Classify(current, July).Bucket);
    }

    [Fact]
    public void MemberWithMultipleContracts_PreservesContractsAndCountsMemberOnce()
    {
        var histories = new[]
        {
            History("M001", "C-1", states: [State(June, 100m, 100m), State(July, 100m, 90m)]),
            History("M001", "C-2", states: [State(June, 100m, 200m), State(July, 100m, 180m)])
        };

        var result = _analyzer.Analyze(histories, June, July);
        var normal = result.BucketSummaries.Single(x => x.Bucket == DebtBucket.Normal);

        Assert.Equal(2, result.Contracts.Count);
        Assert.Equal(1, normal.MemberCount);
        Assert.Equal(2, normal.ContractCount);
    }

    [Fact]
    public void PaymentMovement_ReportsAllFourIndependentTransitions()
    {
        var histories = new[]
        {
            History("M1", "C1", states: [State(June, 1m, 100m), State(July, 1m, 90m)]),
            History("M2", "C2", states: [State(June, 1m, 100m), State(July, 0m, 100m)]),
            History("M3", "C3", states: [State(June, 0m, 100m), State(July, 1m, 90m)]),
            History("M4", "C4", states: [State(June, 0m, 100m), State(July, 0m, 100m)])
        };

        var result = _analyzer.Analyze(histories, June, July);

        Assert.Equal(4, result.PaymentMovements.Count);
        Assert.All(result.PaymentMovements, movement => Assert.Equal(1, movement.ContractCount));
        Assert.Contains(result.PaymentMovements, x => x.PaymentMovement == "Paid -> Not paid");
        Assert.Contains(result.PaymentMovements, x => x.PaymentMovement == "Not paid -> Paid");
    }

    [Fact]
    public void BucketMovement_SeparatesSameBucketBalanceIncreaseAndDecrease()
    {
        var expireDate = new DateOnly(2022, 1, 1);
        var histories = new[]
        {
            History("M1", "C1", expireDate, [State(June, 0m, 80_000m), State(July, 0m, 75_000m)]),
            History("M2", "C2", expireDate, [State(June, 0m, 80_000m), State(July, 0m, 85_000m)])
        };

        var result = _analyzer.Analyze(histories, June, July);

        Assert.Contains(result.BucketMovements, x => x.Category == DebtMovementCategory.SameBucketBalanceDecreased && x.AmountChange == -5_000m);
        Assert.Contains(result.BucketMovements, x => x.Category == DebtMovementCategory.SameBucketBalanceIncreased && x.AmountChange == 5_000m);
    }

    [Fact]
    public void BucketMovement_ClassifiesImprovedWorsenedAndNewRisk()
    {
        var histories = new[]
        {
            History("M1", "C1", states: [State(June, 1m, 100m), State(July, 0m, 100m)]),
            History("M2", "C2", states: [State(June, 0m, 100m), State(July, 1m, 90m)]),
            History("M3", "C3", states: [State(June, 0m, 0m), State(July, 0m, 50m)])
        };

        var contracts = _analyzer.Analyze(histories, June, July).Contracts;

        Assert.Contains(contracts, x => x.Contract.ContractNumber == "C1" && x.MovementCategory == DebtMovementCategory.NewDelinquency);
        Assert.Contains(contracts, x => x.Contract.ContractNumber == "C2" && x.MovementCategory == DebtMovementCategory.Improved);
        Assert.Contains(contracts, x => x.Contract.ContractNumber == "C3" && x.MovementCategory == DebtMovementCategory.NewEnteredRisk);
    }

    [Fact]
    public void PaidOffTransition_IsClosedAndRetainsPaymentEvent()
    {
        var current = History(states: [State(June, 0m, 100m), State(July, 25m, 0m)]);

        var result = Assert.Single(_analyzer.Analyze([current], June, July).Contracts);

        Assert.Equal(DebtBucket.PaidOff, result.CurrentClassification.Bucket);
        Assert.Equal(MonthlyPaymentStatus.Paid, result.CurrentClassification.LatestPaymentStatus);
        Assert.Equal(DebtMovementCategory.ClosedPaidOff, result.MovementCategory);
    }

    [Fact]
    public void BucketComparison_AttributesPaidOffExitToTheStartingBucket()
    {
        var current = History(states: [State(June, 0m, 100m), State(July, 25m, 0m)]);

        var result = _analyzer.Analyze([current], June, July);

        Assert.Equal(1, result.BucketComparisons.Single(x => x.Bucket == DebtBucket.OneMonthDelinquent).PaidOffContracts);
        Assert.Equal(0, result.BucketComparisons.Single(x => x.Bucket == DebtBucket.PaidOff).PaidOffContracts);
    }

    [Fact]
    public void Analyzer_AllowsAReplacementPolicyWithoutChangingSourceFacts()
    {
        var history = History(states: [State(June, 0m, 100m), State(July, 0m, 100m)]);
        var analyzer = new DebtSegmentationAnalyzer(new TestPolicy());

        var classification = analyzer.Classify(history, July);

        Assert.Equal("test-policy", analyzer.PolicyVersion);
        Assert.Equal(DebtBucket.ThreeToSixMonthsDelinquent, classification.Bucket);
        Assert.Equal(2, classification.ConsecutiveMissedMonths);
    }

    [Fact]
    public void NoJulyPayment_DoesNotAutomaticallyMeanOneMonthDelinquent()
    {
        var expired = History(
            expireDate: new DateOnly(2010, 1, 1),
            states: [State(June, 0m, 100m), State(July, 0m, 100m)]);

        var classification = _analyzer.Classify(expired, July);

        Assert.Equal(MonthlyPaymentStatus.NotPaid, classification.LatestPaymentStatus);
        Assert.Equal(DebtBucket.TenYearsAndAbove, classification.Bucket);
    }

    [Fact]
    public void OriginationMonth_WithActualPayment_PreservesFactButRemainsNotDueAndNormal()
    {
        var history = History(
            contractDate: new DateOnly(2026, 7, 10),
            states: [State(July, 100m, 1_000m)]);

        var classification = _analyzer.Classify(history, July);

        Assert.Equal(100m, history.MonthlyStates[July].PaymentAmount);
        Assert.True(history.MonthlyStates[July].HasActualPayment);
        Assert.Equal(MonthlyPaymentStatus.NotDue, classification.LatestPaymentStatus);
        Assert.Equal(DebtBucket.Normal, classification.Bucket);
    }

    [Fact]
    public void FirstDueMonth_WithNoPayment_IsOneMonthDelinquent()
    {
        var history = History(
            contractDate: new DateOnly(2026, 6, 15),
            states: [State(June, 0m, 1_000m), State(July, 0m, 1_000m)]);

        var june = _analyzer.Classify(history, June);
        var july = _analyzer.Classify(history, July);

        Assert.Equal(MonthlyPaymentStatus.NotDue, june.LatestPaymentStatus);
        Assert.Equal(DebtBucket.Normal, june.Bucket);
        Assert.Equal(MonthlyPaymentStatus.NotPaid, july.LatestPaymentStatus);
        Assert.Equal(DebtBucket.OneMonthDelinquent, july.Bucket);
        Assert.Equal(July, july.FirstMissedPaymentPeriod);
        Assert.Equal(1, july.ConsecutiveMissedMonths);
    }

    [Fact]
    public void FirstDueMonth_WithAnyPayment_IsPaidAndNormal()
    {
        var history = History(
            contractDate: new DateOnly(2026, 6, 15),
            states: [State(June, 0m, 1_000m), State(July, 0.01m, 999.99m)]);

        var result = Assert.Single(_analyzer.Analyze([history], June, July).Contracts);

        Assert.Equal("Not due -> Paid", result.PaymentMovement);
        Assert.Equal(MonthlyPaymentStatus.Paid, result.CurrentClassification.LatestPaymentStatus);
        Assert.Equal(DebtBucket.Normal, result.CurrentClassification.Bucket);
    }

    [Fact]
    public void ConsecutiveDueMisses_AdvanceFromOneToTwoMonths()
    {
        var history = History(
            contractDate: new DateOnly(2026, 4, 1),
            states: [State(May, 1m, 1_000m), State(June, 0m, 1_000m), State(July, 0m, 1_000m)]);

        var result = Assert.Single(_analyzer.Analyze([history], June, July).Contracts);

        Assert.Equal("Not paid -> Not paid", result.PaymentMovement);
        Assert.Equal(DebtBucket.OneMonthDelinquent, result.PreviousClassification.Bucket);
        Assert.Equal(DebtBucket.TwoMonthsDelinquent, result.CurrentClassification.Bucket);
        Assert.Equal(DebtMovementCategory.Worsened, result.MovementCategory);
    }

    [Fact]
    public void DuePayment_AfterDelinquency_ReturnsShortTermDebtToNormal()
    {
        var history = History(
            contractDate: new DateOnly(2026, 4, 1),
            states: [State(June, 0m, 1_000m), State(July, 100m, 900m)]);

        var result = Assert.Single(_analyzer.Analyze([history], June, July).Contracts);

        Assert.Equal("Not paid -> Paid", result.PaymentMovement);
        Assert.Equal(DebtBucket.OneMonthDelinquent, result.PreviousClassification.Bucket);
        Assert.Equal(DebtBucket.Normal, result.CurrentClassification.Bucket);
        Assert.Equal(DebtMovementCategory.Improved, result.MovementCategory);
    }

    [Fact]
    public void JulyOriginatedContract_IsNewContractNotNewDelinquency()
    {
        var history = History(
            contractDate: new DateOnly(2026, 7, 5),
            states: [State(June, 0m, 0m), State(July, 0m, 1_000m)]);

        var result = Assert.Single(_analyzer.Analyze([history], June, July).Contracts);

        Assert.True(result.IsNewContract);
        Assert.Equal("New Contract", result.PaymentMovement);
        Assert.Equal(MonthlyPaymentStatus.NotDue, result.CurrentClassification.LatestPaymentStatus);
        Assert.Equal(DebtBucket.Normal, result.CurrentClassification.Bucket);
        Assert.Equal(DebtMovementCategory.NewContract, result.MovementCategory);
        Assert.NotEqual(DebtMovementCategory.NewDelinquency, result.MovementCategory);
    }

    [Fact]
    public void NormalToFirstMiss_IsExplicitNewDelinquency()
    {
        var history = History(
            contractDate: new DateOnly(2026, 4, 1),
            states: [State(June, 100m, 1_000m), State(July, 0m, 1_000m)]);

        var result = Assert.Single(_analyzer.Analyze([history], June, July).Contracts);

        Assert.False(result.IsNewContract);
        Assert.Equal("Paid -> Not paid", result.PaymentMovement);
        Assert.Equal(DebtBucket.Normal, result.PreviousClassification.Bucket);
        Assert.Equal(DebtBucket.OneMonthDelinquent, result.CurrentClassification.Bucket);
        Assert.Equal(DebtMovementCategory.NewDelinquency, result.MovementCategory);
    }

    [Fact]
    public void PaidOffWithoutLaterPayment_UsesClosedStatusInsteadOfFalseNotPaidAlert()
    {
        var history = History(states: [State(June, 25m, 0m), State(July, 0m, 0m)]);

        var result = Assert.Single(_analyzer.Analyze([history], June, July).Contracts);

        Assert.Equal(DebtBucket.PaidOff, result.CurrentClassification.Bucket);
        Assert.Equal(MonthlyPaymentStatus.Closed, result.CurrentClassification.LatestPaymentStatus);
        Assert.Equal("Paid -> Closed / paid off", result.PaymentMovement);
        Assert.DoesNotContain("Not paid", result.PaymentMovement, StringComparison.Ordinal);
    }

    [Fact]
    public void TenYearDebt_PaymentRecoveryDoesNotReplaceLongTermDebtBucket()
    {
        var history = History(
            contractDate: new DateOnly(2000, 1, 1),
            expireDate: new DateOnly(2010, 1, 1),
            states: [State(June, 0m, 80_000m), State(July, 100m, 75_000m)]);

        var result = Assert.Single(_analyzer.Analyze([history], June, July).Contracts);

        Assert.Equal("Not paid -> Paid", result.PaymentMovement);
        Assert.Equal(DebtBucket.TenYearsAndAbove, result.PreviousClassification.Bucket);
        Assert.Equal(DebtBucket.TenYearsAndAbove, result.CurrentClassification.Bucket);
        Assert.Equal(DebtMovementCategory.SameBucketBalanceDecreased, result.MovementCategory);
        Assert.Equal(-5_000m, result.CurrentState.TotalOutstanding - result.PreviousState.TotalOutstanding);
    }

    private static DebtMonthlyState State(DateOnly period, decimal payment, decimal balance) =>
        new(period, payment, balance, 0m, balance);

    private static DebtContractHistory History(
        string memberCode = "M001",
        string contractNumber = "C001",
        DateOnly? expireDate = null,
        IReadOnlyList<DebtMonthlyState>? states = null,
        DateOnly? contractDate = null) => new()
        {
            MemberCode = memberCode,
            ContractNumber = contractNumber,
            ContractDate = contractDate,
            ExpireDate = expireDate,
            MonthlyStates = (states ?? []).ToDictionary(x => x.Period)
        };

    private sealed class TestPolicy : IDebtSegmentationPolicy
    {
        public string Version => "test-policy";
        public IReadOnlyList<DebtBucket> OrderedBuckets => new DebtSegmentationPolicyV1().OrderedBuckets;
        public DateOnly? FirstDuePeriod(DateOnly? contractDate) =>
            new DebtSegmentationPolicyV1().FirstDuePeriod(contractDate);
        public DebtBucket ClassifyConsecutiveMisses(int missedMonths) => DebtBucket.ThreeToSixMonthsDelinquent;
        public DebtBucket? ClassifyExpiredDebt(DateOnly? expireDate, DateOnly asOfDate) => null;
        public int Rank(DebtBucket bucket) => new DebtSegmentationPolicyV1().Rank(bucket);
    }
}
