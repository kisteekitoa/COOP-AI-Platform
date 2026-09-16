using System.Globalization;
using COOPAI.API.Data;
using COOPAI.API.DTOs.Dashboard;
using COOPAI.API.DTOs.DebtSegmentation;
using COOPAI.API.Models.Portfolio;
using COOPAI.API.Services.DebtSegmentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.Dashboard;

public sealed class DashboardService : IDashboardService
{
    public const string CurrentOperationalDataSource = "Current Operational Snapshots (Source A + Source B)";
    public const string PublishedSnapshotDataSource = "Published Portfolio Snapshot";
    private static readonly DashboardLoanTypeClassifier LoanTypeClassifier = new();

    private readonly CoopDbContext _dbContext;
    private readonly IDebtSnapshotStore? _debtStore;
    private readonly IInstallmentMasterSnapshotStore? _installmentStore;
    private readonly DebtSegmentationAnalyzer? _debtAnalyzer;
    private readonly IMonthlyPerformanceTrendService? _monthlyTrend;
    private readonly IMonthlyAmountDueService? _monthlyAmountDue;
    private readonly IWorkQueueService? _workQueue;
    private readonly IDebtAutoSyncStatus? _debtAutoSync;
    private readonly IInstallmentMasterAutoSyncStatus? _installmentAutoSync;
    private readonly DebtSegmentationPreviewOptions? _options;

    // Retained for isolated Published Snapshot tests. The application uses the full DI constructor.
    public DashboardService(CoopDbContext dbContext) => _dbContext = dbContext;

    [ActivatorUtilitiesConstructor]
    public DashboardService(
        CoopDbContext dbContext,
        IDebtSnapshotStore debtStore,
        IInstallmentMasterSnapshotStore installmentStore,
        DebtSegmentationAnalyzer debtAnalyzer,
        IMonthlyPerformanceTrendService monthlyTrend,
        IMonthlyAmountDueService monthlyAmountDue,
        IWorkQueueService workQueue,
        IDebtAutoSyncStatus debtAutoSync,
        IInstallmentMasterAutoSyncStatus installmentAutoSync,
        IOptions<DebtSegmentationPreviewOptions> options)
    {
        _dbContext = dbContext;
        _debtStore = debtStore;
        _installmentStore = installmentStore;
        _debtAnalyzer = debtAnalyzer;
        _monthlyTrend = monthlyTrend;
        _monthlyAmountDue = monthlyAmountDue;
        _workQueue = workQueue;
        _debtAutoSync = debtAutoSync;
        _installmentAutoSync = installmentAutoSync;
        _options = options.Value;
    }

    public Task<DashboardSummaryDto> GetSummaryAsync(
        string? mode = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedMode = string.IsNullOrWhiteSpace(mode)
            ? DashboardDataModes.Current
            : mode.Trim().ToUpperInvariant();
        return normalizedMode == DashboardDataModes.Published
            ? GetPublishedSummaryAsync(cancellationToken)
            : GetCurrentSummaryAsync(cancellationToken);
    }

