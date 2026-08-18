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

    public string FileHash { get; set; } = string.Empty;

    public int TotalRows { get; set; }

    public int ValidRows { get; set; }

    public int UpdateCandidates { get; set; }

    public int ExistingContracts { get; set; }

    public int MissingContracts { get; set; }

    public int ImportedRows { get; set; }

    public int UpdatedRows { get; set; }

    public int FailedRows { get; set; }

    public int SkippedRows { get; set; }

    public int UserSkippedErrorRows { get; set; }

    public int? BatchId { get; set; }

    public List<string> Errors { get; set; } = new();

    public List<ImportError> ErrorDetails { get; set; } = new();

    public List<ImportSkipDetail> SkipDetails { get; set; } = new();

    public List<UserSkippedErrorDetail> UserSkippedErrorDetails { get; set; } = new();

    // ชื่อคอลัมน์ทั้งหมดจาก Excel
    public List<string> Headers { get; set; } = new();

    // ตัวอย่างข้อมูล 20 แถวแรก
    public List<Dictionary<string, string>> PreviewRows { get; set; } = new();
}

public class UserSkippedErrorDetail
{
    public int RowNumber { get; set; }

    public string ContractNo { get; set; } = string.Empty;

    public string MemberNo { get; set; } = string.Empty;

    public string MemberName { get; set; } = string.Empty;

    public string SkipReason { get; set; } = "UserApprovedValidationError";

    public List<ImportError> OriginalErrors { get; set; } = new();
}

public class ImportSkipDetail
{
    public int RowNumber { get; set; }

    public string ContractNo { get; set; } = string.Empty;

    public string MemberName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
