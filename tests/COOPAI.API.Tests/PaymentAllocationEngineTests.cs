using COOPAI.API.Services.DebtSegmentation;

namespace COOPAI.API.Tests;

public sealed class PaymentAllocationEngineTests
{
    private static readonly DateOnly Current = new(2026, 8, 1);
    private static readonly DebtSegmentationAnalyzer Analyzer = new();

    [Fact]
    public void PriorAdvanceCredit_PaysPriorArrearsBeforeCurrentDueAndNeverGoesNegative()
    {
        var result = PaymentAllocationEngine.ApplyAdvanceCredit(150m, 100m, 100m);

        Assert.Equal(100m, result.AppliedToPriorArrears);
        Assert.Equal(50m, result.AppliedToCurrentDue);
        Assert.Equal(0m, result.RemainingAdvanceCredit);
        Assert.Equal(0m, result.RemainingPriorArrears);
        Assert.Equal(50m, result.RemainingCurrentDue);
        Assert.All(new[]
        {
            result.AppliedToPriorArrears, result.AppliedToCurrentDue, result.RemainingAdvanceCredit,
            result.RemainingPriorArrears, result.RemainingCurrentDue
        }, value => Assert.True(value >= 0m));
    }

    [Fact]
    public void KnownPriorArrears_ArePaidBeforeCurrentDueAndTrueExcess()
    {
        var history = History("สห-2569-000444", new DateOnly(2026, 5, 10), new DateOnly(2027, 11, 10),
            (5, 0m, 23_600m), (6, 0m, 23_600m), (7, 0m, 23_600m), (8, 4_000m, 19_600m));

        var result = Allocate(history, Installment(18, 1_311m, history.ContractNumber));

        Assert.Equal(2_622m, result.PriorArrears);
        Assert.Equal(2_622m, result.PaymentToPriorArrears);
        Assert.Equal(1_311m, result.PaymentToCurrentDue);
        Assert.Equal(67m, result.ExcessPayment);
        Assert.Equal(0m, result.UnclassifiedPaymentAmount);
        AssertReconciles(result);
    }

    [Fact]
    public void NotDueSj_UsesAeThenAdvanceWithoutOverlap()
    {
        var history = History("สจ-2569-000999", new DateOnly(2026, 8, 15), new DateOnly(2027, 8, 15),
            (7, 0m, 1_000m), (8, 800m, 200m));
        var installment = Installment(12, 100m, history.ContractNumber) with
        {
            DownPayment = 600m,
            DownPaymentDataStatus = DownPaymentDataStatuses.Available
        };

        var result = Allocate(history, installment);

        Assert.Equal(600m, result.DownPaymentAmount);
        Assert.Equal(200m, result.AdvancePayment);
        Assert.Equal(0m, result.PaymentToCurrentDue);
        AssertReconciles(result);
    }

    [Fact]
    public void NotDueSj_MissingAeFailsClosedAsUnclassified()
    {
        var history = History("สจ-2569-000998", new DateOnly(2026, 8, 15), new DateOnly(2027, 8, 15),
            (7, 0m, 1_000m), (8, 800m, 200m));

        var result = Allocate(history, null);

        Assert.Equal(0m, result.DownPaymentAmount);
        Assert.Equal(0m, result.AdvancePayment);
        Assert.Equal(800m, result.UnclassifiedPaymentAmount);
        Assert.Equal(PaymentAllocationStatuses.UnresolvedDownPaymentEvidence, result.PaymentAllocationStatus);
        AssertReconciles(result);
    }

    [Fact]
    public void NotDueNonSj_IsAdvance()
    {
        var history = History("สห-2569-000680", new DateOnly(2026, 8, 3), new DateOnly(2027, 8, 3),
            (7, 0m, 15_960m), (8, 1_400m, 14_560m));

        var result = Allocate(history, null);

        Assert.Equal(1_400m, result.AdvancePayment);
        Assert.Equal(0m, result.PaymentToCurrentDue);
        Assert.Equal(0m, result.ExcessPayment);
        AssertReconciles(result);
    }

