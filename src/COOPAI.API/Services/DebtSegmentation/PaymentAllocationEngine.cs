using System.Globalization;

namespace COOPAI.API.Services.DebtSegmentation;

/// <summary>
/// Preview-only derived payment allocation. Payment left after a month's obligations
/// is carried as contract-specific advance credit and applied oldest-obligation first.
/// Contractual AmountDue is never rewritten by this ledger.
/// </summary>
public static class PaymentAllocationEngine
{
    public static MonthlyAmountDueContract Allocate(
        DebtContractAnalysis debt,
        MonthlyAmountDueContract due,
        InstallmentMasterContract? installment,
        DebtSegmentationAnalyzer analyzer)
    {
        var prior = DerivePriorBalances(debt, installment, analyzer, due.CurrentPeriod);
        var actual = due.ActualPayment;
        var isNotDueSj = IsSjContract(debt.Contract.ContractNumber) &&
            due.DueBasis == AmountDueBases.NotDue;
        var downPaymentAvailable = installment?.DownPaymentDataStatus == DownPaymentDataStatuses.Available &&
            installment.DownPayment is >= 0m;

        var beginningArrears = prior.Arrears ?? 0m;
        var beginningCredit = prior.AdvanceCredit;
        var creditApplication = prior.Status == PriorArrearsStatuses.Known
            ? ApplyAdvanceCredit(beginningCredit, beginningArrears, due.AmountDue ?? 0m)
            : new AdvanceCreditApplication(0m, 0m, beginningCredit, beginningArrears, due.AmountDue ?? 0m);
        var creditToPrior = creditApplication.AppliedToPriorArrears;
        var creditToCurrent = due.AmountDue.HasValue ? creditApplication.AppliedToCurrentDue : 0m;
        var remainingPrior = creditApplication.RemainingPriorArrears;
        var remainingCurrent = due.AmountDue.HasValue ? creditApplication.RemainingCurrentDue : 0m;
        decimal? remainingCurrentForCash = due.AmountDue.HasValue ? remainingCurrent : null;

        var creditApplied = creditToPrior + creditToCurrent;
        decimal downPayment = 0m, legacyAdvance = 0m, toPrior = 0m, toCurrent = 0m;
        decimal legacyExcess = 0m, newAdvance = 0m, earlyPayoff = 0m, expiredPayoff = 0m, unclassified = 0m;
        var allocationStatus = PaymentAllocationStatuses.Allocated;
        var allocationWarning = prior.Warning;

        if (actual > 0m)
        {
            if (due.DueBasis == AmountDueBases.NotDue)
            {
                if (isNotDueSj && !downPaymentAvailable)
                {
                    unclassified = actual;
                    allocationStatus = PaymentAllocationStatuses.UnresolvedDownPaymentEvidence;
                    allocationWarning = "Source B AE is unavailable; DownPayment must not be guessed.";
                }
                else
                {
                    if (isNotDueSj)
                        downPayment = Math.Min(actual, prior.RemainingDownPayment);
                    newAdvance = actual - downPayment;
                    legacyAdvance = newAdvance;
                }
            }
            else if (IsInTermPayoff(due))
            {
                earlyPayoff = actual;
                allocationStatus = PaymentAllocationStatuses.EarlyPayoff;
            }
            else if (IsExpiredPayoff(due))
            {
                expiredPayoff = actual;
                allocationStatus = PaymentAllocationStatuses.ExpiredPayoff;
            }
            else if (due.DueBasis == AmountDueBases.ExpiredFullBalance && due.AmountDue.HasValue)
            {
                toCurrent = Math.Min(actual, remainingCurrent);
                remainingCurrent -= toCurrent;
                unclassified = actual - toCurrent;
                if (unclassified > 0m)
                    allocationStatus = PaymentAllocationStatuses.Unclassified;
            }
            else if (!due.AmountDue.HasValue || due.DueBasis == AmountDueBases.PaidOff)
            {
                unclassified = actual;
                allocationStatus = PaymentAllocationStatuses.Unclassified;
            }
            else if (prior.Status == PriorArrearsStatuses.Known)
            {
                toPrior = Math.Min(actual, remainingPrior);
                remainingPrior -= toPrior;
                var cashAfterPrior = actual - toPrior;
                toCurrent = Math.Min(cashAfterPrior, remainingCurrent);
                remainingCurrent -= toCurrent;
                newAdvance = cashAfterPrior - toCurrent;
                legacyExcess = newAdvance;
            }
            else
            {
                unclassified = actual;
                allocationStatus = prior.Status;
            }
        }

        var endingCredit = beginningCredit - creditApplied + newAdvance;
        decimal? endingArrears = IsInTermPayoff(due) || IsExpiredPayoff(due)
            ? 0m
            : prior.Status == PriorArrearsStatuses.Known
                ? remainingPrior + remainingCurrent
                : null;
        decimal? effectiveShortfall = due.AmountDue.HasValue
            ? IsInTermPayoff(due) || IsExpiredPayoff(due) ? 0m
                : prior.Status == PriorArrearsStatuses.Known || due.DueBasis == AmountDueBases.ExpiredFullBalance
                    ? remainingCurrent
                    : due.Shortfall
            : null;

        var cashAllocated = downPayment + toPrior + toCurrent + newAdvance +
            earlyPayoff + expiredPayoff + unclassified;
        var ledgerAllocated = creditApplied + downPayment + toPrior + toCurrent +
            earlyPayoff + expiredPayoff + unclassified + endingCredit;
        if (cashAllocated != actual)
            throw new InvalidOperationException(
                $"Current-period cash allocation failed to reconcile for {due.ContractNumber}.");
        if (beginningCredit + actual != ledgerAllocated)
            throw new InvalidOperationException(
                $"Advance-credit ledger failed to reconcile for {due.ContractNumber}.");
        if (new[] { beginningCredit, creditToPrior, creditToCurrent, newAdvance, endingCredit }.Any(x => x < 0m))
            throw new InvalidOperationException(
                $"Advance-credit ledger produced a negative value for {due.ContractNumber}.");

        var warnings = due.Warnings;
        if (!string.IsNullOrWhiteSpace(allocationWarning))
            warnings = warnings.Append(allocationWarning).Distinct(StringComparer.Ordinal).ToArray();
        return due with
        {
            DownPaymentSourceAmount = installment?.DownPayment,
            DownPaymentDataStatus = installment?.DownPaymentDataStatus ?? DownPaymentDataStatuses.Missing,
            DownPaymentAmount = downPayment,
            AdvancePayment = legacyAdvance,
            PriorArrears = prior.Arrears,
            PriorArrearsStatus = prior.Status,
            PaymentToPriorArrears = toPrior,
            PaymentToCurrentDue = toCurrent,
            Shortfall = effectiveShortfall,
            ExcessPayment = legacyExcess,
            IsEarlyPayoff = earlyPayoff > 0m,
            EarlyPayoffAmount = earlyPayoff,
            IsExpiredPayoff = expiredPayoff > 0m,
            ExpiredPayoffAmount = expiredPayoff,
            UnclassifiedPaymentAmount = unclassified,
            PaymentAllocationStatus = allocationStatus,
            PaymentAllocationWarning = allocationWarning,
            PriorCreditPolicyPeriod = null,
            PriorCreditPolicyObligation = null,
            PriorCreditPolicyActualPayment = null,
            PriorCreditPolicyResidualAmount = 0m,
            PriorAdvanceCredit = beginningCredit,
            AdvanceCreditAppliedToPriorArrears = creditToPrior,
            AdvanceCreditAppliedToCurrentDue = creditToCurrent,
            AdvanceCreditApplied = creditApplied,
            RemainingCurrentDueForCash = remainingCurrentForCash,
            NewAdvanceCredit = newAdvance,
            EndingAdvanceCredit = endingCredit,
            EndingArrears = endingArrears,
            Warnings = warnings
        };
    }

