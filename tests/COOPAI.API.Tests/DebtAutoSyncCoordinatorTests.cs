using COOPAI.API.DTOs.DebtSegmentation;
using COOPAI.API.Services.DebtSegmentation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Tests;

public sealed class DebtAutoSyncCoordinatorTests
{
    private static readonly DateTime BaselineTime = new(2026, 9, 6, 9, 32, 47, DateTimeKind.Utc);

    [Fact]
    public async Task UnchangedMetadata_DoesNotRunPipelineOrSpamHistory()
    {
        var store = await StoreWithCurrentAsync(100, BaselineTime);
        var source = Available("same", 100, BaselineTime);
        var pipeline = new FakePipeline(Sync(DebtSyncStatuses.Success, published: true));
        var coordinator = CreateCoordinator(new SequenceProbe(source), pipeline, store);

        await coordinator.PollOnceAsync();
        await coordinator.PollOnceAsync();

        Assert.Equal(0, pipeline.CallCount);
        Assert.Equal(DebtAutoSyncStates.Ready, coordinator.GetStatus().Status);
    }

    [Fact]
    public async Task ChangedMetadataWithSameHash_RunsOnceThenSuppressesUnchangedPolls()
    {
        var store = await StoreWithCurrentAsync(100, BaselineTime);
        var changed = Available("changed", 100, BaselineTime.AddMinutes(1));
        var pipeline = new FakePipeline(Sync(DebtSyncStatuses.NoChange, published: false));
        var coordinator = CreateCoordinator(new SequenceProbe(changed), pipeline, store);

        await coordinator.PollOnceAsync();
        await coordinator.PollOnceAsync();

        Assert.Equal(1, pipeline.CallCount);
        Assert.Equal(DebtAutoSyncStates.Ready, coordinator.GetStatus().Status);
        Assert.NotNull(coordinator.GetStatus().LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public async Task ChangedValidSource_UsesPipelineAndPipelineCanActivateNewSnapshot()
    {
        var store = await StoreWithCurrentAsync(100, BaselineTime);
        var changedTime = BaselineTime.AddMinutes(1);
        var pipeline = new FakePipeline(Sync(DebtSyncStatuses.Success, published: true), async () =>
            await store.PublishAsync(Snapshot("new", 101, changedTime)));
        var coordinator = CreateCoordinator(
            new SequenceProbe(Available("changed", 101, changedTime)), pipeline, store);

        await coordinator.PollOnceAsync();

        Assert.Equal(1, pipeline.CallCount);
        Assert.Equal("new", (await store.GetCurrentAsync())?.SnapshotId);
        Assert.Equal(DebtAutoSyncStates.Ready, coordinator.GetStatus().Status);
    }

    [Fact]
    public async Task InvalidChangedSource_RetainsCurrentAndSuppressesImmediateRepeat()
    {
        var store = await StoreWithCurrentAsync(100, BaselineTime);
        var changed = Available("invalid", 101, BaselineTime.AddMinutes(1));
        var pipeline = new FakePipeline(Sync(DebtSyncStatuses.Failed, published: false));
        var coordinator = CreateCoordinator(new SequenceProbe(changed), pipeline, store);

        await coordinator.PollOnceAsync();
        await coordinator.PollOnceAsync();

        Assert.Equal(1, pipeline.CallCount);
        Assert.Equal("current", (await store.GetCurrentAsync())?.SnapshotId);
        Assert.Equal(DebtAutoSyncStates.Error, coordinator.GetStatus().Status);
    }

    [Fact]
    public async Task NetworkUnavailableThenRecovered_ServiceStaysAliveAndResumes()
    {
        var store = await StoreWithCurrentAsync(100, BaselineTime);
        var unavailable = new DebtAutoSyncSourceState(
            false, DebtSourceTypes.Network, "operational.xlsx", null, null, null,
            "Network source is unavailable.");
        var recovered = Available("recovered", 101, BaselineTime.AddMinutes(1));
        var pipeline = new FakePipeline(Sync(DebtSyncStatuses.Success, published: true));
        var coordinator = CreateCoordinator(new SequenceProbe(unavailable, recovered), pipeline, store);

        await coordinator.PollOnceAsync();
        Assert.Equal(DebtAutoSyncStates.NetworkUnavailable, coordinator.GetStatus().Status);
        Assert.Equal(0, pipeline.CallCount);

        await coordinator.PollOnceAsync();
        Assert.Equal(1, pipeline.CallCount);
        Assert.Equal(DebtAutoSyncStates.Ready, coordinator.GetStatus().Status);
    }

    [Fact]
    public async Task BusySharedPipeline_IsRetriedWithoutMarkingMetadataProcessed()
    {
        var store = await StoreWithCurrentAsync(100, BaselineTime);
        var changed = Available("busy", 101, BaselineTime.AddMinutes(1));
        var pipeline = new FakePipeline(
            Sync(DebtSyncStatuses.Busy, published: false),
            Sync(DebtSyncStatuses.Success, published: true));
        var coordinator = CreateCoordinator(new SequenceProbe(changed), pipeline, store);

        await coordinator.PollOnceAsync();
        await coordinator.PollOnceAsync();

        Assert.Equal(2, pipeline.CallCount);
        Assert.Equal(DebtAutoSyncStates.Ready, coordinator.GetStatus().Status);
    }

    private static DebtAutoSyncCoordinator CreateCoordinator(
        IDebtAutoSyncSourceProbe probe,
        IDebtAutoSyncPipeline pipeline,
        IDebtSnapshotStore store) => new(
            Options.Create(new DebtSegmentationPreviewOptions
            {
                AutoSyncEnabled = true,
                AutoSyncPollSeconds = 60,
                AutoSyncFailureRetrySeconds = 300,
                WorkbookPath = @"\\server\share\operational.xlsx"
            }),
            probe,
            pipeline,
            store,
            NullLogger<DebtAutoSyncCoordinator>.Instance);

    private static DebtAutoSyncSourceState Available(
        string identity,
        long length,
        DateTime modified) => new(
            true, DebtSourceTypes.Network, "operational.xlsx", identity, length, modified,
            "Source metadata is available.");

    private static DebtSyncStatusDto Sync(string status, bool published) => new(
        status, DateTime.UtcNow, status != DebtSyncStatuses.Failed && status != DebtSyncStatuses.Busy,
        published, !published, "Auto", status, "operational.xlsx", "HASH", DateTime.UtcNow,
        1, 1, 0, 0, 0, [], [], new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1),
        1, 0, 1, 100m, 0, 0, published ? "new" : "current", 1)
    {
        SourceType = DebtSourceTypes.Network
    };

    private static async Task<InMemoryDebtSnapshotStore> StoreWithCurrentAsync(long length, DateTime modified)
    {
        var store = new InMemoryDebtSnapshotStore();
        await store.PublishAsync(Snapshot("current", length, modified));
        return store;
    }

    private static PublishedDebtSnapshot Snapshot(string id, long length, DateTime modified) => new(
        id,
        "debt-segmentation-v1",
        "operational.xlsx",
        "HASH",
        length,
        modified,
        DateTime.UtcNow,
        new DebtWorkbookReadResult("operational.xlsx", "sheet", new DateOnly(2026, 8, 1), 0, 0, [], []),
        [],
        [])
    {
        SourceType = DebtSourceTypes.Network
    };

    private sealed class SequenceProbe(params DebtAutoSyncSourceState[] states) : IDebtAutoSyncSourceProbe
    {
        private int _index;

        public Task<DebtAutoSyncSourceState> ProbeAsync(CancellationToken cancellationToken = default)
        {
            var index = Math.Min(_index++, states.Length - 1);
            return Task.FromResult(states[index]);
        }
    }

    private sealed class FakePipeline : IDebtAutoSyncPipeline
    {
        private readonly Queue<DebtSyncStatusDto> _results;
        private readonly Func<Task>? _onFirstCall;

        public FakePipeline(params DebtSyncStatusDto[] results)
            : this(results, null)
        {
        }

        public FakePipeline(DebtSyncStatusDto result, Func<Task> onFirstCall)
            : this([result], onFirstCall)
        {
        }

        private FakePipeline(IEnumerable<DebtSyncStatusDto> results, Func<Task>? onFirstCall)
        {
            _results = new Queue<DebtSyncStatusDto>(results);
            _onFirstCall = onFirstCall;
        }

        public int CallCount { get; private set; }

        public async Task<DebtSyncStatusDto> SyncAutoAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1 && _onFirstCall is not null)
                await _onFirstCall();
            return _results.Count > 1 ? _results.Dequeue() : _results.Peek();
        }
    }
}
