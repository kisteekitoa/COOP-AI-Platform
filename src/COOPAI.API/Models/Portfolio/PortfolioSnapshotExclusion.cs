using COOPAI.API.Models;

namespace COOPAI.API.Models.Portfolio;

public sealed class PortfolioSnapshotExclusion
{
    public long Id { get; set; }
    public int PortfolioSnapshotId { get; set; }
    public int LoanContractId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;

    public PortfolioSnapshot? PortfolioSnapshot { get; set; }
    public LoanContract? LoanContract { get; set; }
}