    private static bool IsInTermPayoff(MonthlyAmountDueContract due) =>
        (due.DueBasis is AmountDueBases.ActiveInstallment or AmountDueBases.FinalInstallment or
            AmountDueBases.InstallmentsElapsedFullBalance) &&
        due.OpeningOutstanding > 0m && due.EndingOutstanding == 0m &&
        due.ActualPayment == due.OpeningOutstanding;

    private static bool IsExpiredPayoff(MonthlyAmountDueContract due) =>
        due.DueBasis == AmountDueBases.ExpiredFullBalance &&
        due.OpeningOutstanding > 0m && due.EndingOutstanding == 0m &&
        due.ActualPayment == due.OpeningOutstanding;

    private static bool IsSjContract(string contractNumber) =>
        contractNumber.StartsWith("สจ-", StringComparison.OrdinalIgnoreCase);

    internal static AdvanceCreditApplication ApplyAdvanceCredit(
        decimal priorAdvanceCredit,
        decimal priorArrears,
        decimal currentAmountDue)
    {
        if (priorAdvanceCredit < 0m || priorArrears < 0m || currentAmountDue < 0m)
            throw new ArgumentOutOfRangeException(nameof(priorAdvanceCredit),
                "Advance credit and obligations must not be negative.");
        var toPrior = Math.Min(priorAdvanceCredit, priorArrears);
        var creditAfterPrior = priorAdvanceCredit - toPrior;
        var toCurrent = Math.Min(creditAfterPrior, currentAmountDue);
        return new AdvanceCreditApplication(
            toPrior,
            toCurrent,
            creditAfterPrior - toCurrent,
            priorArrears - toPrior,
            currentAmountDue - toCurrent);
    }

