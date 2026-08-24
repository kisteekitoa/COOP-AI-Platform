using System.Text.Json;
using COOPAI.API.Data;
using COOPAI.API.Models.Auth;
using COOPAI.API.Models.Portfolio;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Tests;

public sealed class PortfolioSnapshotPublishTests
{
    [Fact]
    public async Task Publish_Disabled_IsConflictAndWritesNothing()
    {
        await using var database = await PublishDatabase.CreateAsync();
        var (userId, targetId, hash) = await database.SeedAsync();
        await using var context = database.CreateContext();
        var service = Service(context, enabled: false);

        var exception = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(
            () => service.PublishAsync(targetId, hash, userId));

        Assert.Equal("PublishingDisabled", exception.Code);
        await using var verification = database.CreateContext();
        Assert.Equal(PortfolioSnapshotStatus.Validated,
            (await verification.PortfolioSnapshots.SingleAsync(x => x.Id == targetId)).Status);
    }

    [Fact]
    public async Task Review_CanPublishUsesServerIntegrityRulesAndWarningsRemainNonBlocking()
    {
        await using var database = await PublishDatabase.CreateAsync();
        var (_, targetId, _) = await database.SeedAsync();
        await using var enabledContext = database.CreateContext();
        var eligible = await Service(enabledContext).GetReviewAsync(targetId);

        Assert.NotNull(eligible);
        Assert.True(eligible.PublishingEnabled);
        Assert.True(eligible.CanPublish);
        Assert.Empty(eligible.PublishBlockedReasons);
        Assert.Equal(1, eligible.Counts.WarningContracts);
        Assert.Equal(1, eligible.Counts.UnresolvedMemberContracts);

        await using var disabledContext = database.CreateContext();
        var disabled = await Service(disabledContext, enabled: false).GetReviewAsync(targetId);
        Assert.NotNull(disabled);
        Assert.False(disabled.CanPublish);
        Assert.Contains("PublishingDisabled", disabled.PublishBlockedReasons);

        await using (var tamperContext = database.CreateContext())
        {
            var record = await tamperContext.PortfolioSnapshotRecords.SingleAsync(
                x => x.PortfolioSnapshotId == targetId);
            record.TotalOutstanding++;
            await tamperContext.SaveChangesAsync();
        }
        await using var tamperedContext = database.CreateContext();
        var tampered = await Service(tamperedContext).GetReviewAsync(targetId);
        Assert.NotNull(tampered);
        Assert.False(tampered.CanPublish);
        Assert.Contains("HashMismatch", tampered.PublishBlockedReasons);
        Assert.Contains("IntegrityMismatch", tampered.PublishBlockedReasons);
    }

    [Fact]
    public async Task Publish_FirstSnapshot_SetsStableAuditAndAllowsWarningsAndUnresolvedMembers()
    {
        await using var database = await PublishDatabase.CreateAsync();
        var (userId, targetId, hash) = await database.SeedAsync();
        await using var context = database.CreateContext();

        var result = await Service(context).PublishAsync(targetId, hash, userId);

        Assert.Equal("Published", result.Status);
        Assert.Equal(userId, result.PublishedByUserId);
        Assert.Null(result.PreviousSupersededSnapshotId);
        await using var verification = database.CreateContext();
        var persisted = await verification.PortfolioSnapshots.SingleAsync(x => x.Id == targetId);
        Assert.Equal(PortfolioSnapshotStatus.Published, persisted.Status);
        Assert.Equal(userId, persisted.PublishedByUserId);
        Assert.NotNull(persisted.PublishedAt);
        Assert.Equal(1, persisted.WarningRecordCount);
        Assert.Equal(1, persisted.UnresolvedMemberContractCount);
        Assert.Single(await verification.PortfolioSnapshots
            .Where(x => x.Status == PortfolioSnapshotStatus.Published).ToListAsync());
    }

