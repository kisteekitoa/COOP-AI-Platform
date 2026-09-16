using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.DebtSegmentation;

public static class DebtSourceTypes
{
    public const string Local = "Local";
    public const string Network = "Network";
}

public sealed class DebtWorkbookAcquisition : IAsyncDisposable
{
    private readonly string? _temporaryPath;

    public DebtWorkbookAcquisition(
        string sourcePath,
        string localPath,
        string sourceType,
        string sourceFileName,
        string sourceHash,
        long sourceSizeBytes,
        DateTime sourceLastWriteTimeUtc,
        string identity,
        string? temporaryPath = null)
    {
        SourcePath = sourcePath;
        LocalPath = localPath;
        SourceType = sourceType;
        SourceFileName = sourceFileName;
        SourceHash = sourceHash;
        SourceSizeBytes = sourceSizeBytes;
        SourceLastWriteTimeUtc = sourceLastWriteTimeUtc;
        Identity = identity;
        _temporaryPath = temporaryPath;
    }

    public string SourcePath { get; }
    public string LocalPath { get; }
    public string SourceType { get; }
    public string SourceFileName { get; }
    public string SourceHash { get; }
    public long SourceSizeBytes { get; }
    public DateTime SourceLastWriteTimeUtc { get; }
    public string Identity { get; }
    public bool UsesLocalStagingCopy => _temporaryPath is not null;

