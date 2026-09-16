using COOPAI.API.DTOs.DebtSegmentation;
using COOPAI.API.Services.DebtSegmentation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Tests;

public sealed class InstallmentMasterAutoSyncCoordinatorTests
{
    private static readonly DateTime Baseline = new(2026, 8, 24, 4, 7, 53, DateTimeKind.Utc);

    [Fact]
    public async Task UnchangedMetadata_DoesNotImportOrCreateHistoryNoise()
    {
        var store = await StoreWithCurrentAsync(100, Baseline);
        var pipeline = new FakePipeline(Result(DebtSyncStatuses.Success, true));
        var coordinator = Create(new SequenceProbe(Available("same", 100, Baseline)), pipeline, store);

        await coordinator.PollOnceAsync();
        await coordinator.PollOnceAsync();

        Assert.Equal(0, pipeline.CallCount);
        Assert.Equal(DebtAutoSyncStates.Ready, coordinator.GetStatus().Status);
    }

    [Fact]
    public async Task ChangedMetadataWithSameSha_RunsOnceThenSuppressesPolls()
    {
        var store = await StoreWithCurrentAsync(100, Baseline);
        var pipeline = new FakePipeline(Result(DebtSyncStatuses.NoChange, false));
        var changed = Available("changed", 100, Baseline.AddMinutes(1));
        var coordinator = Create(new SequenceProbe(changed), pipeline, store);

        await coordinator.PollOnceAsync();
        await coordinator.PollOnceAsync();

        Assert.Equal(1, pipeline.CallCount);
        Assert.NotNull(coordinator.GetStatus().LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public async Task ChangedValidSource_ActivatesNewSnapshot()
    {
        var store = await StoreWithCurrentAsync(100, Baseline);
        var changedTime = Baseline.AddMinutes(1);
        var pipeline = new FakePipeline(Result(DebtSyncStatuses.Success, true), async () =>
            await store.PublishAsync(Snapshot("new", 101, changedTime)));
        var coordinator = Create(new SequenceProbe(Available("changed", 101, changedTime)), pipeline, store);

        await coordinator.PollOnceAsync();

        Assert.Equal(1, pipeline.CallCount);
        Assert.Equal("new", (await store.GetCurrentAsync())?.SnapshotId);
        Assert.Equal(DebtAutoSyncStates.Ready, coordinator.GetStatus().Status);
    }

    [Fact]
    public async Task InvalidSourceRetainsCurrentAndSuppressesImmediateRetry()
    {
        var store = await StoreWithCurrentAsync(100, Baseline);
        var pipeline = new FakePipeline(Result(DebtSyncStatuses.Failed, false));
        var changed = Available("bad", 101, Baseline.AddMinutes(1));
        var coordinator = Create(new SequenceProbe(changed), pipeline, store);

        await coordinator.PollOnceAsync();
        await coordinator.PollOnceAsync();

        Assert.Equal(1, pipeline.CallCount);
        Assert.Equal("current", (await store.GetCurrentAsync())?.SnapshotId);
        Assert.Equal(DebtAutoSyncStates.Error, coordinator.GetStatus().Status);
    }

    [Fact]
    public async Task OfflineThenRecoveredRecoversWithoutRestart()
    {
        var store = await StoreWithCurrentAsync(100, Baseline);
        var unavailable = new DebtAutoSyncSourceState(
            false, DebtSourceTypes.Network, "2534-2569.xlsx", null, null, null, "offline");
        var pipeline = new FakePipeline(Result(DebtSyncStatuses.Success, true));
        var coordinator = Create(new SequenceProbe(
            unavailable, Available("recovered", 101, Baseline.AddMinutes(1))), pipeline, store);

        await coordinator.PollOnceAsync();
        Assert.Equal(DebtAutoSyncStates.NetworkUnavailable, coordinator.GetStatus().Status);
        await coordinator.PollOnceAsync();

        Assert.Equal(1, pipeline.CallCount);
        Assert.Equal(DebtAutoSyncStates.Ready, coordinator.GetStatus().Status);
    }

    [Fact]
    public async Task BusyPipelineIsRetriedOnNextPoll()
    {
        var store = await StoreWithCurrentAsync(100, Baseline);
        var pipeline = new FakePipeline(
            Result(DebtSyncStatuses.Busy, false), Result(DebtSyncStatuses.Success, true));
        var changed = Available("busy", 101, Baseline.AddMinutes(1));
        var coordinator = Create(new SequenceProbe(changed), pipeline, store);

        await coordinator.PollOnceAsync();
        await coordinator.PollOnceAsync();

        Assert.Equal(2, pipeline.CallCount);
        Assert.Equal(DebtAutoSyncStates.Ready, coordinator.GetStatus().Status);
    }

    private static InstallmentMasterAutoSyncCoordinator Create(
        IInstallmentMasterAutoSyncSourceProbe probe,
        IInstallmentMasterSyncPipeline pipeline,
        IInstallmentMasterSnapshotStore store) => new(
            Options.Create(new InstallmentMasterOptions
            {
                AutoSyncEnabled = true,
                AutoSyncPollSeconds = 60,
                AutoSyncFailureRetrySeconds = 300,
                WorkbookPath = @"\\server\share\2534-2569.xlsx"
            }), probe, pipeline, store, NullLogger<InstallmentMasterAutoSyncCoordinator>.Instance);

    private static DebtAutoSyncSourceState Available(string identity, long length, DateTime modified) => new(
        true, DebtSourceTypes.Network, "2534-2569.xlsx", identity, length, modified, "available");

    private static InstallmentMasterSyncStatusDto Result(string status, bool published) => new(
        DateTime.UtcNow, "Auto", status, published, !published, status, "2534-2569.xlsx", "HASH",
        published ? "new" : "current", 1, 1, 1, 0, 0, 0, 0, 1, [], []);

    private static async Task<InMemoryInstallmentMasterSnapshotStore> StoreWithCurrentAsync(
        long length,
        DateTime modified)
    {
        var store = new InMemoryInstallmentMasterSnapshotStore();
        await store.PublishAsync(Snapshot("current", length, modified));
        return store;
    }

    private static PublishedInstallmentMasterSnapshot Snapshot(string id, long length, DateTime modified) => new(
        id, MonthlyAmountDueVersion.InstallmentMasterV3, "2534-2569.xlsx", @"\\server\share\2534-2569.xlsx", "HASH",
        length, modified, DateTime.UtcNow,
        new InstallmentMasterReadResult("2534-2569.xlsx", "2534-2569", 1, 1, 1, 0, 0, 0, 0,
            [new InstallmentMasterContract("C1", 12, 100m, InstallmentValidationStatuses.Usable,
                InstallmentDuplicateStatuses.None, [], 2)], []))
    { SourceType = DebtSourceTypes.Network };

    private sealed class SequenceProbe(params DebtAutoSyncSourceState[] states) : IInstallmentMasterAutoSyncSourceProbe
    {
        private int _index;
        public Task<DebtAutoSyncSourceState> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(states[Math.Min(_index++, states.Length - 1)]);
    }

    private sealed class FakePipeline : IInstallmentMasterSyncPipeline
    {
        private readonly Queue<InstallmentMasterSyncStatusDto> _results;
        private readonly Func<Task>? _onFirst;

        public FakePipeline(params InstallmentMasterSyncStatusDto[] results) : this(results, null) { }
        public FakePipeline(InstallmentMasterSyncStatusDto result, Func<Task> onFirst) : this([result], onFirst) { }
        private FakePipeline(IEnumerable<InstallmentMasterSyncStatusDto> results, Func<Task>? onFirst)
        {
            _results = new Queue<InstallmentMasterSyncStatusDto>(results);
            _onFirst = onFirst;
        }
        public int CallCount { get; private set; }
        public async Task<InstallmentMasterSyncStatusDto> SyncAutoAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1 && _onFirst is not null)
                await _onFirst();
            return _results.Count > 1 ? _results.Dequeue() : _results.Peek();
        }
    }
}
