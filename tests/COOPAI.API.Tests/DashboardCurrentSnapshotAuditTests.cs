using COOPAI.API.Data;
using COOPAI.API.DTOs.Dashboard;
using COOPAI.API.DTOs.DebtSegmentation;
using COOPAI.API.Services.Dashboard;
using COOPAI.API.Services.DebtSegmentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Tests;

[CollectionDefinition("Dashboard current snapshot audit", DisableParallelization = true)]
public sealed class DashboardCurrentSnapshotAuditCollection
{
    public const string Name = "Dashboard current snapshot audit";
}

[Collection(DashboardCurrentSnapshotAuditCollection.Name)]
public sealed class DashboardCurrentSnapshotAuditTests
{
    [Fact]
    public async Task DefaultCurrentDashboard_ReusesAuthoritativeAugustAnalyses_AndDoesNotMutateData()
    {
        var workspace = FindWorkspace();
        if (workspace is null)
            return;

        var environment = new TestHostEnvironment(Path.Combine(workspace, "src", "COOPAI.API"));
        var options = Options.Create(new DebtSegmentationPreviewOptions
        {
            Enabled = true,
            DefaultCurrentPeriod = "2026-08",
            HistoryPath = Path.Combine(workspace, "data", "preview", "debt-sync-history")
        });
        var fileDebtStore = new FileDebtSnapshotStore(options, environment);
        var fileInstallmentStore = new FileInstallmentMasterSnapshotStore(Options.Create(new InstallmentMasterOptions
        {
            Enabled = true,
            HistoryPath = Path.Combine(workspace, "data", "preview", "installment-master-sync-history")
        }), environment);
        var debtStore = new InMemoryDebtSnapshotStore();
        var installmentStore = new InMemoryInstallmentMasterSnapshotStore();
        var initialDebt = await fileDebtStore.GetCurrentAsync();
        var initialInstallment = await fileInstallmentStore.GetCurrentAsync();
        Assert.NotNull(initialDebt);
        Assert.NotNull(initialInstallment);
        await debtStore.PublishAsync(initialDebt);
        await installmentStore.PublishAsync(initialInstallment);
        var analyzer = new DebtSegmentationAnalyzer();
        var monthly = new MonthlyAmountDueService(options, debtStore, installmentStore, analyzer);
        var trend = new MonthlyPerformanceTrendService(debtStore, installmentStore, analyzer);
        var queue = new WorkQueueService(monthly, debtStore);
        var dbOptions = new DbContextOptionsBuilder<CoopDbContext>()
            .UseInMemoryDatabase($"dashboard-current-{Guid.NewGuid():N}")
            .Options;
        await using var db = new CoopDbContext(dbOptions);
        var service = new DashboardService(
            db, debtStore, installmentStore, analyzer, trend, monthly, queue,
            new ReadyDebtStatus(), new ReadyInstallmentStatus(), options);

        var debtBefore = await debtStore.GetCurrentAsync();
        var installmentBefore = await installmentStore.GetCurrentAsync();
        var summary = await service.GetSummaryAsync();
        var explicitCurrent = await service.GetSummaryAsync(DashboardDataModes.Current);

        Assert.True(summary.Available, summary.Message);
        Assert.Equal(DashboardDataModes.Current, summary.Mode);
        Assert.Equal(new DateOnly(2026, 8, 1), summary.DataThroughPeriod);
        Assert.Equal(4_600, summary.TotalContracts);
        Assert.Equal(3_856, summary.OutstandingContracts);
        Assert.Equal(744, summary.PaidOffContracts);
        Assert.Equal(summary.TotalContracts, summary.OutstandingContracts + summary.PaidOffContracts);
        Assert.Equal(216_888_268.08m, summary.PrincipalOutstanding);
        Assert.Equal(85_343_954.92m, summary.ProfitOutstanding);
        Assert.Equal(302_232_223m, summary.TotalOutstanding);
        Assert.Equal(summary.TotalOutstanding, summary.PrincipalOutstanding + summary.ProfitOutstanding);
        Assert.Equal(summary.TotalOutstanding, summary.DebtBuckets.Sum(bucket => bucket.OutstandingAmount));
        Assert.Equal(summary.TotalContracts, summary.DebtBuckets.Sum(bucket => bucket.ContractCount));
        Assert.Equal(53_425_322m, summary.AmountDue);
        Assert.Equal(7_253_308m, summary.ActualPayment);
        Assert.Equal(30, summary.DownPaymentContracts);
        Assert.Equal(630_020m, summary.DownPaymentAmount);
        Assert.Equal(301_605_413m, summary.OpeningOutstanding);
        Assert.Equal(7_880_118m, summary.IncreaseDuringPeriod);
        Assert.Equal(summary.EndingOutstanding,
            summary.OpeningOutstanding + summary.IncreaseDuringPeriod - summary.ActualPayment);
        Assert.NotNull(summary.WorkQueue);
        Assert.Equal(4_600, summary.WorkQueue!.AnalysisUniverseContracts);
        Assert.Equal(2_957, summary.WorkQueue.TotalQueueContracts);
        Assert.Equal(2_957, summary.WorkQueue.CollectionContracts);
        Assert.Equal(0, summary.WorkQueue.ReviewOnlyContracts);
        Assert.Equal(1_490, summary.WorkQueue.UrgentContracts);
        Assert.Equal(1_466, summary.WorkQueue.HighContracts);
        Assert.Equal(1, summary.WorkQueue.MediumContracts);
        Assert.Equal(0, summary.WorkQueue.ReviewContracts);
        Assert.Equal(summary.TotalOutstanding, explicitCurrent.TotalOutstanding);
        Assert.Equal(debtBefore?.SnapshotId, (await debtStore.GetCurrentAsync())?.SnapshotId);
        Assert.Equal(installmentBefore?.SnapshotId, (await installmentStore.GetCurrentAsync())?.SnapshotId);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CurrentWithoutOperationalServices_FailsClosed_InsteadOfReturningPublishedData()
    {
        var dbOptions = new DbContextOptionsBuilder<CoopDbContext>()
            .UseInMemoryDatabase($"dashboard-no-fallback-{Guid.NewGuid():N}")
            .Options;
        await using var db = new CoopDbContext(dbOptions);

        var summary = await new DashboardService(db).GetSummaryAsync();

        Assert.False(summary.Available);
        Assert.Equal(DashboardDataModes.Current, summary.Mode);
        Assert.False(summary.HasPublishedSnapshot);
        Assert.Null(summary.TotalOutstanding);
        Assert.Contains("unavailable", summary.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindWorkspace()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AI_RULES.md")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    private sealed class ReadyDebtStatus : IDebtAutoSyncStatus
    {
        public DebtAutoSyncStatusDto GetStatus() => new(
            true, DebtAutoSyncStates.Ready, 60, DateTime.UtcNow, DateTime.UtcNow,
            DateTime.UtcNow, "Ready", DebtSourceTypes.Local, "source-a.xlsx");
    }

    private sealed class ReadyInstallmentStatus : IInstallmentMasterAutoSyncStatus
    {
        public InstallmentMasterAutoSyncStatusDto GetStatus() => new(
            true, DebtAutoSyncStates.Ready, 60, DateTime.UtcNow, DateTime.UtcNow,
            DateTime.UtcNow, "Ready", DebtSourceTypes.Local, "source-b.xlsx", "hash-b",
            "snapshot-b", 4_600, 4_600, 0);
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "COOPAI.API.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
