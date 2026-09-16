namespace COOPAI.API.Services.DebtSegmentation;

public enum DebtBucket
{
    Normal,
    OneMonthDelinquent,
    TwoMonthsDelinquent,
    ThreeToSixMonthsDelinquent,
    SevenToTwelveMonthsDelinquent,
    OverOneToThreeYears,
    OverThreeToFiveYears,
    OverFiveToTenYears,
    TenYearsAndAbove,
    PaidOff
}

public enum MonthlyPaymentStatus
{
    NotDue,
    Paid,
    NotPaid,
    Closed
}

public enum DebtClassificationBasis
{
    PaidOffBalance,
    ContractExpiryAge,
    BeforeFirstDuePeriod,
    LatestMonthPayment,
    ConsecutiveNoPayment
}

public static class DebtDataQualityWarningCodes
{
    public const string FormulaDerivedNegativeComponentReconciled =
        "FormulaDerivedNegativeComponentReconciled";
    public const string NegativePaymentComponentReconciled =
        "NegativePaymentComponentReconciled";
}

public sealed record DebtCellProvenance(
    string CellAddress,
    string? Formula,
    string? FormulaR1C1)
{
    public bool IsFormulaDerived => !string.IsNullOrWhiteSpace(Formula);
}

public sealed record DebtMonthlyStateProvenance(
    decimal PrincipalPaymentInput,
    decimal ProfitPaymentInput,
    decimal? TotalPaymentInput,
    DebtCellProvenance PrincipalPayment,
    DebtCellProvenance ProfitPayment,
    DebtCellProvenance TotalPayment,
    DebtCellProvenance PrincipalOutstanding,
    DebtCellProvenance ProfitOutstanding,
    DebtCellProvenance TotalOutstanding);

public sealed record DebtDataQualityWarning(
    string Code,
    string ContractKey,
    string ContractNumber,
    int SourceRowNumber,
    DateOnly Period,
    string Field,
    decimal ComponentValue,
    decimal PrincipalOutstanding,
    decimal ProfitOutstanding,
    decimal TotalOutstanding,
    decimal PrincipalPaymentInput,
    decimal ProfitPaymentInput,
    decimal? TotalPaymentInput,
    string CellAddress,
    string Formula,
    string? FormulaR1C1);

public enum DebtMovementCategory
{
    NewContract,
    NewDelinquency,
    NewEnteredRisk,
    Improved,
    Worsened,
    SameBucketBalanceDecreased,
    SameBucketBalanceIncreased,
    SameBucketUnchanged,
    ClosedPaidOff
}

public sealed record DebtMonthlyState(
    DateOnly Period,
    decimal PaymentAmount,
    decimal PrincipalOutstanding,
    decimal ProfitOutstanding,
    decimal TotalOutstanding)
{
    public decimal? ScheduledAmount { get; init; }

    public DebtMonthlyStateProvenance? Provenance { get; init; }

    public decimal? PaymentDifference => ScheduledAmount.HasValue
        ? PaymentAmount - ScheduledAmount.Value
        : null;

    public bool HasActualPayment => PaymentAmount > 0m;
}

public sealed class DebtContractHistory
{
    public int SourceRowNumber { get; init; }
    public string MemberCode { get; init; } = string.Empty;
    public string MemberName { get; init; } = string.Empty;
    public string ContractNumber { get; init; } = string.Empty;
    public string LoanType { get; init; } = string.Empty;
    public DateOnly? ContractDate { get; init; }
    public DateOnly? ExpireDate { get; init; }
    public string? GroupCode { get; init; }
    public string? Branch { get; init; }
    public IReadOnlyDictionary<DateOnly, DebtMonthlyState> MonthlyStates { get; init; } =
        new Dictionary<DateOnly, DebtMonthlyState>();

    public string MemberKey => string.IsNullOrWhiteSpace(MemberCode)
        ? $"contract:{ContractNumber}"
        : MemberCode.Trim();

    public string ContractKey => ContractNumber.Trim().ToUpperInvariant();

}

public sealed record DebtClassification(
    DebtBucket Bucket,
    MonthlyPaymentStatus LatestPaymentStatus,
    DateOnly? FirstDuePeriod,
    DateOnly? FirstMissedPaymentPeriod,
    int ConsecutiveMissedMonths,
    DateOnly? LastPaymentPeriod,
    DebtClassificationBasis CalculationBasis);