    [Fact]
    public void InTermBalanceClosure_IsEarlyPayoffAndNeverExcess()
    {
        var history = History("สห-2566-001083", new DateOnly(2023, 12, 25), new DateOnly(2028, 12, 25),
            (7, 0m, 135_600m), (8, 135_600m, 0m));

        var result = Allocate(history, Installment(60, 4_050m, history.ContractNumber));

        Assert.True(result.IsEarlyPayoff);
        Assert.Equal(135_600m, result.EarlyPayoffAmount);
        Assert.Equal(0m, result.PaymentToCurrentDue);
        Assert.Equal(0m, result.ExcessPayment);
        AssertReconciles(result);
    }

    [Fact]
    public void ExpiredBalanceClosure_IsSeparateFromEarlyPayoff()
    {
        var history = History("C-EXPIRED", new DateOnly(2020, 1, 1), new DateOnly(2026, 7, 1),
            (7, 0m, 500m), (8, 500m, 0m));

        var result = Allocate(history, null);

        Assert.False(result.IsEarlyPayoff);
        Assert.True(result.IsExpiredPayoff);
        Assert.Equal(500m, result.ExpiredPayoffAmount);
        AssertReconciles(result);
    }

    [Fact]
    public void HistoricalOverpayment_CarriesForwardAndResolvesPriorCreditPolicy()
    {
        var history = History("C-POLICY", new DateOnly(2026, 1, 10), new DateOnly(2027, 1, 10),
            (1, 0m, 1_000m), (2, 150m, 850m), (3, 0m, 850m), (4, 0m, 850m),
            (5, 0m, 850m), (6, 0m, 850m), (7, 0m, 850m), (8, 200m, 650m));

        var result = Allocate(history, Installment(12, 100m, history.ContractNumber));

        Assert.Equal(PriorArrearsStatuses.Known, result.PriorArrearsStatus);
        Assert.Equal(450m, result.PriorArrears);
        Assert.Null(result.PriorCreditPolicyPeriod);
        Assert.Equal(0m, result.PriorCreditPolicyResidualAmount);
        Assert.Equal(200m, result.PaymentToPriorArrears);
        Assert.Equal(0m, result.UnclassifiedPaymentAmount);
        Assert.Equal(0m, result.ExcessPayment);
        AssertReconciles(result);
    }

    [Fact]
    public void CarryForwardOneMonth_ExactlyOffsetsNextInstallment()
    {
        var history = History("C-EXACT", new DateOnly(2026, 6, 10), new DateOnly(2027, 6, 10),
            (6, 0m, 1_000m), (7, 200m, 800m), (8, 0m, 800m));

        var result = Allocate(history, Installment(12, 100m, history.ContractNumber));

        Assert.Equal(100m, result.PriorAdvanceCredit);
        Assert.Equal(100m, result.AdvanceCreditAppliedToCurrentDue);
        Assert.Equal(0m, result.RemainingCurrentDueForCash);
        Assert.Equal(0m, result.EndingArrears);
        Assert.Equal(0m, result.EndingAdvanceCredit);
        AssertReconciles(result);
    }

    [Fact]
    public void CarryForwardPartialCredit_ReducesCashNeededWithoutChangingAmountDue()
    {
        var history = History("C-PARTIAL", new DateOnly(2026, 6, 10), new DateOnly(2027, 6, 10),
            (6, 0m, 1_000m), (7, 150m, 850m), (8, 50m, 800m));

        var result = Allocate(history, Installment(12, 100m, history.ContractNumber));

        Assert.Equal(100m, result.AmountDue);
        Assert.Equal(50m, result.PriorAdvanceCredit);
        Assert.Equal(50m, result.AdvanceCreditAppliedToCurrentDue);
        Assert.Equal(50m, result.PaymentToCurrentDue);
        Assert.Equal(0m, result.EndingArrears);
        AssertReconciles(result);
    }

