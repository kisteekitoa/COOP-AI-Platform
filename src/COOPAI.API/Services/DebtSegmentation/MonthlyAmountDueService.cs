using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using COOPAI.API.DTOs.DebtSegmentation;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.DebtSegmentation;

public interface IMonthlyAmountDueService
{
    Task<MonthlyAmountDueAnalysis?> GetAnalysisAsync(
        string? currentPeriod,
        CancellationToken cancellationToken = default);
    Task<MonthlyCollectionDashboardDto> GetDashboardAsync(
        string? currentPeriod,
        CancellationToken cancellationToken = default);
    Task<MonthlyAmountDueContractPageDto> GetContractsAsync(
        string? currentPeriod,
        string? dueBasis,
        string? remainingOrOverdueFilter,
        string? search,
        int page,
        int pageSize,
        bool? sortRemainingMonthsDescending = null,
        CancellationToken cancellationToken = default);
}

public sealed class MonthlyAmountDueService : IMonthlyAmountDueService
{
    private readonly DebtSegmentationPreviewOptions _debtOptions;
    private readonly IDebtSnapshotStore _debtStore;
    private readonly IInstallmentMasterSnapshotStore _installmentStore;
    private readonly DebtSegmentationAnalyzer _debtAnalyzer;
    private readonly SemaphoreSlim _analysisGate = new(1, 1);
    private MonthlyAmountDueAnalysis? _cached;

    public MonthlyAmountDueService(
        IOptions<DebtSegmentationPreviewOptions> debtOptions,
        IDebtSnapshotStore debtStore,
        IInstallmentMasterSnapshotStore installmentStore,
        DebtSegmentationAnalyzer debtAnalyzer)
    {
        _debtOptions = debtOptions.Value;
        _debtStore = debtStore;
        _installmentStore = installmentStore;
        _debtAnalyzer = debtAnalyzer;
    }

    public async Task<MonthlyCollectionDashboardDto> GetDashboardAsync(
        string? currentPeriod,
        CancellationToken cancellationToken = default)
    {
        var period = ParsePeriod(currentPeriod ?? _debtOptions.DefaultCurrentPeriod);
        var analysis = await GetAnalysisAsync(period, cancellationToken);
        if (analysis is null)
        {
            return new MonthlyCollectionDashboardDto(
                false, "A valid Debt Snapshot is required for Monthly Amount Due analysis.", null,
                MonthlyAmountDueVersion.V1, null, null, period, null, [], null, null);
        }

        var known = analysis.Contracts.Where(x => x.AmountDue.HasValue).ToArray();
        var unknown = analysis.Contracts.Where(x => !x.AmountDue.HasValue).ToArray();
        var totals = new MonthlyCollectionSummaryDto(
            analysis.Contracts.Count,
            known.Sum(x => x.AmountDue ?? 0m),
            analysis.Contracts.Sum(x => x.ActualPayment),
            known.Sum(x => x.Shortfall ?? 0m),
            analysis.Contracts.Sum(x => x.ExcessPayment),
            analysis.Contracts.Sum(x => x.AdvancePayment),
            unknown.Length,
            unknown.Sum(x => x.OpeningOutstanding),
            analysis.Contracts.Count(x => x.DueBasis is
                AmountDueBases.FinalInstallment or AmountDueBases.InstallmentsElapsedFullBalance));
        return new MonthlyCollectionDashboardDto(
            true,
            analysis.InstallmentSnapshotId is null
                ? "Debt data is available; no valid Installment Master Snapshot is available, so normal due installments remain Unknown."
                : "Monthly Amount Due is calculated from the latest valid Debt and Installment Master snapshots.",
            analysis.AnalysisId,
            analysis.AnalysisVersion,
            analysis.DebtSnapshotId,
            analysis.InstallmentSnapshotId,
            period,
            totals,
            analysis.Breakdowns,
            analysis.DataQuality,
            analysis.GeneratedAtUtc)
        {
            PaymentAllocation = AllocationSummary(analysis.Contracts, analysis.DataQuality.MatchedContracts)
        };
    }

    public Task<MonthlyAmountDueAnalysis?> GetAnalysisAsync(
        string? currentPeriod,
        CancellationToken cancellationToken = default) =>
        GetAnalysisAsync(ParsePeriod(currentPeriod ?? _debtOptions.DefaultCurrentPeriod), cancellationToken);