public sealed record DebtContractAnalysis(
    DebtContractHistory Contract,
    DebtMonthlyState PreviousState,
    DebtMonthlyState CurrentState,
    DebtClassification PreviousClassification,
    DebtClassification CurrentClassification,
    string PaymentMovement,
    DebtMovementCategory MovementCategory,
    bool IsNewContract);

public sealed record DebtBucketSummary(
    DebtBucket Bucket,
    int MemberCount,
    int ContractCount,
    decimal PrincipalOutstanding,
    decimal ProfitOutstanding,
    decimal OutstandingAmount,
    int PaidMemberCount,
    int PaidContractCount,
    int NotPaidMemberCount,
    int NotPaidContractCount,
    int NotDueMemberCount,
    int NotDueContractCount);

public sealed record DebtBucketPeriodComparison(
    DebtBucket Bucket,
    int PreviousMemberCount,
    int CurrentMemberCount,
    int MemberDifference,
    int PreviousContractCount,
    int CurrentContractCount,
    int ContractDifference,
    decimal PreviousOutstanding,
    decimal CurrentOutstanding,
    decimal AmountDifference,
    int EnteredContracts,
    int ImprovedContracts,
    int WorsenedContracts,
    int PaidOffContracts,
    int SameBucketBalanceDecreasedContracts,
    int SameBucketBalanceIncreasedContracts);

public sealed record DebtMovementSummary(
    string PreviousBucket,
    string CurrentBucket,
    string PaymentMovement,
    DebtMovementCategory? Category,
    int MemberCount,
    int ContractCount,
    decimal PreviousOutstanding,
    decimal CurrentOutstanding,
    decimal AmountChange);

public sealed record DebtSegmentationAnalysis(
    DateOnly PreviousPeriod,
    DateOnly CurrentPeriod,
    IReadOnlyList<DebtContractAnalysis> Contracts,
    IReadOnlyList<DebtBucketSummary> BucketSummaries,
    IReadOnlyList<DebtBucketPeriodComparison> BucketComparisons,
    IReadOnlyList<DebtMovementSummary> PaymentMovements,
    IReadOnlyList<DebtMovementSummary> BucketMovements);

public enum DebtSyncTrigger
{
    Automatic,
    Manual
}

public static class DebtSyncStatuses
{
    public const string Success = "Success";
    public const string NoChange = "NoChange";
    public const string Failed = "Failed";
    public const string Busy = "Busy";
}

public sealed record PublishedDebtSnapshot(
    string SnapshotId,
    string PolicyVersion,
    string SourceFileName,
    string SourceFileHash,
    long SourceFileSizeBytes,
    DateTime SourceLastWriteTimeUtc,
    DateTime PublishedAtUtc,
    DebtWorkbookReadResult Workbook,
    IReadOnlyList<DebtDataQualityWarning> DataQualityWarnings,
    IReadOnlyList<string> ValidationWarnings)
{
    public string SourceType { get; init; } = DebtSourceTypes.Local;
}

public sealed record DebtSyncHistoryEntry(
    DateTime AttemptedAtUtc,
    bool Success,
    bool Published,
    bool PreviousSnapshotPreserved,
    string Trigger,
    string Message,
    string SourceFileName,
    string? SourceFileHash,
    DateTime? PublishedAtUtc,
    int SourceRows,
    int ContractCount,
    int AddedContracts,
    int RemovedContracts,
    int ChangedContracts,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<string> Warnings)
{
    public string Status { get; init; } = string.Empty;
    public DateOnly? PreviousPeriod { get; init; }
    public DateOnly? CurrentPeriod { get; init; }
    public int CandidateRows { get; init; }
    public int ExcludedRows { get; init; }
    public int MemberCount { get; init; }
    public decimal OutstandingAmount { get; init; }
    public int NewContracts { get; init; }
    public int PaidOffContracts { get; init; }
    public string? SnapshotId { get; init; }
    public long DurationMilliseconds { get; init; }
    public string SourceType { get; init; } = DebtSourceTypes.Local;
}