    [Fact]
    public void CreditGreaterThanInstallment_CarriesRemainingCreditForwardAgain()
    {
        var history = History("C-GREATER", new DateOnly(2026, 6, 10), new DateOnly(2027, 6, 10),
            (6, 0m, 1_000m), (7, 250m, 750m), (8, 0m, 750m));

        var result = Allocate(history, Installment(12, 100m, history.ContractNumber));

        Assert.Equal(150m, result.PriorAdvanceCredit);
        Assert.Equal(100m, result.AdvanceCreditApplied);
        Assert.Equal(50m, result.EndingAdvanceCredit);
        Assert.Equal(0m, result.EndingArrears);
        AssertReconciles(result);
    }

    [Fact]
    public void CreditSurvivesAcrossMultipleFutureMonths()
    {
        var history = History("C-MULTI", new DateOnly(2026, 4, 10), new DateOnly(2027, 4, 10),
            (4, 0m, 1_000m), (5, 350m, 650m), (6, 0m, 650m), (7, 0m, 650m), (8, 0m, 650m));

        var result = Allocate(history, Installment(12, 100m, history.ContractNumber));

        Assert.Equal(50m, result.PriorAdvanceCredit);
        Assert.Equal(50m, result.AdvanceCreditAppliedToCurrentDue);
        Assert.Equal(50m, result.EndingArrears);
        Assert.Equal(0m, result.EndingAdvanceCredit);
        AssertReconciles(result);
    }

    [Fact]
    public void ExistingCreditAndNewPayment_CanGenerateNewEndingCredit()
    {
        var history = History("C-CREDIT-CASH", new DateOnly(2026, 6, 10), new DateOnly(2027, 6, 10),
            (6, 0m, 1_000m), (7, 150m, 850m), (8, 75m, 775m));

        var result = Allocate(history, Installment(12, 100m, history.ContractNumber));

        Assert.Equal(50m, result.AdvanceCreditAppliedToCurrentDue);
        Assert.Equal(50m, result.PaymentToCurrentDue);
        Assert.Equal(25m, result.NewAdvanceCredit);
        Assert.Equal(25m, result.EndingAdvanceCredit);
        Assert.Equal(25m, result.ExcessPayment);
        AssertReconciles(result);
    }

    [Fact]
    public void AdvanceCreditIsConsumedBeforeAReplacementArrearIsCreated()
    {
        var history = History("C-ARREARS-CREDIT", new DateOnly(2026, 5, 10), new DateOnly(2027, 5, 10),
            (5, 0m, 1_000m), (6, 250m, 750m), (7, 0m, 750m), (8, 0m, 750m));

        var result = Allocate(history, Installment(12, 100m, history.ContractNumber));

        Assert.Equal(50m, result.PriorAdvanceCredit);
        Assert.Equal(50m, result.AdvanceCreditAppliedToCurrentDue);
        Assert.Equal(50m, result.EndingArrears);
        Assert.Equal(0m, result.EndingAdvanceCredit);
        AssertReconciles(result);
    }

    [Fact]
    public void PayoffWithPriorCredit_DoesNotCreateNewAdvanceCredit()
    {
        var history = History("C-PAYOFF-CREDIT", new DateOnly(2026, 6, 10), new DateOnly(2027, 6, 10),
            (6, 0m, 1_000m), (7, 150m, 850m), (8, 850m, 0m));

        var result = Allocate(history, Installment(12, 100m, history.ContractNumber));

        Assert.True(result.IsEarlyPayoff);
        Assert.Equal(850m, result.EarlyPayoffAmount);
        Assert.Equal(50m, result.AdvanceCreditApplied);
        Assert.Equal(0m, result.NewAdvanceCredit);
        Assert.Equal(0m, result.EndingAdvanceCredit);
        AssertReconciles(result);
    }