    public async Task<MonthlyAmountDueContractPageDto> GetContractsAsync(
        string? currentPeriod,
        string? dueBasis,
        string? remainingOrOverdueFilter,
        string? search,
        int page,
        int pageSize,
        bool? sortRemainingMonthsDescending = null,
        CancellationToken cancellationToken = default)
    {
        var period = ParsePeriod(currentPeriod ?? _debtOptions.DefaultCurrentPeriod);
        var analysis = await GetAnalysisAsync(period, cancellationToken);
        if (analysis is null)
            return new MonthlyAmountDueContractPageDto(false, "A valid Debt Snapshot is required.", 1, 50, 0, []);

        return BuildContractPage(
            analysis.Contracts, dueBasis, remainingOrOverdueFilter, search,
            page, pageSize, sortRemainingMonthsDescending);
    }

    public static MonthlyAmountDueContractPageDto BuildContractPage(
        IEnumerable<MonthlyAmountDueContract> contracts,
        string? dueBasis,
        string? remainingOrOverdueFilter,
        string? search,
        int page,
        int pageSize,
        bool? sortRemainingMonthsDescending = null)
    {
        // The complete derived dataset is filtered and numerically sorted before a page is selected.
        IEnumerable<MonthlyAmountDueContract> query = contracts;
        if (!string.IsNullOrWhiteSpace(remainingOrOverdueFilter))
            query = query.Where(item => MatchesRemainingOrOverdueFilter(item, remainingOrOverdueFilter));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(item => item.ContractNumber.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(dueBasis))
            query = query.Where(item => string.Equals(item.DueBasis, dueBasis.Trim(), StringComparison.OrdinalIgnoreCase));
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        IOrderedEnumerable<MonthlyAmountDueContract> ordered;
        if (sortRemainingMonthsDescending.HasValue)
        {
            ordered = OrderByRemainingOrOverdueMonths(query, sortRemainingMonthsDescending.Value);
        }
        else
        {
            ordered = query.OrderByDescending(x => x.Shortfall ?? -1m)
                .ThenByDescending(x => x.OpeningOutstanding)
                .ThenBy(x => x.ContractNumber, StringComparer.OrdinalIgnoreCase);
        }
        var matches = ordered.ToArray();
        var items = matches.Skip((page - 1) * pageSize).Take(pageSize).Select(Map).ToArray();
        return new MonthlyAmountDueContractPageDto(
            true, "Monthly Amount Due contract details are available.", page, pageSize, matches.Length, items);
    }

    public static bool MatchesRemainingOrOverdueFilter(
        MonthlyAmountDueContract item,
        string filter)
    {
        var normalized = filter.Trim().ToLowerInvariant();
        if (normalized == RemainingOrOverdueFilters.NotDue)
            return item.ContractStatus == "NotDue";
        if (normalized == RemainingOrOverdueFilters.PaidOff)
            return item.ContractStatus == "PaidOff";

        // Not-due and paid-off contracts have dedicated status filters and do not leak into numeric ranges.
        if (item.ContractStatus is "NotDue" or "PaidOff" || !item.RemainingOrOverdueMonths.HasValue)
            return false;

        var months = item.RemainingOrOverdueMonths.Value;
        return normalized switch
        {
            RemainingOrOverdueFilters.OverdueAll => months < 0,
            RemainingOrOverdueFilters.OverdueTenYearsOrMore => months <= -120,
            RemainingOrOverdueFilters.OverdueFiveToTenYears => months is >= -119 and <= -60,
            RemainingOrOverdueFilters.OverdueThreeToFiveYears => months is >= -59 and <= -36,
            RemainingOrOverdueFilters.OverdueOneToThreeYears => months is >= -35 and <= -12,
            RemainingOrOverdueFilters.OverdueUpToOneYear => months is >= -11 and <= -1,
            RemainingOrOverdueFilters.DueThisMonth => months == 0,
            RemainingOrOverdueFilters.RemainingOneToSixMonths => months is >= 1 and <= 6,
            RemainingOrOverdueFilters.RemainingSevenToTwelveMonths => months is >= 7 and <= 12,
            RemainingOrOverdueFilters.RemainingOneToThreeYears => months is >= 13 and <= 36,
            RemainingOrOverdueFilters.RemainingMoreThanThreeYears => months >= 37,
            _ => throw new ArgumentException($"Unsupported remaining/overdue filter '{filter}'.", nameof(filter))
        };
    }

