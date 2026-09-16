using System.Text.Json;
using COOPAI.API.Services.DebtSegmentation;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace COOPAI.API.Tests;

public sealed class WorkQueueCurrentSnapshotAuditTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CurrentPreviewSnapshots_ProduceACompleteReadOnlyQueueAndSourceBDownPaymentAudit()
    {
        var workspace = FindWorkspace();
        if (workspace is null)
            return;

        var environment = new TestHostEnvironment(Path.Combine(workspace, "src", "COOPAI.API"));
        var debtStore = new FileDebtSnapshotStore(Options.Create(new DebtSegmentationPreviewOptions
        {
            Enabled = true,
            DefaultCurrentPeriod = "2026-08",
            HistoryPath = Path.Combine(workspace, "data", "preview", "debt-sync-history")
        }), environment);
        var installmentStore = new FileInstallmentMasterSnapshotStore(Options.Create(new InstallmentMasterOptions
        {
            Enabled = true,
            HistoryPath = Path.Combine(workspace, "data", "preview", "installment-master-sync-history")
        }), environment);
        var monthly = new MonthlyAmountDueService(
            Options.Create(new DebtSegmentationPreviewOptions { DefaultCurrentPeriod = "2026-08" }),
            debtStore,
            installmentStore,
            new DebtSegmentationAnalyzer());
        var service = new WorkQueueService(monthly, debtStore);

        var first = await service.GetAsync("2026-08", null, null, null, null, null, null, null, 1, 200);
        Assert.True(first.Available);
        Assert.NotNull(first.Summary);

        var pageCount = (int)Math.Ceiling(first.TotalCount / 200m);
        var all = new List<COOPAI.API.DTOs.DebtSegmentation.WorkQueueItemDto>(first.TotalCount);
        for (var page = 1; page <= pageCount; page++)
        {
            var result = await service.GetAsync(
                "2026-08", null, null, null, null, null, null, null, page, 200);
            all.AddRange(result.Items);
        }

        var debt = await debtStore.GetCurrentAsync();
        var sourceB = await installmentStore.GetCurrentAsync();
        Assert.NotNull(debt);
        Assert.NotNull(sourceB);
        var previousSourceB = await LoadPreviousInstallmentSnapshotAsync(workspace, sourceB.SnapshotId);
        Assert.NotNull(previousSourceB);
        var sourceBByContract = sourceB.Workbook.Contracts.ToDictionary(
            item => item.ContractKey,
            StringComparer.OrdinalIgnoreCase);
        var matched = debt.Workbook.Contracts
            .Where(item => sourceBByContract.ContainsKey(item.ContractKey))
            .ToArray();
        var unmatched = debt.Workbook.Contracts
            .Where(item => !sourceBByContract.ContainsKey(item.ContractKey))
            .ToArray();
        var downPaymentStatuses = sourceB.Workbook.Contracts
            .GroupBy(item => item.DownPaymentDataStatus, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var available = sourceB.Workbook.Contracts
            .Where(item => item.DownPaymentDataStatus == DownPaymentDataStatuses.Available)
            .ToArray();

        Assert.Equal(first.TotalCount, all.Count);
        Assert.Equal(first.TotalCount, all.Select(item => item.ContractNo)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(0, first.Summary!.PaidOffCollectionContracts);
        Assert.Equal(0, first.Summary.NotDueFalseNoPaymentContracts);
        Assert.Equal(0, first.Summary.FullyCreditCoveredFalseNoPaymentContracts);
        Assert.All(matched, item =>
            Assert.Equal(DownPaymentDataStatuses.Available,
                sourceBByContract[item.ContractKey].DownPaymentDataStatus));

        var previousInstallmentStore = new InMemoryInstallmentMasterSnapshotStore();
        await previousInstallmentStore.PublishAsync(previousSourceB);
        var previousMonthly = new MonthlyAmountDueService(
            Options.Create(new DebtSegmentationPreviewOptions { DefaultCurrentPeriod = "2026-08" }),
            debtStore,
            previousInstallmentStore,
            new DebtSegmentationAnalyzer());
        var previousAnalysis = await previousMonthly.GetAnalysisAsync("2026-08");
        Assert.NotNull(previousAnalysis);
        var previousQueueService = new WorkQueueService(previousMonthly, debtStore);
        var previousQueue = await previousQueueService.GetAsync(
            "2026-08", null, null, null, null, null, null, null, 1, 200);
        var currentDashboard = await monthly.GetDashboardAsync("2026-08");
        var previousDashboard = await previousMonthly.GetDashboardAsync("2026-08");
        var priorMissing = previousAnalysis.Contracts
            .Where(item => item.PaymentAllocationStatus == PaymentAllocationStatuses.UnresolvedDownPaymentEvidence)
            .OrderBy(item => item.ContractNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var priorMissingAudit = priorMissing.Select(item =>
        {
            sourceBByContract.TryGetValue(item.ContractNumber, out var currentSourceB);
            return new
            {
                item.ContractNumber,
                PriorPaymentLikeAmount = item.UnclassifiedPaymentAmount,
                Matched = currentSourceB is not null,
                AeAvailable = currentSourceB?.DownPaymentDataStatus == DownPaymentDataStatuses.Available,
                AeAmount = currentSourceB?.DownPayment,
                AeStatus = currentSourceB?.DownPaymentDataStatus ?? "Unmatched",
                Classification = currentSourceB is null
                    ? "Unmatched"
                    : currentSourceB.DownPaymentDataStatus != DownPaymentDataStatuses.Available
                        ? currentSourceB.DownPaymentDataStatus
                        : currentSourceB.DownPayment > 0m ? "Positive" : "Zero"
            };
        }).ToArray();

        Assert.Empty(priorMissing);
        Assert.Equal(0m, priorMissing.Sum(item => item.UnclassifiedPaymentAmount));

        output.WriteLine(JsonSerializer.Serialize(new
        {
            Queue = new
            {
                first.AnalysisId,
                first.DebtSnapshotId,
                first.InstallmentSnapshotId,
                Universe = first.Summary.AnalysisUniverseContracts,
                first.Summary.TotalQueueContracts,
                first.Summary.CollectionContracts,
                first.Summary.CollectionOutstanding,
                first.Summary.ReviewOnlyContracts,
                first.Summary.ReviewOnlyAmount,
                first.Summary.Priorities,
                first.Summary.NoPaymentContracts,
                first.Summary.CurrentShortfallContracts,
                first.Summary.PriorArrearsContracts,
                first.Summary.ExpiredOutstandingContracts,
                first.Summary.UnclassifiedReviewContracts,
                first.Summary.UnknownPriorArrearsContracts,
                first.Summary.MissingDownPaymentEvidenceContracts,
                PagesAt200 = pageCount,
                TraversedRows = all.Count
            },
            SourceBDownPaymentAudit = new
            {
                Mode = "Read-only preserved Source B snapshot audit",
                sourceB.SnapshotId,
                sourceB.AnalysisVersion,
                sourceB.SourceFileName,
                sourceB.SourceFileHash,
                sourceB.SourceFileSizeBytes,
                sourceB.SourceLastWriteTimeUtc,
                sourceB.Workbook.WorksheetName,
                sourceB.Workbook.SchemaMode,
                AuthoritativeColumn = "AE / LREC_SAL_ADVANCE",
                sourceB.Workbook.SourceRowCount,
                sourceB.Workbook.ContractCount,
                Statuses = downPaymentStatuses,
                AvailableZero = available.Count(item => item.DownPayment == 0m),
                AvailablePositive = available.Count(item => item.DownPayment > 0m),
                AvailableAmount = available.Sum(item => item.DownPayment ?? 0m),
                MatchedDebtContracts = matched.Length,
                MatchedAvailable = matched.Count(item =>
                    sourceBByContract[item.ContractKey].DownPaymentDataStatus == DownPaymentDataStatuses.Available),
                UnmatchedDebtContracts = unmatched.Length,
                UnmatchedSample = unmatched.Take(10).Select(item => item.ContractNumber),
                GuessingAllowed = false
            },
            PriorThirtyDownPaymentEvidenceAudit = new
            {
                PreviousSourceBSnapshotId = previousSourceB.SnapshotId,
                PriorContracts = priorMissing.Length,
                PriorPaymentLikeAmount = priorMissing.Sum(item => item.UnclassifiedPaymentAmount),
                Matched = priorMissingAudit.Count(item => item.Matched),
                PositiveAuthoritativeAe = priorMissingAudit.Count(item => item.Classification == "Positive"),
                AuthoritativeAeAmount = priorMissingAudit
                    .Where(item => item.AeAvailable)
                    .Sum(item => item.AeAmount ?? 0m),
                Zero = priorMissingAudit.Count(item => item.Classification == "Zero"),
                Unmatched = priorMissingAudit.Count(item => item.Classification == "Unmatched"),
                InvalidOrConflicting = priorMissingAudit.Count(item =>
                    item.AeStatus is DownPaymentDataStatuses.Invalid or DownPaymentDataStatuses.ConflictingDuplicate),
                Contracts = priorMissingAudit
            },
            OperationalComparison = new
            {
                Previous = OperationalNumbers(previousDashboard, previousQueue),
                Current = OperationalNumbers(currentDashboard, first)
            },
            Safety = new
            {
                ProductionConnections = 0,
                ProductionMutations = 0,
                PublishOperations = 0,
                WorkQueueMutations = 0,
                OfficerAssignmentOperations = 0,
                FollowUpOperations = 0
            }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object OperationalNumbers(
        COOPAI.API.DTOs.DebtSegmentation.MonthlyCollectionDashboardDto dashboard,
        COOPAI.API.DTOs.DebtSegmentation.WorkQueuePageDto queue)
    {
        var allocation = dashboard.PaymentAllocation!;
        var summary = queue.Summary!;
        return new
        {
            AnalysisUniverse = dashboard.Totals!.ContractCount,
            dashboard.Totals.AmountDue,
            dashboard.Totals.ActualPayment,
            allocation.DownPaymentAmount,
            allocation.PriorAdvanceCreditAmount,
            allocation.AdvanceCreditAppliedAmount,
            allocation.NewAdvanceCreditAmount,
            allocation.EndingAdvanceCreditAmount,
            allocation.PaymentToPriorArrears,
            allocation.PaymentToCurrentDue,
            allocation.EarlyPayoffAmount,
            allocation.ExpiredPayoffAmount,
            Unclassified = allocation.UnclassifiedPaymentAmount,
            MissingDownPaymentEvidence = summary.MissingDownPaymentEvidenceContracts,
            WorkQueueTotal = summary.TotalQueueContracts,
            Collection = summary.CollectionContracts,
            ReviewOnly = summary.ReviewOnlyContracts,
            Urgent = summary.Priorities.Single(item => item.Priority == WorkQueuePriorities.Urgent).ContractCount,
            High = summary.Priorities.Single(item => item.Priority == WorkQueuePriorities.High).ContractCount,
            Medium = summary.Priorities.Single(item => item.Priority == WorkQueuePriorities.Medium).ContractCount,
            Review = summary.Priorities.Single(item => item.Priority == WorkQueuePriorities.Review).ContractCount
        };
    }

    private static async Task<PublishedInstallmentMasterSnapshot?> LoadPreviousInstallmentSnapshotAsync(
        string workspace,
        string currentSnapshotId)
    {
        var path = Path.Combine(workspace, "data", "preview", "installment-master-sync-history", "snapshots");
        if (!Directory.Exists(path))
            return null;
        var snapshots = new List<PublishedInstallmentMasterSnapshot>();
        foreach (var file in Directory.EnumerateFiles(path, "installment-master-v2-*.json"))
        {
            await using var stream = File.OpenRead(file);
            var snapshot = await JsonSerializer.DeserializeAsync<PublishedInstallmentMasterSnapshot>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (snapshot is not null && snapshot.SnapshotId != currentSnapshotId)
                snapshots.Add(snapshot);
        }
        return snapshots.OrderByDescending(item => item.PublishedAtUtc).FirstOrDefault();
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

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "COOPAI.API.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
