// ==========================================================
// COOP-AI
// File        : ImportError.cs
// Module      : Import Error Reporting
// Version     : 0.1.000
// Description : Structured import validation error details
// ==========================================================

namespace COOPAI.API.Services.Import;

public class ImportError
{
    public int RowNumber { get; set; }

    public string ContractNo { get; set; } = string.Empty;

    public string MemberNo { get; set; } = string.Empty;

    public string MemberName { get; set; } = string.Empty;

    public string FieldName { get; set; } = string.Empty;

    public string ErrorType { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public string RawValue { get; set; } = string.Empty;

    public string ParsedValue { get; set; } = string.Empty;

    public bool CanSkip { get; set; }

    public string SuggestedAction { get; set; } = string.Empty;

    public bool WasUserSkipped { get; set; }
}