    private async Task<MonthlyAmountDueAnalysis?> GetAnalysisAsync(
        DateOnly period,
        CancellationToken cancellationToken)
    {
        var debt = await _debtStore.GetCurrentAsync(cancellationToken);
        if (debt is null || !debt.Workbook.Contracts.Any(x => x.MonthlyStates.ContainsKey(period)))
            return null;
        var installment = await _installmentStore.GetCurrentAsync(cancellationToken);
        var analysisId = AnalysisId(debt.SnapshotId, installment?.SnapshotId, period);
        if (_cached?.AnalysisId == analysisId)
            return _cached;

        await _analysisGate.WaitAsync(cancellationToken);
        try
        {
            if (_cached?.AnalysisId == analysisId)
                return _cached;
            var debtAnalysis = _debtAnalyzer.Analyze(debt.Workbook.Contracts, period.AddMonths(-1), period);
            var installmentByContract = (installment?.Workbook.Contracts ?? [])
                .ToDictionary(x => x.ContractKey, StringComparer.OrdinalIgnoreCase);
            var contracts = debtAnalysis.Contracts
                .Select(item =>
                {
                    var schedule = installmentByContract.GetValueOrDefault(item.Contract.ContractKey);
                    var due = Calculate(item, period, schedule);
                    return PaymentAllocationEngine.Allocate(item, due, schedule, _debtAnalyzer);
                })
                .ToArray();
            var matched = debtAnalysis.Contracts.Count(x => installmentByContract.ContainsKey(x.Contract.ContractKey));
            var usableMatched = debtAnalysis.Contracts.Count(x =>
                installmentByContract.TryGetValue(x.Contract.ContractKey, out var schedule) && schedule.IsUsable);
            var scheduleWarnings = contracts.Count(x =>
                x.DueBasis == AmountDueBases.InstallmentsElapsedFullBalance ||
                x.Warnings.Any(warning => warning.StartsWith("ContractStartDate", StringComparison.Ordinal)));
            var elapsed = contracts.Count(x => x.DueBasis == AmountDueBases.InstallmentsElapsedFullBalance);
            var missingDue = contracts.Count(x => x.DueBasis == AmountDueBases.MissingInstallmentData);
            var missingRecord = contracts.Count(x => x.DueBasis == AmountDueBases.MissingInstallmentData &&
                x.InstallmentDataStatus == InstallmentDataStatuses.Missing);
            var dataQuality = new MonthlyCollectionDataQuality(
                contracts.Length,
                installment?.Workbook.ContractCount ?? 0,
                matched,
                contracts.Length - matched,
                contracts.Length == 0 ? 0m : decimal.Round(matched * 100m / contracts.Length, 2),
                installment?.Workbook.DuplicateContractNumberGroups ?? 0,
                installment?.Workbook.InvalidTotalInstallmentRows ?? 0,
                installment?.Workbook.InvalidMonthlyInstallmentRows ?? 0,
                usableMatched,
                missingDue,
                scheduleWarnings,
                elapsed,
                missingRecord);
            var breakdowns = new[] { "Active/InTerm", "Expired", "NotDue", "PaidOff", "Unknown" }
                .Select(category => Breakdown(category, contracts.Where(x => x.ContractStatus == category)))
                .ToArray();
            _cached = new MonthlyAmountDueAnalysis(
                analysisId, MonthlyAmountDueVersion.V1, debt.SnapshotId, installment?.SnapshotId,
                period, contracts, breakdowns, dataQuality, DateTime.UtcNow);
            return _cached;
        }
        finally
        {
            _analysisGate.Release();
        }
    }