    private static PriorBalancesResult DerivePriorBalances(
        DebtContractAnalysis currentDebt,
        InstallmentMasterContract? installment,
        DebtSegmentationAnalyzer analyzer,
        DateOnly current)
    {
        var currentDue = MonthlyAmountDueService.Calculate(currentDebt, current, installment);
        if (currentDue.DueBasis is AmountDueBases.PaidOff or AmountDueBases.ExpiredFullBalance)
        {
            var notApplicableDownPayment = installment?.DownPaymentDataStatus == DownPaymentDataStatuses.Available &&
                installment.DownPayment is >= 0m
                ? installment.DownPayment.Value
                : 0m;
            return new(PriorArrearsStatuses.NotApplicable, 0m, 0m, notApplicableDownPayment, null);
        }
        var firstDue = currentDebt.CurrentClassification.FirstDuePeriod;
        if (!firstDue.HasValue)
            return Unknown(PriorArrearsStatuses.UnknownMissingFirstDue, "FirstDuePeriod is unavailable.");

        var availableStart = currentDebt.Contract.MonthlyStates.Keys.Min();
        var missingHistoryStatus = firstDue.Value < availableStart
            ? PriorArrearsStatuses.UnknownHistoryBeforeAvailablePeriod
            : firstDue.Value == availableStart && current > availableStart
                ? PriorArrearsStatuses.UnknownMissingMonthlyFacts
                : null;
        if (missingHistoryStatus is not null)
        {
            if (IsInTermPayoff(currentDue) || IsExpiredPayoff(currentDue))
            {
                return new PriorBalancesResult(
                    PriorArrearsStatuses.NotApplicable, 0m, 0m, 0m,
                    "Current outstanding was paid off exactly; historical allocation is not applicable to current action.");
            }
            if (TryReconstructPriorPosition(
                    currentDebt, installment, firstDue.Value, availableStart, current,
                    out var reconstructed, out var failure))
            {
                return reconstructed;
            }

            return Unknown(missingHistoryStatus,
                $"Schedule reconstruction failed closed: {failure}");
        }

        var arrears = 0m;
        var advanceCredit = 0m;
        var isSj = IsSjContract(currentDebt.Contract.ContractNumber);
        var downPaymentAvailable = installment?.DownPaymentDataStatus == DownPaymentDataStatuses.Available &&
            installment.DownPayment is >= 0m;
        var remainingDownPayment = downPaymentAvailable ? installment!.DownPayment!.Value : 0m;

        for (var period = availableStart; period < current; period = period.AddMonths(1))
        {
            if (!currentDebt.Contract.MonthlyStates.TryGetValue(period, out var state))
                return Unknown(PriorArrearsStatuses.UnknownMissingMonthlyFacts,
                    $"Normalized monthly facts are incomplete at {period.ToString("yyyy-MM", CultureInfo.InvariantCulture)}.");

            if (period < firstDue.Value)
            {
                var preDuePayment = Math.Max(state.PaymentAmount, 0m);
                if (preDuePayment == 0m)
                    continue;
                if (isSj && !downPaymentAvailable)
                    return Unknown(PriorArrearsStatuses.UnknownHistoricalObligation,
                        $"Source B AE is unavailable for historical DownPayment at {period.ToString("yyyy-MM", CultureInfo.InvariantCulture)}.");
                if (isSj)
                {
                    var historicalDownPayment = Math.Min(preDuePayment, remainingDownPayment);
                    remainingDownPayment -= historicalDownPayment;
                    preDuePayment -= historicalDownPayment;
                }
                advanceCredit += preDuePayment;
                continue;
            }

            if (!currentDebt.Contract.MonthlyStates.ContainsKey(period.AddMonths(-1)))
                return Unknown(PriorArrearsStatuses.UnknownMissingMonthlyFacts,
                    $"Normalized monthly facts are incomplete at {period.ToString("yyyy-MM", CultureInfo.InvariantCulture)}.");
            var historicalDebt = analyzer.Analyze([currentDebt.Contract], period.AddMonths(-1), period).Contracts.Single();
            var historicalDue = MonthlyAmountDueService.Calculate(historicalDebt, period, installment);
            if (!historicalDue.AmountDue.HasValue)
                return Unknown(PriorArrearsStatuses.UnknownHistoricalObligation,
                    $"Historical obligation is unknown at {period.ToString("yyyy-MM", CultureInfo.InvariantCulture)}.");

            var creditToPrior = Math.Min(advanceCredit, arrears);
            advanceCredit -= creditToPrior;
            arrears -= creditToPrior;
            var creditToCurrent = Math.Min(advanceCredit, historicalDue.AmountDue.Value);
            advanceCredit -= creditToCurrent;
            var remainingCurrent = historicalDue.AmountDue.Value - creditToCurrent;

            var payment = historicalDue.ActualPayment;
            if (IsInTermPayoff(historicalDue) || IsExpiredPayoff(historicalDue))
            {
                arrears = 0m;
                continue;
            }

            var toPrior = Math.Min(payment, arrears);
            payment -= toPrior;
            arrears -= toPrior;
            var toCurrent = Math.Min(payment, remainingCurrent);
            payment -= toCurrent;
            remainingCurrent -= toCurrent;
            advanceCredit += payment;
            arrears += remainingCurrent;
        }

        var status = currentDue.DueBasis is AmountDueBases.NotDue or AmountDueBases.PaidOff or
            AmountDueBases.ExpiredFullBalance
            ? PriorArrearsStatuses.NotApplicable
            : PriorArrearsStatuses.Known;
        return new(status, status == PriorArrearsStatuses.Known ? arrears : 0m,
            advanceCredit, remainingDownPayment, null);
    }

