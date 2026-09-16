namespace COOPAI.API.Services.DebtSegmentation;

public sealed class InstallmentMasterOptions
{
    public const string SectionName = "InstallmentMasterPreview";

    public bool Enabled { get; set; }
    public bool AutoSyncEnabled { get; set; }
    public int AutoSyncPollSeconds { get; set; } = 60;
    public int AutoSyncFailureRetrySeconds { get; set; } = 300;
    public int AutoSyncProbeTimeoutSeconds { get; set; } = 10;
    public string WorkbookPath { get; set; } = string.Empty;
    public string WorksheetName { get; set; } = "2534-2569";
    public string HistoryPath { get; set; } = string.Empty;
    public string StagingPath { get; set; } = string.Empty;
    public int StabilizationDelayMilliseconds { get; set; } = 750;
    public int NetworkStabilizationDelayMilliseconds { get; set; } = 2000;
    public int MinimumFileAgeSeconds { get; set; } = 2;
}