    [Fact]
    public void AllocationNeverNetsAdvanceCreditAcrossContracts()
    {
        var creditHistory = History("C-CREDIT-ONLY", new DateOnly(2026, 6, 10), new DateOnly(2027, 6, 10),
            (6, 0m, 1_000m), (7, 200m, 800m), (8, 0m, 800m));
        var arrearsHistory = History("C-ARREARS-ONLY", new DateOnly(2026, 6, 10), new DateOnly(2027, 6, 10),
            (6, 0m, 1_000m), (7, 0m, 1_000m), (8, 0m, 1_000m));

        var credit = Allocate(creditHistory, Installment(12, 100m, creditHistory.ContractNumber));
        var arrears = Allocate(arrearsHistory, Installment(12, 100m, arrearsHistory.ContractNumber));

        Assert.Equal(100m, credit.AdvanceCreditApplied);
        Assert.Equal(0m, arrears.AdvanceCreditApplied);
        Assert.Equal(200m, arrears.EndingArrears);
        AssertReconciles(credit);
        AssertReconciles(arrears);
    }

    [Fact]
    public void MissingHistory_RemainsUnknownAndDoesNotFabricateCredit()
    {
        var history = History("C-MISSING-HISTORY", new DateOnly(2025, 1, 10), new DateOnly(2027, 1, 10),
            (7, 200m, 800m), (8, 100m, 700m));

        var result = Allocate(history, Installment(24, 100m, history.ContractNumber) with
        {
            ContractualObligation = null
        });

        Assert.Equal(PriorArrearsStatuses.UnknownHistoryBeforeAvailablePeriod, result.PriorArrearsStatus);
        Assert.Null(result.PriorArrears);
        Assert.Equal(0m, result.PriorAdvanceCredit);
        Assert.Equal(100m, result.UnclassifiedPaymentAmount);
        AssertReconciles(result);
    }

    [Fact]
    public void MissingHistory_WithAh_ReconstructsPriorArrearsFromOpeningBalance()
    {
        var history = History("C-RECONSTRUCT", new DateOnly(2025, 1, 10), new DateOnly(2027, 1, 10),
            (7, 0m, 800m), (8, 100m, 700m));

        var result = Allocate(history, Installment(24, 100m, history.ContractNumber));

        Assert.Equal(PriorArrearsStatuses.Known, result.PriorArrearsStatus);
        Assert.Equal(200m, result.PriorArrears);
        Assert.Equal(100m, result.PaymentToPriorArrears);
        Assert.Equal(200m, result.EndingArrears);
        Assert.Equal(0m, result.UnclassifiedPaymentAmount);
        Assert.Contains("reconstructed", result.PaymentAllocationWarning, StringComparison.OrdinalIgnoreCase);
        AssertReconciles(result);
    }

    [Fact]
    public void JanuaryFirstDue_ReconstructsWithoutDecemberFacts()
    {
        var history = History("C-JAN-FIRST-DUE", new DateOnly(2025, 12, 10), new DateOnly(2026, 12, 10),
            (1, 100m, 1_100m), (2, 100m, 1_000m), (3, 100m, 900m), (4, 100m, 800m),
            (5, 100m, 700m), (6, 100m, 600m), (7, 100m, 500m), (8, 100m, 400m));

        var result = Allocate(history, Installment(12, 100m, history.ContractNumber));

        Assert.Equal(PriorArrearsStatuses.Known, result.PriorArrearsStatus);
        Assert.Equal(0m, result.PriorArrears);
        Assert.Equal(0m, result.PriorAdvanceCredit);
        Assert.Equal(100m, result.PaymentToCurrentDue);
        Assert.Equal(0m, result.EndingArrears);
        AssertReconciles(result);
    }

    [Fact]
    public void ExactPayoffWithMissingHistory_IsTerminalNotDataReview()
    {
        var history = History("C-OLD-PAYOFF", new DateOnly(2025, 1, 10), new DateOnly(2027, 1, 10),
            (7, 0m, 800m), (8, 800m, 0m));

        var result = Allocate(history, Installment(24, 100m, history.ContractNumber));

        Assert.Equal(PriorArrearsStatuses.NotApplicable, result.PriorArrearsStatus);
        Assert.True(result.IsEarlyPayoff);
        Assert.Equal(0m, result.EndingArrears);
        Assert.Equal(0m, result.UnclassifiedPaymentAmount);
        AssertReconciles(result);
    }

