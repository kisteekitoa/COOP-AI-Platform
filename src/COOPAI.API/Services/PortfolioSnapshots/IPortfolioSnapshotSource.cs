namespace COOPAI.API.Services.PortfolioSnapshots;

public interface IPortfolioSnapshotSource
{
    Task<PortfolioSourceReadResult> ReadAsync(
        string controlledCopyPath,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default);
}
