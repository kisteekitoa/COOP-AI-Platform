using COOPAI.API.DTOs.DebtSegmentation;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.DebtSegmentation;

public interface IInstallmentMasterAutoSyncSourceProbe
{
    Task<DebtAutoSyncSourceState> ProbeAsync(CancellationToken cancellationToken = default);
}

public interface IInstallmentMasterAutoSyncStatus
{
    InstallmentMasterAutoSyncStatusDto GetStatus();
}

public sealed class InstallmentMasterAutoSyncSourceProbe : IInstallmentMasterAutoSyncSourceProbe
{
    private readonly InstallmentMasterOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly object _probeGate = new();
    private Task<DebtAutoSyncSourceState>? _pendingProbe;

    public InstallmentMasterAutoSyncSourceProbe(
        IOptions<InstallmentMasterOptions> options,
        IHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public async Task<DebtAutoSyncSourceState> ProbeAsync(CancellationToken cancellationToken = default)
    {
        Task<DebtAutoSyncSourceState> probe;
        lock (_probeGate)
        {
            if (_pendingProbe is null || _pendingProbe.IsCompleted)
                _pendingProbe = Task.Run(ReadSourceState, CancellationToken.None);
            probe = _pendingProbe;
        }

        try
        {
            return await probe.WaitAsync(
                TimeSpan.FromSeconds(Math.Clamp(_options.AutoSyncProbeTimeoutSeconds, 1, 60)),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return Unavailable("Installment Master metadata check timed out; the latest valid snapshot remains active.");
        }
    }

    private DebtAutoSyncSourceState ReadSourceState()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_options.WorkbookPath))
                return Unavailable("Installment Master source is not configured.");
            var path = Path.IsPathRooted(_options.WorkbookPath)
                ? Path.GetFullPath(_options.WorkbookPath)
                : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _options.WorkbookPath));
            var sourceType = DebtWorkbookAcquirer.IsNetworkPath(path)
                ? DebtSourceTypes.Network
                : DebtSourceTypes.Local;
            var file = new FileInfo(path);
            file.Refresh();
            if (!file.Exists)
                return Unavailable("The configured Installment Master is temporarily unavailable.", sourceType, file.Name);
            return new DebtAutoSyncSourceState(
                true, sourceType, file.Name, $"{path}|{file.Length}|{file.LastWriteTimeUtc.Ticks}",
                file.Length, file.LastWriteTimeUtc, "Installment Master metadata is available.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable("The Installment Master is unavailable or access was denied.");
        }
    }

    private DebtAutoSyncSourceState Unavailable(
        string message,
        string? sourceType = null,
        string? sourceFileName = null) => new(
            false,
            sourceType ?? (DebtWorkbookAcquirer.IsNetworkPath(_options.WorkbookPath)
                ? DebtSourceTypes.Network
                : DebtSourceTypes.Local),
            sourceFileName ?? Path.GetFileName(_options.WorkbookPath),
            null, null, null, message);
}

public sealed class InstallmentMasterAutoSyncCoordinator : BackgroundService, IInstallmentMasterAutoSyncStatus
{
    private readonly InstallmentMasterOptions _options;
    private readonly IInstallmentMasterAutoSyncSourceProbe _probe;
    private readonly IInstallmentMasterSyncPipeline _pipeline;
    private readonly IInstallmentMasterSnapshotStore _store;
    private readonly ILogger<InstallmentMasterAutoSyncCoordinator> _logger;
    private readonly object _statusGate = new();
    private InstallmentMasterAutoSyncStatusDto _status;
    private bool _baselineLoaded;
    private string? _processedMetadataIdentity;
    private string? _failedMetadataIdentity;
    private DateTime _retryAfterUtc;
    private bool _reachable;
    private string? _lastUnavailableMessage;

    public InstallmentMasterAutoSyncCoordinator(
        IOptions<InstallmentMasterOptions> options,
        IInstallmentMasterAutoSyncSourceProbe probe,
        IInstallmentMasterSyncPipeline pipeline,
        IInstallmentMasterSnapshotStore store,
        ILogger<InstallmentMasterAutoSyncCoordinator> logger)
    {
        _options = options.Value;
        _probe = probe;
        _pipeline = pipeline;
        _store = store;
        _logger = logger;
        _status = new InstallmentMasterAutoSyncStatusDto(
            _options.AutoSyncEnabled,
            _options.AutoSyncEnabled ? DebtAutoSyncStates.Checking : DebtAutoSyncStates.Disabled,
            Math.Clamp(_options.AutoSyncPollSeconds, 5, 3600),
            null, null, null,
            _options.AutoSyncEnabled ? "Installment Master Auto Sync is starting." : "Installment Master Auto Sync is disabled.",
            DebtWorkbookAcquirer.IsNetworkPath(_options.WorkbookPath) ? DebtSourceTypes.Network : DebtSourceTypes.Local,
            Path.GetFileName(_options.WorkbookPath), null, null, 0, 0, 0);
    }

