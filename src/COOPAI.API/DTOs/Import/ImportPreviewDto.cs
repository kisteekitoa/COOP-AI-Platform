namespace COOPAI.API.DTOs.Import;

public class ImportPreviewRowDto
{
    public int RowNumber { get; set; }

    public Dictionary<string, string> Values { get; set; } = new();
}