    public static MonthlyAmountDueContract Calculate(
        DebtContractAnalysis debt,
        DateOnly period,
        InstallmentMasterContract? installment)
    {
        var firstDue = debt.CurrentClassification.FirstDuePeriod;
        var opening = Math.Max(0m, debt.PreviousState.TotalOutstanding);
        var actual = Math.Max(0m, debt.CurrentState.PaymentAmount);
        var ending = Math.Max(0m, debt.CurrentState.TotalOutstanding);
        var installmentNumber = CurrentInstallmentNumber(firstDue, period);
        var totalInstallments = installment?.TotalInstallments;
        var monthlyInstallment = installment?.MonthlyInstallment;
        var warnings = new List<string>();
        decimal? amountDue;
        string dueBasis;
        string contractStatus;

        if (opening <= 0m && ending <= 0m)
        {
            amountDue = 0m;
            dueBasis = AmountDueBases.PaidOff;
            contractStatus = "PaidOff";
        }
        else if (firstDue.HasValue && period < firstDue.Value)
        {
            amountDue = 0m;
            dueBasis = AmountDueBases.NotDue;
            contractStatus = "NotDue";
        }
        else if (debt.Contract.ExpireDate.HasValue && debt.Contract.ExpireDate.Value < period.AddMonths(1))
        {
            amountDue = opening;
            dueBasis = AmountDueBases.ExpiredFullBalance;
            contractStatus = "Expired";
        }
        else if (!firstDue.HasValue)
        {
            amountDue = null;
            dueBasis = AmountDueBases.MissingInstallmentData;
            contractStatus = "Unknown";
            warnings.Add("ContractStartDate is unavailable, so the current installment number cannot be determined.");
        }
        else if (installment?.IsUsable == true && totalInstallments.HasValue &&
                 installmentNumber > totalInstallments.Value)
        {
            amountDue = opening;
            dueBasis = AmountDueBases.InstallmentsElapsedFullBalance;
            contractStatus = "Active/InTerm";
            warnings.Add("Installment schedule has elapsed while ContractExpireDate still appears within term.");
        }
        else if (installment?.IsUsable == true && totalInstallments.HasValue &&
                 installmentNumber == totalInstallments.Value)
        {
            amountDue = opening;
            dueBasis = AmountDueBases.FinalInstallment;
            contractStatus = "Active/InTerm";
        }
        else if (installment?.IsUsable == true && monthlyInstallment.HasValue && totalInstallments.HasValue)
        {
            amountDue = Math.Min(monthlyInstallment.Value, opening);
            dueBasis = AmountDueBases.ActiveInstallment;
            contractStatus = "Active/InTerm";
        }
        else
        {
            amountDue = null;
            dueBasis = AmountDueBases.MissingInstallmentData;
            contractStatus = "Unknown";
        }

        decimal? shortfall = amountDue.HasValue ? Math.Max(amountDue.Value - actual, 0m) : null;
        var rawExcess = amountDue.HasValue ? Math.Max(actual - amountDue.Value, 0m) : 0m;
        var advance = dueBasis == AmountDueBases.NotDue ? rawExcess : 0m;
        var excess = dueBasis == AmountDueBases.NotDue ? 0m : rawExcess;
        var installmentStatus = InstallmentStatus(installment, amountDue.HasValue &&
            dueBasis is not AmountDueBases.ActiveInstallment and
            not AmountDueBases.FinalInstallment and
            not AmountDueBases.InstallmentsElapsedFullBalance);
        var contractualObligation = installment?.ContractualObligation;
        decimal? finalInstallment = contractualObligation is > 0m &&
            totalInstallments is > 0 && monthlyInstallment is > 0m
            ? ContractSchedulePositionCalculator.FinalInstallment(
                contractualObligation.Value, totalInstallments.Value, monthlyInstallment.Value)
            : null;
        return new MonthlyAmountDueContract(
            debt.Contract.ContractNumber,
            DebtLabels.Bucket(debt.CurrentClassification.Bucket),
            contractStatus,
            period,
            firstDue,
            debt.Contract.ContractDate,
            debt.Contract.ExpireDate,
            RemainingOrOverdueMonths(debt.Contract.ExpireDate, period),
            opening,
            actual,
            ending,
            installmentNumber,
            totalInstallments,
            monthlyInstallment,
            amountDue,
            shortfall,
            excess,
            advance,
            dueBasis,
            installmentStatus,
            warnings.Concat(installment?.ValidationWarnings ?? []).Distinct(StringComparer.Ordinal).ToArray())
        {
            ContractualObligation = contractualObligation,
            FinalInstallment = finalInstallment
        };
    }

    public static int CurrentInstallmentNumber(DateOnly? firstDuePeriod, DateOnly currentPeriod)
    {
        if (!firstDuePeriod.HasValue || currentPeriod < firstDuePeriod.Value)
            return 0;
        return ((currentPeriod.Year - firstDuePeriod.Value.Year) * 12) +
               currentPeriod.Month - firstDuePeriod.Value.Month + 1;
    }

    public static int? RemainingOrOverdueMonths(DateOnly? contractExpireDate, DateOnly currentPeriod)
    {
        if (!contractExpireDate.HasValue)
            return null;
        return ((contractExpireDate.Value.Year - currentPeriod.Year) * 12) +
               contractExpireDate.Value.Month - currentPeriod.Month;
    }

