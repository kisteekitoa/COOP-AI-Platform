using COOPAI.API.Models.Portfolio;

namespace COOPAI.API.Services.PortfolioSnapshots;

public sealed class PortfolioSnapshotDryRunResult
{
    public required PortfolioSourceReadResult Source { get; init; }
    public required PortfolioSnapshot Snapshot { get; init; }
    public required IReadOnlyList<PortfolioValidationIssue> Issues { get; init; }
    public bool IsValid => Issues.All(x => x.Severity != PortfolioValidationSeverity.Blocking);
}
