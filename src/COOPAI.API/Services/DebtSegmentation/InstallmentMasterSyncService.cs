using System.Diagnostics;
using COOPAI.API.DTOs.DebtSegmentation;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.DebtSegmentation;

public interface IInstallmentMasterSyncPipeline
{
    Task<InstallmentMasterSyncStatusDto> SyncAutoAsync(CancellationToken cancellationToken = default);
}

public interface IInstallmentMasterSyncService : IInstallmentMasterSyncPipeline
{
    Task<InstallmentMasterSyncStatusDto> SyncNowAsync(CancellationToken cancellationToken = default);
    Task<InstallmentMasterSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InstallmentMasterSyncStatusDto>> GetHistoryAsync(
        int take,
        CancellationToken cancellationToken = default);
}

public interface IInstallmentMasterWorkbookAcquirer
{
    Task<DebtWorkbookAcquisition> AcquireAsync(CancellationToken cancellationToken = default);
}

public sealed class InstallmentMasterWorkbookAcquirer : IInstallmentMasterWorkbookAcquirer
{
    private readonly InstallmentMasterOptions _options;
    private readonly IHostEnvironment _environment;

    public InstallmentMasterWorkbookAcquirer(
        IOptions<InstallmentMasterOptions> options,
        IHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public Task<DebtWorkbookAcquisition> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolvePath();
        var mapped = Options.Create(new DebtSegmentationPreviewOptions
        {
            Enabled = _options.Enabled,
            WorkbookPath = path,
            HistoryPath = _options.HistoryPath,
            StagingPath = _options.StagingPath,
            StabilizationDelayMilliseconds = _options.StabilizationDelayMilliseconds,
            NetworkStabilizationDelayMilliseconds = _options.NetworkStabilizationDelayMilliseconds,
            MinimumFileAgeSeconds = _options.MinimumFileAgeSeconds
        });
        var sharedAcquirer = new DebtWorkbookAcquirer(mapped, _environment);
        return sharedAcquirer.AcquireAsync(new DebtWorkbookLocation(true, path, []), cancellationToken);
    }

    private string ResolvePath()
    {
        if (string.IsNullOrWhiteSpace(_options.WorkbookPath))
            throw new InvalidOperationException("InstallmentMasterPreview:WorkbookPath is required.");
        var path = Path.IsPathRooted(_options.WorkbookPath)
            ? Path.GetFullPath(_options.WorkbookPath)
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _options.WorkbookPath));
        if (!Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            throw new InvalidOperationException("Installment Master source must be an exact completed .xlsx workbook path.");
        return path;
    }
}

public sealed class InstallmentMasterSyncService : IInstallmentMasterSyncService
{
    private readonly InstallmentMasterOptions _options;
    private readonly IInstallmentMasterWorkbookAcquirer _acquirer;
    private readonly InstallmentMasterWorkbookReader _reader;
    private readonly IInstallmentMasterSnapshotStore _store;
    private readonly SemaphoreSlim _syncLock;
    private PublishedInstallmentMasterSnapshot? _current;
    private bool _restored;

    public InstallmentMasterSyncService(
        IOptions<InstallmentMasterOptions> options,
        IInstallmentMasterWorkbookAcquirer acquirer,
        InstallmentMasterWorkbookReader reader,
        IInstallmentMasterSnapshotStore store,
        PreviewSyncGate syncGate)
    {
        _options = options.Value;
        _acquirer = acquirer;
        _reader = reader;
        _store = store;
        _syncLock = syncGate.Semaphore;
    }

    public Task<InstallmentMasterSyncStatusDto> SyncNowAsync(CancellationToken cancellationToken = default) =>
        SyncAsync(DebtSyncTrigger.Manual, cancellationToken);

    public Task<InstallmentMasterSyncStatusDto> SyncAutoAsync(CancellationToken cancellationToken = default) =>
        SyncAsync(DebtSyncTrigger.Automatic, cancellationToken);

