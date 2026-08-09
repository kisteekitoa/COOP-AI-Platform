// ==========================================================
// COOP-AI
// File        : ImportLoanRecord.cs
// Module      : Import
// Version     : 0.2.100
// Description : Temporary staging table for imported loan data
// ==========================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COOPAI.API.Models.Import;

public class ImportLoanRecord
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Import Batch
    /// </summary>
    public Guid BatchId { get; set; }

    /// <summary>
    /// Excel Row Number
    /// </summary>
    public int RowNo { get; set; }

    // ===============================
    // Member
    // ===============================

    [MaxLength(30)]
    public string MemberNo { get; set; } = string.Empty;

    [MaxLength(200)]
    public string MemberName { get; set; } = string.Empty;

    // ===============================
    // Contract
    // ===============================

    [MaxLength(30)]
    public string ContractNo { get; set; } = string.Empty;

    public DateTime? ContractDate { get; set; }

    public DateTime? ExpireDate { get; set; }

    // ===============================
    // Financial
    // ===============================

    [Column(TypeName = "decimal(18,2)")]
    public decimal LoanAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PrincipalBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ProfitBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalBalance { get; set; }

    // ===============================
    // Collection
    // ===============================

    public int OverdueDays { get; set; }

    // ===============================
    // Validation
    // ===============================

    public bool IsValid { get; set; }

    [MaxLength(500)]
    public string ErrorMessage { get; set; } = string.Empty;

    // ===============================
    // Audit
    // ===============================

    public DateTime ImportedAt { get; set; } = DateTime.Now;
}