    private async Task<DashboardSummaryDto> GetCurrentSummaryAsync(CancellationToken cancellationToken)
    {
        if (_debtStore is null || _installmentStore is null || _debtAnalyzer is null ||
            _monthlyTrend is null || _monthlyAmountDue is null || _workQueue is null)
        {
            return CurrentUnavailable("Current operational dashboard services are unavailable.");
        }

        var debt = await _debtStore.GetCurrentAsync(cancellationToken);
        if (debt is null)
            return CurrentUnavailable("A valid current Source A Debt Snapshot is required. Published data was not substituted.");

        var period = debt.Workbook.DataThroughPeriod;
        if (!period.HasValue)
            return CurrentUnavailable("The current Source A snapshot has no trusted data-through period. Published data was not substituted.");

        var installment = await _installmentStore.GetCurrentAsync(cancellationToken);
        if (installment is null)
            return CurrentUnavailable("A valid current Source B Installment Snapshot is required. Published data was not substituted.");

        var periodText = period.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var analysis = _debtAnalyzer.Analyze(debt.Workbook.Contracts, period.Value.AddMonths(-1), period.Value);
        var trend = await _monthlyTrend.GetAsync(cancellationToken);
        var monthly = await _monthlyAmountDue.GetDashboardAsync(periodText, cancellationToken);
        var queue = await _workQueue.GetAsync(
            periodText, null, null, null, null, null, null, null, 1, 1, cancellationToken);
        var trendMonth = trend.Months.SingleOrDefault(month =>
            month.PeriodYear == period.Value.Year && month.PeriodMonth == period.Value.Month);

        if (!trend.Available || !monthly.Available || monthly.Totals is null ||
            monthly.PaymentAllocation is null || !queue.Available || queue.Summary is null || trendMonth is null)
        {
            return CurrentUnavailable(
                $"Current Source A/B data is present, but one or more authoritative current analyses are unavailable " +
                $"(trend={trend.Available}, trendMonth={trendMonth is not null}, monthly={monthly.Available}, " +
                $"monthlyTotals={monthly.Totals is not null}, allocation={monthly.PaymentAllocation is not null}, " +
                $"queue={queue.Available}, queueSummary={queue.Summary is not null}). Published data was not substituted.",
                debt, installment, period);
        }

        var contracts = analysis.Contracts;
        var asOfDate = period.Value.AddMonths(1).AddDays(-1);
        var outstanding = contracts.Where(contract => contract.CurrentState.TotalOutstanding > 0m).ToArray();
        var paidOff = contracts.Where(contract => contract.CurrentState.TotalOutstanding <= 0m).ToArray();
        var expired = contracts.Where(contract => contract.Contract.ExpireDate <= asOfDate).ToArray();
        var inTerm = contracts.Except(expired).ToArray();
        var principal = Money(contracts.Sum(contract => contract.CurrentState.PrincipalOutstanding));
        var profit = Money(contracts.Sum(contract => contract.CurrentState.ProfitOutstanding));
        var ending = Money(contracts.Sum(contract => contract.CurrentState.TotalOutstanding));
        var expiredOutstanding = expired.Where(contract => contract.CurrentState.TotalOutstanding > 0m).ToArray();
        var bucketCount = analysis.BucketSummaries.Sum(bucket => bucket.ContractCount);
        var bucketOutstanding = Money(analysis.BucketSummaries.Sum(bucket => bucket.OutstandingAmount));
        var queueSummary = queue.Summary;
        var allocation = monthly.PaymentAllocation;

        var reconciliationErrors = new List<string>();
        Require(contracts.Count == outstanding.Length + paidOff.Length, "contract status count", reconciliationErrors);
        Require(contracts.Count == inTerm.Length + expired.Length, "contract term count", reconciliationErrors);
        Require(contracts.Count == bucketCount, "debt bucket count", reconciliationErrors);
        Require(principal + profit == ending,
            $"principal + profit = ending outstanding ({principal} + {profit} != {ending})", reconciliationErrors);
        Require(bucketOutstanding == ending, "debt bucket outstanding", reconciliationErrors);
        Require(trendMonth.ContractCount == contracts.Count, "monthly performance universe", reconciliationErrors);
        Require(trendMonth.EndingOutstanding == ending, "monthly performance ending outstanding", reconciliationErrors);
        Require(trendMonth.ActualPayment == monthly.Totals.ActualPayment, "actual payment", reconciliationErrors);
        Require(trendMonth.AmountDue == monthly.Totals.AmountDue, "amount due", reconciliationErrors);
        Require(trendMonth.OpeningOutstanding.HasValue && trendMonth.IncreaseDuringPeriod.HasValue &&
            trendMonth.OpeningOutstanding.Value + trendMonth.IncreaseDuringPeriod.Value - trendMonth.ActualPayment == ending,
            "opening + increase - actual payment = ending", reconciliationErrors);
        Require(monthly.Totals.UnknownAmountDueContracts == 0, "known AmountDue coverage", reconciliationErrors);
        Require(queueSummary.AnalysisUniverseContracts == contracts.Count, "work queue universe", reconciliationErrors);
        Require(queueSummary.TotalQueueContracts == queueSummary.CollectionContracts + queueSummary.ReviewOnlyContracts,
            "work queue partition", reconciliationErrors);
        Require(queueSummary.ExpiredOutstandingContracts == expiredOutstanding.Length,
            "expired outstanding queue count", reconciliationErrors);
        Require(Money(queueSummary.ExpiredOutstandingAmount) == Money(expiredOutstanding.Sum(contract => contract.CurrentState.TotalOutstanding)),
            "expired outstanding queue amount", reconciliationErrors);
        Require(string.Equals(trend.DebtSnapshotId, debt.SnapshotId, StringComparison.Ordinal) &&
            string.Equals(monthly.DebtSnapshotId, debt.SnapshotId, StringComparison.Ordinal) &&
            string.Equals(queue.DebtSnapshotId, debt.SnapshotId, StringComparison.Ordinal),
            "Source A snapshot provenance", reconciliationErrors);
        Require(string.Equals(trend.InstallmentSnapshotId, installment.SnapshotId, StringComparison.Ordinal) &&
            string.Equals(monthly.InstallmentSnapshotId, installment.SnapshotId, StringComparison.Ordinal) &&
            string.Equals(queue.InstallmentSnapshotId, installment.SnapshotId, StringComparison.Ordinal),
            "Source B snapshot provenance", reconciliationErrors);

        if (reconciliationErrors.Count > 0)
        {
            return CurrentUnavailable(
                $"Current dashboard reconciliation failed: {string.Join(", ", reconciliationErrors)}. Published data was not substituted.",
                debt, installment, period);
        }

        var expectedPeriod = ParsePeriod(_options?.DefaultCurrentPeriod);
        var isStale = expectedPeriod.HasValue && period.Value < expectedPeriod.Value;
        var debtStatus = _debtAutoSync?.GetStatus();
        var installmentStatus = _installmentAutoSync?.GetStatus();

        return new DashboardSummaryDto
        {
            Mode = DashboardDataModes.Current,
            Available = true,
            Message = "Current dashboard is reconciled from the active Source A/B snapshots and existing operational analyses.",
            HasPublishedSnapshot = false,
            DataSource = CurrentOperationalDataSource,
            GeneratedAt = DateTime.UtcNow,
            IsReconciled = true,
            DataThroughPeriod = period,
            AsOfDate = asOfDate,
            DebtSnapshotId = debt.SnapshotId,
            SourceAFingerprint = debt.SourceFileHash,
            SourceASnapshotPublishedAt = debt.PublishedAtUtc,
            InstallmentSnapshotId = installment.SnapshotId,
            SourceBFingerprint = installment.SourceFileHash,
            SourceBSnapshotPublishedAt = installment.PublishedAtUtc,
            DebtLastSuccessfulSyncAt = debtStatus?.LastSuccessfulSyncAtUtc,
            InstallmentLastSuccessfulSyncAt = installmentStatus?.LastSuccessfulSyncAtUtc,
            IsStale = isStale,
            FreshnessWarning = isStale
                ? $"Current source is through {PeriodText(period.Value)}, earlier than configured current period {PeriodText(expectedPeriod!.Value)}."
                : null,
            TotalSourceRows = debt.Workbook.WorksheetRowCount,
            TotalContracts = contracts.Count,
            InTermContracts = inTerm.Length,
            ExpiredContracts = expired.Length,
            OutstandingContracts = outstanding.Length,
            PaidOffContracts = paidOff.Length,
            InTermOutstandingContracts = inTerm.Count(contract => contract.CurrentState.TotalOutstanding > 0m),
            InTermPaidOffContracts = inTerm.Count(contract => contract.CurrentState.TotalOutstanding <= 0m),
            ExpiredOutstandingContracts = expiredOutstanding.Length,
            ExpiredPaidOffContracts = expired.Count(contract => contract.CurrentState.TotalOutstanding <= 0m),
            WarningContracts = debt.DataQualityWarnings.Select(warning => warning.ContractKey).Distinct(StringComparer.Ordinal).Count(),
            PrincipalOutstanding = principal,
            ProfitOutstanding = profit,
            TotalOutstanding = ending,
            EndingOutstanding = ending,
            ExpiredOutstandingBalance = Money(expiredOutstanding.Sum(contract => contract.CurrentState.TotalOutstanding)),
            AmountDue = monthly.Totals.AmountDue,
            ActualPayment = monthly.Totals.ActualPayment,
            DownPaymentContracts = allocation.DownPaymentContracts,
            DownPaymentAmount = allocation.DownPaymentAmount,
            OpeningOutstanding = trendMonth.OpeningOutstanding,
            IncreaseDuringPeriod = trendMonth.IncreaseDuringPeriod,
            WorkQueue = MapQueue(queueSummary),
            DebtBuckets = analysis.BucketSummaries.Select(bucket => new DashboardDebtBucketDto
            {
                Bucket = bucket.Bucket.ToString(),
                Label = DebtLabels.Bucket(bucket.Bucket),
                ContractCount = bucket.ContractCount,
                OutstandingAmount = Money(bucket.OutstandingAmount)
            }).ToList(),
            ContractTypes = CurrentContractTypes(contracts)
        };
    }

