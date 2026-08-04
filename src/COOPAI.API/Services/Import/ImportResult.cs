// ==========================================================
// COOP-AI
// File        : ImportResult.cs
// Module      : Loan Import
// Version     : 0.2.000
// ==========================================================

namespace COOPAI.API.Services.Import;

public class ImportResult
{
    public bool Success { get; set; }

    public int TotalRows { get; set; }

    public int ImportedRows { get; set; }

    public int UpdatedRows { get; set; }

    public int FailedRows { get; set; }

    public List<string> Errors { get; set; } = new();

    // ชื่อคอลัมน์ทั้งหมดจาก Excel
    public List<string> Headers { get; set; } = new();

    // ตัวอย่างข้อมูล 20 แถวแรก
    public List<Dictionary<string, string>> PreviewRows { get; set; } = new();
}