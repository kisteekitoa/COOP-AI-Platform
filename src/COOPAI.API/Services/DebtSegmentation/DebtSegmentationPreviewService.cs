using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using COOPAI.API.DTOs.DebtSegmentation;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.DebtSegmentation;

public interface IDebtAutoSyncPipeline
{
    Task<DebtSyncStatusDto> SyncAutoAsync(CancellationToken cancellationToken = default);
}

public interface IDebtSegmentationPreviewService : IDebtAutoSyncPipeline
{
    Task<DebtSyncStatusDto> SyncNowAsync(string? currentPeriod, CancellationToken cancellationToken = default);
    Task<DebtPreviewDashboardDto> GetDashboardAsync(string? currentPeriod, CancellationToken cancellationToken = default);
    Task<DebtContractPageDto> GetContractsAsync(
        string? currentPeriod, string? bucket, string? paymentStatus, string? loanType,
        string? groupCode, string? search, string? paymentMovement, string? previousBucket,
        string? currentBucket, string? movementCategory, int page, int pageSize,
        string? branch = null, bool sortTotalDescending = true,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DebtSyncHistoryDto>> GetSyncHistoryAsync(
        int take,
        CancellationToken cancellationToken = default);
}

public sealed class DebtSegmentationPreviewService : IDebtSegmentationPreviewService
{
    private readonly DebtSegmentationPreviewOptions _options;
    private readonly OperationalDebtWorkbookReader _reader;
    private readonly DebtSegmentationAnalyzer _analyzer;
    private readonly IDebtSnapshotStore _snapshotStore;
    private readonly IDebtWorkbookLocator _workbookLocator;
    private readonly IDebtWorkbookAcquirer _workbookAcquirer;
    private readonly SemaphoreSlim _syncLock;
    private PublishedDebtSnapshot? _publishedSnapshot;
    private bool _historyRestored;
    private string? _observedFileIdentity;
    private DebtSyncResult _lastSync = new(
        false, false, false, DebtSyncTrigger.Automatic, "Debt Sync has not run.", string.Empty,
        null, null, 0, 0, 0, 0, 0, [], []);

    public DebtSegmentationPreviewService(
        IOptions<DebtSegmentationPreviewOptions> options,
        IHostEnvironment environment,
        OperationalDebtWorkbookReader reader,
        DebtSegmentationAnalyzer analyzer,
        IDebtSnapshotStore? snapshotStore = null,
        IDebtWorkbookLocator? workbookLocator = null,
        IDebtWorkbookAcquirer? workbookAcquirer = null,
        PreviewSyncGate? syncGate = null)
    {
        _options = options.Value;
        _reader = reader;
        _analyzer = analyzer;
        _snapshotStore = snapshotStore ?? new InMemoryDebtSnapshotStore();
        _workbookLocator = workbookLocator ?? new DebtWorkbookLocator(options, environment, reader);
        _workbookAcquirer = workbookAcquirer ?? new DebtWorkbookAcquirer(options, environment);
        _syncLock = (syncGate ?? new PreviewSyncGate()).Semaphore;
    }

    public async Task<IReadOnlyList<DebtSyncHistoryDto>> GetSyncHistoryAsync(
        int take,
        CancellationToken cancellationToken = default) =>
        (await _snapshotStore.ListSyncHistoryAsync(take, cancellationToken))
        .Select(MapHistory)
        .ToArray();

    public async Task<DebtSyncStatusDto> SyncNowAsync(
        string? currentPeriod, CancellationToken cancellationToken = default)
    {
        var current = ParsePeriod(currentPeriod ?? _options.DefaultCurrentPeriod);
        return MapSync(await SyncAsync(
            current, DebtSyncTrigger.Manual, true, rejectIfBusy: true,
            useWorkbookPeriod: false, cancellationToken));
    }

    public async Task<DebtSyncStatusDto> SyncAutoAsync(CancellationToken cancellationToken = default)
    {
        var configuredCurrent = ParsePeriod(_options.DefaultCurrentPeriod);
        return MapSync(await SyncAsync(
            configuredCurrent, DebtSyncTrigger.Automatic, true, rejectIfBusy: true,
            useWorkbookPeriod: true, cancellationToken));
    }

