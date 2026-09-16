using COOPAI.API.DTOs.DebtSegmentation;
using COOPAI.API.Services.DebtSegmentation;

namespace COOPAI.API.Tests;

public sealed class WorkQueueServiceTests
{
    [Fact]
    public void PaidOffAndCompletedPayoffs_DoNotRemainCollectionTasks()
    {
        Assert.Null(WorkQueueService.BuildItem(Row("PAID") with
        {
            ContractStatus = "PaidOff", DebtBucket = "Paid off / no outstanding balance",
            OpeningOutstanding = 0m, EndingOutstanding = 0m, AmountDue = 0m, Shortfall = 0m
        }));
        Assert.Null(WorkQueueService.BuildItem(Row("EARLY") with
        {
            EndingOutstanding = 0m, Shortfall = 0m, IsEarlyPayoff = true, EarlyPayoffAmount = 1_000m
        }));
        Assert.Null(WorkQueueService.BuildItem(Row("EXPIRED-PAID") with
        {
            ContractStatus = "Expired", EndingOutstanding = 0m, Shortfall = 0m,
            IsExpiredPayoff = true, ExpiredPayoffAmount = 1_000m
        }));
    }

    [Fact]
    public void NotDueWithNoPayment_IsNotCollection_ButMissingDownPaymentEvidenceIsReviewOnly()
    {
        var plain = WorkQueueService.BuildItem(Row("NEW") with
        {
            ContractStatus = "NotDue", DueBasis = AmountDueBases.NotDue,
            ActualPayment = 0m, AmountDue = 0m, Shortfall = 0m
        });
        var review = WorkQueueService.BuildItem(Row("สจ-NEW") with
        {
            ContractStatus = "NotDue", DueBasis = AmountDueBases.NotDue,
            ActualPayment = 0m, AmountDue = 0m, Shortfall = 0m,
            PaymentAllocationStatus = PaymentAllocationStatuses.UnresolvedDownPaymentEvidence
        });

        Assert.Null(plain);
        Assert.NotNull(review);
        Assert.Equal(WorkQueueTypes.DataReview, review.QueueType);
        Assert.Contains(WorkQueueReasons.MissingDownPaymentEvidence, review.ReasonCodes);
        Assert.DoesNotContain(WorkQueueReasons.NoPaymentCurrentPeriod, review.ReasonCodes);
    }

    [Fact]
    public void DueNoPaymentRequiresAnUncoveredObligation()
    {
        var unpaid = WorkQueueService.BuildItem(Row("UNPAID") with
        {
            ActualPayment = 0m, AmountDue = 100m, Shortfall = 100m, EndingArrears = 100m
        });
        var creditCovered = WorkQueueService.BuildItem(Row("CREDIT") with
        {
            ActualPayment = 0m, AmountDue = 100m, Shortfall = 0m, EndingArrears = 0m,
            PriorAdvanceCredit = 100m, AdvanceCreditAppliedToCurrentDue = 100m,
            AdvanceCreditApplied = 100m, RemainingCurrentDueForCash = 0m
        });

        Assert.Contains(WorkQueueReasons.NoPaymentCurrentPeriod, unpaid!.ReasonCodes);
        Assert.Contains(WorkQueueReasons.CurrentDueShortfall, unpaid.ReasonCodes);
        Assert.Null(creditCovered);
    }

    [Fact]
    public void PartialPaymentPriorArrearsAndUnknownHistoryRemainAdditiveAndExplicit()
    {
        var known = WorkQueueService.BuildItem(Row("KNOWN") with
        {
            ActualPayment = 50m, AmountDue = 100m, Shortfall = 50m,
            PriorArrearsStatus = PriorArrearsStatuses.Known, PriorArrears = 25m, EndingArrears = 75m
        });
        var unknown = WorkQueueService.BuildItem(Row("UNKNOWN") with
        {
            ActualPayment = 0m, AmountDue = 100m, Shortfall = 100m,
            PriorArrearsStatus = PriorArrearsStatuses.UnknownHistoryBeforeAvailablePeriod,
            PriorArrears = null, UnclassifiedPaymentAmount = 10m
        });

        Assert.Contains(WorkQueueReasons.CurrentDueShortfall, known!.ReasonCodes);
        Assert.Contains(WorkQueueReasons.PriorArrears, known.ReasonCodes);
        Assert.Equal(25m, known.KnownPriorArrears);
        Assert.Contains(WorkQueueReasons.UnknownPriorArrears, unknown!.ReasonCodes);
        Assert.Contains(WorkQueueReasons.DataReviewUnclassified, unknown.ReasonCodes);
        Assert.Null(unknown.KnownPriorArrears);
    }