    private static bool TryReconstructPriorPosition(
        DebtContractAnalysis currentDebt,
        InstallmentMasterContract? installment,
        DateOnly firstDue,
        DateOnly availableStart,
        DateOnly current,
        out PriorBalancesResult result,
        out string failure)
    {
        result = default!;
        failure = string.Empty;
        if (installment is null)
            return Fail("exact ContractNo Source A to Source B join is unavailable", out failure);
        if (installment.ContractualObligation is not > 0m)
            return Fail("AH/LCONT_AMOUNT_SAL is missing, non-numeric, or non-positive", out failure);
        if (installment.TotalInstallments is not > 0)
            return Fail("AL/LREG_MAX_INSTALL is missing, non-numeric, or non-positive", out failure);
        if (installment.MonthlyInstallment is not > 0m)
            return Fail("AM/LREG_SALINT is missing, non-numeric, or non-positive", out failure);

        var obligation = installment.ContractualObligation.Value;
        var totalInstallments = installment.TotalInstallments.Value;
        var monthlyInstallment = installment.MonthlyInstallment.Value;
        var finalInstallment = ContractSchedulePositionCalculator.FinalInstallment(
            obligation, totalInstallments, monthlyInstallment);
        if (finalInstallment <= 0m)
            return Fail("the exact final installment derived from AH, AL, and AM is non-positive", out failure);

        if (!TryValidateAvailableFacts(
                currentDebt.Contract, availableStart, current, obligation,
                out var openingOutstanding, out failure))
        {
            return false;
        }

        var expectedBeforeCurrent = ContractSchedulePositionCalculator.ExpectedDue(
            firstDue, current.AddMonths(-1), obligation, totalInstallments, monthlyInstallment);
        var satisfiedBeforeCurrent = obligation - openingOutstanding;
        var priorPosition = expectedBeforeCurrent - satisfiedBeforeCurrent;
        if (Math.Abs(priorPosition) <= ContractSchedulePositionCalculator.Tolerance)
            priorPosition = 0m;

        result = new PriorBalancesResult(
            PriorArrearsStatuses.Known,
            Math.Max(priorPosition, 0m),
            Math.Max(-priorPosition, 0m),
            0m,
            "Prior contractual position was reconstructed from AH/LCONT_AMOUNT_SAL and authoritative opening outstanding; historical transactions were not inferred.");
        return true;
    }

