// ==========================================================
// COOP-AI
// File        : WorksheetScanResult.cs
// ==========================================================

namespace COOPAI.API.Services.Import;

public class WorksheetScanResult
{
    public int TotalRows { get; set; }

    public int TotalColumns { get; set; }

    public List<List<string>> Rows { get; set; } = new();
}