    public static IOrderedEnumerable<MonthlyAmountDueContract> OrderByRemainingOrOverdueMonths(
        IEnumerable<MonthlyAmountDueContract> source,
        bool descending)
    {
        var valuesFirst = source.OrderBy(item => item.RemainingOrOverdueMonths.HasValue ? 0 : 1);
        return descending
            ? valuesFirst.ThenByDescending(item => item.RemainingOrOverdueMonths)
                .ThenBy(item => item.ContractNumber, StringComparer.OrdinalIgnoreCase)
            : valuesFirst.ThenBy(item => item.RemainingOrOverdueMonths)
                .ThenBy(item => item.ContractNumber, StringComparer.OrdinalIgnoreCase);
    }

    private static string InstallmentStatus(InstallmentMasterContract? installment, bool notRequired)
    {
        if (notRequired)
            return InstallmentDataStatuses.NotRequired;
        if (installment is null)
            return InstallmentDataStatuses.Missing;
        if (installment.DuplicateStatus is
            InstallmentDuplicateStatuses.ConflictingTotalInstallments or
            InstallmentDuplicateStatuses.ConflictingMonthlyInstallment or
            InstallmentDuplicateStatuses.ConflictingBoth or
            InstallmentDuplicateStatuses.ConflictingContractualObligation)
            return InstallmentDataStatuses.ConflictingDuplicate;
        return installment.IsUsable ? InstallmentDataStatuses.Available : InstallmentDataStatuses.Invalid;
    }

    private static MonthlyCollectionBreakdown Breakdown(
        string category,
        IEnumerable<MonthlyAmountDueContract> source)
    {
        var rows = source.ToArray();
        return new MonthlyCollectionBreakdown(
            category,
            rows.Length,
            rows.Sum(x => x.OpeningOutstanding),
            rows.Sum(x => x.AmountDue ?? 0m),
            rows.Sum(x => x.ActualPayment),
            rows.Sum(x => x.Shortfall ?? 0m),
            rows.Sum(x => x.ExcessPayment),
            rows.Sum(x => x.AdvancePayment));
    }

    private static MonthlyAmountDueContractDto Map(MonthlyAmountDueContract item) => new(
        item.ContractNumber, item.DebtBucket, item.ContractStatus, item.CurrentPeriod,
        item.FirstDuePeriod, item.ContractStartDate, item.ContractExpireDate,
        item.RemainingOrOverdueMonths,
        item.OpeningOutstanding, item.ActualPayment, item.EndingOutstanding,
        item.CurrentInstallmentNumber, item.TotalInstallments, item.MonthlyInstallment,
        item.AmountDue, item.Shortfall, item.ExcessPayment, item.AdvancePayment,
        item.DueBasis, item.InstallmentDataStatus, item.Warnings)
    {
        ContractualObligation = item.ContractualObligation,
        FinalInstallment = item.FinalInstallment,
        DownPaymentSourceAmount = item.DownPaymentSourceAmount,
        DownPaymentDataStatus = item.DownPaymentDataStatus,
        DownPaymentAmount = item.DownPaymentAmount,
        PriorArrears = item.PriorArrears,
        PriorArrearsStatus = item.PriorArrearsStatus,
        PaymentToPriorArrears = item.PaymentToPriorArrears,
        PaymentToCurrentDue = item.PaymentToCurrentDue,
        IsEarlyPayoff = item.IsEarlyPayoff,
        EarlyPayoffAmount = item.EarlyPayoffAmount,
        IsExpiredPayoff = item.IsExpiredPayoff,
        ExpiredPayoffAmount = item.ExpiredPayoffAmount,
        UnclassifiedPaymentAmount = item.UnclassifiedPaymentAmount,
        PaymentAllocationStatus = item.PaymentAllocationStatus,
        PaymentAllocationWarning = item.PaymentAllocationWarning,
        PriorCreditPolicyPeriod = item.PriorCreditPolicyPeriod,
        PriorCreditPolicyObligation = item.PriorCreditPolicyObligation,
        PriorCreditPolicyActualPayment = item.PriorCreditPolicyActualPayment,
        PriorCreditPolicyResidualAmount = item.PriorCreditPolicyResidualAmount,
        PriorAdvanceCredit = item.PriorAdvanceCredit,
        AdvanceCreditAppliedToPriorArrears = item.AdvanceCreditAppliedToPriorArrears,
        AdvanceCreditAppliedToCurrentDue = item.AdvanceCreditAppliedToCurrentDue,
        AdvanceCreditApplied = item.AdvanceCreditApplied,
        RemainingCurrentDueForCash = item.RemainingCurrentDueForCash,
        NewAdvanceCredit = item.NewAdvanceCredit,
        EndingAdvanceCredit = item.EndingAdvanceCredit,
        EndingArrears = item.EndingArrears
    };

