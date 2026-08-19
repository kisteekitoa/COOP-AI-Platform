namespace COOPAI.API.Models.Portfolio;

public enum PortfolioSnapshotStatus
{
    Draft,
    Validated,
    Failed,
    Rejected,
    Published,
    Superseded
}

public enum PortfolioSnapshotInclusionStatus
{
    Included,
    IncludedWithWarning
}

public enum PortfolioSourceRowKind
{
    Unknown,
    Contract,
    TemplatePlaceholder
}

public enum PortfolioOpeningSide
{
    Unknown,
    Previous,
    Current
}

public enum PortfolioTermStatus
{
    Unknown,
    InTerm,
    Expired
}

public enum PortfolioBalanceStatus
{
    Unknown,
    Outstanding,
    PaidOff
}

public enum PortfolioCanonicalMatchStatus
{
    Matched,
    Missing
}

public enum PortfolioMemberMatchStatus
{
    Matched,
    Missing,
    Ambiguous
}

public static class PortfolioSnapshotCodes
{
    public const string ExcelSourceType = "Excel";
    public const string MalformedShadowContractNo = "MalformedShadowContractNo";
    public const string UnsupportedContractType = "UnsupportedContractType";
    public const string Negative = "Negative";
    public const string TotalBalanceMismatch = "TotalBalanceMismatch";
    public const string MissingCanonicalContract = "MissingCanonicalContract";
    public const string MissingMemberMatch = "MissingMemberMatch";
}
