namespace COOPAI.API.DTOs.Dashboard;

public static class DashboardDataModes
{
    public const string Current = "CURRENT";
    public const string Published = "PUBLISHED";
}

public sealed class DashboardSummaryDto
{
    public string Mode { get; set; } = DashboardDataModes.Current;
    public bool Available { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool HasPublishedSnapshot { get; set; }
    public string? DataSource { get; set; }
    public int? SnapshotId { get; set; }
    public DateOnly? AsOfDate { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime GeneratedAt { get; set; }
    public bool? IsReconciled { get; set; }
    public int? TotalSourceRows { get; set; }
    public int? TotalContracts { get; set; }
    public int? TemplatePlaceholderRows { get; set; }
    public int? InTermContracts { get; set; }
    public int? ExpiredContracts { get; set; }
    public int? OutstandingContracts { get; set; }
    public int? PaidOffContracts { get; set; }
    public int? InTermOutstandingContracts { get; set; }
    public int? InTermPaidOffContracts { get; set; }
    public int? ExpiredOutstandingContracts { get; set; }
    public int? ExpiredPaidOffContracts { get; set; }
    public int? WarningContracts { get; set; }
    public int? UnresolvedMemberContracts { get; set; }
    public int? ShadowExcludedContracts { get; set; }
    public int? CanonicalMatchedContracts { get; set; }
    public int? CanonicalMissingContracts { get; set; }
    public decimal? PrincipalOutstanding { get; set; }
    public decimal? ProfitOutstanding { get; set; }
    public decimal? TotalOutstanding { get; set; }
    public decimal? ExpiredOutstandingBalance { get; set; }
    public int? PublishedRecordCount { get; set; }
    public DateOnly? DataThroughPeriod { get; set; }
    public string? DebtSnapshotId { get; set; }
    public string? SourceAFingerprint { get; set; }
    public DateTime? SourceASnapshotPublishedAt { get; set; }
    public string? InstallmentSnapshotId { get; set; }
    public string? SourceBFingerprint { get; set; }
    public DateTime? SourceBSnapshotPublishedAt { get; set; }
    public DateTime? DebtLastSuccessfulSyncAt { get; set; }
    public DateTime? InstallmentLastSuccessfulSyncAt { get; set; }
    public bool IsStale { get; set; }
    public string? FreshnessWarning { get; set; }
    public decimal? AmountDue { get; set; }
    public decimal? ActualPayment { get; set; }
    public int? DownPaymentContracts { get; set; }
    public decimal? DownPaymentAmount { get; set; }
    public decimal? OpeningOutstanding { get; set; }
    public decimal? IncreaseDuringPeriod { get; set; }
    public decimal? EndingOutstanding { get; set; }
    public DashboardWorkQueueSummaryDto? WorkQueue { get; set; }
    public List<DashboardDebtBucketDto> DebtBuckets { get; set; } = [];
    public List<DashboardContractTypeDto> ContractTypes { get; set; } = [];
}

public sealed class DashboardWorkQueueSummaryDto
{
    public int AnalysisUniverseContracts { get; set; }
    public int TotalQueueContracts { get; set; }
    public int CollectionContracts { get; set; }
    public int ReviewOnlyContracts { get; set; }
    public int UrgentContracts { get; set; }
    public int HighContracts { get; set; }
    public int MediumContracts { get; set; }
    public int ReviewContracts { get; set; }
}

public sealed class DashboardDebtBucketDto
{
    public string Bucket { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int ContractCount { get; set; }
    public decimal OutstandingAmount { get; set; }
}

public sealed class DashboardContractTypeDto
{
    public string Prefix { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ContractCount { get; set; }
    public int OutstandingContractCount { get; set; }
    public int PaidOffContractCount { get; set; }
    public int InTermCount { get; set; }
    public int ExpiredCount { get; set; }
    public decimal PrincipalOutstanding { get; set; }
    public decimal ProfitOutstanding { get; set; }
    public decimal TotalOutstanding { get; set; }
}
