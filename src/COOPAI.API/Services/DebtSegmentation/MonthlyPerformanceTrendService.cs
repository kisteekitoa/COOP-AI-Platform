using COOPAI.API.DTOs.DebtSegmentation;

namespace COOPAI.API.Services.DebtSegmentation;

public interface IMonthlyPerformanceTrendService
{
    Task<MonthlyPerformanceTrendDto> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only monthly projection over normalized Source A states. The same V1.1
/// contract ledger used by Monthly Collection is evaluated for every available month.
/// </summary>
public sealed class MonthlyPerformanceTrendService : IMonthlyPerformanceTrendService
{
    private static readonly string[] ThaiMonths =
    [
        "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
        "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม"
    ];

    private readonly IDebtSnapshotStore _debtStore;
    private readonly IInstallmentMasterSnapshotStore _installmentStore;
    private readonly DebtSegmentationAnalyzer _analyzer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cacheKey;
    private MonthlyPerformanceTrendDto? _cached;

    public MonthlyPerformanceTrendService(
        IDebtSnapshotStore debtStore,
        IInstallmentMasterSnapshotStore installmentStore,
        DebtSegmentationAnalyzer analyzer)
    {
        _debtStore = debtStore;
        _installmentStore = installmentStore;
        _analyzer = analyzer;
    }

    public async Task<MonthlyPerformanceTrendDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var debt = await _debtStore.GetCurrentAsync(cancellationToken);
        if (debt is null)
            return Unavailable("A valid Debt Snapshot is required for monthly performance analysis.");

        var availablePeriods = debt.Workbook.Contracts
            .SelectMany(contract => contract.MonthlyStates.Keys)
            .Distinct()
            .OrderBy(period => period)
            .ToArray();
        if (availablePeriods.Length == 0)
            return Unavailable("The current Debt Snapshot has no normalized monthly states.", debt.SnapshotId);

        var trustedLatestPeriod = debt.Workbook.DataThroughPeriod ?? availablePeriods[^1];
        var latestYear = trustedLatestPeriod.Year;
        var periods = availablePeriods
            .Where(period => period.Year == latestYear && period <= trustedLatestPeriod)
            .ToArray();
        if (periods.Length == 0)
            return Unavailable("The current Debt Snapshot has no normalized states through its trusted data period.", debt.SnapshotId);
        var installment = await _installmentStore.GetCurrentAsync(cancellationToken);
        var cacheKey = $"{debt.SnapshotId}|{installment?.SnapshotId ?? "none"}|{latestYear}";
        if (_cacheKey == cacheKey && _cached is not null)
            return _cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cacheKey == cacheKey && _cached is not null)
                return _cached;

