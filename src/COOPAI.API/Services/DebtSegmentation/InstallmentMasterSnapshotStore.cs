using System.Text.Json;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.DebtSegmentation;

public interface IInstallmentMasterSnapshotStore
{
    Task<PublishedInstallmentMasterSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task PublishAsync(PublishedInstallmentMasterSnapshot snapshot, CancellationToken cancellationToken = default);
    Task RecordSyncAsync(InstallmentMasterSyncHistoryEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InstallmentMasterSyncHistoryEntry>> ListSyncHistoryAsync(
        int take,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryInstallmentMasterSnapshotStore : IInstallmentMasterSnapshotStore
{
    private readonly object _gate = new();
    private readonly List<InstallmentMasterSyncHistoryEntry> _history = [];
    private PublishedInstallmentMasterSnapshot? _current;

    public Task<PublishedInstallmentMasterSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_current);
    }

    public Task PublishAsync(PublishedInstallmentMasterSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _current = snapshot;
        return Task.CompletedTask;
    }

    public Task RecordSyncAsync(InstallmentMasterSyncHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _history.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InstallmentMasterSyncHistoryEntry>> ListSyncHistoryAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<InstallmentMasterSyncHistoryEntry>>(
                _history.OrderByDescending(x => x.AttemptedAtUtc).Take(Math.Clamp(take, 1, 200)).ToArray());
    }
}

public sealed class FileInstallmentMasterSnapshotStore : IInstallmentMasterSnapshotStore
{
    private const string CurrentFileName = "current.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _rootPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileInstallmentMasterSnapshotStore(
        IOptions<InstallmentMasterOptions> options,
        IHostEnvironment environment)
    {
        var configured = options.Value.HistoryPath;
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("InstallmentMasterPreview:HistoryPath is required for durable Preview history.");
        _rootPath = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
        if (DebtWorkbookAcquirer.IsNetworkPath(_rootPath))
            throw new InvalidOperationException("Installment Master history must remain on the local COOP-AI machine.");
    }

    public async Task<PublishedInstallmentMasterSnapshot?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_rootPath, CurrentFileName);
        if (!File.Exists(path))
            return null;
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<PublishedInstallmentMasterSnapshot>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The current Preview Installment Master snapshot is empty or invalid.");
    }

    public async Task PublishAsync(
        PublishedInstallmentMasterSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshotsPath = Path.Combine(_rootPath, "snapshots");
            Directory.CreateDirectory(snapshotsPath);
            var immutablePath = Path.Combine(snapshotsPath, $"{snapshot.SnapshotId}.json");
            if (!File.Exists(immutablePath))
                await WriteNewAsync(immutablePath, snapshot, cancellationToken);
            Directory.CreateDirectory(_rootPath);
            await WriteReplacingAsync(Path.Combine(_rootPath, CurrentFileName), snapshot, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordSyncAsync(
        InstallmentMasterSyncHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var attemptsPath = Path.Combine(_rootPath, "sync-attempts");
            Directory.CreateDirectory(attemptsPath);
            var fileName = $"{entry.AttemptedAtUtc:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.json";
            await WriteNewAsync(Path.Combine(attemptsPath, fileName), entry, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<InstallmentMasterSyncHistoryEntry>> ListSyncHistoryAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var attemptsPath = Path.Combine(_rootPath, "sync-attempts");
        if (!Directory.Exists(attemptsPath))
            return [];
        var results = new List<InstallmentMasterSyncHistoryEntry>();
        foreach (var path in Directory.EnumerateFiles(attemptsPath, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                     .Take(Math.Clamp(take, 1, 200)))
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var entry = await JsonSerializer.DeserializeAsync<InstallmentMasterSyncHistoryEntry>(stream, JsonOptions, cancellationToken);
            if (entry is not null)
                results.Add(entry);
        }
        return results.OrderByDescending(x => x.AttemptedAtUtc).ToArray();
    }

    private static async Task WriteNewAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task WriteReplacingAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await WriteNewAsync(tempPath, value, cancellationToken);
            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
