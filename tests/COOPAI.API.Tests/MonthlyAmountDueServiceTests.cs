using COOPAI.API.Services.DebtSegmentation;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Tests;

public sealed class MonthlyAmountDueServiceTests
{
    private static readonly DateOnly Current = new(2026, 8, 1);

    [Fact]
    public void BeforeFirstDuePeriod_IsZeroAndEarlyPaymentIsAdvance()
    {
        var result = Calculate(new DateOnly(2026, 8, 15), null, 0m, 250m, 1_000m, null);

        Assert.Equal(0m, result.AmountDue);
        Assert.Equal(0m, result.Shortfall);
        Assert.Equal(250m, result.AdvancePayment);
        Assert.Equal(0m, result.ExcessPayment);
        Assert.Equal(AmountDueBases.NotDue, result.DueBasis);
        Assert.Equal("NotDue", result.ContractStatus);
        Assert.Equal(0, result.CurrentInstallmentNumber);
    }

    [Fact]
    public void PaidOffBeforeObligation_IsZero()
    {
        var result = Calculate(new DateOnly(2025, 1, 1), null, 0m, 0m, 0m, Installment(12, 100m));

        Assert.Equal(0m, result.AmountDue);
        Assert.Equal(AmountDueBases.PaidOff, result.DueBasis);
        Assert.Equal("PaidOff", result.ContractStatus);
    }

    [Fact]
    public void ActiveNormalInstallment_UsesMonthlyInstallment()
    {
        var result = Calculate(new DateOnly(2026, 6, 1), new DateOnly(2027, 12, 31),
            1_000m, 30m, 970m, Installment(12, 100m));

        Assert.Equal(100m, result.AmountDue);
        Assert.Equal(70m, result.Shortfall);
        Assert.Equal(AmountDueBases.ActiveInstallment, result.DueBasis);
        Assert.Equal("Active/InTerm", result.ContractStatus);
        Assert.Equal(2, result.CurrentInstallmentNumber);
        Assert.Equal(12, result.TotalInstallments);
    }

    [Fact]
    public void ActiveInstallment_IsCappedAtLowerOpeningBalance()
    {
        var result = Calculate(new DateOnly(2026, 6, 1), new DateOnly(2027, 12, 31),
            75m, 0m, 75m, Installment(12, 100m));

        Assert.Equal(75m, result.AmountDue);
    }

    [Fact]
    public void ExpiredContract_UsesOpeningBalanceAndPaymentDoesNotReduceAmountDue()
    {
        var result = Calculate(new DateOnly(2020, 1, 1), new DateOnly(2026, 7, 31),
            50_000m, 5_000m, 45_000m, null);

        Assert.Equal(50_000m, result.AmountDue);
        Assert.Equal(45_000m, result.Shortfall);
        Assert.Equal(AmountDueBases.ExpiredFullBalance, result.DueBasis);
        Assert.Equal(-1, result.RemainingOrOverdueMonths);
    }

    [Theory]
    [InlineData(1750, 1750)]
    [InlineData(2000, 2000)]
    [InlineData(5000, 5000)]
    public void FinalInstallment_UsesFullOpeningBalance(decimal opening, decimal expected)
    {
        var result = Calculate(new DateOnly(2026, 7, 1), new DateOnly(2027, 12, 31),
            opening, 0m, opening, Installment(1, 2_000m));

        Assert.Equal(expected, result.AmountDue);
        Assert.Equal(AmountDueBases.FinalInstallment, result.DueBasis);
    }