            var installmentByContract = (installment?.Workbook.Contracts ?? [])
                .ToDictionary(contract => contract.ContractKey, StringComparer.OrdinalIgnoreCase);
            var months = periods
                .Select(period => BuildMonth(debt.Workbook.Contracts, installmentByContract, period))
                .ToArray();
            _cached = new MonthlyPerformanceTrendDto(
                true,
                "Monthly performance is derived from normalized Source A states using Payment Allocation V1.1.",
                debt.SnapshotId,
                installment?.SnapshotId,
                latestYear,
                periods[0],
                periods[^1],
                months);
            _cacheKey = cacheKey;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private MonthlyPerformancePeriodDto BuildMonth(
        IReadOnlyList<DebtContractHistory> histories,
        IReadOnlyDictionary<string, InstallmentMasterContract> installmentByContract,
        DateOnly period)
    {
        var monthlyHistories = histories.Where(contract =>
        {
            if (!contract.ContractDate.HasValue)
                return true;
            var start = contract.ContractDate.Value;
            return new DateOnly(start.Year, start.Month, 1) <= period;
        }).ToArray();
        var analysis = _analyzer.Analyze(monthlyHistories, period.AddMonths(-1), period);
        var incompleteReasons = new HashSet<string>(StringComparer.Ordinal);
        var missingOpeningContracts = 0;
        var allocations = analysis.Contracts.Select(debt =>
        {
            var schedule = installmentByContract.GetValueOrDefault(debt.Contract.ContractKey);
            var due = MonthlyAmountDueService.Calculate(debt, period, schedule);
            var hasPreviousState = debt.Contract.MonthlyStates.ContainsKey(period.AddMonths(-1));
            var contractStartPeriod = debt.Contract.ContractDate.HasValue
                ? new DateOnly(debt.Contract.ContractDate.Value.Year, debt.Contract.ContractDate.Value.Month, 1)
                : (DateOnly?)null;
            var isNewContract = contractStartPeriod == period;
            var openingUnavailable = !hasPreviousState && !isNewContract;
            if (openingUnavailable)
            {
                missingOpeningContracts++;
                incompleteReasons.Add("Opening outstanding before the first available Source A month is unavailable for one or more existing contracts.");
                if (due.DueBasis != AmountDueBases.NotDue)
                {
                    due = due with
                    {
                        AmountDue = null,
                        Shortfall = null,
                        Warnings = due.Warnings.Append(
                            "Opening outstanding is unavailable; opening-dependent AmountDue remains Unknown.")
                            .Distinct(StringComparer.Ordinal).ToArray()
                    };
                }
            }

            return new MonthlyContractResult(
                debt,
                PaymentAllocationEngine.Allocate(debt, due, schedule, _analyzer),
                hasPreviousState,
                isNewContract,
                openingUnavailable);
        }).ToArray();

        var rows = allocations.Select(item => item.Allocation).ToArray();
        var unknownDue = rows.Count(row => !row.AmountDue.HasValue);
        var unknownPrior = rows.Count(row => row.PriorArrearsStatus.StartsWith("Unknown", StringComparison.Ordinal) ||
            row.PriorArrearsStatus == PriorArrearsStatuses.PriorCreditPolicyRequired);
        var unknownEndingArrears = rows.Count(row => !row.EndingArrears.HasValue);
        if (unknownDue > 0)
            incompleteReasons.Add("AmountDue is Unknown for one or more contracts because required opening or installment facts are unavailable.");
        if (unknownPrior > 0)
            incompleteReasons.Add("Prior arrears are Unknown for one or more contracts; missing history is not treated as zero.");

        AddPeriodGapReason(histories, period, incompleteReasons);

        decimal? opening = missingOpeningContracts == 0
            ? allocations.Where(item => item.HasPreviousState && !item.IsNewContract)
                .Sum(item => item.Debt.PreviousState.TotalOutstanding)
            : null;
        decimal? amountDue = unknownDue == 0 ? rows.Sum(row => row.AmountDue!.Value) : null;
        decimal? priorArrears = unknownPrior == 0 ? rows.Sum(row => row.PriorArrears ?? 0m) : null;
        decimal? endingArrears = unknownEndingArrears == 0 ? rows.Sum(row => row.EndingArrears ?? 0m) : null;
        var ending = rows.Sum(row => row.EndingOutstanding);
        var actual = rows.Sum(row => row.ActualPayment);
        decimal? increaseDuringPeriod = opening.HasValue
            ? ending - opening.Value + actual
            : null;
        var movementPairs = allocations.Select(item => new
        {
            Opening = item.IsNewContract
                ? 0m
                : item.HasPreviousState ? item.Debt.PreviousState.TotalOutstanding : (decimal?)null,
            item.Allocation.EndingOutstanding
        }).ToArray();
        int? openingOutstandingContracts = missingOpeningContracts == 0
            ? movementPairs.Count(item => item.Opening > 0m)
            : null;
        int? newOutstandingContracts = missingOpeningContracts == 0
            ? movementPairs.Count(item => item.Opening == 0m && item.EndingOutstanding > 0m)
            : null;
        int? reducedOutstandingContracts = missingOpeningContracts == 0
            ? movementPairs.Count(item => item.Opening > 0m && item.EndingOutstanding == 0m)
            : null;
        var endingOutstandingContracts = rows.Count(row => row.EndingOutstanding > 0m);
        var paidOffContracts = rows.Count(row => row.EndingOutstanding == 0m);
        AssertMovementReconciliation(period, opening, increaseDuringPeriod, actual, ending,
            openingOutstandingContracts, newOutstandingContracts, reducedOutstandingContracts,
            endingOutstandingContracts, rows.Length, paidOffContracts);
        var paidOffCandidatesUnknown = allocations.Any(item => item.OpeningUnavailable && item.Allocation.EndingOutstanding == 0m);
        var paidOff = allocations.Where(item => item.HasPreviousState && !item.IsNewContract &&
            item.Debt.PreviousState.TotalOutstanding > 0m && item.Allocation.EndingOutstanding == 0m).ToArray();
        if (paidOffCandidatesUnknown)
            incompleteReasons.Add("Paid-off movement in the first available month cannot be proven without opening balances.");

        var buckets = analysis.BucketSummaries.Select(bucket => new MonthlyDebtBucketTrendDto(
            DebtLabels.Bucket(bucket.Bucket),
            DebtLabels.BucketThai(bucket.Bucket),
            bucket.ContractCount,
            bucket.OutstandingAmount)).ToArray();
        AssertBucketReconciliation(period, rows, buckets);

        var cashAllocated = rows.Sum(row => row.DownPaymentAmount + row.NewAdvanceCredit +
            row.PaymentToPriorArrears + row.PaymentToCurrentDue + row.EarlyPayoffAmount +
            row.ExpiredPayoffAmount + row.UnclassifiedPaymentAmount);
        var ledgerDifference = rows.Sum(row => row.PriorAdvanceCredit + row.ActualPayment -
            row.AdvanceCreditApplied - row.DownPaymentAmount - row.PaymentToPriorArrears -
            row.PaymentToCurrentDue - row.EarlyPayoffAmount - row.ExpiredPayoffAmount -
            row.UnclassifiedPaymentAmount - row.EndingAdvanceCredit);

        return new MonthlyPerformancePeriodDto(
            period.Year,
            period.Month,
            $"{ThaiMonths[period.Month - 1]} {period.Year + 543}",
            rows.Length,
            analysis.Contracts.Select(item => item.Contract.MemberKey)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            opening,
            increaseDuringPeriod,
            ending,
            opening.HasValue ? ending - opening.Value : null,
            openingOutstandingContracts,
            newOutstandingContracts,
            reducedOutstandingContracts,
            endingOutstandingContracts,
            paidOffContracts,
            amountDue,
            unknownDue,
            actual,
            priorArrears,
            rows.Count(row => row.PriorArrearsStatus == PriorArrearsStatuses.Known),
            unknownPrior,
            rows.Sum(row => row.PriorAdvanceCredit),
            rows.Sum(row => row.AdvanceCreditApplied),
            rows.Sum(row => row.NewAdvanceCredit),
            rows.Sum(row => row.EndingAdvanceCredit),
            rows.Sum(row => row.DownPaymentAmount),
            rows.Sum(row => row.AdvancePayment),
            rows.Sum(row => row.PaymentToPriorArrears),
            rows.Sum(row => row.PaymentToCurrentDue),
            rows.Count(row => row.IsEarlyPayoff),
            rows.Sum(row => row.EarlyPayoffAmount),
            rows.Count(row => row.IsExpiredPayoff),
            rows.Sum(row => row.ExpiredPayoffAmount),
            rows.Count(row => row.UnclassifiedPaymentAmount > 0m),
            rows.Sum(row => row.UnclassifiedPaymentAmount),
            paidOffCandidatesUnknown ? null : paidOff.Length,
            paidOffCandidatesUnknown ? null : paidOff.Sum(item => item.Allocation.ActualPayment),
            allocations.Count(item => item.IsNewContract),
            endingArrears,
            unknownEndingArrears,
            actual - cashAllocated,
            ledgerDifference,
            incompleteReasons.Count == 0,
            incompleteReasons.OrderBy(reason => reason, StringComparer.Ordinal).ToArray(),
            buckets);
    }

