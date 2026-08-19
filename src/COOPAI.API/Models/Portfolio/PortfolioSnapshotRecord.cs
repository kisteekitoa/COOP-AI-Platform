using COOPAI.API.Models;

namespace COOPAI.API.Models.Portfolio;

public sealed class PortfolioSnapshotRecord
{
    public long Id { get; set; }
    public int PortfolioSnapshotId { get; set; }
    public int SourceRowNumber { get; set; }
    public string SourceRecordKey { get; set; } = string.Empty;
    public string NormalizedContractNo { get; set; } = string.Empty;
    public DateOnly? ContractDate { get; set; }
    public DateOnly? ExpireDate { get; set; }
    public int? LoanContractId { get; set; }
    public int? MemberId { get; set; }
    public PortfolioSourceRowKind SourceRowKind { get; set; }
    public PortfolioOpeningSide OpeningSide { get; set; }
    public PortfolioTermStatus TermStatus { get; set; }
    public PortfolioBalanceStatus BalanceStatus { get; set; }
    public PortfolioCanonicalMatchStatus CanonicalMatchStatus { get; set; }
    public PortfolioMemberMatchStatus MemberMatchStatus { get; set; }
    public string LoanTypePrefix { get; set; } = string.Empty;
    public PortfolioSnapshotInclusionStatus InclusionStatus { get; set; }
    public string WarningCodesJson { get; set; } = "[]";

    public decimal PrincipalOpening { get; set; }
    public decimal ProfitOpening { get; set; }
    public decimal TotalOpening { get; set; }
    public decimal PrincipalRepayment { get; set; }
    public decimal ProfitRepayment { get; set; }
    public decimal TotalRepayment { get; set; }
    public decimal PrincipalOutstanding { get; set; }
    public decimal ProfitOutstanding { get; set; }
    public decimal TotalOutstanding { get; set; }

    public PortfolioSnapshot? PortfolioSnapshot { get; set; }
    public LoanContract? LoanContract { get; set; }
    public Member? Member { get; set; }
}