    [Fact]
    public void InstallmentsElapsed_UsesOpeningBalanceAndEmitsScheduleWarning()
    {
        var result = Calculate(new DateOnly(2026, 5, 1), new DateOnly(2027, 12, 31),
            5_000m, 0m, 5_000m, Installment(2, 1_000m));

        Assert.Equal(5_000m, result.AmountDue);
        Assert.Equal(AmountDueBases.InstallmentsElapsedFullBalance, result.DueBasis);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void MissingMonthlyInstallmentForNormalDueContract_IsUnknown()
    {
        var invalid = new InstallmentMasterContract(
            "C1", 12, null, InstallmentValidationStatuses.InvalidMonthlyInstallment,
            InstallmentDuplicateStatuses.None, [], 2);
        var result = Calculate(new DateOnly(2026, 6, 1), new DateOnly(2027, 12, 31),
            1_000m, 0m, 1_000m, invalid);

        Assert.Null(result.AmountDue);
        Assert.Null(result.Shortfall);
        Assert.Equal(AmountDueBases.MissingInstallmentData, result.DueBasis);
        Assert.Equal(InstallmentDataStatuses.Invalid, result.InstallmentDataStatus);
    }

    [Fact]
    public void MissingInstallmentForExpiredContract_IsStillComputable()
    {
        var result = Calculate(new DateOnly(2020, 1, 1), new DateOnly(2026, 7, 31),
            1_000m, 100m, 900m, null);

        Assert.Equal(1_000m, result.AmountDue);
        Assert.Equal(InstallmentDataStatuses.NotRequired, result.InstallmentDataStatus);
    }

    [Fact]
    public void MissingInstallmentForNewNotDueContract_IsZero()
    {
        var result = Calculate(new DateOnly(2026, 8, 15), new DateOnly(2028, 1, 1),
            0m, 100m, 1_000m, null);

        Assert.Equal(0m, result.AmountDue);
        Assert.Equal(100m, result.AdvancePayment);
        Assert.Equal(InstallmentDataStatuses.NotRequired, result.InstallmentDataStatus);
    }

    [Theory]
    [InlineData("2026-08-01", "2026-08-01", 1)]
    [InlineData("2026-01-01", "2026-08-01", 8)]
    [InlineData("2026-09-01", "2026-08-01", 0)]
    public void CurrentInstallmentNumber_IsScheduleBased(string first, string current, int expected)
    {
        Assert.Equal(expected, MonthlyAmountDueService.CurrentInstallmentNumber(
            DateOnly.Parse(first), DateOnly.Parse(current)));
    }

    [Theory]
    [InlineData("2026-09-15", "2026-08-01", 1)]
    [InlineData("2026-08-31", "2026-08-01", 0)]
    [InlineData("2026-07-01", "2026-08-01", -1)]
    [InlineData("2024-08-20", "2026-08-01", -24)]
    [InlineData("2015-11-30", "2026-08-01", -129)]
    [InlineData("2027-01-15", "2026-12-01", 1)]
    [InlineData("2025-12-15", "2026-01-01", -1)]
    public void RemainingOrOverdueMonths_UsesExpiryAndPeriodMonthsOnly(
        string expiry,
        string current,
        int expected)
    {
        Assert.Equal(expected, MonthlyAmountDueService.RemainingOrOverdueMonths(
            DateOnly.Parse(expiry), DateOnly.Parse(current)));
    }

    [Fact]
    public void RemainingOrOverdueMonths_IsNullWhenExpiryIsUnavailable()
    {
        Assert.Null(MonthlyAmountDueService.RemainingOrOverdueMonths(null, Current));
    }

    [Fact]
    public void SignedRemainingMonths_SortsNumericallyWithUnknownExpiryLast()
    {
        var rows = new[]
        {
            Calculate(new DateOnly(2020, 1, 1), new DateOnly(2026, 9, 15), 1_000m, 0m, 1_000m, Installment(100, 100m, "plus-one")),
            Calculate(new DateOnly(2020, 1, 1), new DateOnly(2024, 8, 15), 1_000m, 0m, 1_000m, Installment(100, 100m, "minus-twenty-four")),
            Calculate(new DateOnly(2020, 1, 1), null, 1_000m, 0m, 1_000m, Installment(100, 100m, "unknown")),
            Calculate(new DateOnly(2020, 1, 1), new DateOnly(2026, 8, 15), 1_000m, 0m, 1_000m, Installment(100, 100m, "zero")),
        };

        var sorted = MonthlyAmountDueService.OrderByRemainingOrOverdueMonths(rows, descending: false)
            .Select(x => x.RemainingOrOverdueMonths)
            .ToArray();

        Assert.Equal([-24, 0, 1, null], sorted);
    }

    [Fact]
    public void LongExpiredContract_ExposesSignedMonthsInsteadOfScheduleRatioAsPrimaryMetric()
    {
        var result = Calculate(new DateOnly(2010, 1, 1), new DateOnly(2015, 11, 30),
            1_000m, 0m, 1_000m, Installment(12, 100m));

        Assert.Equal("Expired", result.ContractStatus);
        Assert.Equal(-129, result.RemainingOrOverdueMonths);
        Assert.True(result.CurrentInstallmentNumber > result.TotalInstallments);
    }

    [Theory]
    [InlineData(RemainingOrOverdueFilters.OverdueTenYearsOrMore, -121, true)]
    [InlineData(RemainingOrOverdueFilters.OverdueTenYearsOrMore, -120, true)]
    [InlineData(RemainingOrOverdueFilters.OverdueTenYearsOrMore, -119, false)]
    [InlineData(RemainingOrOverdueFilters.OverdueFiveToTenYears, -120, false)]
    [InlineData(RemainingOrOverdueFilters.OverdueFiveToTenYears, -119, true)]
    [InlineData(RemainingOrOverdueFilters.OverdueFiveToTenYears, -60, true)]
    [InlineData(RemainingOrOverdueFilters.OverdueFiveToTenYears, -59, false)]
    [InlineData(RemainingOrOverdueFilters.OverdueThreeToFiveYears, -60, false)]
    [InlineData(RemainingOrOverdueFilters.OverdueThreeToFiveYears, -59, true)]
    [InlineData(RemainingOrOverdueFilters.OverdueThreeToFiveYears, -36, true)]
    [InlineData(RemainingOrOverdueFilters.OverdueThreeToFiveYears, -35, false)]
    [InlineData(RemainingOrOverdueFilters.OverdueOneToThreeYears, -36, false)]
    [InlineData(RemainingOrOverdueFilters.OverdueOneToThreeYears, -35, true)]
    [InlineData(RemainingOrOverdueFilters.OverdueOneToThreeYears, -12, true)]
    [InlineData(RemainingOrOverdueFilters.OverdueOneToThreeYears, -11, false)]
    [InlineData(RemainingOrOverdueFilters.OverdueUpToOneYear, -12, false)]
    [InlineData(RemainingOrOverdueFilters.OverdueUpToOneYear, -11, true)]
    [InlineData(RemainingOrOverdueFilters.OverdueUpToOneYear, -1, true)]
    [InlineData(RemainingOrOverdueFilters.OverdueUpToOneYear, 0, false)]
    [InlineData(RemainingOrOverdueFilters.DueThisMonth, -1, false)]
    [InlineData(RemainingOrOverdueFilters.DueThisMonth, 0, true)]
    [InlineData(RemainingOrOverdueFilters.DueThisMonth, 1, false)]
    [InlineData(RemainingOrOverdueFilters.RemainingOneToSixMonths, 0, false)]
    [InlineData(RemainingOrOverdueFilters.RemainingOneToSixMonths, 1, true)]
    [InlineData(RemainingOrOverdueFilters.RemainingOneToSixMonths, 6, true)]
    [InlineData(RemainingOrOverdueFilters.RemainingOneToSixMonths, 7, false)]
    [InlineData(RemainingOrOverdueFilters.RemainingSevenToTwelveMonths, 6, false)]
    [InlineData(RemainingOrOverdueFilters.RemainingSevenToTwelveMonths, 7, true)]
    [InlineData(RemainingOrOverdueFilters.RemainingSevenToTwelveMonths, 12, true)]
    [InlineData(RemainingOrOverdueFilters.RemainingSevenToTwelveMonths, 13, false)]
    [InlineData(RemainingOrOverdueFilters.RemainingOneToThreeYears, 12, false)]
    [InlineData(RemainingOrOverdueFilters.RemainingOneToThreeYears, 13, true)]
    [InlineData(RemainingOrOverdueFilters.RemainingOneToThreeYears, 36, true)]
    [InlineData(RemainingOrOverdueFilters.RemainingOneToThreeYears, 37, false)]
    [InlineData(RemainingOrOverdueFilters.RemainingMoreThanThreeYears, 36, false)]
    [InlineData(RemainingOrOverdueFilters.RemainingMoreThanThreeYears, 37, true)]
    public void RemainingOrOverdueFilters_UseExactRawNumericBoundaries(
        string filter,
        int months,
        bool expected)
    {
        Assert.Equal(expected, MonthlyAmountDueService.MatchesRemainingOrOverdueFilter(
            Row("C1", months), filter));
    }

    [Fact]
    public void OverdueAllAndSpecialStatuses_AreSeparated()
    {
        Assert.True(MonthlyAmountDueService.MatchesRemainingOrOverdueFilter(
            Row("ACTIVE", -1), RemainingOrOverdueFilters.OverdueAll));
        Assert.False(MonthlyAmountDueService.MatchesRemainingOrOverdueFilter(
            Row("PAID", -1, "PaidOff"), RemainingOrOverdueFilters.OverdueAll));
        Assert.False(MonthlyAmountDueService.MatchesRemainingOrOverdueFilter(
            Row("NOT-DUE", 48, "NotDue"), RemainingOrOverdueFilters.RemainingMoreThanThreeYears));
        Assert.True(MonthlyAmountDueService.MatchesRemainingOrOverdueFilter(
            Row("PAID", null, "PaidOff"), RemainingOrOverdueFilters.PaidOff));
        Assert.True(MonthlyAmountDueService.MatchesRemainingOrOverdueFilter(
            Row("NOT-DUE", null, "NotDue"), RemainingOrOverdueFilters.NotDue));
    }

    [Fact]
    public void SearchAndRemainingFilter_CombineAcrossTheFullDataset()
    {
        var page = MonthlyAmountDueService.BuildContractPage(
            [Row("ABC-100", -5), Row("ABC-200", 5), Row("XYZ-100", -5)],
            null, RemainingOrOverdueFilters.OverdueUpToOneYear, "  abc-1  ",
            1, 50, false);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("ABC-100", Assert.Single(page.Items).ContractNumber);
    }

    [Fact]
    public void NumericSorting_HappensBeforePagination()
    {
        var rows = Enumerable.Range(1, 100)
            .Reverse()
            .Select(months => Row($"C-{months:D3}", months));

        var firstPage = MonthlyAmountDueService.BuildContractPage(
            rows, null, null, null, 1, 50, false);

        Assert.Equal(Enumerable.Range(1, 50),
            firstPage.Items.Select(item => item.RemainingOrOverdueMonths!.Value));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void Pagination_FirstMiddleAndLastPagesReachEveryFilteredRowWithoutDuplicates(int pageSize)
    {
        var rows = Enumerable.Range(1, 451)
            .Select(index => Row($"C-{index:D4}", index))
            .ToArray();
        var pageCount = (int)Math.Ceiling(rows.Length / (decimal)pageSize);
        var middlePage = Math.Max(2, (pageCount + 1) / 2);

        var first = MonthlyAmountDueService.BuildContractPage(rows, null, null, null, 1, pageSize, false);
        var middle = MonthlyAmountDueService.BuildContractPage(rows, null, null, null, middlePage, pageSize, false);
        var last = MonthlyAmountDueService.BuildContractPage(rows, null, null, null, pageCount, pageSize, false);
        Assert.Equal(pageSize, first.Items.Count);
        Assert.NotEmpty(middle.Items);
        Assert.Equal(rows.Length - ((pageCount - 1) * pageSize), last.Items.Count);

        var all = Enumerable.Range(1, pageCount)
            .SelectMany(page => MonthlyAmountDueService.BuildContractPage(
                rows, null, null, null, page, pageSize, false).Items)
            .Select(item => item.ContractNumber)
            .ToArray();
        Assert.Equal(rows.Length, all.Length);
        Assert.Equal(rows.Length, all.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(rows.Select(row => row.ContractNumber).Order(StringComparer.Ordinal),
            all.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FirstDueMonth_RemainsMonthAfterContractStart()
    {
        var policy = new DebtSegmentationPolicyV1();
        Assert.Equal(new DateOnly(2026, 9, 1), policy.FirstDuePeriod(new DateOnly(2026, 8, 15)));
    }

    [Fact]
    public async Task ShortfallRemainsAndUncertainHistoryPreventsFalseExcess()
    {
        var debtStore = new InMemoryDebtSnapshotStore();
        var installmentStore = new InMemoryInstallmentMasterSnapshotStore();
        await debtStore.PublishAsync(DebtSnapshot([
            History("C1", new DateOnly(2026, 6, 1), null, 1_000m, 0m, 1_000m),
            History("C2", new DateOnly(2026, 6, 1), null, 1_000m, 200m, 800m)
        ], "debt-a"));
        await installmentStore.PublishAsync(InstallmentSnapshot([
            Installment(12, 100m, "C1"), Installment(12, 100m, "C2")
        ], "inst-a"));
        var service = Service(debtStore, installmentStore);

        var result = await service.GetDashboardAsync("2026-08");

        Assert.Equal(200m, result.Totals?.AmountDue);
        Assert.Equal(200m, result.Totals?.ActualPayment);
        Assert.Equal(100m, result.Totals?.Shortfall);
        Assert.Equal(0m, result.Totals?.ExcessPayment);
        Assert.Equal(200m, result.PaymentAllocation?.UnclassifiedPaymentAmount);
        Assert.Equal(0m, result.PaymentAllocation?.ReconciliationDifference);
    }

    [Fact]
    public async Task SnapshotCombinationControlsDerivedIdentityWithoutDuplicates()
    {
        var debtStore = new InMemoryDebtSnapshotStore();
        var installmentStore = new InMemoryInstallmentMasterSnapshotStore();
        await debtStore.PublishAsync(DebtSnapshot([
            History("C1", new DateOnly(2026, 6, 1), null, 1_000m, 0m, 1_000m)
        ], "debt-a"));
        await installmentStore.PublishAsync(InstallmentSnapshot([Installment(12, 100m, "C1")], "inst-a"));
        var service = Service(debtStore, installmentStore);

        var initial = await service.GetDashboardAsync("2026-08");
        var repeated = await service.GetDashboardAsync("2026-08");
        await installmentStore.PublishAsync(InstallmentSnapshot([Installment(12, 200m, "C1")], "inst-b"));
        var sourceBChanged = await service.GetDashboardAsync("2026-08");
        await debtStore.PublishAsync(DebtSnapshot([
            History("C1", new DateOnly(2026, 6, 1), null, 900m, 0m, 900m)
        ], "debt-b"));
        var sourceAChanged = await service.GetDashboardAsync("2026-08");

        Assert.Equal(initial.AnalysisId, repeated.AnalysisId);
        Assert.NotEqual(initial.AnalysisId, sourceBChanged.AnalysisId);
        Assert.NotEqual(sourceBChanged.AnalysisId, sourceAChanged.AnalysisId);
        Assert.Equal(200m, sourceBChanged.Totals?.AmountDue);
    }

    private static MonthlyAmountDueContract Calculate(
        DateOnly start,
        DateOnly? expire,
        decimal opening,
        decimal payment,
        decimal ending,
        InstallmentMasterContract? installment)
    {
        var history = History("C1", start, expire, opening, payment, ending);
        var debt = new DebtSegmentationAnalyzer().Analyze([history], Current.AddMonths(-1), Current).Contracts.Single();
        return MonthlyAmountDueService.Calculate(debt, Current, installment);
    }

    private static MonthlyAmountDueContract Row(
        string contractNumber,
        int? remainingOrOverdueMonths,
        string contractStatus = "Active/InTerm") =>
        Calculate(new DateOnly(2026, 6, 1), new DateOnly(2030, 1, 1),
            1_000m, 0m, 1_000m, Installment(120, 100m)) with
        {
            ContractNumber = contractNumber,
            RemainingOrOverdueMonths = remainingOrOverdueMonths,
            ContractStatus = contractStatus
        };

    private static DebtContractHistory History(
        string contract,
        DateOnly start,
        DateOnly? expire,
        decimal opening,
        decimal payment,
        decimal ending) => new()
    {
        ContractNumber = contract,
        MemberCode = contract,
        ContractDate = start,
        ExpireDate = expire,
        MonthlyStates = new Dictionary<DateOnly, DebtMonthlyState>
        {
            [Current.AddMonths(-1)] = new(Current.AddMonths(-1), 0m, opening, 0m, opening),
            [Current] = new(Current, payment, ending, 0m, ending)
        }
    };

    private static InstallmentMasterContract Installment(int total, decimal monthly, string contract = "C1") =>
        new(contract, total, monthly, InstallmentValidationStatuses.Usable,
            InstallmentDuplicateStatuses.None, [], 2);

    private static PublishedDebtSnapshot DebtSnapshot(IReadOnlyList<DebtContractHistory> contracts, string id) => new(
        id, "debt-segmentation-v1", "debt.xlsx", id, 1, DateTime.UtcNow, DateTime.UtcNow,
        new DebtWorkbookReadResult("debt.xlsx", "sheet", Current, contracts.Count + 1, 0, contracts, []), [], []);

    private static PublishedInstallmentMasterSnapshot InstallmentSnapshot(
        IReadOnlyList<InstallmentMasterContract> contracts,
        string id) => new(
            id, MonthlyAmountDueVersion.V1, "installment.xlsx", "installment.xlsx", id, 1,
            DateTime.UtcNow, DateTime.UtcNow,
            new InstallmentMasterReadResult(
                "installment.xlsx", "2534-2569", contracts.Count, contracts.Count,
                contracts.Count(x => x.IsUsable), contracts.Count(x => !x.IsUsable), 0, 0, 0, contracts, []));

    private static MonthlyAmountDueService Service(
        IDebtSnapshotStore debt,
        IInstallmentMasterSnapshotStore installment) => new(
            Options.Create(new DebtSegmentationPreviewOptions { DefaultCurrentPeriod = "2026-08" }),
            debt, installment, new DebtSegmentationAnalyzer());
}