    private static void AddPeriodGapReason(
        IReadOnlyList<DebtContractHistory> histories,
        DateOnly period,
        ISet<string> reasons)
    {
        if (period.Month == 1)
            return;
        var previous = period.AddMonths(-1);
        if (!histories.Any(contract => contract.MonthlyStates.ContainsKey(previous)))
            reasons.Add($"Source A has no normalized monthly state for {previous:yyyy-MM}; no month was invented.");
    }

    private static void AssertBucketReconciliation(
        DateOnly period,
        IReadOnlyList<MonthlyAmountDueContract> rows,
        IReadOnlyList<MonthlyDebtBucketTrendDto> buckets)
    {
        if (buckets.Sum(bucket => bucket.ContractCount) != rows.Count)
            throw new InvalidOperationException($"Debt bucket contract counts do not reconcile at {period:yyyy-MM}.");
        if (buckets.Sum(bucket => bucket.OutstandingBalance) != rows.Sum(row => row.EndingOutstanding))
            throw new InvalidOperationException($"Debt bucket balances do not reconcile at {period:yyyy-MM}.");
    }

    private static void AssertMovementReconciliation(
        DateOnly period,
        decimal? opening,
        decimal? increase,
        decimal actualPayment,
        decimal ending,
        int? openingContracts,
        int? newContracts,
        int? reducedContracts,
        int endingContracts,
        int totalContracts,
        int paidOffContracts)
    {
        if (opening.HasValue && opening.Value + increase!.Value - actualPayment != ending)
            throw new InvalidOperationException($"Outstanding money movement does not reconcile at {period:yyyy-MM}.");
        if (openingContracts.HasValue && openingContracts.Value + newContracts!.Value - reducedContracts!.Value != endingContracts)
            throw new InvalidOperationException($"Outstanding contract movement does not reconcile at {period:yyyy-MM}.");
        if (endingContracts + paidOffContracts != totalContracts)
            throw new InvalidOperationException($"Outstanding and paid-off contract counts do not reconcile at {period:yyyy-MM}.");
    }

    private static MonthlyPerformanceTrendDto Unavailable(string message, string? debtSnapshotId = null) =>
        new(false, message, debtSnapshotId, null, null, null, null, []);

    private sealed record MonthlyContractResult(
        DebtContractAnalysis Debt,
        MonthlyAmountDueContract Allocation,
        bool HasPreviousState,
        bool IsNewContract,
        bool OpeningUnavailable);
}
