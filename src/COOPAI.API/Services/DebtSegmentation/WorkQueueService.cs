using COOPAI.API.DTOs.DebtSegmentation;

namespace COOPAI.API.Services.DebtSegmentation;

public interface IWorkQueueService
{
    Task<WorkQueuePageDto> GetAsync(
        string? currentPeriod,
        string? queueType,
        string? priority,
        string? debtBucket,
        string? payment,
        string? contractStatus,
        string? reason,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public static class WorkQueueTypes
{
    public const string Collection = "COLLECTION";
    public const string DataReview = "DATA_REVIEW";
}

public static class WorkQueuePriorities
{
    public const string Urgent = "URGENT";
    public const string High = "HIGH";
    public const string Medium = "MEDIUM";
    public const string Review = "REVIEW";
}

public static class WorkQueueReasons
{
    public const string NoPaymentCurrentPeriod = "NO_PAYMENT_CURRENT_PERIOD";
    public const string CurrentDueShortfall = "CURRENT_DUE_SHORTFALL";
    public const string PriorArrears = "PRIOR_ARREARS";
    public const string ContractExpiredOutstanding = "CONTRACT_EXPIRED_OUTSTANDING";
    public const string LongTermOverdue = "LONG_TERM_OVERDUE";
    public const string DataReviewUnclassified = "DATA_REVIEW_UNCLASSIFIED";
    public const string UnknownPriorArrears = "UNKNOWN_PRIOR_ARREARS";
    public const string MissingDownPaymentEvidence = "MISSING_DOWNPAYMENT_EVIDENCE";
}

public static class WorkQueuePaymentFilters
{
    public const string NoPayment = "NO_PAYMENT";
    public const string PartialPayment = "PARTIAL_PAYMENT";
    public const string HasPayment = "HAS_PAYMENT";
}

public sealed class WorkQueueService(
    IMonthlyAmountDueService monthlyAmountDueService,
    IDebtSnapshotStore debtSnapshotStore) : IWorkQueueService
{
    private static readonly HashSet<string> LongTermBuckets = new(StringComparer.Ordinal)
    {
        "7–12 months delinquent", ">1 to 3 years", ">3 to 5 years",
        ">5 to 10 years", "10 years and above"
    };

    public async Task<WorkQueuePageDto> GetAsync(
        string? currentPeriod,
        string? queueType,
        string? priority,
        string? debtBucket,
        string? payment,
        string? contractStatus,
        string? reason,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var analysis = await monthlyAmountDueService.GetAnalysisAsync(currentPeriod, cancellationToken);
        if (analysis is null)
        {
            return new WorkQueuePageDto(
                false, "A valid current Preview analysis is required for Today's Work Queue.",
                null, null, null, default, null, 1, 50, 0, [], null);
        }

        var debt = await debtSnapshotStore.GetCurrentAsync(cancellationToken);
        var members = (debt?.Workbook.Contracts ?? [])
            .GroupBy(contract => contract.ContractNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return BuildPage(
            analysis, members, queueType, priority, debtBucket, payment,
            contractStatus, reason, search, page, pageSize);
    }

    public static WorkQueuePageDto BuildPage(
        MonthlyAmountDueAnalysis analysis,
        IReadOnlyDictionary<string, DebtContractHistory>? members,
        string? queueType,
        string? priority,
        string? debtBucket,
        string? payment,
        string? contractStatus,
        string? reason,
        string? search,
        int page,
        int pageSize)
    {
        members ??= new Dictionary<string, DebtContractHistory>(StringComparer.OrdinalIgnoreCase);
        var queue = analysis.Contracts
            .Select(contract => BuildItem(contract, members.GetValueOrDefault(contract.ContractNumber)))
            .Where(item => item is not null)
            .Cast<WorkQueueItemDto>()
            .ToArray();
        var summary = BuildSummary(analysis.Contracts, queue);

        IEnumerable<WorkQueueItemDto> query = queue;
        if (!string.IsNullOrWhiteSpace(queueType))
            query = query.Where(item => EqualsFilter(item.QueueType, queueType));
        if (!string.IsNullOrWhiteSpace(priority))
            query = query.Where(item => EqualsFilter(item.Priority, priority));
        if (!string.IsNullOrWhiteSpace(debtBucket))
            query = query.Where(item => EqualsFilter(item.DebtBucket, debtBucket));
        if (!string.IsNullOrWhiteSpace(payment))
            query = ApplyPaymentFilter(query, payment);
        if (!string.IsNullOrWhiteSpace(contractStatus))
        {
            var normalizedStatus = contractStatus.Trim();
            query = normalizedStatus.Equals("InTerm", StringComparison.OrdinalIgnoreCase)
                ? query.Where(item => item.ContractStatus == "Active/InTerm")
                : query.Where(item => EqualsFilter(item.ContractStatus, normalizedStatus));
        }
        if (!string.IsNullOrWhiteSpace(reason))
            query = query.Where(item => item.ReasonCodes.Contains(reason.Trim(), StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search))
            query = ApplySearchFilter(query, search);

        var ordered = query
            .OrderBy(item => item.PrioritySortOrder)
            .ThenByDescending(item => BucketSeverity(item.DebtBucket))
            .ThenBy(item => item.RemainingOrOverdueMonths.HasValue ? 0 : 1)
            .ThenBy(item => item.RemainingOrOverdueMonths)
            .ThenByDescending(item => item.EndingOutstanding)
            .ThenBy(item => item.ContractNo, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return new WorkQueuePageDto(
            true,
            "Today's Work Queue is derived read-only from the current Preview payment allocation.",
            analysis.AnalysisId,
            analysis.DebtSnapshotId,
            analysis.InstallmentSnapshotId,
            analysis.CurrentPeriod,
            summary,
            page,
            pageSize,
            ordered.Length,
            ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
            analysis.GeneratedAtUtc);
    }

    public static WorkQueueItemDto? BuildItem(
        MonthlyAmountDueContract contract,
        DebtContractHistory? member = null)
    {
        var collectionEligible = contract.ContractStatus is not "PaidOff" and not "NotDue" &&
            contract.EndingOutstanding > 0m && !contract.IsEarlyPayoff && !contract.IsExpiredPayoff;
        var collectionReasons = new List<string>();
        var reviewReasons = new List<string>();

        var hasUnpaidObligation = contract.Shortfall is > 0m || contract.EndingArrears is > 0m;
        if (collectionEligible && contract.ActualPayment == 0m && hasUnpaidObligation)
            collectionReasons.Add(WorkQueueReasons.NoPaymentCurrentPeriod);
        if (collectionEligible && contract.AmountDue is > 0m && contract.Shortfall is > 0m)
            collectionReasons.Add(WorkQueueReasons.CurrentDueShortfall);
        if (collectionEligible && contract.PriorArrearsStatus == PriorArrearsStatuses.Known &&
            contract.PriorArrears is > 0m)
            collectionReasons.Add(WorkQueueReasons.PriorArrears);
        if (collectionEligible && contract.ContractStatus == "Expired")
            collectionReasons.Add(WorkQueueReasons.ContractExpiredOutstanding);
        if (collectionEligible && LongTermBuckets.Contains(contract.DebtBucket))
            collectionReasons.Add(WorkQueueReasons.LongTermOverdue);

        if (contract.UnclassifiedPaymentAmount > 0m)
            reviewReasons.Add(WorkQueueReasons.DataReviewUnclassified);
        if (IsUnknownPriorArrears(contract.PriorArrearsStatus))
            reviewReasons.Add(WorkQueueReasons.UnknownPriorArrears);
        if (contract.PaymentAllocationStatus == PaymentAllocationStatuses.UnresolvedDownPaymentEvidence)
            reviewReasons.Add(WorkQueueReasons.MissingDownPaymentEvidence);

        if (collectionReasons.Count == 0 && reviewReasons.Count == 0)
            return null;

        var queueType = collectionReasons.Count > 0 ? WorkQueueTypes.Collection : WorkQueueTypes.DataReview;
        var priority = Priority(contract, collectionReasons, queueType);
        var allReasons = collectionReasons.Concat(reviewReasons).ToArray();
        var labels = allReasons.Select(ReasonLabelThai).ToArray();
        var assignment = OfficerAssignmentPolicy.Resolve(member?.GroupCode);
        var currentDelinquencyMonths = CurrentDelinquencyMonths(contract);
        var currentDelinquencyLabel = CurrentDelinquencyLabelThai(currentDelinquencyMonths);
        var expiredAgeMonths = ExpiredAgeMonths(contract);
        var expiredAgeLabel = expiredAgeMonths.HasValue
            ? ExpiredAgeLabelThai(expiredAgeMonths.Value)
            : null;
        return new WorkQueueItemDto(
            contract.ContractNumber,
            string.IsNullOrWhiteSpace(member?.MemberCode) ? null : member.MemberCode,
            string.IsNullOrWhiteSpace(member?.MemberName) ? null : member.MemberName,
            string.IsNullOrWhiteSpace(member?.GroupCode) ? null : member.GroupCode,
            assignment.OfficerName,
            assignment.Status,
            assignment.AssignmentRule,
            assignment.OfficerCandidates,
            contract.ContractStatus,
            contract.DebtBucket,
            DebtBucketLabelThai(contract.DebtBucket),
            currentDelinquencyMonths,
            currentDelinquencyLabel,
            expiredAgeMonths,
            expiredAgeLabel,
            contract.RemainingOrOverdueMonths,
            RemainingLabelThai(contract.RemainingOrOverdueMonths, contract.ContractStatus),
            contract.OpeningOutstanding,
            contract.EndingOutstanding,
            contract.MonthlyInstallment,
            contract.AmountDue,
            contract.ActualPayment,
            contract.PriorArrearsStatus == PriorArrearsStatuses.Known ? contract.PriorArrears : null,
            contract.PriorArrearsStatus,
            contract.PriorAdvanceCredit,
            contract.AdvanceCreditApplied,
            contract.EndingAdvanceCredit,
            contract.PaymentToPriorArrears,
            contract.PaymentToCurrentDue,
            contract.Shortfall,
            contract.UnclassifiedPaymentAmount,
            queueType,
            priority.Name,
            priority.SortOrder,
            allReasons,
            labels,
            labels[0],
            contract.Warnings);
    }

    private static WorkQueueSummaryDto BuildSummary(
        IReadOnlyList<MonthlyAmountDueContract> analysisContracts,
        IReadOnlyList<WorkQueueItemDto> queue)
    {
        var collection = queue.Where(item => item.QueueType == WorkQueueTypes.Collection).ToArray();
        var review = queue.Where(item => item.QueueType == WorkQueueTypes.DataReview).ToArray();
        var noPaymentContractNumbers = collection
            .Where(HasReason(WorkQueueReasons.NoPaymentCurrentPeriod))
            .Select(item => item.ContractNo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var priorities = new[]
        {
            WorkQueuePriorities.Urgent, WorkQueuePriorities.High,
            WorkQueuePriorities.Medium, WorkQueuePriorities.Review
        }.Select(value => new WorkQueuePrioritySummaryDto(
            value,
            queue.Count(item => item.Priority == value),
            queue.Where(item => item.Priority == value).Sum(item => item.EndingOutstanding))).ToArray();
        var longTerm = LongTermBuckets
            .OrderByDescending(BucketSeverity)
            .Select(bucket => new WorkQueueLongTermBucketDto(
                bucket,
                DebtBucketLabelThai(bucket),
                collection.Count(item => item.DebtBucket == bucket),
                collection.Where(item => item.DebtBucket == bucket).Sum(item => item.EndingOutstanding)))
            .ToArray();
        var officerSummaries = OfficerAssignmentPolicy.OfficerNames
            .Select(officerName =>
            {
                var assigned = queue
                    .Where(item => item.AssignmentStatus == OfficerAssignmentStatuses.Assigned &&
                        string.Equals(item.OfficerName, officerName, StringComparison.Ordinal))
                    .ToArray();
                return new WorkQueueOfficerSummaryDto(
                    officerName,
                    assigned.Length,
                    assigned.Sum(item => item.EndingOutstanding),
                    assigned.Count(item => item.Priority == WorkQueuePriorities.Urgent),
                    assigned.Count(item => item.Priority == WorkQueuePriorities.High));
            })
            .ToArray();

        return new WorkQueueSummaryDto(
            analysisContracts.Count,
            queue.Count,
            collection.Length,
            collection.Sum(item => item.EndingOutstanding),
            review.Length,
            review.Sum(item => item.UnclassifiedAmount),
            priorities,
            collection.Count(HasReason(WorkQueueReasons.NoPaymentCurrentPeriod)),
            collection.Count(HasReason(WorkQueueReasons.CurrentDueShortfall)),
            collection.Where(HasReason(WorkQueueReasons.CurrentDueShortfall)).Sum(item => item.CurrentShortfall ?? 0m),
            collection.Count(HasReason(WorkQueueReasons.PriorArrears)),
            collection.Where(HasReason(WorkQueueReasons.PriorArrears)).Sum(item => item.KnownPriorArrears ?? 0m),
            collection.Count(HasReason(WorkQueueReasons.ContractExpiredOutstanding)),
            collection.Where(HasReason(WorkQueueReasons.ContractExpiredOutstanding)).Sum(item => item.EndingOutstanding),
            longTerm,
            queue.Count(HasReason(WorkQueueReasons.DataReviewUnclassified)),
            queue.Where(HasReason(WorkQueueReasons.DataReviewUnclassified)).Sum(item => item.UnclassifiedAmount),
            queue.Count(HasReason(WorkQueueReasons.UnknownPriorArrears)),
            queue.Count(HasReason(WorkQueueReasons.MissingDownPaymentEvidence)),
            queue.Where(HasReason(WorkQueueReasons.MissingDownPaymentEvidence)).Sum(item => item.UnclassifiedAmount),
            collection.Count(item => item.ContractStatus == "PaidOff"),
            analysisContracts.Count(item => item.ContractStatus == "NotDue" && item.ActualPayment == 0m &&
                noPaymentContractNumbers.Contains(item.ContractNumber)),
            analysisContracts.Count(item => item.ContractStatus is not "PaidOff" and not "NotDue" &&
                item.ActualPayment == 0m && item.AmountDue is > 0m && item.AdvanceCreditApplied > 0m &&
                item.RemainingCurrentDueForCash == 0m && item.EndingArrears == 0m &&
                noPaymentContractNumbers.Contains(item.ContractNumber)),
            queue.Count(item => item.AssignmentStatus == OfficerAssignmentStatuses.Assigned),
            queue.Count(item => item.AssignmentStatus == OfficerAssignmentStatuses.Unassigned),
            queue.Count(item => item.AssignmentStatus == OfficerAssignmentStatuses.Conflict),
            officerSummaries);
    }

    private const string AssignmentStatusSearchPrefix = "__ASSIGNMENT_STATUS__:";
    private const string OfficerSearchPrefix = "__OFFICER__:";

    private static IEnumerable<WorkQueueItemDto> ApplySearchFilter(
        IEnumerable<WorkQueueItemDto> query,
        string search)
    {
        var term = search.Trim();

        if (term.StartsWith(AssignmentStatusSearchPrefix, StringComparison.Ordinal))
        {
            var status = term[AssignmentStatusSearchPrefix.Length..].Trim();
            return query.Where(item =>
                string.Equals(item.AssignmentStatus, status, StringComparison.OrdinalIgnoreCase));
        }

        if (term.StartsWith(OfficerSearchPrefix, StringComparison.Ordinal))
        {
            var officerName = term[OfficerSearchPrefix.Length..].Trim();
            return query.Where(item =>
                item.AssignmentStatus == OfficerAssignmentStatuses.Assigned &&
                string.Equals(item.OfficerName, officerName, StringComparison.Ordinal));
        }

        return query.Where(item =>
            item.ContractNo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (item.MemberNo?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (item.MemberName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (item.GroupCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (item.OfficerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private static IEnumerable<WorkQueueItemDto> ApplyPaymentFilter(
        IEnumerable<WorkQueueItemDto> query,
        string payment) => payment.Trim().ToUpperInvariant() switch
        {
            WorkQueuePaymentFilters.NoPayment => query.Where(item => item.ActualPayment == 0m),
            WorkQueuePaymentFilters.PartialPayment => query.Where(item => item.ActualPayment > 0m && item.CurrentShortfall is > 0m),
            WorkQueuePaymentFilters.HasPayment => query.Where(item => item.ActualPayment > 0m),
            _ => throw new ArgumentException($"Unsupported payment filter '{payment}'.", nameof(payment))
        };

    private static Func<WorkQueueItemDto, bool> HasReason(string reason) =>
        item => item.ReasonCodes.Contains(reason, StringComparer.Ordinal);

    private static bool EqualsFilter(string value, string filter) =>
        value.Equals(filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnknownPriorArrears(string status) =>
        status.StartsWith("Unknown", StringComparison.Ordinal) ||
        status == PriorArrearsStatuses.PriorCreditPolicyRequired;

    private static (string Name, int SortOrder) Priority(
        MonthlyAmountDueContract contract,
        IReadOnlyCollection<string> collectionReasons,
        string queueType)
    {
        if (queueType == WorkQueueTypes.DataReview)
            return (WorkQueuePriorities.Review, 3);
        var severeLongTerm = contract.DebtBucket is ">5 to 10 years" or "10 years and above";
        var expiredWithUnpaidObligation = collectionReasons.Contains(WorkQueueReasons.ContractExpiredOutstanding) &&
            (contract.Shortfall is > 0m || contract.PriorArrears is > 0m);
        if (severeLongTerm || expiredWithUnpaidObligation)
            return (WorkQueuePriorities.Urgent, 0);
        if (collectionReasons.Contains(WorkQueueReasons.NoPaymentCurrentPeriod) ||
            collectionReasons.Contains(WorkQueueReasons.PriorArrears) ||
            contract.DebtBucket is "3–6 months delinquent" or "7–12 months delinquent" or
                ">1 to 3 years" or ">3 to 5 years")
            return (WorkQueuePriorities.High, 1);
        return (WorkQueuePriorities.Medium, 2);
    }

    private static int BucketSeverity(string bucket) => bucket switch
    {
        "10 years and above" => 9,
        ">5 to 10 years" => 8,
        ">3 to 5 years" => 7,
        ">1 to 3 years" => 6,
        "7–12 months delinquent" => 5,
        "3–6 months delinquent" => 4,
        "2 months delinquent" => 3,
        "1 month delinquent" => 2,
        "Normal" => 1,
        _ => 0
    };

    public static string RemainingLabelThai(int? months, string contractStatus)
    {
        if (contractStatus == "PaidOff") return "ชำระหมดแล้ว";
        if (contractStatus == "NotDue") return "ยังไม่ถึงงวดแรก";
        if (!months.HasValue) return "ไม่ทราบ";
        if (months.Value == 0) return "ครบกำหนดเดือนนี้";
        var prefix = months.Value < 0 ? "เกินกำหนด" : "คงเหลือ";
        var absolute = Math.Abs(months.Value);
        var years = absolute / 12;
        var remainingMonths = absolute % 12;
        if (years == 0) return $"{prefix} {remainingMonths} เดือน";
        if (remainingMonths == 0) return $"{prefix} {years} ปี";
        return $"{prefix} {years} ปี {remainingMonths} เดือน";
    }

    private static string ReasonLabelThai(string reason) => reason switch
    {
        WorkQueueReasons.NoPaymentCurrentPeriod => "ไม่ชำระงวดปัจจุบัน",
        WorkQueueReasons.CurrentDueShortfall => "ชำระงวดปัจจุบันไม่ครบ",
        WorkQueueReasons.PriorArrears => "มียอดค้างยกมา",
        WorkQueueReasons.ContractExpiredOutstanding => "สัญญาเกินกำหนดและยังมียอดหนี้",
        WorkQueueReasons.LongTermOverdue => "ค้างชำระระยะยาว",
        WorkQueueReasons.DataReviewUnclassified => "มีรายการรับชำระที่ยังจำแนกไม่ได้",
        WorkQueueReasons.UnknownPriorArrears => "ข้อมูลยอดค้างยกมายังไม่สมบูรณ์",
        WorkQueueReasons.MissingDownPaymentEvidence => "รอตรวจสอบหลักฐานเงินดาวน์",
        _ => reason
    };

    public static int? CurrentDelinquencyMonths(MonthlyAmountDueContract contract)
    {
        if (!contract.FirstDuePeriod.HasValue ||
            contract.ContractualObligation is not > 0m ||
            contract.TotalInstallments is not > 0 ||
            contract.MonthlyInstallment is not > 0m)
            return null;

        var obligation = contract.ContractualObligation.Value;
        var totalInstallments = contract.TotalInstallments.Value;
        var monthlyInstallment = contract.MonthlyInstallment.Value;
        var finalInstallment = ContractSchedulePositionCalculator.FinalInstallment(
            obligation, totalInstallments, monthlyInstallment);

        if (finalInstallment <= 0m)
            return null;

        var dueCount = ContractSchedulePositionCalculator.DueCount(
            contract.FirstDuePeriod.Value,
            contract.CurrentPeriod,
            totalInstallments);

        if (dueCount <= 0)
            return 0;

        // "ขาดชำระ" means the age of the OLDEST due installment that is still
        // not fully paid. All satisfied contractual value is allocated oldest-first.
        // This is deliberately different from "remaining installments" and from
        // EndingOutstanding / MonthlyInstallment.
        var satisfied = Math.Clamp(
            obligation - contract.EndingOutstanding,
            0m,
            obligation);

        for (var installmentNumber = 1; installmentNumber <= dueCount; installmentNumber++)
        {
            var installmentAmount = installmentNumber == totalInstallments
                ? finalInstallment
                : monthlyInstallment;

            if (satisfied + ContractSchedulePositionCalculator.Tolerance >= installmentAmount)
            {
                satisfied = Math.Max(0m, satisfied - installmentAmount);
                continue;
            }

            var oldestUnpaidDuePeriod = contract.FirstDuePeriod.Value.AddMonths(installmentNumber - 1);
            var monthsOld =
                ((contract.CurrentPeriod.Year - oldestUnpaidDuePeriod.Year) * 12) +
                contract.CurrentPeriod.Month - oldestUnpaidDuePeriod.Month + 1;

            return Math.Max(1, monthsOld);
        }

        // All installments that are due as of the analysis period are fully satisfied.
        return 0;
    }

    public static int? ExpiredAgeMonths(MonthlyAmountDueContract contract)
    {
        if (!string.Equals(contract.ContractStatus, "Expired", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!contract.RemainingOrOverdueMonths.HasValue)
            return null;

        return Math.Max(1, -contract.RemainingOrOverdueMonths.Value);
    }

    public static string CurrentDelinquencyLabelThai(int? months) => months switch
    {
        null => "สถานะขาดชำระ: คำนวณไม่ได้",
        <= 0 => "สถานะขาดชำระ: ปกติ",
        1 => "ขาดชำระ 1 เดือน",
        2 => "ขาดชำระ 2 เดือน",
        <= 6 => "ขาดชำระ 3–6 เดือน",
        <= 12 => "ขาดชำระ 7–12 เดือน",
        <= 36 => "ขาดชำระเกิน 1 ปี แต่ไม่เกิน 3 ปี",
        <= 60 => "ขาดชำระเกิน 3 ปี แต่ไม่เกิน 5 ปี",
        <= 119 => "ขาดชำระเกิน 5 ปี แต่ไม่เกิน 10 ปี",
        _ => "ขาดชำระ 10 ปีขึ้นไป"
    };

    public static string ExpiredAgeLabelThai(int months) => months switch
    {
        <= 1 => "เลยสัญญา 1 เดือน",
        2 => "เลยสัญญา 2 เดือน",
        <= 6 => "เลยสัญญา 3–6 เดือน",
        <= 12 => "เลยสัญญา 7–12 เดือน",
        <= 36 => "เลยสัญญา 1–3 ปี",
        <= 60 => "เลยสัญญา 3–5 ปี",
        <= 119 => "เลยสัญญา 5–10 ปี",
        _ => "เลยสัญญา 10 ปีขึ้นไป"
    };

    private static string DebtBucketLabelThai(string bucket) => bucket switch
    {
        "Normal" => "ปกติ",
        "1 month delinquent" => "ขาด 1 เดือน",
        "2 months delinquent" => "ขาด 2 เดือน",
        "3–6 months delinquent" => "ขาด 3–6 เดือน",
        "7–12 months delinquent" => "ขาด 7–12 เดือน",
        ">1 to 3 years" => "เกิน 1 แต่ไม่เกิน 3 ปี",
        ">3 to 5 years" => "เกิน 3 แต่ไม่เกิน 5 ปี",
        ">5 to 10 years" => "เกิน 5 แต่ไม่เกิน 10 ปี",
        "10 years and above" => "10 ปีขึ้นไป",
        "Paid off / no outstanding balance" => "ชำระหมด / ไม่มียอดคงเหลือ",
        _ => bucket
    };
}
