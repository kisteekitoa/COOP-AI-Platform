namespace COOPAI.API.Services.DebtSegmentation;

public sealed class DebtSegmentationPreviewOptions
{
    public const string SectionName = "DebtSegmentationPreview";

    public bool Enabled { get; set; }
    public bool AutoSyncOnQueryEnabled { get; set; } = true;
    public bool AutoSyncEnabled { get; set; }
    public int AutoSyncPollSeconds { get; set; } = 60;
    public int AutoSyncFailureRetrySeconds { get; set; } = 300;
    public int AutoSyncProbeTimeoutSeconds { get; set; } = 10;
    public string WorkbookPath { get; set; } = string.Empty;
    public string HistoryPath { get; set; } = string.Empty;
    public string StagingPath { get; set; } = string.Empty;
    public string DefaultCurrentPeriod { get; set; } = "2026-07";
    public int StabilizationDelayMilliseconds { get; set; }
    public int NetworkStabilizationDelayMilliseconds { get; set; } = 2000;
    public int MinimumFileAgeSeconds { get; set; }
}