    [Theory]
    [InlineData(PortfolioSnapshotStatus.Draft, "InvalidSnapshotTransition")]
    [InlineData(PortfolioSnapshotStatus.Rejected, "InvalidSnapshotTransition")]
    [InlineData(PortfolioSnapshotStatus.Superseded, "InvalidSnapshotTransition")]
    [InlineData(PortfolioSnapshotStatus.Published, "SnapshotAlreadyPublished")]
    public async Task Publish_RequiresValidatedLifecycle(
        PortfolioSnapshotStatus status,
        string expectedCode)
    {
        await using var database = await PublishDatabase.CreateAsync();
        var (userId, targetId, hash) = await database.SeedAsync(status);
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(
            () => Service(context).PublishAsync(targetId, hash, userId));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task Publish_WrongReviewedHash_DoesNotMutateTarget()
    {
        await using var database = await PublishDatabase.CreateAsync();
        var (userId, targetId, _) = await database.SeedAsync();
        await using var context = database.CreateContext();

        var exception = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(
            () => Service(context).PublishAsync(targetId, new string('F', 64), userId));

        Assert.Equal("SnapshotContentHashMismatch", exception.Code);
        await using var verification = database.CreateContext();
        Assert.Equal(PortfolioSnapshotStatus.Validated,
            (await verification.PortfolioSnapshots.SingleAsync(x => x.Id == targetId)).Status);
    }

    [Fact]
    public async Task Publish_BlockingErrorOrPersistedTamper_IsRejected()
    {
        await using var database = await PublishDatabase.CreateAsync();
        var (userId, blockingId, blockingHash) = await database.SeedAsync();
        var (_, tamperedId, tamperedHash) = await database.SeedAsync(
            asOfDate: new DateOnly(2026, 7, 31));
        await using (var mutate = database.CreateContext())
        {
            var blocking = await mutate.PortfolioSnapshots.SingleAsync(x => x.Id == blockingId);
            blocking.BlockingErrorCount = 1;
            var tampered = await mutate.PortfolioSnapshots.SingleAsync(x => x.Id == tamperedId);
            tampered.TotalContractCount = 2;
            await mutate.SaveChangesAsync();
        }

        await using var first = database.CreateContext();
        var blockingException = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(
            () => Service(first).PublishAsync(blockingId, blockingHash, userId));
        await using var second = database.CreateContext();
        var tamperException = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(
            () => Service(second).PublishAsync(tamperedId, tamperedHash, userId));

        Assert.Equal("BlockingErrors", blockingException.Code);
        Assert.Equal("SnapshotIntegrityMismatch", tamperException.Code);
    }

    [Fact]
    public async Task Publish_SecondSnapshot_AtomicallySupersedesPreviousAndRetryIsImmutable()
    {
        await using var database = await PublishDatabase.CreateAsync();
        var (userId, firstId, firstHash) = await database.SeedAsync();
        await using (var firstContext = database.CreateContext())
            await Service(firstContext).PublishAsync(firstId, firstHash, userId);
        var (_, secondId, secondHash) = await database.SeedAsync(
            asOfDate: new DateOnly(2026, 7, 31));

        DateTime firstPublishedAt;
        await using (var before = database.CreateContext())
            firstPublishedAt = (await before.PortfolioSnapshots.SingleAsync(x => x.Id == firstId)).PublishedAt!.Value;

        await using (var secondContext = database.CreateContext())
        {
            var result = await Service(secondContext).PublishAsync(secondId, secondHash, userId);
            Assert.Equal(firstId, result.PreviousSupersededSnapshotId);
        }

        await using (var retryContext = database.CreateContext())
        {
            var retry = await Assert.ThrowsAsync<PortfolioSnapshotWorkflowException>(
                () => Service(retryContext).PublishAsync(secondId, secondHash, userId));
            Assert.Equal("SnapshotAlreadyPublished", retry.Code);
        }

        await using var verification = database.CreateContext();
        var first = await verification.PortfolioSnapshots.SingleAsync(x => x.Id == firstId);
        var second = await verification.PortfolioSnapshots.SingleAsync(x => x.Id == secondId);
        Assert.Equal(PortfolioSnapshotStatus.Superseded, first.Status);
        Assert.NotNull(first.SupersededAt);
        Assert.Equal(secondId, first.SupersededBySnapshotId);
        Assert.Equal(firstPublishedAt, first.PublishedAt);
        Assert.Equal(PortfolioSnapshotStatus.Published, second.Status);
        Assert.Single(await verification.PortfolioSnapshots
            .Where(x => x.Status == PortfolioSnapshotStatus.Published).ToListAsync());
    }

    [Fact]
    public async Task Publish_FailureAfterSupersedeSave_RollsBackEverything()
    {
        await using var database = await PublishDatabase.CreateAsync();
        var (userId, firstId, firstHash) = await database.SeedAsync();
        await using (var firstContext = database.CreateContext())
            await Service(firstContext).PublishAsync(firstId, firstHash, userId);
        var (_, secondId, secondHash) = await database.SeedAsync(
            asOfDate: new DateOnly(2026, 7, 31));
        await using var failingContext = database.CreateContext();
        var service = Service(
            failingContext,
            hooks: [new ThrowingPublishHook(PortfolioSnapshotPersistenceStage.PublishAfterSupersedeSaved)]);

        await Assert.ThrowsAsync<InjectedPublishException>(
            () => service.PublishAsync(secondId, secondHash, userId));

        await using var verification = database.CreateContext();
        Assert.Equal(PortfolioSnapshotStatus.Published,
            (await verification.PortfolioSnapshots.SingleAsync(x => x.Id == firstId)).Status);
        Assert.Equal(PortfolioSnapshotStatus.Validated,
            (await verification.PortfolioSnapshots.SingleAsync(x => x.Id == secondId)).Status);
        Assert.Single(await verification.PortfolioSnapshots
            .Where(x => x.Status == PortfolioSnapshotStatus.Published).ToListAsync());
    }

    [Fact]
    public async Task DatabaseInvariant_RejectsTwoCurrentPublishedRows()
    {
        await using var database = await PublishDatabase.CreateAsync();
        _ = await database.SeedAsync(PortfolioSnapshotStatus.Published);
        await using var context = database.CreateContext();
        context.PortfolioSnapshots.Add(ValidSnapshot(
            PortfolioSnapshotStatus.Published,
            new DateOnly(2026, 7, 31),
            "Z"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentPublish_TwoProcessesHaveOneWinnerAndOneCurrentSnapshot()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"coopai-publish-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 5
        }.ToString();
        try
        {
            var options = new DbContextOptionsBuilder<CoopDbContext>()
                .UseSqlite(connectionString)
                .Options;
            int userId;
            int firstTargetId;
            string firstHash;
            int secondTargetId;
            string secondHash;
            await using (var setup = new CoopDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var user = new CoopUser
                {
                    UserName = "concurrent.manager",
                    NormalizedUserName = "CONCURRENT.MANAGER",
                    DisplayName = "Concurrent Manager",
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N")
                };
                var current = ValidSnapshot(
                    PortfolioSnapshotStatus.Published,
                    new DateOnly(2026, 5, 31),
                    "P");
                var first = ValidSnapshot(
                    PortfolioSnapshotStatus.Validated,
                    new DateOnly(2026, 6, 30),
                    "Q");
                var second = ValidSnapshot(
                    PortfolioSnapshotStatus.Validated,
                    new DateOnly(2026, 7, 31),
                    "R");
                setup.Users.Add(user);
                setup.PortfolioSnapshots.AddRange(current, first, second);
                await setup.SaveChangesAsync();
                userId = user.Id;
                firstTargetId = first.Id;
                firstHash = first.SnapshotContentHash;
                secondTargetId = second.Id;
                secondHash = second.SnapshotContentHash;
            }

            using var barrier = new Barrier(2);
            var hook = new PublishObservationBarrierHook(barrier);
            var firstAttempt = Task.Run(() => AttemptAsync(firstTargetId, firstHash));
            var secondAttempt = Task.Run(() => AttemptAsync(secondTargetId, secondHash));
            var outcomes = await Task.WhenAll(firstAttempt, secondAttempt);

            Assert.Single(outcomes, outcome => outcome == "Published");
            Assert.Single(outcomes, outcome =>
                outcome is "ConcurrentPublishDetected" or "PublishConflict");
            await using var verification = new CoopDbContext(options);
            Assert.Single(await verification.PortfolioSnapshots
                .Where(x => x.Status == PortfolioSnapshotStatus.Published)
                .ToListAsync());

            async Task<string> AttemptAsync(int targetId, string hash)
            {
                await using var context = new CoopDbContext(options);
                try
                {
                    var result = await Service(context, hooks: [hook])
                        .PublishAsync(targetId, hash, userId);
                    return result.Status;
                }
                catch (PortfolioSnapshotWorkflowException exception)
                {
                    return exception.Code;
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static PortfolioSnapshotWorkflowService Service(
        CoopDbContext context,
        bool enabled = true,
        IEnumerable<IPortfolioSnapshotPersistenceHook>? hooks = null) =>
        new(
            context,
            null!,
            hooks,
            options: Options.Create(new PortfolioSnapshotOptions
            {
                PublishingEnabled = enabled
            }));

    private static PortfolioSnapshot ValidSnapshot(
        PortfolioSnapshotStatus status,
        DateOnly asOfDate,
        string seed)
    {
        var record = new PortfolioSnapshotRecord
        {
            SourceRowNumber = 7,
            SourceRecordKey = $"row:{seed}",
            NormalizedContractNo = $"สม-2569-0000{seed[0] % 10}",
            ContractDate = new DateOnly(2025, 1, 1),
            ExpireDate = asOfDate,
            SourceRowKind = PortfolioSourceRowKind.Contract,
            OpeningSide = PortfolioOpeningSide.Previous,
            TermStatus = PortfolioTermStatus.InTerm,
            BalanceStatus = PortfolioBalanceStatus.Outstanding,
            CanonicalMatchStatus = PortfolioCanonicalMatchStatus.Missing,
            MemberMatchStatus = PortfolioMemberMatchStatus.Missing,
            InclusionStatus = PortfolioSnapshotInclusionStatus.IncludedWithWarning,
            LoanTypePrefix = "สม",
            WarningCodesJson = JsonSerializer.Serialize(new[] { PortfolioSnapshotCodes.Negative }),
            PrincipalOpening = 80m,
            ProfitOpening = 20m,
            TotalOpening = 100m,
            PrincipalOutstanding = 80m,
            ProfitOutstanding = 20m,
            TotalOutstanding = 100m
        };
        var snapshot = new PortfolioSnapshot
        {
            AsOfDate = asOfDate,
            Revision = 1,
            Status = status,
            DefinitionVersion = 2,
            SourceFileName = $"{seed}.xlsx",
            SourceFileHash = new string(seed[0], 64),
            SourceFileSizeBytes = 1,
            SourceRetrievedAt = DateTime.UtcNow,
            TotalSourceRows = 1,
            TotalContractCount = 1,
            MatchedCanonicalCount = 0,
            MissingCanonicalCount = 1,
            UnresolvedMemberContractCount = 1,
            WarningRecordCount = 1,
            InTermContractCount = 1,
            OutstandingContractCount = 1,
            InTermOutstandingContractCount = 1,
            PrincipalOpening = 80m,
            ProfitOpening = 20m,
            TotalOpening = 100m,
            PrincipalOutstanding = 80m,
            ProfitOutstanding = 20m,
            TotalOutstanding = 100m,
            ValidatedAt = status == PortfolioSnapshotStatus.Draft ? null : DateTime.UtcNow,
            Records = [record]
        };
        snapshot.SnapshotContentHash = new PortfolioSnapshotContentHasher().Compute(snapshot);
        return snapshot;
    }

    private sealed class PublishDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly DbContextOptions<CoopDbContext> _options;
        private char _seed = 'A';

        private PublishDatabase()
        {
            _options = new DbContextOptionsBuilder<CoopDbContext>()
                .UseSqlite(_connection)
                .Options;
        }

        public static async Task<PublishDatabase> CreateAsync()
        {
            var database = new PublishDatabase();
            await database._connection.OpenAsync();
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public CoopDbContext CreateContext() => new(_options);

        public async Task<(int UserId, int SnapshotId, string Hash)> SeedAsync(
            PortfolioSnapshotStatus status = PortfolioSnapshotStatus.Validated,
            DateOnly? asOfDate = null)
        {
            await using var context = CreateContext();
            var user = await context.Users.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (user is null)
            {
                user = new CoopUser
                {
                    UserName = "publish.manager",
                    NormalizedUserName = "PUBLISH.MANAGER",
                    DisplayName = "Publish Manager",
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N")
                };
                context.Users.Add(user);
                await context.SaveChangesAsync();
            }

            var seed = (_seed++).ToString();
            var snapshot = ValidSnapshot(
                status,
                asOfDate ?? new DateOnly(2026, 6, 30),
                seed);
            context.PortfolioSnapshots.Add(snapshot);
            await context.SaveChangesAsync();
            return (user.Id, snapshot.Id, snapshot.SnapshotContentHash);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class ThrowingPublishHook(PortfolioSnapshotPersistenceStage failureStage)
        : IPortfolioSnapshotPersistenceHook
    {
        public Task OnStageAsync(
            PortfolioSnapshotPersistenceStage stage,
            PortfolioSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (stage == failureStage)
                throw new InjectedPublishException();
            return Task.CompletedTask;
        }
    }

    private sealed class PublishObservationBarrierHook(Barrier barrier)
        : IPortfolioSnapshotPersistenceHook
    {
        public Task OnStageAsync(
            PortfolioSnapshotPersistenceStage stage,
            PortfolioSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (stage == PortfolioSnapshotPersistenceStage.PublishAfterCurrentObserved &&
                !barrier.SignalAndWait(TimeSpan.FromSeconds(10), cancellationToken))
            {
                throw new TimeoutException("Concurrent publish observation barrier timed out.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class InjectedPublishException : Exception;
}
