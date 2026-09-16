namespace COOPAI.API.Services.DebtSegmentation;

public static class PreviewSyncSourceTypes
{
    public const string Debt = "Debt";
    public const string InstallmentMaster = "InstallmentMaster";
}

public static class InstallmentValidationStatuses
{
    public const string Usable = "Usable";
    public const string InvalidContractualObligation = "InvalidContractualObligation";
    public const string InvalidTotalInstallments = "InvalidTotalInstallments";
    public const string InvalidMonthlyInstallment = "InvalidMonthlyInstallment";
    public const string InvalidTotalAndMonthlyInstallment = "InvalidTotalAndMonthlyInstallment";
    public const string ConflictingDuplicate = "ConflictingDuplicate";
}

public static class InstallmentDuplicateStatuses
{
    public const string None = "None";
    public const string Identical = "Identical";
    public const string ConflictingTotalInstallments = "ConflictingTotalInstallments";
    public const string ConflictingMonthlyInstallment = "ConflictingMonthlyInstallment";
    public const string ConflictingBoth = "ConflictingBoth";
    public const string ConflictingContractualObligation = "ConflictingContractualObligation";
}

public static class DownPaymentDataStatuses
{
    public const string Available = "Available";
    public const string Missing = "Missing";
    public const string Invalid = "Invalid";
    public const string ConflictingDuplicate = "ConflictingDuplicate";
}

public static class InstallmentMasterSchemaModes
{
    public const string Headered = "HEADERED";
    public const string HeaderlessPositional = "HEADERLESS_POSITIONAL";
    public const string Unknown = "UNKNOWN";
}

public sealed class InstallmentMasterSchemaValidationException(
    string schemaMode,
    string message) : InvalidOperationException(message)
{
    public string SchemaMode { get; } = schemaMode;
}

public sealed record InstallmentMasterContract(
    string ContractNumber,
    int? TotalInstallments,
    decimal? MonthlyInstallment,
    string ValidationStatus,
    string DuplicateStatus,
    IReadOnlyList<string> ValidationWarnings,
    int SourceRowNumber)
{
    public string ContractKey => ContractNumber.Trim().ToUpperInvariant();
    public bool IsUsable => ValidationStatus == InstallmentValidationStatuses.Usable &&
        DuplicateStatus is InstallmentDuplicateStatuses.None or InstallmentDuplicateStatuses.Identical;
    public decimal? DownPayment { get; init; }
    public string DownPaymentDataStatus { get; init; } = DownPaymentDataStatuses.Missing;
    public decimal? ContractualObligation { get; init; }
}

public sealed record InstallmentMasterReadResult(
    string SourceFileName,
    string WorksheetName,
    int SourceRowCount,
    int ContractCount,
    int ValidContractCount,
    int InvalidContractCount,
    int DuplicateContractNumberGroups,
    int InvalidTotalInstallmentRows,
    int InvalidMonthlyInstallmentRows,
    IReadOnlyList<InstallmentMasterContract> Contracts,
    IReadOnlyList<string> Warnings)
{
    public string SchemaMode { get; init; } = InstallmentMasterSchemaModes.Headered;
}

public sealed record PublishedInstallmentMasterSnapshot(
    string SnapshotId,
    string AnalysisVersion,
    string SourceFileName,
    string SourcePath,
    string SourceFileHash,
    long SourceFileSizeBytes,
    DateTime SourceLastWriteTimeUtc,
    DateTime PublishedAtUtc,
    InstallmentMasterReadResult Workbook)
{
    public string SourceType { get; init; } = DebtSourceTypes.Local;
}