    [Fact]
    public void ExpiredAndLongTermReasonsUseExistingStatusAndBucket()
    {
        var item = WorkQueueService.BuildItem(Row("OLD") with
        {
            ContractStatus = "Expired", DebtBucket = "10 years and above",
            RemainingOrOverdueMonths = -383, AmountDue = 1_000m, Shortfall = 1_000m
        });

        Assert.Equal(WorkQueuePriorities.Urgent, item!.Priority);
        Assert.Contains(WorkQueueReasons.ContractExpiredOutstanding, item.ReasonCodes);
        Assert.Contains(WorkQueueReasons.LongTermOverdue, item.ReasonCodes);
        Assert.Equal("เกินกำหนด 31 ปี 11 เดือน", item.RemainingOrOverdueLabel);
    }

    [Fact]
    public void UnclassifiedAndMissingDownPaymentEvidenceAreReviewReasons_NotGuessedCashAllocation()
    {
        var item = WorkQueueService.BuildItem(Row("สจ-REVIEW") with
        {
            ContractStatus = "NotDue", DueBasis = AmountDueBases.NotDue,
            ActualPayment = 500m, AmountDue = 0m, Shortfall = 0m,
            UnclassifiedPaymentAmount = 500m, DownPaymentAmount = 0m,
            DownPaymentDataStatus = DownPaymentDataStatuses.Missing,
            PaymentAllocationStatus = PaymentAllocationStatuses.UnresolvedDownPaymentEvidence
        });

        Assert.Equal(WorkQueueTypes.DataReview, item!.QueueType);
        Assert.Equal(WorkQueuePriorities.Review, item.Priority);
        Assert.Contains(WorkQueueReasons.DataReviewUnclassified, item.ReasonCodes);
        Assert.Contains(WorkQueueReasons.MissingDownPaymentEvidence, item.ReasonCodes);
        Assert.Equal(500m, item.UnclassifiedAmount);
    }

    [Fact]
    public void PriorityAndRawNumericOrderingAreDeterministicWithStableContractTieBreaker()
    {
        var rows = new[]
        {
            Row("MEDIUM-B", 1) with { ActualPayment = 50m, Shortfall = 50m },
            Row("MEDIUM-A", 1) with { ActualPayment = 50m, Shortfall = 50m },
            Row("HIGH", -4) with { DebtBucket = "3–6 months delinquent" },
            Row("URGENT", -121) with { DebtBucket = "10 years and above" },
            Row("REVIEW", null) with
            {
                ContractStatus = "NotDue", DueBasis = AmountDueBases.NotDue,
                AmountDue = 0m, Shortfall = 0m, UnclassifiedPaymentAmount = 1m
            }
        };
        var page = Page(rows);

        Assert.Equal(["URGENT", "HIGH", "MEDIUM-A", "MEDIUM-B", "REVIEW"],
            page.Items.Select(item => item.ContractNo));
        Assert.Equal(page.Items.Select(item => item.Priority), Page(rows).Items.Select(item => item.Priority));
    }