public sealed record DebtSyncResult(
    bool Success,
    bool Published,
    bool PreviousSnapshotPreserved,
    DebtSyncTrigger Trigger,
    string Message,
    string SourceFileName,
    string? SourceFileHash,
    DateTime? PublishedAtUtc,
    int SourceRows,
    int ContractCount,
    int AddedContracts,
    int RemovedContracts,
    int ChangedContracts,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<string> Warnings)
{
    public string Status { get; init; } = string.Empty;
    public DateTime AttemptedAtUtc { get; init; }
    public DateOnly? PreviousPeriod { get; init; }
    public DateOnly? CurrentPeriod { get; init; }
    public int CandidateRows { get; init; }
    public int ExcludedRows { get; init; }
    public int MemberCount { get; init; }
    public decimal OutstandingAmount { get; init; }
    public int NewContracts { get; init; }
    public int PaidOffContracts { get; init; }
    public string? SnapshotId { get; init; }
    public long DurationMilliseconds { get; init; }
    public string SourceType { get; init; } = DebtSourceTypes.Local;
}

public static class DebtLabels
{
    public static string Bucket(DebtBucket bucket) => bucket switch
    {
        DebtBucket.Normal => "Normal",
        DebtBucket.OneMonthDelinquent => "1 month delinquent",
        DebtBucket.TwoMonthsDelinquent => "2 months delinquent",
        DebtBucket.ThreeToSixMonthsDelinquent => "3–6 months delinquent",
        DebtBucket.SevenToTwelveMonthsDelinquent => "7–12 months delinquent",
        DebtBucket.OverOneToThreeYears => ">1 to 3 years",
        DebtBucket.OverThreeToFiveYears => ">3 to 5 years",
        DebtBucket.OverFiveToTenYears => ">5 to 10 years",
        DebtBucket.TenYearsAndAbove => "10 years and above",
        DebtBucket.PaidOff => "Paid off / no outstanding balance",
        _ => bucket.ToString()
    };

    public static string BucketThai(DebtBucket bucket) => bucket switch
    {
        DebtBucket.Normal => "ปกติ",
        DebtBucket.OneMonthDelinquent => "ขาด 1 เดือน",
        DebtBucket.TwoMonthsDelinquent => "ขาด 2 เดือน",
        DebtBucket.ThreeToSixMonthsDelinquent => "ขาด 3–6 เดือน",
        DebtBucket.SevenToTwelveMonthsDelinquent => "ขาด 7–12 เดือน",
        DebtBucket.OverOneToThreeYears => "เกิน 1 แต่ไม่เกิน 3 ปี",
        DebtBucket.OverThreeToFiveYears => "เกิน 3 แต่ไม่เกิน 5 ปี",
        DebtBucket.OverFiveToTenYears => "เกิน 5 แต่ไม่เกิน 10 ปี",
        DebtBucket.TenYearsAndAbove => "10 ปีขึ้นไป",
        DebtBucket.PaidOff => "ชำระหมด / ไม่มียอดคงเหลือ",
        _ => bucket.ToString()
    };

    public static string Payment(MonthlyPaymentStatus status) => status switch
    {
        MonthlyPaymentStatus.NotDue => "Not due",
        MonthlyPaymentStatus.Paid => "Paid",
        MonthlyPaymentStatus.NotPaid => "Not paid",
        MonthlyPaymentStatus.Closed => "Closed / paid off",
        _ => status.ToString()
    };

    public static string Basis(DebtClassificationBasis basis) => basis switch
    {
        DebtClassificationBasis.PaidOffBalance => "No outstanding balance",
        DebtClassificationBasis.ContractExpiryAge => "Elapsed time after contract expiry",
        DebtClassificationBasis.BeforeFirstDuePeriod => "Before first payment due period",
        DebtClassificationBasis.LatestMonthPayment => "Positive payment in evaluated month",
        DebtClassificationBasis.ConsecutiveNoPayment => "Current consecutive non-payment run",
        _ => basis.ToString()
    };

    public static string Category(DebtMovementCategory category) => category switch
    {
        DebtMovementCategory.NewContract => "New contract",
        DebtMovementCategory.NewDelinquency => "New delinquency",
        DebtMovementCategory.NewEnteredRisk => "Entered risk / reopened",
        DebtMovementCategory.Improved => "Improved",
        DebtMovementCategory.Worsened => "Worsened",
        DebtMovementCategory.SameBucketBalanceDecreased => "Same bucket but balance decreased",
        DebtMovementCategory.SameBucketBalanceIncreased => "Same bucket but balance increased",
        DebtMovementCategory.SameBucketUnchanged => "Same bucket unchanged",
        DebtMovementCategory.ClosedPaidOff => "Closed / paid off",
        _ => category.ToString()
    };
}
