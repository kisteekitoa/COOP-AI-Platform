using COOPAI.API.Models.Portfolio;

namespace COOPAI.API.Services.PortfolioSnapshots;

public enum PortfolioValidationSeverity
{
    Warning,
    Blocking
}

public sealed record PortfolioValidationIssue(
    string Code,
    string Message,
    int? SourceRowNumber = null,
    PortfolioValidationSeverity Severity = PortfolioValidationSeverity.Blocking);

public sealed record PortfolioFinancialValues(
    decimal Principal,
    decimal Profit,
    decimal Total);

public sealed class PortfolioSourceRow
{
    public int SourceRowNumber { get; init; }
    public string SourceRecordKey { get; init; } = string.Empty;
    public string MemberNo { get; init; } = string.Empty;
    public string ContractNo { get; init; } = string.Empty;
    public DateOnly? ContractDate { get; init; }
    public DateOnly? ExpireDate { get; init; }
    public PortfolioSourceRowKind SourceRowKind { get; init; }
    public PortfolioOpeningSide OpeningSide { get; init; }
    public bool HasValidOpeningValues { get; init; }
    public bool HasValidRepaymentValues { get; init; }
    public bool HasValidOutstandingValues { get; init; }
    public PortfolioFinancialValues Opening { get; init; } = new(0m, 0m, 0m);
    public PortfolioFinancialValues Repayment { get; init; } = new(0m, 0m, 0m);
    public PortfolioFinancialValues Outstanding { get; init; } = new(0m, 0m, 0m);
    public PortfolioFinancialValues? DisplayedOpening { get; init; }
}

public sealed record PortfolioReportSummary(
    PortfolioFinancialValues Opening,
    PortfolioFinancialValues Repayment,
    PortfolioFinancialValues Outstanding);

public static class PortfolioContractStatusClassifier
{
    public static PortfolioTermStatus ClassifyTerm(DateOnly? expireDate, DateOnly asOfDate) =>
        expireDate switch
        {
            null => PortfolioTermStatus.Unknown,
            { } value when value < asOfDate => PortfolioTermStatus.Expired,
            _ => PortfolioTermStatus.InTerm
        };

    public static PortfolioBalanceStatus ClassifyBalance(decimal? outstandingTotal) =>
        outstandingTotal switch
        {
            null => PortfolioBalanceStatus.Unknown,
            > 0m => PortfolioBalanceStatus.Outstanding,
            0m => PortfolioBalanceStatus.PaidOff,
            _ => PortfolioBalanceStatus.Unknown
        };
}

public sealed class PortfolioSourceReadResult
{
    public DateOnly AsOfDate { get; init; }
    public string SourceFileName { get; init; } = string.Empty;
    public string SourceFileHash { get; init; } = string.Empty;
    public long SourceFileSizeBytes { get; init; }
    public DateTime SourceRetrievedAt { get; init; }
    public DateOnly SourceDataThroughDate { get; init; }
    public IReadOnlyList<PortfolioSourceRow> Rows { get; init; } = Array.Empty<PortfolioSourceRow>();
    public PortfolioReportSummary? ReportSummary { get; init; }
    public IReadOnlyList<PortfolioValidationIssue> Issues { get; init; } = Array.Empty<PortfolioValidationIssue>();
}