public sealed record InstallmentMasterSyncHistoryEntry(
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

public sealed record InstallmentMasterSyncResult(
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

public static class MonthlyAmountDueVersion
{
    public const string V1 = "monthly-amount-due-v1";
    public const string PaymentAllocationV1 = "payment-allocation-v1";
    public const string PaymentAllocationV11 = "payment-allocation-v1.1";
    public const string InstallmentMasterV2 = "installment-master-v2";
    public const string InstallmentMasterV3 = "installment-master-v3";
}

public static class PriorArrearsStatuses
{
    public const string Known = "Known";
    public const string NotApplicable = "NotApplicable";
    public const string UnknownMissingFirstDue = "UnknownMissingFirstDue";
    public const string UnknownHistoryBeforeAvailablePeriod = "UnknownHistoryBeforeAvailablePeriod";
    public const string UnknownMissingMonthlyFacts = "UnknownMissingMonthlyFacts";
    public const string UnknownHistoricalObligation = "UnknownHistoricalObligation";
    public const string PriorCreditPolicyRequired = "PriorCreditPolicyRequired";
}

public static class PaymentAllocationStatuses
{
    public const string Allocated = "Allocated";
    public const string EarlyPayoff = "EarlyPayoff";
    public const string ExpiredPayoff = "ExpiredPayoff";
    public const string UnresolvedDownPaymentEvidence = "UnresolvedDownPaymentEvidence";
    public const string Unclassified = "Unclassified";
}

public static class AmountDueBases
{
    public const string PaidOff = "PaidOff";
    public const string NotDue = "NotDue";
    public const string ActiveInstallment = "ActiveInstallment";
    public const string FinalInstallment = "FinalInstallment";
    public const string ExpiredFullBalance = "ExpiredFullBalance";
    public const string InstallmentsElapsedFullBalance = "InstallmentsElapsedFullBalance";
    public const string MissingInstallmentData = "MissingInstallmentData";
}

public static class InstallmentDataStatuses
{
    public const string Available = "Available";
    public const string NotRequired = "NotRequired";
    public const string Missing = "Missing";
    public const string Invalid = "Invalid";
    public const string ConflictingDuplicate = "ConflictingDuplicate";
}

public static class RemainingOrOverdueFilters
{
    public const string OverdueAll = "overdue-all";
    public const string OverdueTenYearsOrMore = "overdue-10-years-or-more";
    public const string OverdueFiveToTenYears = "overdue-5-to-10-years";
    public const string OverdueThreeToFiveYears = "overdue-3-to-5-years";
    public const string OverdueOneToThreeYears = "overdue-1-to-3-years";
    public const string OverdueUpToOneYear = "overdue-up-to-1-year";
    public const string DueThisMonth = "due-this-month";
    public const string RemainingOneToSixMonths = "remaining-1-to-6-months";
    public const string RemainingSevenToTwelveMonths = "remaining-7-to-12-months";
    public const string RemainingOneToThreeYears = "remaining-1-to-3-years";
    public const string RemainingMoreThanThreeYears = "remaining-more-than-3-years";
    public const string NotDue = "not-due";
    public const string PaidOff = "paid-off";
}

public sealed record MonthlyAmountDueContract(
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

public sealed record MonthlyCollectionBreakdown(
    string Category,
    int ContractCount,
    decimal OpeningOutstanding,
    decimal AmountDue,
    decimal ActualPayment,
    decimal Shortfall,
    decimal ExcessPayment,
    decimal AdvancePayment);

public sealed record MonthlyCollectionDataQuality(
    int DebtContracts,
    int InstallmentMasterContracts,
    int MatchedContracts,
    int UnmatchedContracts,
    decimal MatchCoveragePercent,
    int DuplicateContractNumberGroups,
    int InvalidTotalInstallmentRows,
    int InvalidMonthlyInstallmentRows,
    int UsableInstallmentSchedules,
    int CurrentDueContractsMissingInstallmentData,
    int ScheduleInconsistencies,
    int InstallmentsElapsedContracts,
    int ActiveContractsMissingInstallmentRecord);

public sealed record MonthlyAmountDueAnalysis(
    string AnalysisId,
    string AnalysisVersion,
    string DebtSnapshotId,
    string? InstallmentSnapshotId,
    DateOnly CurrentPeriod,
    IReadOnlyList<MonthlyAmountDueContract> Contracts,
    IReadOnlyList<MonthlyCollectionBreakdown> Breakdowns,
    MonthlyCollectionDataQuality DataQuality,
    DateTime GeneratedAtUtc);
