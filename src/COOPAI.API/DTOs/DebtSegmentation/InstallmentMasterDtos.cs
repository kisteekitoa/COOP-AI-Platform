using COOPAI.API.Services.DebtSegmentation;

namespace COOPAI.API.DTOs.DebtSegmentation;

public sealed record InstallmentMasterAutoSyncStatusDto(
    bool Enabled,
    string Status,
    int PollSeconds,
    DateTime? LastCheckedAtUtc,
    DateTime? LastSyncAttemptAtUtc,
    DateTime? LastSuccessfulSyncAtUtc,
    string Message,
    string SourceType,
    string SourceFileName,
    string? SourceFileHash,
    string? SnapshotId,
    int ContractCount,
    int ValidContractCount,
    int InvalidContractCount)
{
    public string SchemaMode { get; init; } = InstallmentMasterSchemaModes.Unknown;
}

public sealed record InstallmentMasterSyncStatusDto(
    DateTime AttemptedAtUtc,
    string Trigger,
    string Status,
    bool Published,
    bool PreviousSnapshotPreserved,
    string Message,
    string SourceFileName,
    string? SourceFileHash,
    string? SnapshotId,
    int SourceRows,
    int ContractCount,
    int ValidContractCount,
    int InvalidContractCount,
    int DuplicateContractNumberGroups,
    int InvalidTotalInstallmentRows,
    int InvalidMonthlyInstallmentRows,
    long DurationMilliseconds,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public string SourceType { get; init; } = PreviewSyncSourceTypes.InstallmentMaster;
    public string SchemaMode { get; init; } = InstallmentMasterSchemaModes.Unknown;
}

public sealed record InstallmentMasterSnapshotDto(
    bool Available,
    string Message,
    string? SnapshotId,
    string? SourceFileName,
    string? SourceFileHash,
    DateTime? SourceLastWriteTimeUtc,
    DateTime? PublishedAtUtc,
    int SourceRows,
    int ContractCount,
    int ValidContractCount,
    int InvalidContractCount,
    int DuplicateContractNumberGroups,
    int InvalidTotalInstallmentRows,
    int InvalidMonthlyInstallmentRows,
    IReadOnlyList<string> Warnings)
{
    public string SchemaMode { get; init; } = InstallmentMasterSchemaModes.Unknown;
}

public sealed record MonthlyCollectionSummaryDto(
    int ContractCount,
    decimal AmountDue,
    decimal ActualPayment,
    decimal Shortfall,
    decimal ExcessPayment,
    decimal AdvancePayment,
    int UnknownAmountDueContracts,
    decimal UnknownAmountDueOutstanding,
    int FinalInstallmentContracts);

public sealed record PaymentAllocationSummaryDto(
    decimal ActualPayment,
    int DownPaymentContracts,
    decimal DownPaymentAmount,
    int AdvancePaymentContracts,
    decimal AdvancePaymentAmount,
    int PriorArrearsPaymentContracts,
    decimal PaymentToPriorArrears,
    int CurrentDuePaymentContracts,
    decimal PaymentToCurrentDue,
    int TrueExcessContracts,
    decimal TrueExcessPayment,
    int EarlyPayoffContracts,
    decimal EarlyPayoffAmount,
    int ExpiredPayoffContracts,
    decimal ExpiredPayoffAmount,
    int UnclassifiedPaymentContracts,
    decimal UnclassifiedPaymentAmount,
    int KnownPriorArrearsContracts,
    decimal KnownPriorArrearsAmount,
    int UnknownPriorArrearsContracts,
    int PriorCreditPolicyRequiredContracts,
    decimal PriorCreditPolicyResidualAmount,
    int SourceBDownPaymentMatchedContracts,
    int DebtContracts,
    decimal SourceBDownPaymentCoveragePercent,
    decimal AllocatedPaymentAmount,
    decimal ReconciliationDifference,
    bool IsTrueExcessProvisional,
    string PaymentAllocationVersion,
    int PriorAdvanceCreditContracts,
    decimal PriorAdvanceCreditAmount,
    int AdvanceCreditAppliedContracts,
    decimal AdvanceCreditAppliedAmount,
    int NewAdvanceCreditContracts,
    decimal NewAdvanceCreditAmount,
    int EndingAdvanceCreditContracts,
    decimal EndingAdvanceCreditAmount,
    decimal LedgerReconciliationDifference,
    string TrueExcessTreatment);

