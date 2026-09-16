using System.Text.Json;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.DebtSegmentation;

public interface IDebtSnapshotStore
{
    Task<PublishedDebtSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task PublishAsync(PublishedDebtSnapshot snapshot, CancellationToken cancellationToken = default);
    Task RecordSyncAsync(DebtSyncHistoryEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DebtSyncHistoryEntry>> ListSyncHistoryAsync(
        int take,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryDebtSnapshotStore : IDebtSnapshotStore
{
    private readonly object _gate = new();
    private readonly List<DebtSyncHistoryEntry> _history = [];
    private PublishedDebtSnapshot? _current;

    public Task<PublishedDebtSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_current);
    }

    public Task PublishAsync(PublishedDebtSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _current = snapshot;
        return Task.CompletedTask;
    }

    public Task RecordSyncAsync(DebtSyncHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _history.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DebtSyncHistoryEntry>> ListSyncHistoryAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DebtSyncHistoryEntry>>(
                _history.OrderByDescending(x => x.AttemptedAtUtc).Take(Math.Clamp(take, 1, 200)).ToArray());
        }
    }
}

public sealed class FileDebtSnapshotStore : IDebtSnapshotStore
{
    private const string CurrentFileName = "current.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _rootPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileDebtSnapshotStore(
        IOptions<DebtSegmentationPreviewOptions> options,
        IHostEnvironment environment)
    {
        var configured = options.Value.HistoryPath;
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("DebtSegmentationPreview:HistoryPath is required for durable Preview history.");
        _rootPath = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
    }

    public async Task<PublishedDebtSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_rootPath, CurrentFileName);
        if (!File.Exists(path))
            return null;

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<PublishedDebtSnapshot>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The current Preview debt publication is empty or invalid.");
    }

    public async Task PublishAsync(PublishedDebtSnapshot snapshot, CancellationToken cancellationToken = default)
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

    public async Task RecordSyncAsync(DebtSyncHistoryEntry entry, CancellationToken cancellationToken = default)
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

    public async Task<IReadOnlyList<DebtSyncHistoryEntry>> ListSyncHistoryAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var attemptsPath = Path.Combine(_rootPath, "sync-attempts");
        if (!Directory.Exists(attemptsPath))
            return [];

        var results = new List<DebtSyncHistoryEntry>();
        foreach (var path in Directory.EnumerateFiles(attemptsPath, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                     .Take(Math.Clamp(take, 1, 200)))
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var entry = await JsonSerializer.DeserializeAsync<DebtSyncHistoryEntry>(stream, JsonOptions, cancellationToken);
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
