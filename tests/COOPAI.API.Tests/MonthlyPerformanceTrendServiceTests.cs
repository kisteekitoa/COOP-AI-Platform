using COOPAI.API.Services.DebtSegmentation;

namespace COOPAI.API.Tests;

public sealed class MonthlyPerformanceTrendServiceTests
{
    [Fact]
    public async Task Periods_AreDynamicOrderedAndExtendWithANewLiveSourceMonth()
    {
        var debtStore = new InMemoryDebtSnapshotStore();
        var installmentStore = new InMemoryInstallmentMasterSnapshotStore();
        var history = History("C1", new DateOnly(2025, 12, 15), null,
            (2025, 12, 0m, 1_000m), (2026, 1, 100m, 900m),
            (2026, 2, 100m, 800m), (2026, 12, 0m, 800m));
        var future = History("FUTURE", new DateOnly(2026, 3, 10), null,
            (2026, 1, 0m, 0m), (2026, 2, 0m, 0m), (2026, 3, 0m, 500m));
        await debtStore.PublishAsync(DebtSnapshot([history, future], "debt-1", new DateOnly(2026, 2, 1)));
        await installmentStore.PublishAsync(InstallmentSnapshot(
            [Installment("C1", 100m), Installment("FUTURE", 100m)], "installment-1"));
        var service = Service(debtStore, installmentStore);

        var initial = await service.GetAsync();

        Assert.Equal([1, 2], initial.Months.Select(month => month.PeriodMonth));
        Assert.All(initial.Months, month => Assert.Equal(1, month.ContractCount));
        Assert.Equal(new DateOnly(2026, 1, 1), initial.FirstPeriod);
        Assert.Equal(new DateOnly(2026, 2, 1), initial.LatestPeriod);

        history = History("C1", new DateOnly(2025, 12, 15), null,
            (2025, 12, 0m, 1_000m), (2026, 1, 100m, 900m),
            (2026, 2, 100m, 800m), (2026, 3, 100m, 700m), (2026, 12, 0m, 700m));
        await debtStore.PublishAsync(DebtSnapshot([history, future], "debt-2", new DateOnly(2026, 3, 1)));

        var extended = await service.GetAsync();

        Assert.Equal([1, 2, 3], extended.Months.Select(month => month.PeriodMonth));
        Assert.Equal(new DateOnly(2026, 3, 1), extended.LatestPeriod);
        Assert.Equal(2, extended.Months[^1].ContractCount);
    }

    [Fact]
    public async Task OpeningEndingCashAndBuckets_ReconcileForEveryKnownMonth()
    {
        var carry = History("CARRY", new DateOnly(2025, 12, 10), null,
            (2025, 12, 0m, 1_000m),
            (2026, 1, 250m, 900m),
            (2026, 2, 0m, 850m),
            (2026, 3, 200m, 700m));
        var result = await TrendAsync([carry], [Installment("CARRY", 100m)]);

        Assert.Equal(3, result.Months.Count);
        Assert.All(result.Months, month =>
        {
            Assert.Equal(0m, month.CashReconciliationDifference);
            Assert.Equal(0m, month.LedgerReconciliationDifference);
            Assert.Equal(month.ContractCount, month.DebtBuckets.Sum(bucket => bucket.ContractCount));
            Assert.Equal(month.EndingOutstanding, month.DebtBuckets.Sum(bucket => bucket.OutstandingBalance));
            Assert.Equal(month.EndingOutstanding,
                month.OpeningOutstanding + month.IncreaseDuringPeriod - month.ActualPayment);
            Assert.Equal(month.EndingOutstandingContracts,
                month.OpeningOutstandingContracts + month.NewOutstandingContracts - month.ReducedOutstandingContracts);
            Assert.Equal(month.ContractCount, month.EndingOutstandingContracts + month.PaidOffContracts);
        });
        Assert.Equal(result.Months[0].EndingOutstanding, result.Months[1].OpeningOutstanding);
        Assert.Equal(result.Months[1].EndingOutstanding, result.Months[2].OpeningOutstanding);
        Assert.Equal(result.Months[0].EndingAdvanceCredit, result.Months[1].PriorAdvanceCredit);
        Assert.Equal(result.Months[1].EndingAdvanceCredit, result.Months[2].PriorAdvanceCredit);
        Assert.Equal(150m, result.Months[0].NewAdvanceCredit);
        Assert.Equal(100m, result.Months[1].AdvanceCreditApplied);
    }

    [Fact]
    public async Task BucketBalances_UseTheClassificationAndEndingBalanceFromTheirOwnMonth()
    {
        var moving = History("MOVING", new DateOnly(2025, 12, 1), null,
            (2026, 1, 100m, 900m),
            (2026, 2, 0m, 850m));
        var result = await TrendAsync([moving], [Installment("MOVING", 100m)]);

        var january = result.Months.Single(month => month.PeriodMonth == 1);
        var february = result.Months.Single(month => month.PeriodMonth == 2);
        Assert.Equal(900m, january.DebtBuckets.Single(bucket => bucket.Bucket == "Normal").OutstandingBalance);
        Assert.Equal(0m, january.DebtBuckets.Single(bucket => bucket.Bucket == "1 month delinquent").OutstandingBalance);
        Assert.Equal(0m, february.DebtBuckets.Single(bucket => bucket.Bucket == "Normal").OutstandingBalance);
        Assert.Equal(850m, february.DebtBuckets.Single(bucket => bucket.Bucket == "1 month delinquent").OutstandingBalance);
        Assert.Equal(january.EndingOutstanding, january.DebtBuckets.Sum(bucket => bucket.OutstandingBalance));
        Assert.Equal(february.EndingOutstanding, february.DebtBuckets.Sum(bucket => bucket.OutstandingBalance));
    }