    private static PaymentAllocationSummaryDto AllocationSummary(
        IReadOnlyList<MonthlyAmountDueContract> contracts,
        int matchedContracts)
    {
        var actual = contracts.Sum(x => x.ActualPayment);
        var allocated = contracts.Sum(x => x.DownPaymentAmount + x.NewAdvanceCredit +
            x.PaymentToPriorArrears + x.PaymentToCurrentDue +
            x.EarlyPayoffAmount + x.ExpiredPayoffAmount);
        var unclassified = contracts.Sum(x => x.UnclassifiedPaymentAmount);
        var ledgerDifference = contracts.Sum(x => x.PriorAdvanceCredit + x.ActualPayment -
            x.AdvanceCreditApplied - x.DownPaymentAmount - x.PaymentToPriorArrears -
            x.PaymentToCurrentDue - x.EarlyPayoffAmount - x.ExpiredPayoffAmount -
            x.UnclassifiedPaymentAmount - x.EndingAdvanceCredit);
        var unknownPrior = contracts.Count(x => x.PriorArrearsStatus.StartsWith("Unknown", StringComparison.Ordinal));
        var policyRequired = contracts.Count(x => x.PriorArrearsStatus == PriorArrearsStatuses.PriorCreditPolicyRequired);
        return new PaymentAllocationSummaryDto(
            actual,
            contracts.Count(x => x.DownPaymentAmount > 0m), contracts.Sum(x => x.DownPaymentAmount),
            contracts.Count(x => x.AdvancePayment > 0m), contracts.Sum(x => x.AdvancePayment),
            contracts.Count(x => x.PaymentToPriorArrears > 0m), contracts.Sum(x => x.PaymentToPriorArrears),
            contracts.Count(x => x.PaymentToCurrentDue > 0m), contracts.Sum(x => x.PaymentToCurrentDue),
            contracts.Count(x => x.ExcessPayment > 0m), contracts.Sum(x => x.ExcessPayment),
            contracts.Count(x => x.IsEarlyPayoff), contracts.Sum(x => x.EarlyPayoffAmount),
            contracts.Count(x => x.IsExpiredPayoff), contracts.Sum(x => x.ExpiredPayoffAmount),
            contracts.Count(x => x.UnclassifiedPaymentAmount > 0m), unclassified,
            contracts.Count(x => x.PriorArrearsStatus == PriorArrearsStatuses.Known),
            contracts.Where(x => x.PriorArrearsStatus == PriorArrearsStatuses.Known).Sum(x => x.PriorArrears ?? 0m),
            unknownPrior, policyRequired,
            contracts.Where(x => x.PriorArrearsStatus == PriorArrearsStatuses.PriorCreditPolicyRequired)
                .Sum(x => x.PriorCreditPolicyResidualAmount),
            matchedContracts, contracts.Count,
            contracts.Count == 0 ? 0m : decimal.Round(matchedContracts * 100m / contracts.Count, 2),
            allocated, actual - allocated - unclassified,
            false,
            MonthlyAmountDueVersion.PaymentAllocationV11,
            contracts.Count(x => x.PriorAdvanceCredit > 0m), contracts.Sum(x => x.PriorAdvanceCredit),
            contracts.Count(x => x.AdvanceCreditApplied > 0m), contracts.Sum(x => x.AdvanceCreditApplied),
            contracts.Count(x => x.NewAdvanceCredit > 0m), contracts.Sum(x => x.NewAdvanceCredit),
            contracts.Count(x => x.EndingAdvanceCredit > 0m), contracts.Sum(x => x.EndingAdvanceCredit),
            ledgerDifference,
            "Legacy TrueExcess is retained as an informational alias; residual overpayment is carried as NewAdvanceCredit.");
    }

    private static string AnalysisId(string debtSnapshotId, string? installmentSnapshotId, DateOnly period)
    {
        var identity = $"{debtSnapshotId}|{installmentSnapshotId ?? "none"}|{period:yyyy-MM}|{MonthlyAmountDueVersion.V1}|{MonthlyAmountDueVersion.PaymentAllocationV11}";
        return $"{MonthlyAmountDueVersion.V1}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))}";
    }

    private static DateOnly ParsePeriod(string value)
    {
        if (DateOnly.TryParseExact(
                $"{value.Trim()}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return parsed;
        throw new ArgumentException("Period must use YYYY-MM format.", nameof(value));
    }
}