    private static bool TryValidateAvailableFacts(
        DebtContractHistory contract,
        DateOnly availableStart,
        DateOnly current,
        decimal contractualObligation,
        out decimal openingOutstanding,
        out string failure)
    {
        openingOutstanding = 0m;
        failure = string.Empty;
        DebtMonthlyState? previous = null;
        for (var period = availableStart; period <= current; period = period.AddMonths(1))
        {
            if (!contract.MonthlyStates.TryGetValue(period, out var state))
                return Fail($"monthly facts are missing at {period:yyyy-MM}", out failure);
            if (state.PaymentAmount < 0m)
                return Fail($"payment is negative at {period:yyyy-MM}", out failure);
            if (state.TotalOutstanding < 0m)
                return Fail($"outstanding is negative at {period:yyyy-MM}", out failure);
            if (state.TotalOutstanding > contractualObligation + ContractSchedulePositionCalculator.Tolerance)
                return Fail($"outstanding exceeds AH/LCONT_AMOUNT_SAL at {period:yyyy-MM}", out failure);

            if (previous is not null)
            {
                var increase = state.TotalOutstanding - previous.TotalOutstanding + state.PaymentAmount;
                if (increase > ContractSchedulePositionCalculator.Tolerance)
                    return Fail($"unsupported contractual-obligation increase {increase} exists at {period:yyyy-MM}", out failure);
            }
            previous = state;
        }

        if (current == availableStart)
        {
            var currentState = contract.MonthlyStates[current];
            openingOutstanding = currentState.TotalOutstanding + currentState.PaymentAmount;
        }
        else
        {
            openingOutstanding = contract.MonthlyStates[current.AddMonths(-1)].TotalOutstanding;
        }

        if (openingOutstanding < 0m)
            return Fail("authoritative opening outstanding is negative", out failure);
        if (openingOutstanding > contractualObligation + ContractSchedulePositionCalculator.Tolerance)
            return Fail("authoritative opening outstanding exceeds AH/LCONT_AMOUNT_SAL", out failure);
        return true;
    }

    private static bool Fail(string message, out string failure)
    {
        failure = message;
        return false;
    }

    private static PriorBalancesResult Unknown(string status, string warning) =>
        new(status, null, 0m, 0m, warning);

    private sealed record PriorBalancesResult(
        string Status,
        decimal? Arrears,
        decimal AdvanceCredit,
        decimal RemainingDownPayment,
        string? Warning);

    internal readonly record struct AdvanceCreditApplication(
        decimal AppliedToPriorArrears,
        decimal AppliedToCurrentDue,
        decimal RemainingAdvanceCredit,
        decimal RemainingPriorArrears,
        decimal RemainingCurrentDue);
}