    [Fact]
    public async Task NewContract_IsCountedByStartMonthAndNotDueUntilNextMonth()
    {
        var history = History("NEW", new DateOnly(2026, 1, 20), null,
            (2026, 1, 20m, 500m), (2026, 2, 0m, 500m));
        var result = await TrendAsync([history], [Installment("NEW", 100m)]);

        var january = result.Months[0];
        var february = result.Months[1];
        Assert.Equal(1, january.NewContractCount);
        Assert.Equal(0m, january.AmountDue);
        Assert.Equal(20m, january.AdvancePayment);
        Assert.Equal(20m, january.EndingAdvanceCredit);
        Assert.Equal(20m, february.PriorAdvanceCredit);
        Assert.Equal(20m, february.AdvanceCreditApplied);
        Assert.Equal(100m, february.AmountDue);
    }

    [Fact]
    public async Task OutstandingMoneyAndContractMovements_AreDerivedAndReconcile()
    {
        var carry = History("CARRY", new DateOnly(2025, 12, 1), null,
            (2026, 1, 0m, 1_000m), (2026, 2, 100m, 900m));
        var paidOff = History("PAID", new DateOnly(2025, 12, 1), null,
            (2026, 1, 0m, 100m), (2026, 2, 100m, 0m));
        var entering = History("NEW", new DateOnly(2026, 2, 1), null,
            (2026, 2, 0m, 500m));
        var result = await TrendAsync(
            [carry, paidOff, entering],
            [Installment("CARRY", 100m), Installment("PAID", 100m), Installment("NEW", 100m)]);

        var february = result.Months.Single(month => month.PeriodMonth == 2);
        Assert.Equal(1_100m, february.OpeningOutstanding);
        Assert.Equal(500m, february.IncreaseDuringPeriod);
        Assert.Equal(200m, february.ActualPayment);
        Assert.Equal(1_400m, february.EndingOutstanding);
        Assert.Equal(2, february.OpeningOutstandingContracts);
        Assert.Equal(1, february.NewOutstandingContracts);
        Assert.Equal(1, february.ReducedOutstandingContracts);
        Assert.Equal(2, february.EndingOutstandingContracts);
        Assert.Equal(1, february.PaidOffContracts);
        Assert.Equal(february.EndingOutstanding,
            february.OpeningOutstanding + february.IncreaseDuringPeriod - february.ActualPayment);
        Assert.Equal(february.EndingOutstandingContracts,
            february.OpeningOutstandingContracts + february.NewOutstandingContracts - february.ReducedOutstandingContracts);
        Assert.Equal(february.ContractCount, february.EndingOutstandingContracts + february.PaidOffContracts);
    }

    [Fact]
    public async Task ArrearsCarryForward_IsChronologicalAndNeverNetsAcrossContracts()
    {
        var arrears = History("ARREARS", new DateOnly(2025, 11, 1), null,
            (2025, 11, 0m, 1_000m), (2025, 12, 0m, 1_000m),
            (2026, 1, 50m, 950m), (2026, 2, 300m, 650m));
        var advance = History("ADVANCE", new DateOnly(2026, 1, 1), null,
            (2026, 1, 400m, 800m), (2026, 2, 0m, 800m));
        var result = await TrendAsync(
            [arrears, advance],
            [Installment("ARREARS", 100m), Installment("ADVANCE", 100m)]);

        var january = result.Months[0];
        var february = result.Months[1];
        Assert.Equal(100m, january.PriorArrears);
        Assert.Equal(50m, january.PaymentToPriorArrears);
        Assert.Equal(0m, january.PaymentToCurrentDue);
        Assert.Equal(400m, january.EndingAdvanceCredit);
        Assert.Equal(400m, february.PriorAdvanceCredit);
        Assert.Equal(100m, february.AdvanceCreditApplied);
        Assert.Equal(150m, february.PaymentToPriorArrears);
        Assert.Equal(100m, february.PaymentToCurrentDue);
    }

    [Fact]
    public async Task Payoffs_AreTerminalAndClassifiedWithoutDoubleCounting()
    {
        var early = History("EARLY", new DateOnly(2025, 12, 1), new DateOnly(2026, 12, 31),
            (2025, 12, 0m, 100m), (2026, 1, 100m, 0m));
        var expired = History("EXPIRED", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31),
            (2025, 12, 0m, 120m), (2026, 1, 120m, 0m));
        var result = await TrendAsync(
            [early, expired],
            [Installment("EARLY", 50m), Installment("EXPIRED", 50m)]);
        var january = Assert.Single(result.Months);