    [Fact]
    public void LaterContractualIncrease_FailsClosedAndKeepsUnknown()
    {
        var history = History("C-INCREASE", new DateOnly(2025, 1, 10), new DateOnly(2027, 1, 10),
            (7, 0m, 800m), (8, 100m, 900m));

        var result = Allocate(history, Installment(24, 100m, history.ContractNumber));

        Assert.Equal(PriorArrearsStatuses.UnknownHistoryBeforeAvailablePeriod, result.PriorArrearsStatus);
        Assert.Null(result.PriorArrears);
        Assert.Equal(100m, result.UnclassifiedPaymentAmount);
        Assert.Contains("unsupported contractual-obligation increase", result.PaymentAllocationWarning,
            StringComparison.Ordinal);
        AssertReconciles(result);
    }

    [Fact]
    public void AllocationDoesNotChangeAmountDueDebtBucketOrRemainingMonths()
    {
        var history = History("C-STABLE", new DateOnly(2026, 5, 1), new DateOnly(2027, 8, 1),
            (5, 0m, 1_000m), (6, 0m, 1_000m), (7, 0m, 1_000m), (8, 250m, 750m));
        var installment = Installment(12, 100m, history.ContractNumber);
        var debt = Analyzer.Analyze([history], Current.AddMonths(-1), Current).Contracts.Single();
        var before = MonthlyAmountDueService.Calculate(debt, Current, installment);

        var after = PaymentAllocationEngine.Allocate(debt, before, installment, Analyzer);

        Assert.Equal(before.AmountDue, after.AmountDue);
        Assert.Equal(before.DebtBucket, after.DebtBucket);
        Assert.Equal(before.RemainingOrOverdueMonths, after.RemainingOrOverdueMonths);
        Assert.Equal(50m, after.Shortfall);
        AssertReconciles(after);
    }

    private static MonthlyAmountDueContract Allocate(
        DebtContractHistory history,
        InstallmentMasterContract? installment)
    {
        var debt = Analyzer.Analyze([history], Current.AddMonths(-1), Current).Contracts.Single();
        var due = MonthlyAmountDueService.Calculate(debt, Current, installment);
        return PaymentAllocationEngine.Allocate(debt, due, installment, Analyzer);
    }

    private static void AssertReconciles(MonthlyAmountDueContract result)
    {
        var cashComponents = new[]
        {
            result.DownPaymentAmount, result.PaymentToPriorArrears,
            result.PaymentToCurrentDue, result.NewAdvanceCredit, result.EarlyPayoffAmount,
            result.ExpiredPayoffAmount, result.UnclassifiedPaymentAmount
        };
        var ledgerOutputs = new[]
        {
            result.AdvanceCreditApplied, result.DownPaymentAmount, result.PaymentToPriorArrears,
            result.PaymentToCurrentDue, result.EarlyPayoffAmount, result.ExpiredPayoffAmount,
            result.UnclassifiedPaymentAmount, result.EndingAdvanceCredit
        };
        Assert.All(cashComponents.Concat(ledgerOutputs), value => Assert.True(value >= 0m));
        Assert.Equal(result.ActualPayment, cashComponents.Sum());
        Assert.Equal(result.PriorAdvanceCredit + result.ActualPayment, ledgerOutputs.Sum());
    }

    private static InstallmentMasterContract Installment(int total, decimal monthly, string contract) =>
        new(contract, total, monthly, InstallmentValidationStatuses.Usable,
            InstallmentDuplicateStatuses.None, [], 2)
        {
            DownPayment = 0m,
            DownPaymentDataStatus = DownPaymentDataStatuses.Available,
            ContractualObligation = total * monthly
        };

    private static DebtContractHistory History(
        string contract,
        DateOnly start,
        DateOnly? expire,
        params (int Month, decimal Payment, decimal Ending)[] facts) => new()
    {
        ContractNumber = contract,
        MemberCode = contract,
        ContractDate = start,
        ExpireDate = expire,
        MonthlyStates = facts.ToDictionary(
            fact => new DateOnly(2026, fact.Month, 1),
            fact => new DebtMonthlyState(new DateOnly(2026, fact.Month, 1),
                fact.Payment, fact.Ending, 0m, fact.Ending))
    };
}
