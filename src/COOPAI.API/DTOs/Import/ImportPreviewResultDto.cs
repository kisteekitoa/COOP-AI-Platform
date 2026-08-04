namespace COOPAI.API.DTOs.Import;

public class ImportPreviewResultDto
{
    public bool Success { get; set; }

    public int TotalRows { get; set; }

    public List<ImportPreviewRowDto> Rows { get; set; } = new();

    public List<string> Errors { get; set; } = new();
}