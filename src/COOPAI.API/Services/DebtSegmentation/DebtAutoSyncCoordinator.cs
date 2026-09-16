using COOPAI.API.DTOs.DebtSegmentation;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.DebtSegmentation;

public static class DebtAutoSyncStates
{
    public const string Disabled = "Disabled";
    public const string Checking = "Checking";
    public const string Ready = "Ready";
    public const string Syncing = "Syncing";
    public const string NetworkUnavailable = "NetworkUnavailable";
    public const string Error = "Error";
}

public sealed record DebtAutoSyncSourceState(
    bool Reachable,
    string SourceType,
    string SourceFileName,
    string? MetadataIdentity,
    long? Length,
    DateTime? LastWriteTimeUtc,
    string Message);

public interface IDebtAutoSyncSourceProbe
{
    Task<DebtAutoSyncSourceState> ProbeAsync(CancellationToken cancellationToken = default);
}

public interface IDebtAutoSyncStatus
{
    DebtAutoSyncStatusDto GetStatus();
}

public sealed class DebtAutoSyncSourceProbe : IDebtAutoSyncSourceProbe
{
    private readonly DebtSegmentationPreviewOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly object _probeGate = new();
    private Task<DebtAutoSyncSourceState>? _pendingProbe;

    public DebtAutoSyncSourceProbe(
        IOptions<DebtSegmentationPreviewOptions> options,
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

        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.AutoSyncProbeTimeoutSeconds, 1, 60));
        try
        {
            return await probe.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return Unavailable("Network source metadata check timed out; the current Debt Snapshot remains active.");
        }
    }

    private DebtAutoSyncSourceState ReadSourceState()
    {
        try
        {
            var configured = _options.WorkbookPath;
            if (string.IsNullOrWhiteSpace(configured))
                return Unavailable("Debt workbook source is not configured.");
            var path = Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));
            var sourceType = DebtWorkbookAcquirer.IsNetworkPath(path)
                ? DebtSourceTypes.Network
                : DebtSourceTypes.Local;
            var file = new FileInfo(path);
            file.Refresh();
            if (!file.Exists)
                return Unavailable("The configured debt workbook is temporarily unavailable.", sourceType, file.Name);
            return new DebtAutoSyncSourceState(
                true,
                sourceType,
                file.Name,
                $"{path}|{file.Length}|{file.LastWriteTimeUtc.Ticks}",
                file.Length,
                file.LastWriteTimeUtc,
                "Source metadata is available.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable(
                "The network source is temporarily unavailable or access was denied.",
                DebtWorkbookAcquirer.IsNetworkPath(_options.WorkbookPath)
                    ? DebtSourceTypes.Network
                    : DebtSourceTypes.Local,
                Path.GetFileName(_options.WorkbookPath));
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
            null,
            null,
            null,
            message);
}

public sealed class DebtAutoSyncCoordinator : BackgroundService, IDebtAutoSyncStatus
{
    private readonly DebtSegmentationPreviewOptions _options;
    private readonly IDebtAutoSyncSourceProbe _sourceProbe;
    private readonly IDebtAutoSyncPipeline _syncPipeline;
    private readonly IDebtSnapshotStore _snapshotStore;
    private readonly ILogger<DebtAutoSyncCoordinator> _logger;
    private readonly object _statusGate = new();
    private DebtAutoSyncStatusDto _status;
    private bool _baselineLoaded;
    private string? _processedMetadataIdentity;
    private string? _pendingFailureIdentity;
    private DateTime _retryAfterUtc;
    private string? _lastLoggedAvailabilityMessage;
    private bool _reachabilityConfirmed;

    public DebtAutoSyncCoordinator(
        IOptions<DebtSegmentationPreviewOptions> options,
        IDebtAutoSyncSourceProbe sourceProbe,
        IDebtAutoSyncPipeline syncPipeline,
        IDebtSnapshotStore snapshotStore,
        ILogger<DebtAutoSyncCoordinator> logger)
    {
        _options = options.Value;
        _sourceProbe = sourceProbe;
        _syncPipeline = syncPipeline;
        _snapshotStore = snapshotStore;
        _logger = logger;
        _status = new DebtAutoSyncStatusDto(
            _options.AutoSyncEnabled,
            _options.AutoSyncEnabled ? DebtAutoSyncStates.Checking : DebtAutoSyncStates.Disabled,
            Math.Clamp(_options.AutoSyncPollSeconds, 5, 3600),
            null, null, null,
            _options.AutoSyncEnabled ? "Auto Sync is starting." : "Auto Sync is disabled.",
            DebtWorkbookAcquirer.IsNetworkPath(_options.WorkbookPath)
                ? DebtSourceTypes.Network
                : DebtSourceTypes.Local,
            Path.GetFileName(_options.WorkbookPath));
    }

