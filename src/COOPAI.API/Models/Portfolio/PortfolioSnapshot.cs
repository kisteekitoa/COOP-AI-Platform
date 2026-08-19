namespace COOPAI.API.Models.Portfolio;

public sealed class PortfolioSnapshot
{
    public int Id { get; set; }
    public DateOnly AsOfDate { get; set; }
    public int Revision { get; set; } = 1;
    public PortfolioSnapshotStatus Status { get; set; } = PortfolioSnapshotStatus.Draft;
    public int DefinitionVersion { get; set; } = 2;
    public string CurrencyCode { get; set; } = "THB";
    public string SourceType { get; set; } = PortfolioSnapshotCodes.ExcelSourceType;
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceFileHash { get; set; } = string.Empty;
    public long SourceFileSizeBytes { get; set; }
    public DateTime SourceRetrievedAt { get; set; }
    public DateOnly? SourceDataThroughDate { get; set; }
    public string SnapshotContentHash { get; set; } = string.Empty;

    public int TotalSourceRows { get; set; }
    public int TotalContractCount { get; set; }
    public int PlaceholderRowCount { get; set; }
    public int MatchedCanonicalCount { get; set; }
    public int MissingCanonicalCount { get; set; }
    public int UnresolvedMemberContractCount { get; set; }
    public int WarningRecordCount { get; set; }
    public int BlockingErrorCount { get; set; }
    public int ShadowExcludedCount { get; set; }
    public int InTermContractCount { get; set; }
    public int ExpiredContractCount { get; set; }
    public int OutstandingContractCount { get; set; }
    public int PaidOffContractCount { get; set; }
    public int InTermOutstandingContractCount { get; set; }
    public int InTermPaidOffContractCount { get; set; }
    public int ExpiredOutstandingContractCount { get; set; }
    public int ExpiredPaidOffContractCount { get; set; }
    public decimal ExpiredOutstandingTotal { get; set; }

    public decimal PrincipalOpening { get; set; }
    public decimal ProfitOpening { get; set; }
    public decimal TotalOpening { get; set; }
    public decimal PrincipalRepayment { get; set; }
    public decimal ProfitRepayment { get; set; }
    public decimal TotalRepayment { get; set; }
    public decimal PrincipalOutstanding { get; set; }
    public decimal ProfitOutstanding { get; set; }
    public decimal TotalOutstanding { get; set; }
    public decimal PrincipalDifference { get; set; }
    public decimal ProfitDifference { get; set; }
    public decimal TotalDifference { get; set; }
    public decimal ComponentDifference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ValidatedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }

    public ICollection<PortfolioSnapshotRecord> Records { get; set; } = new List<PortfolioSnapshotRecord>();
    public ICollection<PortfolioSnapshotExclusion> Exclusions { get; set; } = new List<PortfolioSnapshotExclusion>();
}
