namespace COOPAI.API.Security;

public sealed class ImportUploadOptions
{
    public const string SectionName = "ImportUpload";
    public const long DefaultMaxUploadBytes = 25 * 1024 * 1024;

    public string? TempDirectory { get; set; }

    public long MaxUploadBytes { get; set; } = DefaultMaxUploadBytes;

    public int RetentionHours { get; set; } = 24;

    public string[] AllowedExtensions { get; set; } = [".xlsx", ".xls"];
}