    public async Task<InstallmentMasterSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await RestoreAsync(cancellationToken);
        var snapshot = _current;
        return snapshot is null
            ? new InstallmentMasterSnapshotDto(
                false, "No valid Installment Master Snapshot is available.", null, null, null, null, null,
                0, 0, 0, 0, 0, 0, 0, [])
            {
                SchemaMode = InstallmentMasterSchemaModes.Unknown
            }
            : new InstallmentMasterSnapshotDto(
                true, "The latest valid Installment Master Snapshot is available.", snapshot.SnapshotId,
                snapshot.SourceFileName, snapshot.SourceFileHash, snapshot.SourceLastWriteTimeUtc,
                snapshot.PublishedAtUtc, snapshot.Workbook.SourceRowCount, snapshot.Workbook.ContractCount,
                snapshot.Workbook.ValidContractCount, snapshot.Workbook.InvalidContractCount,
                snapshot.Workbook.DuplicateContractNumberGroups, snapshot.Workbook.InvalidTotalInstallmentRows,
                snapshot.Workbook.InvalidMonthlyInstallmentRows, snapshot.Workbook.Warnings)
            {
                SchemaMode = snapshot.Workbook.SchemaMode
            };
    }

    public async Task<IReadOnlyList<InstallmentMasterSyncStatusDto>> GetHistoryAsync(
        int take,
        CancellationToken cancellationToken = default) =>
        (await _store.ListSyncHistoryAsync(take, cancellationToken)).Select(Map).ToArray();

    private async Task<InstallmentMasterSyncStatusDto> SyncAsync(
        DebtSyncTrigger trigger,
        CancellationToken cancellationToken)
    {
        var attemptedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var acquiredGate = await _syncLock.WaitAsync(0, cancellationToken);
        if (!acquiredGate)
        {
            return await RecordAsync(new InstallmentMasterSyncResult(
                attemptedAt, TriggerLabel(trigger), DebtSyncStatuses.Busy, false, true,
                "Another Preview workbook sync is running; Installment Master Sync was skipped safely.",
                Path.GetFileName(_options.WorkbookPath), null, _current?.SnapshotId,
                0, 0, 0, 0, 0, 0, 0, stopwatch.ElapsedMilliseconds,
                ["No workbook was read and no snapshot was changed."], []), cancellationToken);
        }

        try
        {
            await RestoreAsync(cancellationToken);
            if (!_options.Enabled)
                return await FailureAsync(trigger, attemptedAt, stopwatch,
                    "Installment Master Preview is disabled.", ["Preview source is disabled."], cancellationToken);

            DebtWorkbookAcquisition acquisition;
            try
            {
                acquisition = await _acquirer.AcquireAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await FailureAsync(trigger, attemptedAt, stopwatch,
                    "Installment Master acquisition failed; the previous valid snapshot was retained.",
                    [exception.Message], cancellationToken);
            }

            await using (acquisition)
            {
                InstallmentMasterReadResult workbook;
                try
                {
                    workbook = _reader.Read(acquisition.LocalPath, _options.WorksheetName) with
                    {
                        SourceFileName = acquisition.SourceFileName
                    };
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return await FailureAsync(trigger, attemptedAt, stopwatch,
                        "Installment Master schema/read validation failed; the previous valid snapshot was retained.",
                        [exception.Message], cancellationToken, acquisition.SourceFileName,
                        acquisition.SourceHash,
                        schemaMode: (exception as InstallmentMasterSchemaValidationException)?.SchemaMode);
                }

                if (_current is not null &&
                    acquisition.SourceLastWriteTimeUtc < _current.SourceLastWriteTimeUtc)
                {
                    return await FailureAsync(trigger, attemptedAt, stopwatch,
                        "The candidate is older than the current Installment Master Snapshot; the current snapshot was retained.",
                        ["An older source cannot replace a newer validated snapshot."], cancellationToken,
                        acquisition.SourceFileName, acquisition.SourceHash, workbook);
                }

                if (_current is not null &&
                    string.Equals(_current.SourceFileHash, acquisition.SourceHash, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_current.AnalysisVersion, MonthlyAmountDueVersion.InstallmentMasterV3, StringComparison.Ordinal))
                {
                    return await RecordAsync(Result(
                        trigger, attemptedAt, stopwatch, DebtSyncStatuses.NoChange, false, true,
                        "Installment Master content is unchanged; no duplicate snapshot was created.",
                        acquisition.SourceFileName, acquisition.SourceHash, _current.SnapshotId, workbook),
                        cancellationToken);
                }

                var snapshot = new PublishedInstallmentMasterSnapshot(
                    $"{MonthlyAmountDueVersion.InstallmentMasterV3}-{acquisition.SourceHash}",
                    MonthlyAmountDueVersion.InstallmentMasterV3,
                    acquisition.SourceFileName,
                    acquisition.SourcePath,
                    acquisition.SourceHash,
                    acquisition.SourceSizeBytes,
                    acquisition.SourceLastWriteTimeUtc,
                    DateTime.UtcNow,
                    workbook)
                {
                    SourceType = acquisition.SourceType
                };
                try
                {
                    await _store.PublishAsync(snapshot, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return await FailureAsync(trigger, attemptedAt, stopwatch,
                        "Validated Installment Master candidate could not be committed; the previous snapshot was retained.",
                        [$"Snapshot publish failed: {exception.Message}"], cancellationToken,
                        acquisition.SourceFileName, acquisition.SourceHash, workbook);
                }

                _current = snapshot;
                return await RecordAsync(Result(
                    trigger, attemptedAt, stopwatch, DebtSyncStatuses.Success, true, false,
                    "Installment Master validated and the new immutable snapshot is current.",
                    acquisition.SourceFileName, acquisition.SourceHash, snapshot.SnapshotId, workbook),
                    cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await FailureAsync(trigger, attemptedAt, stopwatch,
                "Installment Master Sync failed unexpectedly; the previous valid snapshot was retained.",
                [$"Unexpected Preview sync failure: {exception.Message}"], cancellationToken);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (_restored)
            return;
        _current = await _store.GetCurrentAsync(cancellationToken);
        _restored = true;
    }

    private Task<InstallmentMasterSyncStatusDto> FailureAsync(
        DebtSyncTrigger trigger,
        DateTime attemptedAt,
        Stopwatch stopwatch,
        string message,
        IReadOnlyList<string> errors,
        CancellationToken cancellationToken,
        string? sourceFileName = null,
        string? sourceHash = null,
        InstallmentMasterReadResult? workbook = null,
        string? schemaMode = null) =>
        RecordAsync(Result(
            trigger, attemptedAt, stopwatch, DebtSyncStatuses.Failed, false, _current is not null,
            message, sourceFileName ?? Path.GetFileName(_options.WorkbookPath), sourceHash,
            _current?.SnapshotId, workbook, errors, schemaMode), cancellationToken);

    private static InstallmentMasterSyncResult Result(
        DebtSyncTrigger trigger,
        DateTime attemptedAt,
        Stopwatch stopwatch,
        string status,
        bool published,
        bool preserved,
        string message,
        string sourceFileName,
        string? sourceHash,
        string? snapshotId,
        InstallmentMasterReadResult? workbook,
        IReadOnlyList<string>? errors = null,
        string? schemaMode = null) => new(
            attemptedAt,
            TriggerLabel(trigger),
            status,
            published,
            preserved,
            message,
            sourceFileName,
            sourceHash,
            snapshotId,
            workbook?.SourceRowCount ?? 0,
            workbook?.ContractCount ?? 0,
            workbook?.ValidContractCount ?? 0,
            workbook?.InvalidContractCount ?? 0,
            workbook?.DuplicateContractNumberGroups ?? 0,
            workbook?.InvalidTotalInstallmentRows ?? 0,
            workbook?.InvalidMonthlyInstallmentRows ?? 0,
            stopwatch.ElapsedMilliseconds,
        errors ?? [],
        workbook?.Warnings ?? [])
    {
        SchemaMode = workbook?.SchemaMode ?? schemaMode ?? InstallmentMasterSchemaModes.Unknown
    };

    private async Task<InstallmentMasterSyncStatusDto> RecordAsync(
        InstallmentMasterSyncResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await _store.RecordSyncAsync(new InstallmentMasterSyncHistoryEntry(
                result.AttemptedAtUtc, result.Trigger, result.Status, result.Published,
                result.PreviousSnapshotPreserved, result.Message, result.SourceFileName,
                result.SourceFileHash, result.SnapshotId, result.SourceRows, result.ContractCount,
                result.ValidContractCount, result.InvalidContractCount,
                result.DuplicateContractNumberGroups, result.InvalidTotalInstallmentRows,
                result.InvalidMonthlyInstallmentRows, result.DurationMilliseconds,
                result.Errors, result.Warnings)
            {
                SchemaMode = result.SchemaMode
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = result with
            {
                Warnings = result.Warnings.Concat(
                    [$"Sync history could not be retained: {exception.Message}"]).ToArray()
            };
        }
        return Map(result);
    }

    private static InstallmentMasterSyncStatusDto Map(InstallmentMasterSyncResult result) => new(
        result.AttemptedAtUtc, result.Trigger, result.Status, result.Published,
        result.PreviousSnapshotPreserved, result.Message, result.SourceFileName,
        result.SourceFileHash, result.SnapshotId, result.SourceRows, result.ContractCount,
        result.ValidContractCount, result.InvalidContractCount,
        result.DuplicateContractNumberGroups, result.InvalidTotalInstallmentRows,
        result.InvalidMonthlyInstallmentRows, result.DurationMilliseconds,
        result.Errors, result.Warnings)
    {
        SchemaMode = result.SchemaMode
    };

    private static InstallmentMasterSyncStatusDto Map(InstallmentMasterSyncHistoryEntry result) => new(
        result.AttemptedAtUtc, result.Trigger, result.Status, result.Published,
        result.PreviousSnapshotPreserved, result.Message, result.SourceFileName,
        result.SourceFileHash, result.SnapshotId, result.SourceRows, result.ContractCount,
        result.ValidContractCount, result.InvalidContractCount,
        result.DuplicateContractNumberGroups, result.InvalidTotalInstallmentRows,
        result.InvalidMonthlyInstallmentRows, result.DurationMilliseconds,
        result.Errors, result.Warnings)
    {
        SchemaMode = result.SchemaMode
    };

    private static string TriggerLabel(DebtSyncTrigger trigger) =>
        trigger == DebtSyncTrigger.Automatic ? "Auto" : "Manual";
}
