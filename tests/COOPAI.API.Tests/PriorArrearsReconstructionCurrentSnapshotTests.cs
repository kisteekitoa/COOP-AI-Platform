using System.Text.Json;
using COOPAI.API.Services.DebtSegmentation;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace COOPAI.API.Tests;

public sealed class PriorArrearsReconstructionCurrentSnapshotTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CurrentSnapshots_ReconstructPriorPositionAndValidateKnownCohort()
    {
        var workspace = FindWorkspace();
        if (workspace is null)
            return;

        using var configuration = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(workspace, "src", "COOPAI.API", "appsettings.Development.json")));
        var sourceBOptions = configuration.RootElement.GetProperty("InstallmentMasterPreview");
        var sourceBPath = sourceBOptions.GetProperty("WorkbookPath").GetString()!;
        var worksheetName = sourceBOptions.GetProperty("WorksheetName").GetString()!;
        if (!File.Exists(sourceBPath))
            return;

        var sourceBWorkbook = new InstallmentMasterWorkbookReader().Read(sourceBPath, worksheetName);
        var sourceBFile = new FileInfo(sourceBPath);
        var installmentStore = new InMemoryInstallmentMasterSnapshotStore();
        await installmentStore.PublishAsync(new PublishedInstallmentMasterSnapshot(
            "prior-arrears-reconstruction-v1-current-data",
            MonthlyAmountDueVersion.InstallmentMasterV3,
            sourceBFile.Name,
            sourceBFile.FullName,
            "READ_ONLY_CURRENT_DATA",
            sourceBFile.Length,
            sourceBFile.LastWriteTimeUtc,
            DateTime.UtcNow,
            sourceBWorkbook));

        var environment = new TestHostEnvironment(Path.Combine(workspace, "src", "COOPAI.API"));
        var debtStore = new FileDebtSnapshotStore(Options.Create(new DebtSegmentationPreviewOptions
        {
            Enabled = true,
            DefaultCurrentPeriod = "2026-08",
            HistoryPath = Path.Combine(workspace, "data", "preview", "debt-sync-history")
        }), environment);
        var monthly = new MonthlyAmountDueService(
            Options.Create(new DebtSegmentationPreviewOptions { DefaultCurrentPeriod = "2026-08" }),
            debtStore,
            installmentStore,
            new DebtSegmentationAnalyzer());
        var analysis = await monthly.GetAnalysisAsync("2026-08");
        Assert.NotNull(analysis);
        var dashboard = await monthly.GetDashboardAsync("2026-08");
        var queueService = new WorkQueueService(monthly, debtStore);
        var firstPage = await queueService.GetAsync(
            "2026-08", null, null, null, null, null, null, null, 1, 200);
        Assert.NotNull(firstPage.Summary);

        var debt = await debtStore.GetCurrentAsync();
        Assert.NotNull(debt);
        var sourceB = sourceBWorkbook.Contracts.ToDictionary(
            item => item.ContractKey, StringComparer.OrdinalIgnoreCase);
        var allocation = analysis.Contracts.ToDictionary(
            item => item.ContractNumber, StringComparer.OrdinalIgnoreCase);
        var currentPeriod = new DateOnly(2026, 8, 1);
        var knownValidation = debt.Workbook.Contracts
            .Select(contract =>
            {
                var firstDue = contract.ContractDate.HasValue
                    ? new DateOnly(contract.ContractDate.Value.Year, contract.ContractDate.Value.Month, 1).AddMonths(1)
                    : (DateOnly?)null;
                sourceB.TryGetValue(contract.ContractKey, out var schedule);
                allocation.TryGetValue(contract.ContractKey, out var actual);
                return new { Contract = contract, FirstDue = firstDue, Schedule = schedule, Actual = actual };
            })
            .Where(item => item.FirstDue.HasValue &&
                item.FirstDue.Value >= new DateOnly(2026, 2, 1) &&
                item.FirstDue.Value <= new DateOnly(2026, 8, 1) &&
                item.Schedule?.IsUsable == true && item.Schedule.ContractualObligation is > 0m &&
                item.Actual is not null)
            .Select(item =>
            {
                var actual = item.Actual!;
                var schedule = item.Schedule!;
                var terminal = actual.EndingOutstanding == 0m;
                var actualState = terminal
                    ? "PaidOff"
                    : actual.EndingArrears is > ContractSchedulePositionCalculator.Tolerance
                        ? ContractSchedulePositions.Behind
                        : actual.EndingAdvanceCredit > ContractSchedulePositionCalculator.Tolerance
                            ? ContractSchedulePositions.Ahead
                            : ContractSchedulePositions.Current;
                var reconstructed = terminal
                    ? null
                    : ContractSchedulePositionCalculator.Calculate(
                        item.FirstDue!.Value,
                        currentPeriod,
                        schedule.ContractualObligation!.Value,
                        schedule.TotalInstallments!.Value,
                        schedule.MonthlyInstallment!.Value,
                        actual.EndingOutstanding);
                var reconstructedState = terminal ? "PaidOff" : reconstructed!.Position;
                var actualOverdue = terminal ? 0m : actual.EndingArrears ?? 0m;
                var reconstructedOverdue = terminal ? 0m : reconstructed!.OverdueContractualAmount;
                return new
                {
                    actual.ContractNumber,
                    ActualState = actualState,
                    ReconstructedState = reconstructedState,
                    ActualOverdue = actualOverdue,
                    ReconstructedOverdue = reconstructedOverdue
                };
            })
            .ToArray();

        var exactStates = knownValidation.Count(item => item.ActualState == item.ReconstructedState);
        var actualBehind = knownValidation.Where(item => item.ActualState == ContractSchedulePositions.Behind).ToArray();
        var exactOverdue = actualBehind.Count(item =>
            Math.Abs(item.ActualOverdue - item.ReconstructedOverdue) <= ContractSchedulePositionCalculator.Tolerance);
        var falseOverdue = knownValidation.Count(item =>
            item.ReconstructedState == ContractSchedulePositions.Behind && item.ActualState != ContractSchedulePositions.Behind);
        var falseNormal = knownValidation.Count(item =>
            item.ReconstructedState == ContractSchedulePositions.Current && item.ActualState != ContractSchedulePositions.Current);
        var falseAdvance = knownValidation.Count(item =>
            item.ReconstructedState == ContractSchedulePositions.Ahead && item.ActualState != ContractSchedulePositions.Ahead);
        var missedOverdue = knownValidation.Count(item =>
            item.ActualState == ContractSchedulePositions.Behind && item.ReconstructedState != ContractSchedulePositions.Behind);

        Assert.Equal(629, knownValidation.Length);
        Assert.Equal(629, exactStates);
        Assert.Equal(0, falseOverdue);
        Assert.Equal(0, falseNormal);
        Assert.Equal(0, falseAdvance);
        Assert.Equal(0, missedOverdue);
        Assert.Equal(253, actualBehind.Length);
        Assert.Equal(253, exactOverdue);

        var expectedDeep = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["สจ-2565-000906"] = 43_400m,
            ["สจ-2566-000734"] = 0m,
            ["สจ-2566-000744"] = 8_400m,
            ["สจ-2566-000752"] = 14_600m,
            ["สจ-2566-000759"] = 0m
        };
        foreach (var expected in expectedDeep)
        {
            var actual = allocation[expected.Key];
            Assert.Equal(PriorArrearsStatuses.Known, actual.PriorArrearsStatus);
            Assert.Equal(expected.Value, actual.EndingArrears);
        }

        var reviewRows = new List<COOPAI.API.DTOs.DebtSegmentation.WorkQueueItemDto>();
        var pages = (int)Math.Ceiling(firstPage.TotalCount / 200m);
        for (var page = 1; page <= pages; page++)
        {
            var result = await queueService.GetAsync(
                "2026-08", null, null, null, null, null, null, null, page, 200);
            reviewRows.AddRange(result.Items.Where(item => item.QueueType == WorkQueueTypes.DataReview));
        }

        var unknownPrior = analysis.Contracts.Count(item =>
            item.PriorArrearsStatus.StartsWith("Unknown", StringComparison.Ordinal) ||
            item.PriorArrearsStatus == PriorArrearsStatuses.PriorCreditPolicyRequired);
        var payment = dashboard.PaymentAllocation!;
        Assert.Equal(0m, payment.ReconciliationDifference);
        Assert.Equal(0m, payment.LedgerReconciliationDifference);

        output.WriteLine(JsonSerializer.Serialize(new
        {
            SourceB = new
            {
                sourceBWorkbook.SchemaMode,
                sourceBWorkbook.SourceRowCount,
                sourceBWorkbook.ContractCount,
                sourceBWorkbook.ValidContractCount,
                sourceBWorkbook.InvalidContractCount,
                WithAh = sourceBWorkbook.Contracts.Count(item => item.ContractualObligation is > 0m)
            },
            KnownValidation = new
            {
                Contracts = knownValidation.Length,
                ExactStates = exactStates,
                FalseOverdue = falseOverdue,
                FalseNormal = falseNormal,
                FalseAdvance = falseAdvance,
                MissedOverdue = missedOverdue,
                ActualBehind = actualBehind.Length,
                ExactOverdue = exactOverdue
            },
            WorkQueue = new
            {
                firstPage.Summary!.TotalQueueContracts,
                firstPage.Summary.CollectionContracts,
                firstPage.Summary.ReviewOnlyContracts,
                Priorities = firstPage.Summary.Priorities,
                UnknownPriorArrears = unknownPrior,
                RemainingDataReview = reviewRows.Select(item => new
                {
                    item.ContractNo,
                    item.ReasonCodes,
                    item.DataQualityWarnings
                })
            },
            Reconciliation = new
            {
                payment.ActualPayment,
                payment.AllocatedPaymentAmount,
                payment.UnclassifiedPaymentAmount,
                payment.ReconciliationDifference,
                payment.LedgerReconciliationDifference
            }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? FindWorkspace()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "COOPAI.API")))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "COOPAI.API.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
