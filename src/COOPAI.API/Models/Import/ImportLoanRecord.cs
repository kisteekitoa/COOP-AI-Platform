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

    [NotMapped]
    public string LoanAmountRaw { get; set; } = string.Empty;

    [NotMapped]
    public decimal? LoanAmountParsedValue { get; set; }

    [NotMapped]
    public string LoanAmountParseStatus { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PrincipalBalance { get; set; }

    [NotMapped]
    public string PrincipalBalanceRaw { get; set; } = string.Empty;

    [NotMapped]
    public decimal? PrincipalBalanceParsedValue { get; set; }

    [NotMapped]
    public string PrincipalBalanceParseStatus { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal ProfitBalance { get; set; }

    [NotMapped]
    public string ProfitBalanceRaw { get; set; } = string.Empty;

    [NotMapped]
    public decimal? ProfitBalanceParsedValue { get; set; }

    [NotMapped]
    public string ProfitBalanceParseStatus { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalBalance { get; set; }

    [NotMapped]
    public string TotalBalanceRaw { get; set; } = string.Empty;

    [NotMapped]
    public decimal? TotalBalanceParsedValue { get; set; }

    [NotMapped]
    public string TotalBalanceParseStatus { get; set; } = string.Empty;

    [NotMapped]
    public decimal? PreviousPrincipal { get; set; }

    [NotMapped]
    public string PreviousPrincipalRaw { get; set; } = string.Empty;

    [NotMapped]
    public decimal? PreviousPrincipalParsedValue { get; set; }

    [NotMapped]
    public string PreviousPrincipalParseStatus { get; set; } = string.Empty;

    [NotMapped]
    public decimal? PreviousProfit { get; set; }

    [NotMapped]
    public string PreviousProfitRaw { get; set; } = string.Empty;

    [NotMapped]
    public decimal? PreviousProfitParsedValue { get; set; }

    [NotMapped]
    public string PreviousProfitParseStatus { get; set; } = string.Empty;

    [NotMapped]
    public decimal? PreviousProfitUnderlyingNumericValue { get; set; }

    [NotMapped]
    public string PreviousProfitNumberFormat { get; set; } = string.Empty;

    [NotMapped]
    public string PreviousProfitBusinessStatus { get; set; } = string.Empty;

    [NotMapped]
    public decimal? PreviousTotal { get; set; }

    [NotMapped]
    public string PreviousTotalRaw { get; set; } = string.Empty;

    [NotMapped]
    public decimal? PreviousTotalParsedValue { get; set; }

    [NotMapped]
    public string PreviousTotalParseStatus { get; set; } = string.Empty;

    [NotMapped]
    public decimal? CurrentPrincipal { get; set; }

    [NotMapped]
    public string CurrentPrincipalRaw { get; set; } = string.Empty;

    [NotMapped]
    public decimal? CurrentPrincipalParsedValue { get; set; }

    [NotMapped]
    public string CurrentPrincipalParseStatus { get; set; } = string.Empty;

    [NotMapped]
    public decimal? CurrentProfit { get; set; }

    [NotMapped]
    public string CurrentProfitRaw { get; set; } = string.Empty;

    [NotMapped]
    public decimal? CurrentProfitParsedValue { get; set; }

    [NotMapped]
    public string CurrentProfitParseStatus { get; set; } = string.Empty;

    [NotMapped]
    public decimal? CurrentTotal { get; set; }

    [NotMapped]
    public string CurrentTotalRaw { get; set; } = string.Empty;

    [NotMapped]
    public decimal? CurrentTotalParsedValue { get; set; }

    [NotMapped]
    public string CurrentTotalParseStatus { get; set; } = string.Empty;

    // ===============================
    // Collection
    // ===============================

    public int OverdueDays { get; set; }

    [NotMapped]
    public string OverdueDaysRaw { get; set; } = string.Empty;

    [NotMapped]
    public int? OverdueDaysParsedValue { get; set; }

    [NotMapped]
    public string OverdueDaysParseStatus { get; set; } = string.Empty;

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