    public ValueTask DisposeAsync()
    {
        if (_temporaryPath is not null)
        {
            try
            {
                File.Delete(_temporaryPath);
            }
            catch (IOException)
            {
                // A stale local stage is safe to retain for later local cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Do not turn a completed sync into a failure because local cleanup was denied.
            }
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class DebtWorkbookAcquisitionException : Exception
{
    public DebtWorkbookAcquisitionException(
        string userMessage,
        string sourceFileName,
        string sourceType,
        string identity,
        Exception? innerException = null)
        : base(userMessage, innerException)
    {
        SourceFileName = sourceFileName;
        SourceType = sourceType;
        Identity = identity;
    }

    public string SourceFileName { get; }
    public string SourceType { get; }
    public string Identity { get; }
}

public interface IDebtWorkbookAcquirer
{
    Task<DebtWorkbookAcquisition> AcquireAsync(
        DebtWorkbookLocation location,
        CancellationToken cancellationToken = default);
}

public sealed class DebtWorkbookAcquirer : IDebtWorkbookAcquirer
{
    private readonly DebtSegmentationPreviewOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DebtWorkbookAcquirer>? _logger;
    private readonly Func<string, bool> _networkPathDetector;
    private readonly Action<string>? _afterNetworkCopyTestHook;

    public DebtWorkbookAcquirer(
        IOptions<DebtSegmentationPreviewOptions> options,
        IHostEnvironment environment,
        ILogger<DebtWorkbookAcquirer>? logger = null)
        : this(options, environment, logger, IsNetworkPath, null)
    {
    }

    internal DebtWorkbookAcquirer(
        IOptions<DebtSegmentationPreviewOptions> options,
        IHostEnvironment environment,
        ILogger<DebtWorkbookAcquirer>? logger,
        Func<string, bool> networkPathDetector,
        Action<string>? afterNetworkCopyTestHook)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
        _networkPathDetector = networkPathDetector;
        _afterNetworkCopyTestHook = afterNetworkCopyTestHook;
    }

    public async Task<DebtWorkbookAcquisition> AcquireAsync(
        DebtWorkbookLocation location,
        CancellationToken cancellationToken = default)
    {
        var path = location.Path;
        var sourceType = _networkPathDetector(path) ? DebtSourceTypes.Network : DebtSourceTypes.Local;
        var sourceFileName = Path.GetFileName(path);
        FileMetadata initial;
        try
        {
            initial = ReadMetadata(path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(sourceType, sourceFileName, path, exception);
        }

        var identity = FileIdentity(path, initial);
        if (_options.MinimumFileAgeSeconds > 0 &&
            DateTime.UtcNow - initial.LastWriteTimeUtc < TimeSpan.FromSeconds(_options.MinimumFileAgeSeconds))
        {
            throw new DebtWorkbookAcquisitionException(
                "The source workbook has not reached the configured minimum file age. Wait for the save to finish and try Sync Now again.",
                sourceFileName, sourceType, identity);
        }

        var delay = sourceType == DebtSourceTypes.Network
            ? Math.Max(_options.StabilizationDelayMilliseconds, _options.NetworkStabilizationDelayMilliseconds)
            : _options.StabilizationDelayMilliseconds;
        if (delay > 0)
            await Task.Delay(delay, cancellationToken);

        FileMetadata stable;
        try
        {
            stable = ReadMetadata(path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(sourceType, sourceFileName, identity, exception);
        }

        if (initial != stable)
        {
            throw new DebtWorkbookAcquisitionException(
                "The source file size or modified time changed during stabilization. Wait for the save to finish and try Sync Now again.",
                sourceFileName, sourceType, identity);
        }

        return sourceType == DebtSourceTypes.Network
            ? await AcquireNetworkCopyAsync(path, sourceFileName, stable, identity, cancellationToken)
            : await AcquireLocalAsync(path, sourceFileName, stable, identity, cancellationToken);
    }

    internal static bool IsNetworkPath(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal);

    private async Task<DebtWorkbookAcquisition> AcquireLocalAsync(
        string path,
        string sourceFileName,
        FileMetadata stable,
        string identity,
        CancellationToken cancellationToken)
    {
        try
        {
            var hash = await HashAsync(path, FileShare.Read, cancellationToken);
            var final = ReadMetadata(path);
            if (stable != final)
            {
                throw new DebtWorkbookAcquisitionException(
                    "The source workbook changed while it was being read. Wait and try Sync Now again.",
                    sourceFileName, DebtSourceTypes.Local, identity);
            }

            return new DebtWorkbookAcquisition(
                path, path, DebtSourceTypes.Local, sourceFileName, hash,
                final.Length, final.LastWriteTimeUtc, identity);
        }
        catch (DebtWorkbookAcquisitionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(DebtSourceTypes.Local, sourceFileName, identity, exception);
        }
    }

    private async Task<DebtWorkbookAcquisition> AcquireNetworkCopyAsync(
        string path,
        string sourceFileName,
        FileMetadata stable,
        string identity,
        CancellationToken cancellationToken)
    {
        var stagingRoot = ResolveStagingRoot();
        Directory.CreateDirectory(stagingRoot);
        var stagePath = Path.Combine(stagingRoot, $"network-source-{Guid.NewGuid():N}.xlsx");
        try
        {
            await using (var input = OpenSharedNetworkRead(path, useAsync: true))
            await using (var output = new FileStream(
                             stagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            _afterNetworkCopyTestHook?.Invoke(path);
            var final = ReadMetadata(path);
            var stageFile = new FileInfo(stagePath);
            if (stable != final || stageFile.Length != final.Length)
            {
                throw new DebtWorkbookAcquisitionException(
                    "The network workbook is being modified. Wait for the save to finish and try Sync Now again.",
                    sourceFileName, DebtSourceTypes.Network, identity);
            }

            var stageHash = await HashAsync(stagePath, FileShare.Read, cancellationToken);
            var sourceHash = await HashAsync(
                path, FileShare.ReadWrite | FileShare.Delete, cancellationToken);
            var verified = ReadMetadata(path);
            if (final != verified || !string.Equals(stageHash, sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new DebtWorkbookAcquisitionException(
                    "The network workbook changed during acquisition. Wait for the save to finish and try Sync Now again.",
                    sourceFileName, DebtSourceTypes.Network, identity);
            }

            return new DebtWorkbookAcquisition(
                path, stagePath, DebtSourceTypes.Network, sourceFileName, sourceHash,
                verified.Length, verified.LastWriteTimeUtc, identity, stagePath);
        }
        catch (DebtWorkbookAcquisitionException)
        {
            TryDeleteStage(stagePath);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            TryDeleteStage(stagePath);
            throw Failure(DebtSourceTypes.Network, sourceFileName, identity, exception);
        }
    }

    private string ResolveStagingRoot()
    {
        var configured = string.IsNullOrWhiteSpace(_options.StagingPath)
            ? Path.Combine(_options.HistoryPath, "staging")
            : _options.StagingPath;
        var resolved = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));
        if (IsNetworkPath(resolved))
            throw new InvalidOperationException("Debt Sync staging must be on the local COOP-AI machine.");
        return resolved;
    }

    private DebtWorkbookAcquisitionException Failure(
        string sourceType,
        string sourceFileName,
        string identity,
        Exception exception)
    {
        _logger?.LogWarning(exception, "Debt workbook acquisition failed for source type {SourceType}", sourceType);
        var message = sourceType == DebtSourceTypes.Network
            ? "The network source workbook is unavailable, missing, locked, or access was denied. Verify the share and try Sync Now again."
            : "The configured source workbook is unavailable or could not be read. Verify the file and try Sync Now again.";
        return new DebtWorkbookAcquisitionException(message, sourceFileName, sourceType, identity, exception);
    }

    private static FileMetadata ReadMetadata(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists)
            throw new FileNotFoundException("Source workbook was not found.", path);
        return new FileMetadata(file.Length, file.LastWriteTimeUtc);
    }

    private static string FileIdentity(string path, FileMetadata metadata) =>
        $"{path}|{metadata.Length}|{metadata.LastWriteTimeUtc.Ticks}";

    private static FileStream OpenSharedNetworkRead(string path, bool useAsync) => new(
        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
        1024 * 1024, (useAsync ? FileOptions.Asynchronous : FileOptions.None) | FileOptions.SequentialScan);

    private static async Task<string> HashAsync(
        string path,
        FileShare fileShare,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, fileShare, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void TryDeleteStage(string stagePath)
    {
        try
        {
            File.Delete(stagePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct FileMetadata(long Length, DateTime LastWriteTimeUtc);
}