    private async Task<DashboardSummaryDto> GetPublishedSummaryAsync(CancellationToken cancellationToken)
    {
        var summary = await _dbContext.PortfolioSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Status == PortfolioSnapshotStatus.Published)
            .Select(snapshot => new DashboardSummaryDto
            {
                Mode = DashboardDataModes.Published,
                Available = true,
                Message = "Dashboard is displaying the immutable Published Portfolio Snapshot.",
                HasPublishedSnapshot = true,
                DataSource = PublishedSnapshotDataSource,
                SnapshotId = snapshot.Id,
                AsOfDate = snapshot.AsOfDate,
                PublishedAt = snapshot.PublishedAt,
                GeneratedAt = DateTime.UtcNow,
                IsReconciled =
                    snapshot.TotalSourceRows == snapshot.TotalContractCount + snapshot.PlaceholderRowCount &&
                    snapshot.TotalContractCount == snapshot.InTermContractCount + snapshot.ExpiredContractCount &&
                    snapshot.TotalContractCount == snapshot.OutstandingContractCount + snapshot.PaidOffContractCount &&
                    snapshot.TotalContractCount == snapshot.InTermOutstandingContractCount +
                        snapshot.InTermPaidOffContractCount + snapshot.ExpiredOutstandingContractCount +
                        snapshot.ExpiredPaidOffContractCount &&
                    snapshot.PrincipalOutstanding + snapshot.ProfitOutstanding == snapshot.TotalOutstanding &&
                    snapshot.PrincipalDifference == 0m && snapshot.ProfitDifference == 0m &&
                    snapshot.TotalDifference == 0m && snapshot.ComponentDifference == 0m,
                TotalSourceRows = snapshot.TotalSourceRows,
                TotalContracts = snapshot.TotalContractCount,
                TemplatePlaceholderRows = snapshot.PlaceholderRowCount,
                InTermContracts = snapshot.InTermContractCount,
                ExpiredContracts = snapshot.ExpiredContractCount,
                OutstandingContracts = snapshot.OutstandingContractCount,
                PaidOffContracts = snapshot.PaidOffContractCount,
                InTermOutstandingContracts = snapshot.InTermOutstandingContractCount,
                InTermPaidOffContracts = snapshot.InTermPaidOffContractCount,
                ExpiredOutstandingContracts = snapshot.ExpiredOutstandingContractCount,
                ExpiredPaidOffContracts = snapshot.ExpiredPaidOffContractCount,
                WarningContracts = snapshot.WarningRecordCount,
                UnresolvedMemberContracts = snapshot.UnresolvedMemberContractCount,
                ShadowExcludedContracts = snapshot.ShadowExcludedCount,
                CanonicalMatchedContracts = snapshot.MatchedCanonicalCount,
                CanonicalMissingContracts = snapshot.MissingCanonicalCount,
                PrincipalOutstanding = snapshot.PrincipalOutstanding,
                ProfitOutstanding = snapshot.ProfitOutstanding,
                TotalOutstanding = snapshot.TotalOutstanding,
                EndingOutstanding = snapshot.TotalOutstanding,
                ExpiredOutstandingBalance = snapshot.ExpiredOutstandingTotal
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (summary is null)
        {
            return new DashboardSummaryDto
            {
                Mode = DashboardDataModes.Published,
                Available = false,
                HasPublishedSnapshot = false,
                Message = "No Published Portfolio Snapshot is available.",
                GeneratedAt = DateTime.UtcNow
            };
        }

        var snapshotId = summary.SnapshotId!.Value;
        var records = await _dbContext.PortfolioSnapshotRecords
            .AsNoTracking()
            .Where(record => record.PortfolioSnapshotId == snapshotId)
            .Select(record => new
            {
                record.LoanTypePrefix,
                record.TermStatus,
                record.BalanceStatus,
                record.PrincipalOutstanding,
                record.ProfitOutstanding,
                record.TotalOutstanding
            })
            .ToListAsync(cancellationToken);

        summary.PublishedRecordCount = records.Count;
        summary.ContractTypes = records
            .GroupBy(record => record.LoanTypePrefix, StringComparer.Ordinal)
            .Select(group => new ContractTypeAggregate
            {
                Prefix = group.Key,
                ContractCount = group.Count(),
                OutstandingContractCount = group.Count(record => record.BalanceStatus == PortfolioBalanceStatus.Outstanding),
                PaidOffContractCount = group.Count(record => record.BalanceStatus == PortfolioBalanceStatus.PaidOff),
                InTermCount = group.Count(record => record.TermStatus == PortfolioTermStatus.InTerm),
                ExpiredCount = group.Count(record => record.TermStatus == PortfolioTermStatus.Expired),
                PrincipalOutstanding = group.Sum(record => record.PrincipalOutstanding),
                ProfitOutstanding = group.Sum(record => record.ProfitOutstanding),
                TotalOutstanding = group.Sum(record => record.TotalOutstanding)
            })
            .Select(MapContractType)
            .GroupBy(group => new { group.Prefix, group.Name })
            .Select(group => MergeContractType(group.Key.Prefix, group.Key.Name, group))
            .OrderBy(group => group.Prefix == DashboardLoanTypeClassifier.UnknownPrefix)
            .ThenBy(group => group.Prefix, StringComparer.Ordinal)
            .ToList();
        return summary;
    }

    private DashboardSummaryDto CurrentUnavailable(
        string message,
        PublishedDebtSnapshot? debt = null,
        PublishedInstallmentMasterSnapshot? installment = null,
        DateOnly? period = null) => new()
    {
        Mode = DashboardDataModes.Current,
        Available = false,
        HasPublishedSnapshot = false,
        Message = message,
        DataSource = CurrentOperationalDataSource,
        GeneratedAt = DateTime.UtcNow,
        DataThroughPeriod = period,
        DebtSnapshotId = debt?.SnapshotId,
        SourceAFingerprint = debt?.SourceFileHash,
        SourceASnapshotPublishedAt = debt?.PublishedAtUtc,
        InstallmentSnapshotId = installment?.SnapshotId,
        SourceBFingerprint = installment?.SourceFileHash,
        SourceBSnapshotPublishedAt = installment?.PublishedAtUtc
    };

    private static void Require(bool condition, string identity, ICollection<string> errors)
    {
        if (!condition)
            errors.Add(identity);
    }

    private static DateOnly? ParsePeriod(string? value) =>
        DateOnly.TryParseExact(
            $"{value}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var period) ? period : null;

    private static string PeriodText(DateOnly value) => value.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static DashboardWorkQueueSummaryDto MapQueue(WorkQueueSummaryDto queue) => new()
    {
        AnalysisUniverseContracts = queue.AnalysisUniverseContracts,
        TotalQueueContracts = queue.TotalQueueContracts,
        CollectionContracts = queue.CollectionContracts,
        ReviewOnlyContracts = queue.ReviewOnlyContracts,
        UrgentContracts = Priority(queue, WorkQueuePriorities.Urgent),
        HighContracts = Priority(queue, WorkQueuePriorities.High),
        MediumContracts = Priority(queue, WorkQueuePriorities.Medium),
        ReviewContracts = Priority(queue, WorkQueuePriorities.Review)
    };

    private static int Priority(WorkQueueSummaryDto queue, string priority) =>
        queue.Priorities.SingleOrDefault(item => item.Priority == priority)?.ContractCount ?? 0;

    private static List<DashboardContractTypeDto> CurrentContractTypes(
        IReadOnlyList<DebtContractAnalysis> contracts) => contracts
        .GroupBy(contract => LoanTypeClassifier.Classify(contract.Contract.ContractNumber))
        .Select(group => new DashboardContractTypeDto
        {
            Prefix = group.Key.Prefix,
            Name = group.Key.Name,
            ContractCount = group.Count(),
            OutstandingContractCount = group.Count(contract => contract.CurrentState.TotalOutstanding > 0m),
            PaidOffContractCount = group.Count(contract => contract.CurrentState.TotalOutstanding <= 0m),
            InTermCount = group.Count(contract => !contract.Contract.ExpireDate.HasValue ||
                contract.Contract.ExpireDate > contract.CurrentPeriodEnd()),
            ExpiredCount = group.Count(contract => contract.Contract.ExpireDate <= contract.CurrentPeriodEnd()),
            PrincipalOutstanding = Money(group.Sum(contract => contract.CurrentState.PrincipalOutstanding)),
            ProfitOutstanding = Money(group.Sum(contract => contract.CurrentState.ProfitOutstanding)),
            TotalOutstanding = Money(group.Sum(contract => contract.CurrentState.TotalOutstanding))
        })
        .OrderBy(group => group.Prefix == DashboardLoanTypeClassifier.UnknownPrefix)
        .ThenBy(group => group.Prefix, StringComparer.Ordinal)
        .ToList();

    private static DashboardContractTypeDto MapContractType(ContractTypeAggregate aggregate)
    {
        var classification = LoanTypeClassifier.ClassifyPrefix(aggregate.Prefix);
        return new DashboardContractTypeDto
        {
            Prefix = classification.Prefix,
            Name = classification.Name,
            ContractCount = aggregate.ContractCount,
            OutstandingContractCount = aggregate.OutstandingContractCount,
            PaidOffContractCount = aggregate.PaidOffContractCount,
            InTermCount = aggregate.InTermCount,
            ExpiredCount = aggregate.ExpiredCount,
            PrincipalOutstanding = aggregate.PrincipalOutstanding,
            ProfitOutstanding = aggregate.ProfitOutstanding,
            TotalOutstanding = aggregate.TotalOutstanding
        };
    }

    private static DashboardContractTypeDto MergeContractType(
        string prefix,
        string name,
        IEnumerable<DashboardContractTypeDto> group) => new()
    {
        Prefix = prefix,
        Name = name,
        ContractCount = group.Sum(item => item.ContractCount),
        OutstandingContractCount = group.Sum(item => item.OutstandingContractCount),
        PaidOffContractCount = group.Sum(item => item.PaidOffContractCount),
        InTermCount = group.Sum(item => item.InTermCount),
        ExpiredCount = group.Sum(item => item.ExpiredCount),
        PrincipalOutstanding = group.Sum(item => item.PrincipalOutstanding),
        ProfitOutstanding = group.Sum(item => item.ProfitOutstanding),
        TotalOutstanding = group.Sum(item => item.TotalOutstanding)
    };

    private sealed class ContractTypeAggregate
    {
        public string Prefix { get; set; } = string.Empty;
        public int ContractCount { get; set; }
        public int OutstandingContractCount { get; set; }
        public int PaidOffContractCount { get; set; }
        public int InTermCount { get; set; }
        public int ExpiredCount { get; set; }
        public decimal PrincipalOutstanding { get; set; }
        public decimal ProfitOutstanding { get; set; }
        public decimal TotalOutstanding { get; set; }
    }
}

internal static class DashboardDebtContractAnalysisExtensions
{
    public static DateOnly CurrentPeriodEnd(this DebtContractAnalysis contract) =>
        contract.CurrentState.Period.AddMonths(1).AddDays(-1);
}
