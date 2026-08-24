namespace COOPAI.API.Services.PortfolioSnapshots;

public sealed class PortfolioSnapshotOptions
{
    public const string SectionName = "PortfolioSnapshots";

    public bool PublishingEnabled { get; set; } = false;
}