public sealed record MonthlyCollectionDashboardDto(
    bool Available,
    string Message,
    string? AnalysisId,
    string AnalysisVersion,
    string? DebtSnapshotId,
    string? InstallmentSnapshotId,
    DateOnly CurrentPeriod,
    MonthlyCollectionSummaryDto? Totals,
    IReadOnlyList<MonthlyCollectionBreakdown> Breakdowns,
    MonthlyCollectionDataQuality? DataQuality,
    DateTime? GeneratedAtUtc)
{
    public PaymentAllocationSummaryDto? PaymentAllocation { get; init; }
}

public sealed record MonthlyAmountDueContractDto(
    string ContractNumber,
    string DebtBucket,
    string ContractStatus,
    DateOnly CurrentPeriod,
    DateOnly? FirstDuePeriod,
    DateOnly? ContractStartDate,
    DateOnly? ContractExpireDate,
    int? RemainingOrOverdueMonths,
    decimal OpeningOutstanding,
    decimal ActualPayment,
    decimal EndingOutstanding,
    int CurrentInstallmentNumber,
    int? TotalInstallments,
    decimal? MonthlyInstallment,
    decimal? AmountDue,
    decimal? Shortfall,
    decimal ExcessPayment,
    decimal AdvancePayment,
    string DueBasis,
    string InstallmentDataStatus,
    IReadOnlyList<string> Warnings)
{
    public decimal? ContractualObligation { get; init; }
    public decimal? FinalInstallment { get; init; }
    public decimal? DownPaymentSourceAmount { get; init; }
    public string DownPaymentDataStatus { get; init; } = DownPaymentDataStatuses.Missing;
    public decimal DownPaymentAmount { get; init; }
    public decimal? PriorArrears { get; init; }
    public string PriorArrearsStatus { get; init; } = PriorArrearsStatuses.NotApplicable;
    public decimal PaymentToPriorArrears { get; init; }
    public decimal PaymentToCurrentDue { get; init; }
    public bool IsEarlyPayoff { get; init; }
    public decimal EarlyPayoffAmount { get; init; }
    public bool IsExpiredPayoff { get; init; }
    public decimal ExpiredPayoffAmount { get; init; }
    public decimal UnclassifiedPaymentAmount { get; init; }
    public string PaymentAllocationStatus { get; init; } = PaymentAllocationStatuses.Unclassified;
    public string? PaymentAllocationWarning { get; init; }
    public DateOnly? PriorCreditPolicyPeriod { get; init; }
    public decimal? PriorCreditPolicyObligation { get; init; }
    public decimal? PriorCreditPolicyActualPayment { get; init; }
    public decimal PriorCreditPolicyResidualAmount { get; init; }
    public decimal PriorAdvanceCredit { get; init; }
    public decimal AdvanceCreditAppliedToPriorArrears { get; init; }
    public decimal AdvanceCreditAppliedToCurrentDue { get; init; }
    public decimal AdvanceCreditApplied { get; init; }
    public decimal? RemainingCurrentDueForCash { get; init; }
    public decimal NewAdvanceCredit { get; init; }
    public decimal EndingAdvanceCredit { get; init; }
    public decimal? EndingArrears { get; init; }
}

public sealed record MonthlyAmountDueContractPageDto(
    bool Available,
    string Message,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<MonthlyAmountDueContractDto> Items);