    public InstallmentMasterAutoSyncStatusDto GetStatus()
    {
        lock (_statusGate)
            return _status;
    }

    internal async Task PollOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.AutoSyncEnabled)
        {
            Update(status: DebtAutoSyncStates.Disabled, message: "Installment Master Auto Sync is disabled.");
            return;
        }

        var checkedAt = DateTime.UtcNow;
        Update(status: DebtAutoSyncStates.Checking, lastCheckedAtUtc: checkedAt,
            message: "Checking Installment Master metadata.");
        DebtAutoSyncSourceState source;
        try
        {
            source = await _probe.ProbeAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Installment Master Auto Sync metadata probe failed");
            Update(status: DebtAutoSyncStates.Error, lastCheckedAtUtc: checkedAt,
                message: "Metadata check failed; the latest valid Installment Master Snapshot remains active.");
            return;
        }

        if (!source.Reachable || string.IsNullOrWhiteSpace(source.MetadataIdentity))
        {
            _reachable = false;
            if (!string.Equals(_lastUnavailableMessage, source.Message, StringComparison.Ordinal))
            {
                _logger.LogWarning("Installment Master network source unavailable: {Message}", source.Message);
                _lastUnavailableMessage = source.Message;
            }
            Update(status: DebtAutoSyncStates.NetworkUnavailable, lastCheckedAtUtc: checkedAt,
                message: source.Message, sourceType: source.SourceType, sourceFileName: source.SourceFileName);
            return;
        }

        if (!_reachable)
        {
            _logger.LogInformation("Installment Master network source is reachable");
            _reachable = true;
        }
        if (_lastUnavailableMessage is not null)
        {
            _logger.LogInformation("Installment Master network source recovered");
            _lastUnavailableMessage = null;
        }

        if (!_baselineLoaded)
        {
            var current = await _store.GetCurrentAsync(cancellationToken);
            _baselineLoaded = true;
            if (current is not null &&
                string.Equals(current.AnalysisVersion, MonthlyAmountDueVersion.InstallmentMasterV3, StringComparison.Ordinal) &&
                string.Equals(current.SourceType, source.SourceType, StringComparison.Ordinal) &&
                current.SourceFileSizeBytes == source.Length &&
                current.SourceLastWriteTimeUtc == source.LastWriteTimeUtc)
            {
                _processedMetadataIdentity = source.MetadataIdentity;
                _logger.LogInformation("Installment Master Auto Sync skipped: source metadata matches the current snapshot");
                UpdateFromSnapshot(current, DebtAutoSyncStates.Ready, checkedAt,
                    "Watching; source metadata matches the current Installment Master Snapshot.");
                return;
            }
        }

        if (string.Equals(_processedMetadataIdentity, source.MetadataIdentity, StringComparison.Ordinal))
        {
            Update(status: DebtAutoSyncStates.Ready, lastCheckedAtUtc: checkedAt,
                message: "Watching; Installment Master metadata is unchanged.",
                sourceType: source.SourceType, sourceFileName: source.SourceFileName);
            return;
        }

        if (string.Equals(_failedMetadataIdentity, source.MetadataIdentity, StringComparison.Ordinal) &&
            DateTime.UtcNow < _retryAfterUtc)
        {
            Update(status: DebtAutoSyncStates.Error, lastCheckedAtUtc: checkedAt,
                message: "The last attempt failed safely; retry is scheduled.",
                sourceType: source.SourceType, sourceFileName: source.SourceFileName);
            return;
        }

        _logger.LogInformation("Installment Master change detected for {SourceFileName}", source.SourceFileName);
        Update(status: DebtAutoSyncStates.Syncing, lastCheckedAtUtc: checkedAt,
            lastSyncAttemptAtUtc: DateTime.UtcNow,
            message: "Source change detected; Installment Master Sync is running.",
            sourceType: source.SourceType, sourceFileName: source.SourceFileName);
        var result = await _pipeline.SyncAutoAsync(cancellationToken);
        if (result.Status is DebtSyncStatuses.Success or DebtSyncStatuses.NoChange)
        {
            _processedMetadataIdentity = source.MetadataIdentity;
            _failedMetadataIdentity = null;
            _retryAfterUtc = default;
            var current = await _store.GetCurrentAsync(cancellationToken);
            if (current is not null)
                UpdateFromSnapshot(current, DebtAutoSyncStates.Ready, checkedAt, result.Message, result.AttemptedAtUtc);
            else
                Update(status: DebtAutoSyncStates.Ready, lastCheckedAtUtc: checkedAt,
                    lastSyncAttemptAtUtc: result.AttemptedAtUtc,
                    lastSuccessfulSyncAtUtc: result.AttemptedAtUtc, message: result.Message);
            _logger.LogInformation("Installment Master Auto Sync completed with {Status}; snapshot {SnapshotId}",
                result.Status, result.SnapshotId);
            return;
        }

        if (result.Status == DebtSyncStatuses.Busy)
        {
            Update(status: DebtAutoSyncStates.Ready, lastCheckedAtUtc: checkedAt,
                lastSyncAttemptAtUtc: result.AttemptedAtUtc,
                message: "Shared Preview sync pipeline is busy; retrying next poll.");
            return;
        }

        _failedMetadataIdentity = source.MetadataIdentity;
        _retryAfterUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(_options.AutoSyncFailureRetrySeconds, 30, 3600));
        _logger.LogWarning("Installment Master Auto Sync failed; previous snapshot retained: {Message}", result.Message);
        Update(status: DebtAutoSyncStates.Error, lastCheckedAtUtc: checkedAt,
            lastSyncAttemptAtUtc: result.AttemptedAtUtc,
            message: "Sync failed safely; the previous valid Installment Master Snapshot remains active.",
            schemaMode: result.SchemaMode);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutoSyncEnabled)
        {
            _logger.LogInformation("Installment Master Auto Sync watcher is disabled");
            return;
        }
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.AutoSyncPollSeconds, 5, 3600));
        _logger.LogInformation("Installment Master Auto Sync watcher started with {PollSeconds}-second polling",
            interval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Installment Master Auto Sync polling failed; API remains available");
                Update(status: DebtAutoSyncStates.Error,
                    message: "Polling failed safely; the previous valid snapshot remains active.");
            }
            await Task.Delay(interval, stoppingToken);
        }
    }

    private void UpdateFromSnapshot(
        PublishedInstallmentMasterSnapshot snapshot,
        string status,
        DateTime checkedAt,
        string message,
        DateTime? successfulAt = null)
    {
        lock (_statusGate)
        {
            _status = _status with
            {
                Status = status,
                LastCheckedAtUtc = checkedAt,
                LastSyncAttemptAtUtc = successfulAt ?? _status.LastSyncAttemptAtUtc,
                LastSuccessfulSyncAtUtc = successfulAt ?? _status.LastSuccessfulSyncAtUtc,
                Message = message,
                SourceType = snapshot.SourceType,
                SourceFileName = snapshot.SourceFileName,
                SourceFileHash = snapshot.SourceFileHash,
                SnapshotId = snapshot.SnapshotId,
                ContractCount = snapshot.Workbook.ContractCount,
                ValidContractCount = snapshot.Workbook.ValidContractCount,
                InvalidContractCount = snapshot.Workbook.InvalidContractCount,
                SchemaMode = snapshot.Workbook.SchemaMode
            };
        }
    }

    private void Update(
        string? status = null,
        DateTime? lastCheckedAtUtc = null,
        DateTime? lastSyncAttemptAtUtc = null,
        DateTime? lastSuccessfulSyncAtUtc = null,
        string? message = null,
        string? sourceType = null,
        string? sourceFileName = null,
        string? schemaMode = null)
    {
        lock (_statusGate)
        {
            _status = _status with
            {
                Status = status ?? _status.Status,
                LastCheckedAtUtc = lastCheckedAtUtc ?? _status.LastCheckedAtUtc,
                LastSyncAttemptAtUtc = lastSyncAttemptAtUtc ?? _status.LastSyncAttemptAtUtc,
                LastSuccessfulSyncAtUtc = lastSuccessfulSyncAtUtc ?? _status.LastSuccessfulSyncAtUtc,
                Message = message ?? _status.Message,
                SourceType = sourceType ?? _status.SourceType,
                SourceFileName = sourceFileName ?? _status.SourceFileName,
                SchemaMode = schemaMode ?? _status.SchemaMode
            };
        }
    }
}