    public async Task<DebtPreviewDashboardDto> GetDashboardAsync(
        string? currentPeriod, CancellationToken cancellationToken = default)
    {
        var current = ParsePeriod(currentPeriod ?? _options.DefaultCurrentPeriod);
        var previous = current.AddMonths(-1);
        var sync = await PrepareQueryAsync(current, cancellationToken);
        var snapshot = _publishedSnapshot;
        if (snapshot is null)
            return UnavailableDashboard(null, previous, current, sync.Message, sync);

        var policyMessage = PolicyAvailabilityMessage(snapshot);
        if (policyMessage is not null)
            return UnavailableDashboard(snapshot, previous, current, policyMessage, sync);

        var workbook = snapshot.Workbook;
        var availabilityMessage = AvailabilityMessage(workbook, previous, current);
        if (availabilityMessage is not null)
            return UnavailableDashboard(snapshot, previous, current, availabilityMessage, sync);

        var analysis = _analyzer.Analyze(workbook.Contracts, previous, current);
        var totals = new DebtPreviewTotalsDto(
            analysis.Contracts.Select(x => x.Contract.MemberKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            analysis.Contracts.Count,
            analysis.Contracts.Sum(x => x.CurrentState.PrincipalOutstanding),
            analysis.Contracts.Sum(x => x.CurrentState.ProfitOutstanding),
            analysis.Contracts.Sum(x => x.CurrentState.TotalOutstanding),
            analysis.Contracts.Count(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.Paid),
            analysis.Contracts.Count(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.NotPaid),
            analysis.Contracts.Count(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.NotDue));
        return new DebtPreviewDashboardDto(
            true,
            sync.Success ? "Published Debt Snapshot is available." : $"Last valid Debt Snapshot retained. {sync.Message}",
            workbook.SourceFileName,
            snapshot.SourceFileHash,
            snapshot.PublishedAtUtc,
            workbook.WorksheetName,
            workbook.DataThroughPeriod,
            snapshot.PolicyVersion,
            _options.AutoSyncOnQueryEnabled,
            previous,
            current,
            workbook.Warnings.Concat(snapshot.ValidationWarnings).Distinct(StringComparer.Ordinal).ToArray(),
            snapshot.DataQualityWarnings
                .GroupBy(x => x.Code, StringComparer.Ordinal)
                .Select(group => new DebtDataQualityWarningSummaryDto(
                    group.Key,
                    group.Select(x => x.ContractKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    group.Count()))
                .OrderBy(x => x.Code, StringComparer.Ordinal)
                .ToArray(),
            totals,
            MapSync(sync),
            analysis.Contracts.Select(x => x.Contract.LoanType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
            analysis.Contracts.Select(x => x.Contract.Branch).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
            analysis.BucketSummaries.Select(x => new DebtBucketSummaryDto(
                DebtLabels.Bucket(x.Bucket), DebtLabels.BucketThai(x.Bucket), x.MemberCount, x.ContractCount,
                x.PrincipalOutstanding, x.ProfitOutstanding, x.OutstandingAmount,
                x.PaidMemberCount, x.PaidContractCount, x.NotPaidMemberCount, x.NotPaidContractCount,
                x.NotDueMemberCount, x.NotDueContractCount)).ToArray(),
            analysis.BucketComparisons.Select(MapBucketComparison).ToArray(),
            analysis.PaymentMovements.Select(MapMovement).ToArray(),
            analysis.BucketMovements.Select(MapMovement).ToArray());
    }

    public async Task<DebtContractPageDto> GetContractsAsync(
        string? currentPeriod, string? bucket, string? paymentStatus, string? loanType,
        string? groupCode, string? search, string? paymentMovement, string? previousBucket,
        string? currentBucket, string? movementCategory, int page, int pageSize,
        string? branch = null, bool sortTotalDescending = true,
        CancellationToken cancellationToken = default)
    {
        var current = ParsePeriod(currentPeriod ?? _options.DefaultCurrentPeriod);
        var previous = current.AddMonths(-1);
        var sync = await PrepareQueryAsync(current, cancellationToken);
        var snapshot = _publishedSnapshot;
        if (snapshot is null)
            return new DebtContractPageDto(false, sync.Message, 1, Math.Clamp(pageSize, 1, 200), 0, []);

        var policyMessage = PolicyAvailabilityMessage(snapshot);
        if (policyMessage is not null)
            return new DebtContractPageDto(false, policyMessage, 1, Math.Clamp(pageSize, 1, 200), 0, []);

        var availabilityMessage = AvailabilityMessage(snapshot.Workbook, previous, current);
        if (availabilityMessage is not null)
            return new DebtContractPageDto(false, availabilityMessage, 1, Math.Clamp(pageSize, 1, 200), 0, []);

        IEnumerable<DebtContractAnalysis> query = _analyzer.Analyze(snapshot.Workbook.Contracts, previous, current).Contracts;
        query = ApplyLabelFilter(query, bucket, x => DebtLabels.Bucket(x.CurrentClassification.Bucket));
        query = ApplyLabelFilter(query, paymentStatus, x => DebtLabels.Payment(x.CurrentClassification.LatestPaymentStatus));
        query = ApplyLabelFilter(query, loanType, x => x.Contract.LoanType);
        query = ApplyLabelFilter(query, groupCode, x => x.Contract.GroupCode ?? string.Empty);
        query = ApplyLabelFilter(query, branch, x => x.Contract.Branch ?? string.Empty);
        query = ApplyLabelFilter(query, paymentMovement, x => x.PaymentMovement);
        query = ApplyLabelFilter(query, previousBucket, x => DebtLabels.Bucket(x.PreviousClassification.Bucket));
        query = ApplyLabelFilter(query, currentBucket, x => DebtLabels.Bucket(x.CurrentClassification.Bucket));
        query = ApplyLabelFilter(query, movementCategory, x => DebtLabels.Category(x.MovementCategory));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Contract.MemberCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Contract.MemberName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Contract.ContractNumber.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var matches = (sortTotalDescending
                ? query.OrderByDescending(x => x.CurrentState.TotalOutstanding)
                : query.OrderBy(x => x.CurrentState.TotalOutstanding))
            .ThenBy(x => x.Contract.MemberCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Contract.ContractNumber, StringComparer.OrdinalIgnoreCase).ToArray();
        var warningsByContract = snapshot.DataQualityWarnings
            .GroupBy(x => x.ContractKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<DebtDataQualityWarning>)x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var items = matches.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => MapContract(
                item,
                current,
                warningsByContract.GetValueOrDefault(item.Contract.ContractKey) ?? []))
            .ToArray();
        return new DebtContractPageDto(true, "Published Debt Snapshot is available.", page, pageSize, matches.Length, items);
    }

    private async Task<DebtSyncResult> PrepareQueryAsync(
        DateOnly current,
        CancellationToken cancellationToken)
    {
        if (_options.AutoSyncOnQueryEnabled)
            return await SyncAsync(
                current, DebtSyncTrigger.Automatic, false, rejectIfBusy: false,
                useWorkbookPeriod: false, cancellationToken);

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var restoreFailure = await RestorePublishedSnapshotAsync(DebtSyncTrigger.Automatic, cancellationToken);
            if (restoreFailure is not null)
                return restoreFailure;
            if (_publishedSnapshot is null)
            {
                return Failure(
                    DebtSyncTrigger.Automatic,
                    "Automatic sync on query is disabled and no Published Debt Snapshot is available; use Sync Now.",
                    string.Empty,
                    [],
                    []);
            }

            var snapshot = _publishedSnapshot;
            return WithWorkbookMetrics(new DebtSyncResult(
                true, false, true, DebtSyncTrigger.Automatic,
                "Automatic sync on query is disabled; the current Published Debt Snapshot was retained.",
                snapshot.SourceFileName, snapshot.SourceFileHash, snapshot.PublishedAtUtc,
                snapshot.Workbook.WorksheetRowCount, snapshot.Workbook.Contracts.Count,
                0, 0, 0, [], snapshot.ValidationWarnings)
            {
                Status = DebtSyncStatuses.NoChange,
                PreviousPeriod = current.AddMonths(-1),
                CurrentPeriod = current,
                SnapshotId = snapshot.SnapshotId,
                SourceType = snapshot.SourceType
            }, snapshot.Workbook, current);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<DebtSyncResult> SyncAsync(
        DateOnly current,
        DebtSyncTrigger trigger,
        bool force,
        bool rejectIfBusy,
        bool useWorkbookPeriod,
        CancellationToken cancellationToken)
    {
        var attemptedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var sourceType = DebtWorkbookAcquirer.IsNetworkPath(_options.WorkbookPath)
            ? DebtSourceTypes.Network
            : DebtSourceTypes.Local;
        async Task<DebtSyncResult> CompleteAsync(DebtSyncResult result)
        {
            result = result with
            {
                Status = string.IsNullOrWhiteSpace(result.Status)
                    ? result.Success
                        ? result.Published ? DebtSyncStatuses.Success : DebtSyncStatuses.NoChange
                        : DebtSyncStatuses.Failed
                    : result.Status,
                AttemptedAtUtc = attemptedAt,
                PreviousPeriod = current.AddMonths(-1),
                CurrentPeriod = current,
                SnapshotId = result.SnapshotId ?? _publishedSnapshot?.SnapshotId,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                SourceType = sourceType
            };
            return await RecordAsync(result, cancellationToken);
        }

        var acquired = rejectIfBusy
            ? await _syncLock.WaitAsync(0, cancellationToken)
            : await WaitForSyncLockAsync(cancellationToken);
        if (!acquired)
        {
            var busy = Failure(
                trigger,
                "A Debt Sync is already running. Wait for it to finish before trying again.",
                string.Empty,
                ["An overlapping manual sync was rejected; no workbook was read and no snapshot was changed."],
                []) with { Status = DebtSyncStatuses.Busy };
            if (_publishedSnapshot is not null)
                busy = WithWorkbookMetrics(busy, _publishedSnapshot.Workbook, current);
            return await CompleteAsync(busy);
        }
        try
        {
            var restoreFailure = await RestorePublishedSnapshotAsync(trigger, cancellationToken);
            if (restoreFailure is not null)
                return restoreFailure;

            if (!_options.Enabled)
                return await CompleteAsync(Failure(trigger, "Debt segmentation Preview is disabled.", string.Empty, [], []));

            var location = _workbookLocator.Locate();
            if (!location.Found)
            {
                return await CompleteAsync(Failure(
                    trigger,
                    "No completed workbook matching the operational schema was found.",
                    Path.GetFileName(location.Path),
                    location.Diagnostics,
                    []));
            }

            DebtWorkbookAcquisition acquisition;
            try
            {
                acquisition = await _workbookAcquirer.AcquireAsync(location, cancellationToken);
            }
            catch (DebtWorkbookAcquisitionException exception)
            {
                sourceType = exception.SourceType;
                return await CompleteAsync(SetFailure(
                    trigger, exception.SourceFileName, exception.Identity, exception.Message,
                    [exception.Message], []));
            }

            sourceType = acquisition.SourceType;
            var identity = acquisition.Identity;
            if (!force && _publishedSnapshot is not null && string.Equals(_observedFileIdentity, identity, StringComparison.Ordinal))
            {
                await acquisition.DisposeAsync();
                return _lastSync;
            }
            if (_publishedSnapshot is not null &&
                acquisition.SourceLastWriteTimeUtc < _publishedSnapshot.SourceLastWriteTimeUtc)
            {
                await acquisition.DisposeAsync();
                return await CompleteAsync(SetFailure(
                    trigger, acquisition.SourceFileName, identity,
                    "The newest completed operational workbook is older than the current publication; the current publication was retained.",
                    ["A workbook with an older modified time cannot replace newer published debt data."], []));
            }

            DebtWorkbookReadResult staged;
            var sourceHash = acquisition.SourceHash;
            try
            {
                await using (acquisition)
                {
                    staged = _reader.Read(
                        acquisition.LocalPath,
                        allowLegacyDiagnostic: acquisition.SourceType == DebtSourceTypes.Network) with
                    {
                        SourceFileName = acquisition.SourceFileName
                    };
                }
                if (useWorkbookPeriod && staged.DataThroughPeriod.HasValue)
                    current = staged.DataThroughPeriod.Value;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await CompleteAsync(SetFailure(trigger, acquisition.SourceFileName, identity,
                    "The acquired workbook could not be read using the operational debt schema.",
                    [$"Workbook schema/read validation failed: {exception.Message}"], []));
            }

            var validation = ValidateStaged(staged, current);
            var validationWarnings = WarningMessages(validation.Warnings);
            var allWarnings = staged.Warnings.Concat(validationWarnings).Distinct(StringComparer.Ordinal).ToArray();
            if (validation.Errors.Count > 0)
                return await CompleteAsync(WithWorkbookMetrics(SetFailure(trigger, staged.SourceFileName, identity,
                    "Debt Sync validation failed; the previous Published Debt Snapshot was retained.",
                    validation.Errors, allWarnings), staged, current));

            DebtSegmentationAnalysis analysis;
            try
            {
                analysis = _analyzer.Analyze(staged.Contracts, current.AddMonths(-1), current);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await CompleteAsync(WithWorkbookMetrics(SetFailure(
                    trigger, staged.SourceFileName, identity,
                    "Debt analysis could not be completed; the previous valid Debt Snapshot was retained.",
                    [$"Debt analysis failed: {exception.Message}"], allWarnings), staged, current));
            }

            var reconciliationErrors = ValidateReconciliation(staged, analysis, current);
            if (reconciliationErrors.Count > 0)
            {
                return await CompleteAsync(WithAnalysisMetrics(SetFailure(
                    trigger, staged.SourceFileName, identity,
                    "Debt Sync reconciliation failed; the previous valid Debt Snapshot was retained.",
                    reconciliationErrors, allWarnings), staged, analysis));
            }

            var previous = _publishedSnapshot;
            if (previous is not null && string.Equals(previous.SourceFileHash, sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                _observedFileIdentity = identity;
                _lastSync = WithAnalysisMetrics(new DebtSyncResult(
                    true, false, true, trigger, "Workbook content is unchanged; no duplicate Debt Snapshot was published.",
                    staged.SourceFileName, sourceHash, previous.PublishedAtUtc, staged.WorksheetRowCount,
                    staged.Contracts.Count, 0, 0, 0, [], allWarnings)
                {
                    Status = DebtSyncStatuses.NoChange,
                    SnapshotId = previous.SnapshotId
                }, staged, analysis);
                return await CompleteAsync(_lastSync);
            }

            var (added, removed, changed) = Compare(previous?.Workbook.Contracts, staged.Contracts);
            var publishedAt = DateTime.UtcNow;
            var candidate = new PublishedDebtSnapshot(
                $"{_analyzer.PolicyVersion}-{sourceHash}",
                _analyzer.PolicyVersion,
                staged.SourceFileName, sourceHash, acquisition.SourceSizeBytes, acquisition.SourceLastWriteTimeUtc,
                publishedAt, staged, validation.Warnings, validationWarnings)
            {
                SourceType = sourceType
            };
            try
            {
                await _snapshotStore.PublishAsync(candidate, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await CompleteAsync(WithAnalysisMetrics(SetFailure(
                    trigger, staged.SourceFileName, identity,
                    "Validated candidate could not be committed to Preview history; the previous publication was retained.",
                    [$"Preview history publish failed: {exception.Message}"], allWarnings), staged, analysis));
            }

            _publishedSnapshot = candidate;
            _observedFileIdentity = identity;
            _lastSync = WithAnalysisMetrics(new DebtSyncResult(
                true, true, false, trigger, "Workbook validated and the new Debt Snapshot was published atomically.",
                staged.SourceFileName, sourceHash, publishedAt, staged.WorksheetRowCount, staged.Contracts.Count,
                added, removed, changed, [], allWarnings)
            {
                Status = DebtSyncStatuses.Success,
                SnapshotId = candidate.SnapshotId
            }, staged, analysis);
            return await CompleteAsync(_lastSync);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await CompleteAsync(Failure(
                trigger,
                "Debt Sync failed unexpectedly; the previous valid Debt Snapshot was retained.",
                string.Empty,
                [$"Unexpected Preview sync failure: {exception.Message}"],
                []));
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<bool> WaitForSyncLockAsync(CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken);
        return true;
    }

    private async Task<DebtSyncResult?> RestorePublishedSnapshotAsync(
        DebtSyncTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (_historyRestored)
            return null;
        try
        {
            _publishedSnapshot = await _snapshotStore.GetCurrentAsync(cancellationToken);
            _historyRestored = true;
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _historyRestored = true;
            return Failure(
                trigger,
                "Published Preview debt history could not be restored; no candidate was published.",
                string.Empty,
                [$"History restore failed: {exception.Message}"],
                []);
        }
    }

    private DebtSyncResult SetFailure(
        DebtSyncTrigger trigger, string sourceFileName, string identity, string message,
        IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        _observedFileIdentity = identity;
        _lastSync = Failure(trigger, message, sourceFileName, errors, warnings);
        return _lastSync;
    }

    private async Task<DebtSyncResult> RecordAsync(
        DebtSyncResult result,
        CancellationToken cancellationToken)
    {
        _lastSync = result;
        try
        {
            await _snapshotStore.RecordSyncAsync(new DebtSyncHistoryEntry(
                result.AttemptedAtUtc == default ? DateTime.UtcNow : result.AttemptedAtUtc,
                result.Success, result.Published, result.PreviousSnapshotPreserved,
                TriggerLabel(result.Trigger), result.Message, result.SourceFileName, result.SourceFileHash,
                result.PublishedAtUtc, result.SourceRows, result.ContractCount, result.AddedContracts,
                result.RemovedContracts, result.ChangedContracts, result.ValidationErrors, result.Warnings)
            {
                Status = SyncStatus(result.Status, result.Success, result.Published),
                PreviousPeriod = result.PreviousPeriod,
                CurrentPeriod = result.CurrentPeriod,
                CandidateRows = result.CandidateRows,
                ExcludedRows = result.ExcludedRows,
                MemberCount = result.MemberCount,
                OutstandingAmount = result.OutstandingAmount,
                NewContracts = result.NewContracts,
                PaidOffContracts = result.PaidOffContracts,
                SnapshotId = result.SnapshotId,
                DurationMilliseconds = result.DurationMilliseconds,
                SourceType = result.SourceType
            },
                cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _lastSync = result with
            {
                Warnings = result.Warnings.Concat(
                    [$"Sync result history could not be retained: {exception.Message}"]).ToArray()
            };
            return _lastSync;
        }
    }

    private DebtSyncResult Failure(
        DebtSyncTrigger trigger, string message, string sourceFileName,
        IReadOnlyList<string> errors, IReadOnlyList<string> warnings) => new(
        false, false, _publishedSnapshot is not null, trigger, message, sourceFileName, null,
        _publishedSnapshot?.PublishedAtUtc, 0, 0, 0, 0, 0, errors, warnings)
        {
            Status = DebtSyncStatuses.Failed,
            SnapshotId = _publishedSnapshot?.SnapshotId
        };

    private static DebtSyncResult WithWorkbookMetrics(
        DebtSyncResult result,
        DebtWorkbookReadResult workbook,
        DateOnly current)
    {
        var currentStates = workbook.Contracts
            .Select(contract => contract.MonthlyStates.GetValueOrDefault(current))
            .Where(state => state is not null)
            .Select(state => state!)
            .ToArray();
        return result with
        {
            CandidateRows = workbook.Contracts.Count + workbook.ExcludedContractRows,
            ExcludedRows = workbook.ExcludedContractRows,
            MemberCount = workbook.Contracts.Select(x => x.MemberKey)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            OutstandingAmount = currentStates.Sum(x => x.TotalOutstanding)
        };
    }

    private static DebtSyncResult WithAnalysisMetrics(
        DebtSyncResult result,
        DebtWorkbookReadResult workbook,
        DebtSegmentationAnalysis analysis) =>
        WithWorkbookMetrics(result, workbook, analysis.CurrentPeriod) with
        {
            MemberCount = analysis.Contracts.Select(x => x.Contract.MemberKey)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            OutstandingAmount = analysis.Contracts.Sum(x => x.CurrentState.TotalOutstanding),
            NewContracts = analysis.Contracts.Count(x => x.MovementCategory == DebtMovementCategory.NewContract),
            PaidOffContracts = analysis.Contracts.Count(x => x.MovementCategory == DebtMovementCategory.ClosedPaidOff)
        };

    internal static IReadOnlyList<string> ValidateReconciliation(
        DebtWorkbookReadResult workbook,
        DebtSegmentationAnalysis analysis,
        DateOnly current)
    {
        var errors = new List<string>();
        var analyzedCount = analysis.Contracts.Count;
        var analyzedOutstanding = analysis.Contracts.Sum(x => x.CurrentState.TotalOutstanding);
        var bucketCount = analysis.BucketSummaries.Sum(x => x.ContractCount);
        var bucketOutstanding = analysis.BucketSummaries.Sum(x => x.OutstandingAmount);
        var paymentMovementCount = analysis.PaymentMovements.Sum(x => x.ContractCount);
        var bucketMovementCount = analysis.BucketMovements.Sum(x => x.ContractCount);

        if (analysis.PreviousPeriod != current.AddMonths(-1) || analysis.CurrentPeriod != current)
            errors.Add("Analysis periods do not match the requested previous/current period ordering.");
        if (analyzedCount != workbook.Contracts.Count)
            errors.Add($"Analyzed contract count {analyzedCount} does not match included contract count {workbook.Contracts.Count}.");
        if (analysis.Contracts.Select(x => x.Contract.ContractKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != analyzedCount)
            errors.Add("Analyzed contracts do not have a one-to-one stable contract identity.");
        if (analysis.Contracts.Any(x => x.CurrentState.Period != current))
            errors.Add("One or more analyzed current facts belong to a different period.");
        if (bucketCount != analyzedCount)
            errors.Add($"Bucket contract count {bucketCount} does not reconcile to analyzed count {analyzedCount}.");
        if (Math.Abs(bucketOutstanding - analyzedOutstanding) > 0.01m)
            errors.Add($"Bucket outstanding {bucketOutstanding} does not reconcile to analyzed outstanding {analyzedOutstanding}.");
        if (paymentMovementCount != analyzedCount)
            errors.Add($"Payment movement count {paymentMovementCount} does not reconcile to analyzed count {analyzedCount}.");
        if (bucketMovementCount != analyzedCount)
            errors.Add($"Bucket movement count {bucketMovementCount} does not reconcile to analyzed count {analyzedCount}.");
        return errors;
    }

    private static string SyncStatus(string status, bool success, bool published) =>
        !string.IsNullOrWhiteSpace(status)
            ? status
            : success
                ? published ? DebtSyncStatuses.Success : DebtSyncStatuses.NoChange
                : DebtSyncStatuses.Failed;

    private static string TriggerLabel(DebtSyncTrigger trigger) =>
        trigger == DebtSyncTrigger.Automatic ? "Auto" : "Manual";

    private static DebtStagedValidation ValidateStaged(DebtWorkbookReadResult staged, DateOnly current)
    {
        var errors = new List<string>();
        var warnings = new List<DebtDataQualityWarning>();
        var previous = current.AddMonths(-1);
        if (staged.Contracts.Count == 0)
            errors.Add("No analyzable contracts were found.");
        if (!staged.DataThroughPeriod.HasValue || staged.DataThroughPeriod.Value < current)
            errors.Add($"Workbook is not proven through requested period {current:yyyy-MM}.");
        var duplicateKeys = staged.Contracts.GroupBy(x => x.ContractKey, StringComparer.OrdinalIgnoreCase)
            .Where(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1)
            .Select(x => string.IsNullOrWhiteSpace(x.Key) ? "<blank>" : x.Key).Take(10).ToArray();
        if (duplicateKeys.Length > 0)
            errors.Add($"Duplicate or blank stable contract identities were found: {string.Join(", ", duplicateKeys)}.");
        var missingPeriods = staged.Contracts.Count(x =>
            !x.MonthlyStates.ContainsKey(previous) || !x.MonthlyStates.ContainsKey(current));
        if (missingPeriods > 0)
            errors.Add($"{missingPeriods} contracts are missing {previous:yyyy-MM} or {current:yyyy-MM} facts.");
        var evaluatedStates = staged.Contracts
            .SelectMany(contract => contract.MonthlyStates.Values
                .Where(state => state.Period <= current)
                .Select(state => (Contract: contract, State: state)))
            .ToArray();
        var blockingNegativeStates = new List<string>();
        foreach (var (contract, state) in evaluatedStates)
        {
            var hasNegativeComponent = state.PrincipalOutstanding < 0m || state.ProfitOutstanding < 0m;
            var reconciles = Math.Abs(
                (state.PrincipalOutstanding + state.ProfitOutstanding) - state.TotalOutstanding) <= 0.01m;
            var hasNegativePaymentInput = state.Provenance is not null &&
                (state.Provenance.PrincipalPaymentInput < 0m ||
                 state.Provenance.ProfitPaymentInput < 0m ||
                 state.Provenance.TotalPaymentInput < 0m);
            var hasBlockingNegative = state.PaymentAmount < 0m || state.TotalOutstanding < 0m ||
                hasNegativePaymentInput;

            if (hasNegativePaymentInput && TryAddReconciledPaymentWarnings(contract, state, warnings))
                hasBlockingNegative = state.PaymentAmount < 0m || state.TotalOutstanding < 0m;

            if (hasNegativeComponent && reconciles && state.TotalOutstanding >= 0m &&
                state.PaymentAmount >= 0m)
            {
                if (!TryAddReconciledWarning(contract, state, "Principal", state.PrincipalOutstanding,
                        state.Provenance?.PrincipalOutstanding, warnings) ||
                    !TryAddReconciledWarning(contract, state, "Profit", state.ProfitOutstanding,
                        state.Provenance?.ProfitOutstanding, warnings))
                {
                    hasBlockingNegative = true;
                }
            }
            else if (hasNegativeComponent)
            {
                hasBlockingNegative = true;
            }

            if (hasBlockingNegative)
            {
                blockingNegativeStates.Add(
                    $"row {contract.SourceRowNumber}, contract {contract.ContractNumber}, period {state.Period:yyyy-MM} " +
                    $"(payment principal={state.Provenance?.PrincipalPaymentInput}, payment profit={state.Provenance?.ProfitPaymentInput}, " +
                    $"payment total={state.Provenance?.TotalPaymentInput}, outstanding principal={state.PrincipalOutstanding}, " +
                    $"profit={state.ProfitOutstanding}, total={state.TotalOutstanding})");
            }
        }
        if (blockingNegativeStates.Count > 0)
        {
            errors.Add($"{blockingNegativeStates.Count} monthly facts contain blocking negative business values.");
            errors.AddRange(blockingNegativeStates.Take(10).Select(x => $"Blocking negative source value: {x}."));
        }
        var componentMismatches = evaluatedStates.Count(x =>
            Math.Abs((x.State.PrincipalOutstanding + x.State.ProfitOutstanding) - x.State.TotalOutstanding) > 0.01m);
        if (componentMismatches > 0)
            errors.Add($"{componentMismatches} monthly facts do not reconcile principal + profit to total.");
        return new DebtStagedValidation(errors, warnings);
    }

    private static bool TryAddReconciledWarning(
        DebtContractHistory contract,
        DebtMonthlyState state,
        string field,
        decimal componentValue,
        DebtCellProvenance? componentProvenance,
        ICollection<DebtDataQualityWarning> warnings)
    {
        if (componentValue >= 0m)
            return true;
        if (state.Provenance is null || componentProvenance is null || !componentProvenance.IsFormulaDerived)
            return false;
        if (state.Provenance.PrincipalPaymentInput < 0m ||
            state.Provenance.ProfitPaymentInput < 0m ||
            state.Provenance.TotalPaymentInput < 0m)
            return false;

        warnings.Add(new DebtDataQualityWarning(
            DebtDataQualityWarningCodes.FormulaDerivedNegativeComponentReconciled,
            contract.ContractKey,
            contract.ContractNumber,
            contract.SourceRowNumber,
            state.Period,
            field,
            componentValue,
            state.PrincipalOutstanding,
            state.ProfitOutstanding,
            state.TotalOutstanding,
            state.Provenance.PrincipalPaymentInput,
            state.Provenance.ProfitPaymentInput,
            state.Provenance.TotalPaymentInput,
            componentProvenance.CellAddress,
            componentProvenance.Formula!,
            componentProvenance.FormulaR1C1));
        return true;
    }

    private static bool TryAddReconciledPaymentWarnings(
        DebtContractHistory contract,
        DebtMonthlyState state,
        ICollection<DebtDataQualityWarning> warnings)
    {
        var provenance = state.Provenance;
        if (provenance?.TotalPaymentInput is null || provenance.TotalPaymentInput < 0m ||
            Math.Abs((provenance.PrincipalPaymentInput + provenance.ProfitPaymentInput) -
                     provenance.TotalPaymentInput.Value) > 0.01m)
            return false;

        var negativeInputs = new[]
        {
            (Field: "PrincipalPayment", Value: provenance.PrincipalPaymentInput, Cell: provenance.PrincipalPayment),
            (Field: "ProfitPayment", Value: provenance.ProfitPaymentInput, Cell: provenance.ProfitPayment)
        }.Where(x => x.Value < 0m).ToArray();
        if (negativeInputs.Length == 0)
            return false;

        foreach (var input in negativeInputs)
        {
            warnings.Add(new DebtDataQualityWarning(
                DebtDataQualityWarningCodes.NegativePaymentComponentReconciled,
                contract.ContractKey,
                contract.ContractNumber,
                contract.SourceRowNumber,
                state.Period,
                input.Field,
                input.Value,
                state.PrincipalOutstanding,
                state.ProfitOutstanding,
                state.TotalOutstanding,
                provenance.PrincipalPaymentInput,
                provenance.ProfitPaymentInput,
                provenance.TotalPaymentInput,
                input.Cell.CellAddress,
                input.Cell.Formula ?? string.Empty,
                input.Cell.FormulaR1C1));
        }

        return true;
    }

    private static IReadOnlyList<string> WarningMessages(IReadOnlyList<DebtDataQualityWarning> warnings) =>
        warnings.GroupBy(x => x.Code, StringComparer.Ordinal)
            .Select(group => group.Key == DebtDataQualityWarningCodes.NegativePaymentComponentReconciled
                ? $"{group.Key}: {group.Count()} negative payment components across " +
                  $"{group.Select(x => x.ContractKey).Distinct(StringComparer.OrdinalIgnoreCase).Count()} contracts " +
                  "were retained because principal + profit reconcile to a nonnegative payment total within 0.01; original values and formulas remain visible."
                : $"{group.Key}: {group.Count()} formula-derived negative outstanding components across " +
                  $"{group.Select(x => x.ContractKey).Distinct(StringComparer.OrdinalIgnoreCase).Count()} contracts " +
                  "were retained because payment inputs are nonnegative, components reconcile within 0.01, and total is nonnegative.")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static (int Added, int Removed, int Changed) Compare(
        IReadOnlyList<DebtContractHistory>? previous, IReadOnlyList<DebtContractHistory> current)
    {
        if (previous is null)
            return (current.Count, 0, 0);
        var previousByKey = previous.ToDictionary(x => x.ContractKey, StringComparer.OrdinalIgnoreCase);
        var currentByKey = current.ToDictionary(x => x.ContractKey, StringComparer.OrdinalIgnoreCase);
        return (
            currentByKey.Keys.Count(x => !previousByKey.ContainsKey(x)),
            previousByKey.Keys.Count(x => !currentByKey.ContainsKey(x)),
            currentByKey.Count(pair => previousByKey.TryGetValue(pair.Key, out var oldValue) && ContractChanged(oldValue, pair.Value)));
    }

    private static bool ContractChanged(DebtContractHistory previous, DebtContractHistory current)
    {
        if (!string.Equals(previous.MemberCode, current.MemberCode, StringComparison.OrdinalIgnoreCase) ||
            previous.ContractDate != current.ContractDate || previous.ExpireDate != current.ExpireDate ||
            !string.Equals(previous.GroupCode, current.GroupCode, StringComparison.OrdinalIgnoreCase) ||
            previous.MonthlyStates.Count != current.MonthlyStates.Count)
            return true;
        return current.MonthlyStates.Any(pair =>
            !previous.MonthlyStates.TryGetValue(pair.Key, out var prior) || prior != pair.Value);
    }

    private static string FileIdentity(FileInfo file) => file.Exists
        ? $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}"
        : file.FullName;

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string? AvailabilityMessage(DebtWorkbookReadResult workbook, DateOnly previous, DateOnly current)
    {
        if (!workbook.DataThroughPeriod.HasValue)
            return workbook.Warnings.LastOrDefault() ?? "Workbook data-through period is unavailable.";
        if (workbook.DataThroughPeriod.Value < current)
            return $"Requested {current:yyyy-MM} data is unavailable; workbook is proven only through {workbook.DataThroughPeriod:yyyy-MM}.";
        if (!workbook.Contracts.Any(x => x.MonthlyStates.ContainsKey(previous) && x.MonthlyStates.ContainsKey(current)))
            return $"No contracts contain both {previous:yyyy-MM} and {current:yyyy-MM} monthly facts.";
        return null;
    }

    private string? PolicyAvailabilityMessage(PublishedDebtSnapshot snapshot) =>
        string.Equals(snapshot.PolicyVersion, _analyzer.PolicyVersion, StringComparison.Ordinal)
            ? null
            : $"Published Debt Snapshot policy '{snapshot.PolicyVersion}' cannot be evaluated by '{_analyzer.PolicyVersion}'.";

    private DebtPreviewDashboardDto UnavailableDashboard(
        PublishedDebtSnapshot? snapshot, DateOnly previous, DateOnly current,
        string message, DebtSyncResult sync) =>
        new(false, message, snapshot?.SourceFileName ?? sync.SourceFileName, snapshot?.SourceFileHash,
            snapshot?.PublishedAtUtc, snapshot?.Workbook.WorksheetName ?? string.Empty,
            snapshot?.Workbook.DataThroughPeriod, snapshot?.PolicyVersion ?? _analyzer.PolicyVersion,
            _options.AutoSyncOnQueryEnabled, previous, current,
            snapshot?.Workbook.Warnings ?? sync.Warnings, [], null, MapSync(sync), [], [], [], [], [], []);

    private static DebtSyncStatusDto MapSync(DebtSyncResult sync) => new(
        SyncStatus(sync.Status, sync.Success, sync.Published), sync.AttemptedAtUtc,
        sync.Success, sync.Published, sync.PreviousSnapshotPreserved, TriggerLabel(sync.Trigger), sync.Message,
        sync.SourceFileName, sync.SourceFileHash, sync.PublishedAtUtc, sync.SourceRows, sync.ContractCount,
        sync.AddedContracts, sync.RemovedContracts, sync.ChangedContracts, sync.ValidationErrors, sync.Warnings,
        sync.PreviousPeriod, sync.CurrentPeriod, sync.CandidateRows, sync.ExcludedRows, sync.MemberCount,
        sync.OutstandingAmount, sync.NewContracts, sync.PaidOffContracts, sync.SnapshotId,
        sync.DurationMilliseconds)
    {
        SourceType = sync.SourceType
    };

    private static DebtSyncHistoryDto MapHistory(DebtSyncHistoryEntry entry) => new(
        entry.AttemptedAtUtc, entry.Success, entry.Published, entry.PreviousSnapshotPreserved,
        entry.Trigger, entry.Message, entry.SourceFileName, entry.SourceFileHash, entry.PublishedAtUtc,
        entry.SourceRows, entry.ContractCount, entry.AddedContracts, entry.RemovedContracts,
        entry.ChangedContracts, entry.ValidationErrors, entry.Warnings,
        SyncStatus(entry.Status, entry.Success, entry.Published), entry.PreviousPeriod, entry.CurrentPeriod,
        entry.CandidateRows, entry.ExcludedRows, entry.MemberCount, entry.OutstandingAmount,
        entry.NewContracts, entry.PaidOffContracts, entry.SnapshotId, entry.DurationMilliseconds)
    {
        SourceType = entry.SourceType
    };

    private static DebtBucketPeriodComparisonDto MapBucketComparison(DebtBucketPeriodComparison item) => new(
        DebtLabels.Bucket(item.Bucket), DebtLabels.BucketThai(item.Bucket), item.PreviousMemberCount,
        item.CurrentMemberCount, item.MemberDifference, item.PreviousContractCount, item.CurrentContractCount,
        item.ContractDifference, item.PreviousOutstanding, item.CurrentOutstanding, item.AmountDifference,
        item.EnteredContracts, item.ImprovedContracts, item.WorsenedContracts, item.PaidOffContracts,
        item.SameBucketBalanceDecreasedContracts, item.SameBucketBalanceIncreasedContracts);

    private static DebtMovementSummaryDto MapMovement(DebtMovementSummary movement) => new(
        movement.PreviousBucket, movement.CurrentBucket, movement.PaymentMovement,
        movement.Category.HasValue ? DebtLabels.Category(movement.Category.Value) : string.Empty,
        movement.MemberCount, movement.ContractCount, movement.PreviousOutstanding,
        movement.CurrentOutstanding, movement.AmountChange);

    private static DebtContractDetailDto MapContract(
        DebtContractAnalysis item,
        DateOnly evaluatedPeriod,
        IReadOnlyList<DebtDataQualityWarning> dataQualityWarnings) => new(
        item.Contract.SourceRowNumber, item.Contract.MemberCode, item.Contract.MemberName,
        item.Contract.ContractNumber, item.Contract.LoanType, item.Contract.ContractDate,
        item.CurrentClassification.FirstDuePeriod, item.Contract.ExpireDate,
        DebtLabels.Bucket(item.PreviousClassification.Bucket), DebtLabels.Bucket(item.CurrentClassification.Bucket),
        DebtLabels.BucketThai(item.CurrentClassification.Bucket),
        DebtLabels.Payment(item.CurrentClassification.LatestPaymentStatus), item.CurrentState.PaymentAmount,
        item.CurrentState.ScheduledAmount, item.CurrentState.PaymentDifference,
        item.CurrentClassification.FirstMissedPaymentPeriod, item.CurrentClassification.ConsecutiveMissedMonths,
        item.CurrentState.PrincipalOutstanding, item.CurrentState.ProfitOutstanding,
        item.CurrentState.TotalOutstanding, item.Contract.GroupCode, item.Contract.Branch,
        item.PaymentMovement, DebtLabels.Category(item.MovementCategory), item.PreviousState.TotalOutstanding,
        item.CurrentState.TotalOutstanding - item.PreviousState.TotalOutstanding,
        evaluatedPeriod, item.CurrentClassification.LastPaymentPeriod,
        DebtLabels.Basis(item.CurrentClassification.CalculationBasis),
        dataQualityWarnings.Select(MapDataQualityWarning).ToArray());

    private static DebtDataQualityWarningDto MapDataQualityWarning(DebtDataQualityWarning warning) => new(
        warning.Code, warning.Period, warning.Field, warning.ComponentValue,
        warning.PrincipalOutstanding, warning.ProfitOutstanding, warning.TotalOutstanding,
        warning.PrincipalPaymentInput, warning.ProfitPaymentInput, warning.TotalPaymentInput,
        warning.CellAddress, warning.Formula, warning.FormulaR1C1);

    private static IEnumerable<DebtContractAnalysis> ApplyLabelFilter(
        IEnumerable<DebtContractAnalysis> query, string? filter,
        Func<DebtContractAnalysis, string> selector) =>
        string.IsNullOrWhiteSpace(filter)
            ? query
            : query.Where(x => string.Equals(selector(x), filter.Trim(), StringComparison.OrdinalIgnoreCase));

    private static DateOnly ParsePeriod(string value)
    {
        if (DateOnly.TryParseExact(
                $"{value.Trim()}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return parsed;
        throw new ArgumentException("Period must use YYYY-MM format.", nameof(value));
    }

    private sealed record DebtStagedValidation(
        IReadOnlyList<string> Errors,
        IReadOnlyList<DebtDataQualityWarning> Warnings);
}
