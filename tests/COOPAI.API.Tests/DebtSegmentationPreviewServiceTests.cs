using COOPAI.API.DTOs.DebtSegmentation;
using COOPAI.API.Services.DebtSegmentation;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OfficeOpenXml;

namespace COOPAI.API.Tests;

public sealed class DebtSegmentationPreviewServiceTests
{
    [Fact]
    public async Task JulyPreview_DashboardFiltersDrillDownAndPaginationUseCurrentWorkbook()
    {
        var workspace = FindWorkspaceRoot();
        var workbook = Environment.GetEnvironmentVariable("COOPAI_DEBT_JULY_WORKBOOK") ??
            (workspace is null ? string.Empty : Path.Combine(workspace, "data", "preview", "Loan-July-2569.xlsx"));
        if (!File.Exists(workbook))
            return;

        var source = new OperationalDebtWorkbookReader().Read(workbook);
        var july = new DateOnly(2026, 7, 1);
        var negativeFacts = source.Contracts.SelectMany(contract => contract.MonthlyStates.Values
            .Where(state => state.Period <= july)
            .SelectMany(state => new[]
            {
                new { Contract = contract, State = state, Field = "Principal", Value = state.PrincipalOutstanding, Provenance = state.Provenance?.PrincipalOutstanding },
                new { Contract = contract, State = state, Field = "Profit", Value = state.ProfitOutstanding, Provenance = state.Provenance?.ProfitOutstanding }
            })
            .Where(fact => fact.Value < 0m))
            .ToArray();
        var affectedKeys = negativeFacts.Select(x => x.Contract.ContractKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(91, affectedKeys.Count);
        Assert.Equal(390, negativeFacts.Length);
        Assert.All(negativeFacts, fact => Assert.True(fact.Provenance?.IsFormulaDerived));
        Assert.All(negativeFacts, fact => Assert.True(
            fact.State.Provenance is not null &&
            fact.State.Provenance.PrincipalPaymentInput >= 0m &&
            fact.State.Provenance.ProfitPaymentInput >= 0m &&
            fact.State.Provenance.TotalPaymentInput is null or >= 0m));
        Assert.All(negativeFacts, fact => Assert.InRange(
            Math.Abs(fact.State.PrincipalOutstanding + fact.State.ProfitOutstanding - fact.State.TotalOutstanding),
            0m, 0.01m));
        Assert.All(negativeFacts, fact => Assert.True(fact.State.TotalOutstanding >= 0m));
        var first = source.Contracts.First();
        var service = new DebtSegmentationPreviewService(
            Options.Create(new DebtSegmentationPreviewOptions
            {
                Enabled = true,
                WorkbookPath = workbook,
                DefaultCurrentPeriod = "2026-07"
            }),
            new TestHostEnvironment(workspace ?? Path.GetDirectoryName(workbook)!),
            new OperationalDebtWorkbookReader(),
            new DebtSegmentationAnalyzer());

        var dashboard = await service.GetDashboardAsync("2026-07");
        var missedOne = await service.GetContractsAsync(
            "2026-07", "1 month delinquent", null, null, null, null, null, null, null, null, 1, 25);
        var paid = await service.GetContractsAsync(
            "2026-07", null, "Paid", null, null, null, null, null, null, null, 1, 25);
        var drillDown = await service.GetContractsAsync(
            "2026-07", null, null, null, null, null, null,
            "Normal", "1 month delinquent", "New delinquency", 1, 25);
        var searched = await service.GetContractsAsync(
            "2026-07", null, null, first.LoanType, null, first.ContractNumber, null, null, null, null, 1, 10);
        var secondPage = await service.GetContractsAsync(
            "2026-07", null, null, null, null, null, null, null, null, null, 2, 10);

        Assert.True(dashboard.Available, $"{dashboard.Message} {string.Join(" | ", dashboard.Sync.ValidationErrors)}");
        Assert.True(dashboard.Sync.Success);
        Assert.False(string.IsNullOrWhiteSpace(dashboard.SourceFileHash));
        Assert.Equal(4_387, dashboard.Totals?.MemberCount);
        Assert.Equal(4_524, dashboard.Totals?.ContractCount);
        Assert.Equal(301_605_413m, dashboard.Totals?.OutstandingAmount);
        Assert.Equal(1_900, dashboard.Totals?.PaidLatestMonthContracts);
        Assert.Equal(1_997, dashboard.Totals?.NotPaidLatestMonthContracts);
        Assert.Equal(70, dashboard.Totals?.NotDueLatestMonthContracts);
        var warning = dashboard.DataQualityWarnings.Single(x =>
            x.Code == DebtDataQualityWarningCodes.FormulaDerivedNegativeComponentReconciled);
        Assert.Equal(DebtDataQualityWarningCodes.FormulaDerivedNegativeComponentReconciled, warning.Code);
        Assert.Equal(91, warning.AffectedContractCount);
        Assert.Equal(390, warning.FactCount);
        Assert.Contains(dashboard.Warnings, x => x.Contains(warning.Code, StringComparison.Ordinal));
        var paymentWarning = dashboard.DataQualityWarnings.Single(x =>
            x.Code == DebtDataQualityWarningCodes.NegativePaymentComponentReconciled);
        Assert.Equal(1, paymentWarning.AffectedContractCount);
        Assert.Equal(1, paymentWarning.FactCount);
        Assert.Equal(4_524, dashboard.Buckets.Sum(x => x.ContractCount));
        Assert.Equal(3_874, dashboard.Buckets.Where(x => x.Bucket != "Paid off / no outstanding balance").Sum(x => x.ContractCount));
        Assert.Equal(650, dashboard.Buckets.Single(x => x.Bucket == "Paid off / no outstanding balance").ContractCount);
        Assert.Equal(1_740, dashboard.Buckets.Single(x => x.Bucket == "Normal").ContractCount);
        Assert.Equal(70, dashboard.Buckets.Single(x => x.Bucket == "Normal").NotDueContractCount);
        Assert.Equal(428, dashboard.Buckets.Single(x => x.Bucket == "1 month delinquent").ContractCount);
        Assert.Equal(135, dashboard.Buckets.Single(x => x.Bucket == "2 months delinquent").ContractCount);
        Assert.Equal(206, dashboard.Buckets.Single(x => x.Bucket == "3–6 months delinquent").ContractCount);
        Assert.Equal(176, dashboard.Buckets.Single(x => x.Bucket == "7–12 months delinquent").ContractCount);
        Assert.Equal(285, dashboard.Buckets.Single(x => x.Bucket == ">1 to 3 years").ContractCount);
        Assert.Equal(156, dashboard.Buckets.Single(x => x.Bucket == ">3 to 5 years").ContractCount);
        Assert.Equal(232, dashboard.Buckets.Single(x => x.Bucket == ">5 to 10 years").ContractCount);
        Assert.Equal(516, dashboard.Buckets.Single(x => x.Bucket == "10 years and above").ContractCount);
        AssertBucketComparison(dashboard, "Normal", 1_755, 1_758, 207_901_211m, 1_737, 1_740, 185_909_571m);
        AssertBucketComparison(dashboard, "1 month delinquent", 359, 359, 19_813_294m, 428, 428, 40_845_773m);
        AssertBucketComparison(dashboard, "2 months delinquent", 215, 215, 14_522_055m, 135, 135, 7_873_378m);
        AssertBucketComparison(dashboard, "3–6 months delinquent", 364, 364, 23_594_888m, 206, 206, 16_457_683m);
        AssertBucketComparison(dashboard, "7–12 months delinquent", 0, 0, 0m, 176, 176, 11_896_487m);
        AssertBucketComparison(dashboard, ">1 to 3 years", 292, 292, 7_741_732m, 285, 285, 7_770_970m);
        AssertBucketComparison(dashboard, ">3 to 5 years", 155, 155, 4_543_945m, 156, 156, 4_523_294m);
        AssertBucketComparison(dashboard, ">5 to 10 years", 235, 235, 10_555_923m, 232, 232, 10_412_668m);
        AssertBucketComparison(dashboard, "10 years and above", 506, 519, 15_943_173m, 503, 516, 15_915_589m);
        AssertBucketComparison(dashboard, "Paid off / no outstanding balance", 556, 557, 0m, 649, 650, 0m);

        AssertPaymentMovement(dashboard, "Paid -> Paid", 1_343, 1_346, 162_283_692m, 158_190_120m);
        AssertPaymentMovement(dashboard, "Paid -> Not paid", 485, 485, 41_966_627m, 41_966_627m);
        AssertPaymentMovement(dashboard, "Not paid -> Paid", 499, 499, 23_442_603m, 21_365_807m);
        AssertPaymentMovement(dashboard, "Not paid -> Not paid", 1_477, 1_491, 69_168_441m, 69_168_441m);
        AssertPaymentMovement(dashboard, "Not due -> Paid", 55, 55, 6_901_305m, 6_733_890m);
        AssertPaymentMovement(dashboard, "Not due -> Not paid", 21, 21, 853_553m, 853_553m);
        AssertPaymentMovement(dashboard, "New Contract", 70, 70, 3_830_775m, 3_326_975m);
        Assert.Equal(381, dashboard.BucketMovements.Where(x => x.Category == "Improved").Sum(x => x.ContractCount));
        Assert.Equal(449, dashboard.BucketMovements.Where(x => x.Category == "Worsened").Sum(x => x.ContractCount));
        Assert.Equal(428, dashboard.BucketMovements.Where(x => x.Category == "New delinquency").Sum(x => x.ContractCount));
        Assert.Equal(70, dashboard.BucketMovements.Where(x => x.Category == "New contract").Sum(x => x.ContractCount));
        Assert.Equal(1_422, dashboard.BucketMovements.Where(x => x.Category == "Same bucket but balance decreased").Sum(x => x.ContractCount));
        Assert.Equal(0, dashboard.BucketMovements.Where(x => x.Category == "Same bucket but balance increased").Sum(x => x.ContractCount));
        Assert.Equal(1_681, dashboard.BucketMovements.Where(x => x.Category == "Same bucket unchanged").Sum(x => x.ContractCount));
        Assert.Equal(93, dashboard.BucketMovements.Where(x => x.Category == "Closed / paid off").Sum(x => x.ContractCount));

        var cohort = new DebtSegmentationAnalyzer().Analyze(source.Contracts, new DateOnly(2026, 6, 1), july)
            .Contracts.Where(x => affectedKeys.Contains(x.Contract.ContractKey)).ToArray();
        var noPaymentCohort = cohort.Where(x => x.CurrentClassification.LatestPaymentStatus == MonthlyPaymentStatus.NotPaid).ToArray();
        Assert.Equal(3, noPaymentCohort.Length);
        Assert.Equal(0, noPaymentCohort.Count(x => x.CurrentClassification.Bucket == DebtBucket.PaidOff));
        Assert.Equal(1, noPaymentCohort.Count(x => x.CurrentClassification.Bucket == DebtBucket.OverOneToThreeYears));
        Assert.Equal(2, noPaymentCohort.Count(x => x.CurrentClassification.Bucket == DebtBucket.TenYearsAndAbove));
        Assert.DoesNotContain(noPaymentCohort, x => x.CurrentClassification.Bucket == DebtBucket.OneMonthDelinquent);

        var warningContract = await service.GetContractsAsync(
            "2026-07", null, null, null, null, negativeFacts[0].Contract.ContractNumber,
            null, null, null, null, 1, 10);
        var warningDetail = Assert.Single(warningContract.Items);
        Assert.NotEmpty(warningDetail.DataQualityWarnings);
        Assert.All(warningDetail.DataQualityWarnings, item =>
        {
            Assert.Equal(DebtDataQualityWarningCodes.FormulaDerivedNegativeComponentReconciled, item.Code);
            Assert.False(string.IsNullOrWhiteSpace(item.CellAddress));
            Assert.False(string.IsNullOrWhiteSpace(item.Formula));
        });
        Assert.Equal(428, missedOne.TotalCount);
        Assert.Equal(25, missedOne.Items.Count);
        Assert.True(missedOne.Items.Zip(missedOne.Items.Skip(1),
            (left, right) => left.TotalBalance >= right.TotalBalance).All(x => x));
        Assert.Equal(1_900, paid.TotalCount);
        Assert.Equal(428, drillDown.TotalCount);
        Assert.Contains(searched.Items, x => x.ContractNumber == first.ContractNumber);
        Assert.Equal(10, secondPage.Items.Count);
    }

    [Fact]
    public async Task SyncNow_IsIdempotent_AndRowsCanEnterAndLeaveWithoutDuplicateContracts()
    {
        var path = CreateWorkbook();
        try
        {
            var service = CreateService(path);
            var first = await service.SyncNowAsync("2026-07");
            var unchanged = await service.SyncNowAsync("2026-07");

            AddContract(path, 8, "M002", "สม-2569-000002", julyTotal: 200m);
            var changed = await service.SyncNowAsync("2026-07");
            var repeated = await service.SyncNowAsync("2026-07");
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                package.Workbook.Worksheets[OperationalDebtWorkbookReader.CurrentWorksheetName].DeleteRow(8);
                package.Save();
            }
            var removed = await service.SyncNowAsync("2026-07");
            var dashboard = await service.GetDashboardAsync("2026-07");

            Assert.True(first.Published);
            Assert.False(unchanged.Published);
            Assert.Equal(DebtSyncStatuses.Success, first.Status);
            Assert.Equal(DebtSyncStatuses.NoChange, unchanged.Status);
            Assert.Equal(new DateOnly(2026, 6, 1), unchanged.PreviousPeriod);
            Assert.Equal(new DateOnly(2026, 7, 1), unchanged.CurrentPeriod);
            Assert.Equal(1, unchanged.CandidateRows);
            Assert.Equal(1, unchanged.MemberCount);
            Assert.Equal(100m, unchanged.OutstandingAmount);
            Assert.False(string.IsNullOrWhiteSpace(unchanged.SnapshotId));
            Assert.Equal(1, first.AddedContracts);
            Assert.True(changed.Published);
            Assert.Equal(1, changed.AddedContracts);
            Assert.Equal(2, changed.ContractCount);
            Assert.False(repeated.Published);
            Assert.True(removed.Published);
            Assert.Equal(1, removed.RemovedContracts);
            Assert.Equal(1, dashboard.Totals?.ContractCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AutoSyncOnQuery_CanBeDisabledWhileManualSyncRemainsAvailable()
    {
        var path = CreateWorkbook();
        try
        {
            var options = Options.Create(new DebtSegmentationPreviewOptions
            {
                Enabled = true,
                AutoSyncOnQueryEnabled = false,
                WorkbookPath = path,
                DefaultCurrentPeriod = "2026-07"
            });
            var service = new DebtSegmentationPreviewService(
                options,
                new TestHostEnvironment(Path.GetDirectoryName(path)!),
                new OperationalDebtWorkbookReader(),
                new DebtSegmentationAnalyzer());

            var beforeManualSync = await service.GetDashboardAsync("2026-07");
            var manualSync = await service.SyncNowAsync("2026-07");
            var retained = await service.GetDashboardAsync("2026-07");

            Assert.False(beforeManualSync.Available);
            Assert.Contains("use Sync Now", beforeManualSync.Message, StringComparison.Ordinal);
            Assert.True(manualSync.Published);
            Assert.True(retained.Available);
            Assert.False(retained.AutoSyncOnQueryEnabled);
            Assert.Equal("debt-segmentation-v1", retained.PolicyVersion);
            Assert.False(retained.Sync.Published);
            Assert.True(retained.Sync.PreviousSnapshotPreserved);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DurablePreviewHistory_RestoresLastPublicationAndRetainsFailedAttemptAcrossRestart()
    {
        var path = CreateWorkbook();
        var historyPath = Path.Combine(Path.GetTempPath(), $"debt-history-{Guid.NewGuid():N}");
        try
        {
            var options = Options.Create(new DebtSegmentationPreviewOptions
            {
                Enabled = true,
                WorkbookPath = path,
                HistoryPath = historyPath,
                DefaultCurrentPeriod = "2026-07"
            });
            var environment = new TestHostEnvironment(Path.GetDirectoryName(path)!);
            var firstStore = new FileDebtSnapshotStore(options, environment);
            var firstService = new DebtSegmentationPreviewService(
                options, environment, new OperationalDebtWorkbookReader(), new DebtSegmentationAnalyzer(), firstStore);

            var published = await firstService.SyncNowAsync("2026-07");
            Assert.True(published.Published);
            var noChange = await firstService.SyncNowAsync("2026-07");
            Assert.Equal(DebtSyncStatuses.NoChange, noChange.Status);
            Assert.Single(Directory.EnumerateFiles(Path.Combine(historyPath, "snapshots"), "*.json"));

            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                package.Workbook.Worksheets[OperationalDebtWorkbookReader.CurrentWorksheetName].Cells[7, 62].Value = -1m;
                package.Save();
            }

            var restartedStore = new FileDebtSnapshotStore(options, environment);
            var restartedService = new DebtSegmentationPreviewService(
                options, environment, new OperationalDebtWorkbookReader(), new DebtSegmentationAnalyzer(), restartedStore);
            var invalid = await restartedService.SyncNowAsync("2026-07");
            var retained = await restartedService.GetDashboardAsync("2026-07");
            var history = await restartedService.GetSyncHistoryAsync(10);

            Assert.False(invalid.Success);
            Assert.True(invalid.PreviousSnapshotPreserved);
            Assert.True(retained.Available);
            Assert.Equal(1, retained.Totals?.ContractCount);
            Assert.Contains(history, x => x.Published && x.Success);
            Assert.Contains(history, x => x.Status == DebtSyncStatuses.NoChange && x.Success && !x.Published);
            Assert.Contains(history, x => !x.Success && x.PreviousSnapshotPreserved);
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(historyPath))
                Directory.Delete(historyPath, true);
        }
    }

    [Fact]
    public async Task SyncNow_ConcurrentManualAttemptReturnsBusyWithoutReadingSecondCandidate()
    {
        var path = CreateWorkbook();
        try
        {
            var options = Options.Create(new DebtSegmentationPreviewOptions
            {
                Enabled = true,
                AutoSyncOnQueryEnabled = false,
                WorkbookPath = path,
                DefaultCurrentPeriod = "2026-07",
                StabilizationDelayMilliseconds = 500
            });
            var service = new DebtSegmentationPreviewService(
                options,
                new TestHostEnvironment(Path.GetDirectoryName(path)!),
                new OperationalDebtWorkbookReader(),
                new DebtSegmentationAnalyzer());

            var firstTask = service.SyncNowAsync("2026-07");
            var busy = await service.SyncNowAsync("2026-07");
            var first = await firstTask;
            var history = await service.GetSyncHistoryAsync(10);

            Assert.Equal(DebtSyncStatuses.Busy, busy.Status);
            Assert.False(busy.Success);
            Assert.False(busy.Published);
            Assert.True(first.Published);
            Assert.Contains(history, x => x.Status == DebtSyncStatuses.Busy);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncNow_MinimumAgeRejectsChangingCandidateAndPreservesCurrentSnapshot()
    {
        var path = CreateWorkbook();
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
            var options = Options.Create(new DebtSegmentationPreviewOptions
            {
                Enabled = true,
                WorkbookPath = path,
                DefaultCurrentPeriod = "2026-07",
                MinimumFileAgeSeconds = 10
            });
            var service = new DebtSegmentationPreviewService(
                options,
                new TestHostEnvironment(Path.GetDirectoryName(path)!),
                new OperationalDebtWorkbookReader(),
                new DebtSegmentationAnalyzer());
            var first = await service.SyncNowAsync("2026-07");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

            var unstable = await service.SyncNowAsync("2026-07");
            var retained = await service.GetDashboardAsync("2026-07");

            Assert.True(first.Published);
            Assert.Equal(DebtSyncStatuses.Failed, unstable.Status);
            Assert.True(unstable.PreviousSnapshotPreserved);
            Assert.Contains(unstable.ValidationErrors, x => x.Contains("minimum file age", StringComparison.OrdinalIgnoreCase));
            Assert.True(retained.Available);
            Assert.Equal(1, retained.Totals?.ContractCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncNow_FileChangingDuringStabilizationFailsClosed()
    {
        var path = CreateWorkbook();
        try
        {
            var options = Options.Create(new DebtSegmentationPreviewOptions
            {
                Enabled = true,
                AutoSyncOnQueryEnabled = false,
                WorkbookPath = path,
                DefaultCurrentPeriod = "2026-07",
                StabilizationDelayMilliseconds = 500
            });
            var service = new DebtSegmentationPreviewService(
                options,
                new TestHostEnvironment(Path.GetDirectoryName(path)!),
                new OperationalDebtWorkbookReader(),
                new DebtSegmentationAnalyzer());
            var first = await service.SyncNowAsync("2026-07");

            var changingTask = service.SyncNowAsync("2026-07");
            await Task.Delay(100);
            AddContract(path, 8, "M002", "สม-2569-000002", 200m);
            var changing = await changingTask;
            var retained = await service.GetDashboardAsync("2026-07");

            Assert.True(first.Published);
            Assert.Equal(DebtSyncStatuses.Failed, changing.Status);
            Assert.True(changing.PreviousSnapshotPreserved);
            Assert.Contains(changing.ValidationErrors, x => x.Contains("size or modified time changed", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, retained.Totals?.ContractCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReconciliationRejectsAnyPopulationThatLosesContracts()
    {
        var path = CreateWorkbook();
        try
        {
            var workbook = new OperationalDebtWorkbookReader().Read(path);
            var analysis = new DebtSegmentationAnalyzer().Analyze(
                workbook.Contracts, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1));

            var errors = DebtSegmentationPreviewService.ValidateReconciliation(
                workbook, analysis with { PaymentMovements = [] }, new DateOnly(2026, 7, 1));

            Assert.Contains(errors, x => x.Contains("Payment movement count", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncNow_InvalidWorkbookPreservesPreviousPublishedDebtSnapshot()
    {
        var path = CreateWorkbook();
        try
        {
            var service = CreateService(path);
            var first = await service.SyncNowAsync("2026-07");
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                package.Workbook.Worksheets[OperationalDebtWorkbookReader.CurrentWorksheetName].Cells[7, 62].Value = -1m;
                package.Save();
            }

            var invalid = await service.SyncNowAsync("2026-07");
            var dashboard = await service.GetDashboardAsync("2026-07");

            Assert.True(first.Published);
            Assert.False(invalid.Success);
            Assert.True(invalid.PreviousSnapshotPreserved);
            Assert.Contains(invalid.ValidationErrors, x => x.Contains("negative", StringComparison.OrdinalIgnoreCase));
            Assert.True(dashboard.Available);
            Assert.Equal(1, dashboard.Totals?.ContractCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncNow_FormulaDerivedReconciledNegativeComponent_IsWarningAndPreservesRawValues()
    {
        var path = CreateWorkbook();
        try
        {
            SetJulyNegativeComponent(path, formulaDerived: true, principal: -10m, profit: 110m, total: 100m);
            var service = CreateService(path);

            var sync = await service.SyncNowAsync("2026-07");
            var dashboard = await service.GetDashboardAsync("2026-07");
            var contracts = await service.GetContractsAsync(
                "2026-07", null, null, null, null, null, null, null, null, null, 1, 10);

            Assert.True(sync.Success);
            Assert.True(sync.Published);
            Assert.Empty(sync.ValidationErrors);
            Assert.Contains(sync.Warnings, x => x.Contains(
                DebtDataQualityWarningCodes.FormulaDerivedNegativeComponentReconciled,
                StringComparison.Ordinal));
            Assert.Equal(-10m, dashboard.Totals?.PrincipalOutstanding);
            Assert.Equal(110m, dashboard.Totals?.ProfitOutstanding);
            Assert.Equal(100m, dashboard.Totals?.OutstandingAmount);
            var contract = Assert.Single(contracts.Items);
            Assert.Equal(-10m, contract.PrincipalBalance);
            Assert.Equal(110m, contract.ProfitBalance);
            Assert.Equal(100m, contract.TotalBalance);
            var warning = Assert.Single(contract.DataQualityWarnings);
            Assert.Equal("Principal", warning.Field);
            Assert.Equal(-10m, warning.ComponentValue);
            Assert.Equal("SUM(0-10)", warning.Formula);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncNow_RawNegativeComponent_RemainsBlocking()
    {
        var path = CreateWorkbook();
        try
        {
            SetJulyNegativeComponent(path, formulaDerived: false, principal: -10m, profit: 110m, total: 100m);

            var sync = await CreateService(path).SyncNowAsync("2026-07");

            Assert.False(sync.Success);
            Assert.Contains(sync.ValidationErrors, x => x.Contains("blocking negative", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(sync.Warnings, x => x.Contains(
                DebtDataQualityWarningCodes.FormulaDerivedNegativeComponentReconciled,
                StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncNow_NegativePaymentInput_IsBlockingAndIsNotSilentlyNormalized()
    {
        var path = CreateWorkbook();
        try
        {
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var start = 14 + (6 * 7);
                package.Workbook.Worksheets[OperationalDebtWorkbookReader.CurrentWorksheetName]
                    .Cells[7, start + 1].Value = -1m;
                package.Save();
            }

            var sync = await CreateService(path).SyncNowAsync("2026-07");

            Assert.False(sync.Success);
            Assert.Contains(sync.ValidationErrors, x =>
                x.Contains("blocking negative", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncNow_NegativePaymentComponent_IsRetainedOnlyWhenTotalReconciles()
    {
        var path = CreateWorkbook();
        try
        {
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var sheet = package.Workbook.Worksheets[OperationalDebtWorkbookReader.CurrentWorksheetName];
                var start = 14 + (6 * 7);
                sheet.Cells[7, start + 1].Formula = "SUM(0-40)";
                sheet.Cells[7, start + 2].Value = 520m;
                sheet.Cells[7, start + 3].Value = 480m;
                package.Workbook.Calculate();
                package.Save();
            }

            var sync = await CreateService(path).SyncNowAsync("2026-07");

            Assert.True(sync.Success);
            Assert.Contains(sync.Warnings, x => x.Contains(
                DebtDataQualityWarningCodes.NegativePaymentComponentReconciled,
                StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-1, 0, 0, -10, 110, 100, false)]
    [InlineData(0, -1, 0, -10, 110, 100, false)]
    [InlineData(0, 0, -1, -10, 110, 100, false)]
    [InlineData(0, 0, 0, -10, 110, 99, true)]
    [InlineData(0, 0, 0, -110, 100, -10, false)]
    public async Task SyncNow_FormulaNegativeOutsideExactBoundary_RemainsBlocking(
        decimal principalPayment,
        decimal profitPayment,
        decimal totalPayment,
        decimal principal,
        decimal profit,
        decimal total,
        bool expectReconciliationError)
    {
        var path = CreateWorkbook();
        try
        {
            SetJulyNegativeComponent(
                path, formulaDerived: true, principal, profit, total,
                principalPayment, profitPayment, totalPayment);

            var sync = await CreateService(path).SyncNowAsync("2026-07");

            Assert.False(sync.Success);
            Assert.Contains(sync.ValidationErrors, x => x.Contains("blocking negative", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(expectReconciliationError, sync.ValidationErrors.Any(x =>
                x.Contains("do not reconcile", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncAuto_MetadataChangedButContentSame_ReturnsNoChangeAndRecordsAutoHistory()
    {
        var path = CreateWorkbook();
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
            var service = CreateService(path);
            var manual = await service.SyncNowAsync("2026-07");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));

            var automatic = await service.SyncAutoAsync();
            var history = await service.GetSyncHistoryAsync(10);

            Assert.True(manual.Published);
            Assert.Equal(DebtSyncStatuses.NoChange, automatic.Status);
            Assert.False(automatic.Published);
            Assert.Equal(manual.SnapshotId, automatic.SnapshotId);
            Assert.Contains(history, item => item.Trigger == "Auto" && item.Status == DebtSyncStatuses.NoChange);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncAuto_UsesDetectedWorkbookPeriodAndIdenticalBusinessRulesToManual()
    {
        var path = CreateWorkbook();
        try
        {
            var automaticService = CreateService(path);
            var july = await automaticService.SyncNowAsync("2026-07");
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var sheet = package.Workbook.Worksheets[OperationalDebtWorkbookReader.CurrentWorksheetName];
                sheet.Cells[5, 109].Value = "31 สิงหาคม";
                SetMonth(sheet, 7, 8, 10m, 70m, 10m, 80m);
                package.Save();
            }

            var automatic = await automaticService.SyncAutoAsync();
            var automaticDashboard = await automaticService.GetDashboardAsync("2026-08");
            var manualService = CreateService(path);
            var manual = await manualService.SyncNowAsync("2026-08");
            var manualDashboard = await manualService.GetDashboardAsync("2026-08");

            Assert.True(july.Published);
            Assert.True(automatic.Published);
            Assert.Equal(new DateOnly(2026, 8, 1), automatic.CurrentPeriod);
            Assert.Equal("Auto", automatic.Trigger);
            Assert.True(manual.Published);
            Assert.Equal(manual.ContractCount, automatic.ContractCount);
            Assert.Equal(manual.MemberCount, automatic.MemberCount);
            Assert.Equal(manual.OutstandingAmount, automatic.OutstandingAmount);
            Assert.Equal(manualDashboard.Buckets, automaticDashboard.Buckets);
            Assert.Equal(manualDashboard.PaymentMovements, automaticDashboard.PaymentMovements);
            Assert.Equal(manualDashboard.BucketMovements, automaticDashboard.BucketMovements);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncAutoRunning_ManualSyncReturnsBusyFromSharedLock()
    {
        var path = CreateWorkbook();
        try
        {
            var options = Options.Create(new DebtSegmentationPreviewOptions
            {
                Enabled = true,
                WorkbookPath = path,
                DefaultCurrentPeriod = "2026-07",
                StabilizationDelayMilliseconds = 500
            });
            var service = new DebtSegmentationPreviewService(
                options, new TestHostEnvironment(Path.GetDirectoryName(path)!),
                new OperationalDebtWorkbookReader(), new DebtSegmentationAnalyzer());

            var autoTask = service.SyncAutoAsync();
            var manual = await service.SyncNowAsync("2026-07");
            var automatic = await autoTask;

            Assert.Equal(DebtSyncStatuses.Busy, manual.Status);
            Assert.True(automatic.Published);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ManualSyncRunning_AutoSyncReturnsBusyFromSharedLock()
    {
        var path = CreateWorkbook();
        try
        {
            var options = Options.Create(new DebtSegmentationPreviewOptions
            {
                Enabled = true,
                WorkbookPath = path,
                DefaultCurrentPeriod = "2026-07",
                StabilizationDelayMilliseconds = 500
            });
            var service = new DebtSegmentationPreviewService(
                options, new TestHostEnvironment(Path.GetDirectoryName(path)!),
                new OperationalDebtWorkbookReader(), new DebtSegmentationAnalyzer());

            var manualTask = service.SyncNowAsync("2026-07");
            var automatic = await service.SyncAutoAsync();
            var manual = await manualTask;

            Assert.Equal(DebtSyncStatuses.Busy, automatic.Status);
            Assert.True(manual.Published);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SyncAuto_SourceChangingDuringStabilizationFailsAndRetainsCurrentSnapshot()
    {
        var path = CreateWorkbook();
        try
        {
            var options = Options.Create(new DebtSegmentationPreviewOptions
            {
                Enabled = true,
                AutoSyncOnQueryEnabled = false,
                WorkbookPath = path,
                DefaultCurrentPeriod = "2026-07",
                StabilizationDelayMilliseconds = 500
            });
            var service = new DebtSegmentationPreviewService(
                options, new TestHostEnvironment(Path.GetDirectoryName(path)!),
                new OperationalDebtWorkbookReader(), new DebtSegmentationAnalyzer());
            var first = await service.SyncNowAsync("2026-07");

            var autoTask = service.SyncAutoAsync();
            await Task.Delay(100);
            AddContract(path, 8, "M002", "สม-2569-000002", 200m);
            var automatic = await autoTask;
            var dashboard = await service.GetDashboardAsync("2026-07");

            Assert.True(first.Published);
            Assert.Equal(DebtSyncStatuses.Failed, automatic.Status);
            Assert.True(automatic.PreviousSnapshotPreserved);
            Assert.Equal(1, dashboard.Totals?.ContractCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static DebtSegmentationPreviewService CreateService(string workbook) => new(
        Options.Create(new DebtSegmentationPreviewOptions
        {
            Enabled = true,
            WorkbookPath = workbook,
            DefaultCurrentPeriod = "2026-07"
        }),
        new TestHostEnvironment(Path.GetDirectoryName(workbook)!),
        new OperationalDebtWorkbookReader(),
        new DebtSegmentationAnalyzer());

    private static void AssertBucketComparison(
        DebtPreviewDashboardDto dashboard,
        string bucket,
        int previousMembers,
        int previousContracts,
        decimal previousBalance,
        int currentMembers,
        int currentContracts,
        decimal currentBalance)
    {
        var row = dashboard.BucketComparisons.Single(x => x.Bucket == bucket);
        Assert.Equal(previousMembers, row.PreviousMemberCount);
        Assert.Equal(previousContracts, row.PreviousContractCount);
        Assert.Equal(previousBalance, row.PreviousOutstanding);
        Assert.Equal(currentMembers, row.CurrentMemberCount);
        Assert.Equal(currentContracts, row.CurrentContractCount);
        Assert.Equal(currentBalance, row.CurrentOutstanding);
        Assert.Equal(currentMembers - previousMembers, row.MemberDifference);
        Assert.Equal(currentContracts - previousContracts, row.ContractDifference);
        Assert.Equal(currentBalance - previousBalance, row.AmountDifference);
    }

    private static void AssertPaymentMovement(
        DebtPreviewDashboardDto dashboard,
        string movement,
        int members,
        int contracts,
        decimal previousBalance,
        decimal currentBalance)
    {
        var row = dashboard.PaymentMovements.Single(x => x.PaymentMovement == movement);
        Assert.Equal(members, row.MemberCount);
        Assert.Equal(contracts, row.ContractCount);
        Assert.Equal(previousBalance, row.PreviousOutstanding);
        Assert.Equal(currentBalance, row.CurrentOutstanding);
        Assert.Equal(currentBalance - previousBalance, row.AmountChange);
    }

    private static string CreateWorkbook()
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI tests");
        var path = Path.Combine(Path.GetTempPath(), $"debt-sync-{Guid.NewGuid():N}.xlsx");
        using var package = new ExcelPackage(new FileInfo(path));
        var sheet = package.Workbook.Worksheets.Add(OperationalDebtWorkbookReader.CurrentWorksheetName);
        sheet.Cells[2, 1].Value = "รายละเอียดรับชำระหนี้ ประจำปี 2569";
        sheet.Cells[3, 107].Value = "กลุ่ม";
        sheet.Cells[5, 109].Value = "31 กรกฎาคม";
        AddContract(sheet, 7, "M001", "สม-2569-000001", 100m);
        package.Save();
        return path;
    }

    private static void AddContract(string path, int row, string memberCode, string contractNumber, decimal julyTotal)
    {
        using var package = new ExcelPackage(new FileInfo(path));
        AddContract(package.Workbook.Worksheets[OperationalDebtWorkbookReader.CurrentWorksheetName], row, memberCode, contractNumber, julyTotal);
        package.Save();
    }

    private static void AddContract(
        ExcelWorksheet sheet,
        int row,
        string memberCode,
        string contractNumber,
        decimal julyTotal)
    {
        sheet.Cells[row, 2].Value = memberCode;
        sheet.Cells[row, 3].Value = $"สมาชิก {memberCode}";
        sheet.Cells[row, 4].Value = contractNumber;
        sheet.Cells[row, 5].Value = new DateTime(2026, 1, 1);
        sheet.Cells[row, 6].Value = new DateTime(2027, 1, 1);
        sheet.Cells[row, 7].Value = julyTotal;
        SetMonth(sheet, row, 6, 10m, julyTotal - 10m, 10m, julyTotal);
        SetMonth(sheet, row, 7, 10m, julyTotal - 10m, 10m, julyTotal);
    }

    private static void SetMonth(
        ExcelWorksheet sheet,
        int row,
        int month,
        decimal payment,
        decimal principal,
        decimal profit,
        decimal total)
    {
        var start = 14 + ((month - 1) * 7);
        sheet.Cells[row, start + 3].Value = payment;
        sheet.Cells[row, start + 4].Value = principal;
        sheet.Cells[row, start + 5].Value = profit;
        sheet.Cells[row, start + 6].Value = total;
    }

    private static void SetJulyNegativeComponent(
        string path,
        bool formulaDerived,
        decimal principal,
        decimal profit,
        decimal total,
        decimal principalPayment = 0m,
        decimal profitPayment = 0m,
        decimal totalPayment = 0m)
    {
        using var package = new ExcelPackage(new FileInfo(path));
        var sheet = package.Workbook.Worksheets[OperationalDebtWorkbookReader.CurrentWorksheetName];
        var start = 14 + (6 * 7);
        sheet.Cells[7, start + 1].Value = principalPayment;
        sheet.Cells[7, start + 2].Value = profitPayment;
        sheet.Cells[7, start + 3].Value = totalPayment;
        sheet.Cells[7, start + 4].Value = principal;
        if (formulaDerived)
        {
            sheet.Cells[7, start + 4].Formula = principal == -10m
                ? "SUM(0-10)"
                : "SUM(0-110)";
            package.Workbook.Calculate();
        }
        sheet.Cells[7, start + 5].Value = profit;
        sheet.Cells[7, start + 6].Value = total;
        package.Save();
    }

    private static string? FindWorkspaceRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "src", "COOPAI.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
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