        Assert.Equal(1, january.EarlyPayoffCount);
        Assert.Equal(100m, january.EarlyPayoffAmount);
        Assert.Equal(1, january.ExpiredPayoffCount);
        Assert.Equal(120m, january.ExpiredPayoffAmount);
        Assert.Equal(2, january.PaidOffDuringMonthCount);
        Assert.Equal(220m, january.PaidOffDuringMonthAmount);
        Assert.Equal(2, january.DebtBuckets.Single(bucket =>
            bucket.Bucket == DebtLabels.Bucket(DebtBucket.PaidOff)).ContractCount);
        Assert.Equal(0m, january.UnclassifiedAmount);
    }

    [Fact]
    public async Task MissingPrehistory_RemainsUnknownAndCashIsUnclassified()
    {
        var unknown = History("UNKNOWN", new DateOnly(2020, 1, 1), null,
            (2026, 1, 75m, 500m));
        var result = await TrendAsync([unknown], [Installment("UNKNOWN", 100m)]);
        var january = Assert.Single(result.Months);

        Assert.Null(january.OpeningOutstanding);
        Assert.Null(january.IncreaseDuringPeriod);
        Assert.Null(january.OpeningOutstandingContracts);
        Assert.Null(january.NewOutstandingContracts);
        Assert.Null(january.ReducedOutstandingContracts);
        Assert.Equal(1, january.EndingOutstandingContracts);
        Assert.Equal(0, january.PaidOffContracts);
        Assert.Null(january.AmountDue);
        Assert.Null(january.PriorArrears);
        Assert.Null(january.EndingArrears);
        Assert.Equal(0, january.PaidOffDuringMonthCount);
        Assert.Equal(1, january.UnknownAmountDueContractCount);
        Assert.Equal(1, january.PriorArrearsUnknownContractCount);
        Assert.Equal(1, january.UnclassifiedCount);
        Assert.Equal(75m, january.UnclassifiedAmount);
        Assert.False(january.HistoricalClassificationComplete);
        Assert.Equal(0m, january.CashReconciliationDifference);
    }

    private static async Task<COOPAI.API.DTOs.DebtSegmentation.MonthlyPerformanceTrendDto> TrendAsync(
        IReadOnlyList<DebtContractHistory> histories,
        IReadOnlyList<InstallmentMasterContract> installments)
    {
        var debtStore = new InMemoryDebtSnapshotStore();
        var installmentStore = new InMemoryInstallmentMasterSnapshotStore();
        await debtStore.PublishAsync(DebtSnapshot(histories, "debt"));
        await installmentStore.PublishAsync(InstallmentSnapshot(installments, "installment"));
        return await Service(debtStore, installmentStore).GetAsync();
    }

    private static MonthlyPerformanceTrendService Service(
        IDebtSnapshotStore debtStore,
        IInstallmentMasterSnapshotStore installmentStore) =>
        new(debtStore, installmentStore, new DebtSegmentationAnalyzer());

    private static DebtContractHistory History(
        string contract,
        DateOnly start,
        DateOnly? expire,
        params (int Year, int Month, decimal Payment, decimal Ending)[] facts) => new()
    {
        ContractNumber = contract,
        MemberCode = contract,
        ContractDate = start,
        ExpireDate = expire,
        MonthlyStates = facts.ToDictionary(
            fact => new DateOnly(fact.Year, fact.Month, 1),
            fact => new DebtMonthlyState(new DateOnly(fact.Year, fact.Month, 1),
                fact.Payment, fact.Ending, 0m, fact.Ending))
    };

    private static InstallmentMasterContract Installment(string contract, decimal monthly) =>
        new(contract, 120, monthly, InstallmentValidationStatuses.Usable,
            InstallmentDuplicateStatuses.None, [], 2)
        {
            DownPayment = 0m,
            DownPaymentDataStatus = DownPaymentDataStatuses.Available
        };

    private static PublishedDebtSnapshot DebtSnapshot(
        IReadOnlyList<DebtContractHistory> contracts,
        string id,
        DateOnly? dataThrough = null)
    {
        var latest = contracts.SelectMany(contract => contract.MonthlyStates.Keys).Max();
        return new PublishedDebtSnapshot(
            id, "debt-segmentation-v1", "debt.xlsx", id, 1,
            DateTime.UtcNow, DateTime.UtcNow,
            new DebtWorkbookReadResult("debt.xlsx", "sheet", dataThrough ?? latest, contracts.Count + 1, 0, contracts, []),
            [], []);
    }

    private static PublishedInstallmentMasterSnapshot InstallmentSnapshot(
        IReadOnlyList<InstallmentMasterContract> contracts,
        string id) => new(
            id, MonthlyAmountDueVersion.InstallmentMasterV2, "installment.xlsx", "installment.xlsx", id, 1,
            DateTime.UtcNow, DateTime.UtcNow,
            new InstallmentMasterReadResult(
                "installment.xlsx", "2534-2569", contracts.Count, contracts.Count,
                contracts.Count, 0, 0, 0, 0, contracts, []));
}
