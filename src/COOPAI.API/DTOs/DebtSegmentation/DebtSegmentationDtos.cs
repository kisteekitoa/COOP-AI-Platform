using COOPAI.API.Services.DebtSegmentation;

namespace COOPAI.API.DTOs.DebtSegmentation;

public sealed record DebtAutoSyncStatusDto(
    bool Enabled,
    string Status,
    int PollSeconds,
    DateTime? LastCheckedAtUtc,
    DateTime? LastSyncAttemptAtUtc,
    DateTime? LastSuccessfulSyncAtUtc,
    string Message,
    string SourceType,
    string SourceFileName);

public sealed record DebtPreviewTotalsDto(
    int MemberCount,
    int ContractCount,
    decimal PrincipalOutstanding,
    decimal ProfitOutstanding,
    decimal OutstandingAmount,
    int PaidLatestMonthContracts,
    int NotPaidLatestMonthContracts,
    int NotDueLatestMonthContracts);

public sealed record DebtBucketSummaryDto(
    string Bucket,
    string BucketLabel,
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

public sealed record DebtBucketPeriodComparisonDto(
    string Bucket,
    string BucketLabel,
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

public sealed record DebtSyncStatusDto(
    string Status,
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
    IReadOnlyList<string> Warnings,
    DateOnly? PreviousPeriod,
    DateOnly? CurrentPeriod,
    int CandidateRows,
    int ExcludedRows,
    int MemberCount,
    decimal OutstandingAmount,
    int NewContracts,
    int PaidOffContracts,
    string? SnapshotId,
    long DurationMilliseconds)
{
    public string SourceType { get; init; } = DebtSourceTypes.Local;
}

public sealed record DebtSyncHistoryDto(
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
    IReadOnlyList<string> Warnings,
    string Status,
    DateOnly? PreviousPeriod,
    DateOnly? CurrentPeriod,
    int CandidateRows,
    int ExcludedRows,
    int MemberCount,
    decimal OutstandingAmount,
    int NewContracts,
    int PaidOffContracts,
    string? SnapshotId,
    long DurationMilliseconds)
{
    public string SourceType { get; init; } = DebtSourceTypes.Local;
}

public sealed record DebtMovementSummaryDto(
    string PreviousBucket,
    string CurrentBucket,
    string PaymentMovement,
    string Category,
    int MemberCount,
    int ContractCount,
    decimal PreviousOutstanding,
    decimal CurrentOutstanding,
    decimal AmountChange);

public sealed record DebtDataQualityWarningSummaryDto(
    string Code,
    int AffectedContractCount,
    int FactCount);

public sealed record DebtDataQualityWarningDto(
    string Code,
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

public sealed record DebtPreviewDashboardDto(
    bool Available,
    string Message,
    string SourceFileName,
    string? SourceFileHash,
    DateTime? DebtSnapshotPublishedAtUtc,
    string WorksheetName,
    DateOnly? DataThroughPeriod,
    string PolicyVersion,
    bool AutoSyncOnQueryEnabled,
    DateOnly RequestedPreviousPeriod,
    DateOnly RequestedCurrentPeriod,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DebtDataQualityWarningSummaryDto> DataQualityWarnings,
    DebtPreviewTotalsDto? Totals,
    DebtSyncStatusDto Sync,
    IReadOnlyList<string> LoanTypes,
    IReadOnlyList<string> Branches,
    IReadOnlyList<DebtBucketSummaryDto> Buckets,
    IReadOnlyList<DebtBucketPeriodComparisonDto> BucketComparisons,
    IReadOnlyList<DebtMovementSummaryDto> PaymentMovements,
    IReadOnlyList<DebtMovementSummaryDto> BucketMovements);

public sealed record DebtContractDetailDto(
    int SourceRowNumber,
    string MemberCode,
    string MemberName,
    string ContractNumber,
    string LoanType,
    DateOnly? ContractDate,
    DateOnly? FirstDuePeriod,
    DateOnly? ExpireDate,
    string PreviousBucket,
    string CurrentBucket,
    string CurrentBucketLabel,
    string LatestPaymentStatus,
    decimal LatestPaymentAmount,
    decimal? LatestScheduledAmount,
    decimal? LatestPaymentDifference,
    DateOnly? FirstMissedPaymentPeriod,
    int ConsecutiveMissedMonths,
    decimal PrincipalBalance,
    decimal ProfitBalance,
    decimal TotalBalance,
    string? GroupCode,
    string? Branch,
    string PaymentMovement,
    string MovementCategory,
    decimal PreviousBalance,
    decimal AmountChange,
    DateOnly EvaluatedPeriod,
    DateOnly? LastPaymentPeriod,
    string CalculationBasis,
    IReadOnlyList<DebtDataQualityWarningDto> DataQualityWarnings);

public sealed record DebtContractPageDto(
    bool Available,
    string Message,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<DebtContractDetailDto> Items);