    [Fact]
    public void EveryPracticalFilterAndTrimmedSearchRunsBeforePagination()
    {
        var rows = Enumerable.Range(1, 80).Select(index => Row($"ABC-{index:D3}", index) with
        {
            DebtBucket = index <= 60 ? "1 month delinquent" : "Normal",
            ActualPayment = index % 2 == 0 ? 50m : 0m,
            Shortfall = index % 2 == 0 ? 50m : 100m,
            PriorArrearsStatus = index <= 55 ? PriorArrearsStatuses.Known : PriorArrearsStatuses.NotApplicable,
            PriorArrears = index <= 55 ? 20m : 0m
        }).ToArray();

        var page = WorkQueueService.BuildPage(
            Analysis(rows), null,
            WorkQueueTypes.Collection, WorkQueuePriorities.High, "1 month delinquent",
            WorkQueuePaymentFilters.HasPayment, "InTerm", WorkQueueReasons.PriorArrears,
            "  ABC-0  ", 1, 50);

        Assert.Equal(27, page.TotalCount);
        Assert.All(page.Items, item =>
        {
            Assert.Equal(WorkQueueTypes.Collection, item.QueueType);
            Assert.Equal(WorkQueuePriorities.High, item.Priority);
            Assert.Equal("1 month delinquent", item.DebtBucket);
            Assert.True(item.ActualPayment > 0m);
            Assert.Contains(WorkQueueReasons.PriorArrears, item.ReasonCodes);
            Assert.Contains("ABC-0", item.ContractNo);
        });
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void PaginationReachesAllFilteredRowsWithoutDuplicates(int pageSize)
    {
        var rows = Enumerable.Range(1, 451)
            .Select(index => Row($"C-{index:D4}", index))
            .ToArray();
        var pageCount = (int)Math.Ceiling(rows.Length / (decimal)pageSize);
        var first = Page(rows, page: 1, pageSize: pageSize);
        var middle = Page(rows, page: Math.Max(2, (pageCount + 1) / 2), pageSize: pageSize);
        var last = Page(rows, page: pageCount, pageSize: pageSize);

        Assert.Equal(pageSize, first.Items.Count);
        Assert.NotEmpty(middle.Items);
        Assert.Equal(rows.Length - ((pageCount - 1) * pageSize), last.Items.Count);
        var all = Enumerable.Range(1, pageCount)
            .SelectMany(page => Page(rows, page, pageSize).Items)
            .Select(item => item.ContractNo)
            .ToArray();
        Assert.Equal(rows.Length, all.Length);
        Assert.Equal(rows.Length, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SummaryCountsContractsOnceAndProvesFalseCollectionCasesAreZero()
    {
        var rows = new[]
        {
            Row("MULTI") with
            {
                ContractStatus = "Expired", DebtBucket = "7–12 months delinquent",
                PriorArrearsStatus = PriorArrearsStatuses.Known, PriorArrears = 50m,
                UnclassifiedPaymentAmount = 10m
            },
            Row("REVIEW") with
            {
                ContractStatus = "NotDue", DueBasis = AmountDueBases.NotDue,
                AmountDue = 0m, Shortfall = 0m, UnclassifiedPaymentAmount = 20m
            },
            Row("COVERED") with
            {
                ActualPayment = 0m, Shortfall = 0m, EndingArrears = 0m,
                PriorAdvanceCredit = 100m, AdvanceCreditApplied = 100m,
                AdvanceCreditAppliedToCurrentDue = 100m, RemainingCurrentDueForCash = 0m
            },
            Row("PAID") with
            {
                ContractStatus = "PaidOff", EndingOutstanding = 0m,
                OpeningOutstanding = 0m, AmountDue = 0m, Shortfall = 0m
            }
        };
        var page = Page(rows);

        Assert.Equal(4, page.Summary!.AnalysisUniverseContracts);
        Assert.Equal(2, page.Summary.TotalQueueContracts);
        Assert.Equal(1, page.Summary.CollectionContracts);
        Assert.Equal(1, page.Summary.ReviewOnlyContracts);
        Assert.Equal(0, page.Summary.PaidOffCollectionContracts);
        Assert.Equal(0, page.Summary.NotDueFalseNoPaymentContracts);
        Assert.Equal(0, page.Summary.FullyCreditCoveredFalseNoPaymentContracts);
    }

    private static WorkQueuePageDto Page(
        IReadOnlyList<MonthlyAmountDueContract> rows,
        int page = 1,
        int pageSize = 50) =>
        WorkQueueService.BuildPage(
            Analysis(rows), null, null, null, null, null, null, null, null, page, pageSize);

    private static MonthlyAmountDueAnalysis Analysis(IReadOnlyList<MonthlyAmountDueContract> rows) =>
        new("analysis", MonthlyAmountDueVersion.V1, "debt", "installment", new DateOnly(2026, 8, 1),
            rows, [], new MonthlyCollectionDataQuality(rows.Count, 0, 0, 0, 0m, 0, 0, 0, 0, 0, 0, 0, 0),
            DateTime.UnixEpoch);

    private static MonthlyAmountDueContract Row(string contractNo, int? remainingMonths = 12) =>
        new(
            contractNo, "Normal", "Active/InTerm", new DateOnly(2026, 8, 1),
            new DateOnly(2026, 1, 1), new DateOnly(2025, 12, 1), new DateOnly(2027, 8, 1),
            remainingMonths, 1_000m, 0m, 1_000m, 8, 120, 100m, 100m, 100m, 0m, 0m,
            AmountDueBases.ActiveInstallment, InstallmentDataStatuses.Available, [])
        {
            PriorArrearsStatus = PriorArrearsStatuses.NotApplicable,
            PaymentAllocationStatus = PaymentAllocationStatuses.Allocated,
            RemainingCurrentDueForCash = 100m
        };
}