    public DebtAutoSyncStatusDto GetStatus()
    {
        lock (_statusGate)
            return _status;
    }

    internal async Task PollOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.AutoSyncEnabled)
        {
            Update(status: DebtAutoSyncStates.Disabled, message: "Auto Sync is disabled.");
            return;
        }

        var checkedAt = DateTime.UtcNow;
        Update(status: DebtAutoSyncStates.Checking, lastCheckedAtUtc: checkedAt,
            message: "Checking source metadata.");
        DebtAutoSyncSourceState source;
        try
        {
            source = await _sourceProbe.ProbeAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Auto Sync source metadata probe failed");
            Update(status: DebtAutoSyncStates.Error, lastCheckedAtUtc: checkedAt,
                message: "Source metadata check failed; the current Debt Snapshot remains active.");
            return;
        }

        if (!source.Reachable || string.IsNullOrWhiteSpace(source.MetadataIdentity))
        {
            _reachabilityConfirmed = false;
            if (!string.Equals(_lastLoggedAvailabilityMessage, source.Message, StringComparison.Ordinal))
            {
                _logger.LogWarning("Auto Sync network source unavailable: {Message}", source.Message);
                _lastLoggedAvailabilityMessage = source.Message;
            }
            Update(status: DebtAutoSyncStates.NetworkUnavailable, lastCheckedAtUtc: checkedAt,
                message: source.Message, sourceType: source.SourceType, sourceFileName: source.SourceFileName);
            return;
        }

        if (!_reachabilityConfirmed)
        {
            _logger.LogInformation("Auto Sync network source is reachable");
            _reachabilityConfirmed = true;
        }
        if (_lastLoggedAvailabilityMessage is not null)
        {
            _logger.LogInformation("Auto Sync network source is reachable again");
            _lastLoggedAvailabilityMessage = null;
        }

        if (!_baselineLoaded)
        {
            var current = await _snapshotStore.GetCurrentAsync(cancellationToken);
            _baselineLoaded = true;
            if (current is not null &&
                string.Equals(current.SourceType, source.SourceType, StringComparison.Ordinal) &&
                current.SourceFileSizeBytes == source.Length &&
                current.SourceLastWriteTimeUtc == source.LastWriteTimeUtc)
            {
                _processedMetadataIdentity = source.MetadataIdentity;
                _logger.LogInformation(
                    "Auto Sync skipped: source metadata is unchanged and matches the current Debt Snapshot");
                Update(status: DebtAutoSyncStates.Ready, lastCheckedAtUtc: checkedAt,
                    message: "Watching; source metadata matches the current Debt Snapshot.",
                    sourceType: source.SourceType, sourceFileName: source.SourceFileName);
                return;
            }
        }

        if (string.Equals(_processedMetadataIdentity, source.MetadataIdentity, StringComparison.Ordinal))
        {
            Update(status: DebtAutoSyncStates.Ready, lastCheckedAtUtc: checkedAt,
                message: "Watching; source metadata is unchanged.",
                sourceType: source.SourceType, sourceFileName: source.SourceFileName);
            return;
        }

        if (string.Equals(_pendingFailureIdentity, source.MetadataIdentity, StringComparison.Ordinal) &&
            DateTime.UtcNow < _retryAfterUtc)
        {
            Update(status: DebtAutoSyncStates.Error, lastCheckedAtUtc: checkedAt,
                message: "The last Auto Sync failed safely; retry is scheduled.",
                sourceType: source.SourceType, sourceFileName: source.SourceFileName);
            return;
        }

        _logger.LogInformation("Auto Sync change detected for {SourceType} source {SourceFileName}",
            source.SourceType, source.SourceFileName);
        Update(status: DebtAutoSyncStates.Syncing, lastCheckedAtUtc: checkedAt,
            lastSyncAttemptAtUtc: DateTime.UtcNow, message: "Source change detected; Auto Sync is running.",
            sourceType: source.SourceType, sourceFileName: source.SourceFileName);
        _logger.LogInformation("Auto Sync started through the shared Debt Sync pipeline");
        var result = await _syncPipeline.SyncAutoAsync(cancellationToken);
        if (string.Equals(result.Status, DebtSyncStatuses.Success, StringComparison.Ordinal) ||
            string.Equals(result.Status, DebtSyncStatuses.NoChange, StringComparison.Ordinal))
        {
            _processedMetadataIdentity = source.MetadataIdentity;
            _pendingFailureIdentity = null;
            _retryAfterUtc = default;
            _logger.LogInformation("Auto Sync completed with {Status}; snapshot {SnapshotId}",
                result.Status, result.SnapshotId);
            Update(status: DebtAutoSyncStates.Ready, lastCheckedAtUtc: checkedAt,
                lastSyncAttemptAtUtc: result.AttemptedAtUtc,
                lastSuccessfulSyncAtUtc: result.AttemptedAtUtc,
                message: result.Status == DebtSyncStatuses.Success
                    ? "Auto Sync succeeded; the latest valid Debt Snapshot is active."
                    : "Auto Sync completed; content is unchanged.",
                sourceType: source.SourceType, sourceFileName: source.SourceFileName);
            return;
        }

        if (string.Equals(result.Status, DebtSyncStatuses.Busy, StringComparison.Ordinal))
        {
            _logger.LogInformation("Auto Sync skipped because the shared Debt Sync pipeline is busy");
            Update(status: DebtAutoSyncStates.Ready, lastCheckedAtUtc: checkedAt,
                lastSyncAttemptAtUtc: result.AttemptedAtUtc,
                message: "Sync pipeline is busy; Auto Sync will retry on the next poll.",
                sourceType: source.SourceType, sourceFileName: source.SourceFileName);
            return;
        }

        _pendingFailureIdentity = source.MetadataIdentity;
        _retryAfterUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(_options.AutoSyncFailureRetrySeconds, 30, 3600));
        _logger.LogWarning("Auto Sync failed; the previous valid Debt Snapshot was retained: {Message}", result.Message);
        Update(status: DebtAutoSyncStates.Error, lastCheckedAtUtc: checkedAt,
            lastSyncAttemptAtUtc: result.AttemptedAtUtc,
            message: "Auto Sync failed safely; the previous valid Debt Snapshot remains active.",
            sourceType: source.SourceType, sourceFileName: source.SourceFileName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutoSyncEnabled)
        {
            _logger.LogInformation("Debt Auto Sync watcher is disabled");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.AutoSyncPollSeconds, 5, 3600));
        _logger.LogInformation("Debt Auto Sync watcher started with {PollSeconds}-second polling", interval.TotalSeconds);
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
                _logger.LogError(exception, "Debt Auto Sync polling cycle failed; API remains available");
                Update(status: DebtAutoSyncStates.Error,
                    message: "Auto Sync polling failed; the current Debt Snapshot remains active.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private void Update(
        string status,
        string message,
        DateTime? lastCheckedAtUtc = null,
        DateTime? lastSyncAttemptAtUtc = null,
        DateTime? lastSuccessfulSyncAtUtc = null,
        string? sourceType = null,
        string? sourceFileName = null)
    {
        lock (_statusGate)
        {
            _status = _status with
            {
                Enabled = _options.AutoSyncEnabled,
                Status = status,
                LastCheckedAtUtc = lastCheckedAtUtc ?? _status.LastCheckedAtUtc,
                LastSyncAttemptAtUtc = lastSyncAttemptAtUtc ?? _status.LastSyncAttemptAtUtc,
                LastSuccessfulSyncAtUtc = lastSuccessfulSyncAtUtc ?? _status.LastSuccessfulSyncAtUtc,
                Message = message,
                SourceType = sourceType ?? _status.SourceType,
                SourceFileName = sourceFileName ?? _status.SourceFileName
            };
        }
    }
}
