// ==========================================================
// COOP-AI
// File        : LoanImportRow.cs
// Module      : Loan Import
// Version     : 0.1.001
// Description : One row imported from Excel
// ==========================================================

namespace COOPAI.API.Services.Import;

public class LoanImportRow
{
    public string MemberNo { get; set; } = "";

    public string FullName { get; set; } = "";

    public string ContractNo { get; set; } = "";

    public DateTime? ContractDate { get; set; }

    public DateTime? ExpireDate { get; set; }

    public decimal LoanAmount { get; set; }
    public string LoanAmountRaw { get; set; } = string.Empty;
    public decimal? LoanAmountParsedValue { get; set; }
    public string LoanAmountParseStatus { get; set; } = string.Empty;

    public decimal PrincipalBalance { get; set; }

    public string PrincipalBalanceRaw { get; set; } = string.Empty;
    public decimal? PrincipalBalanceParsedValue { get; set; }
    public string PrincipalBalanceParseStatus { get; set; } = string.Empty;

    public decimal ProfitBalance { get; set; }

    public string ProfitBalanceRaw { get; set; } = string.Empty;
    public decimal? ProfitBalanceParsedValue { get; set; }
    public string ProfitBalanceParseStatus { get; set; } = string.Empty;

    public decimal TotalBalance { get; set; }
    public string TotalBalanceRaw { get; set; } = string.Empty;
    public decimal? TotalBalanceParsedValue { get; set; }
    public string TotalBalanceParseStatus { get; set; } = string.Empty;

    public decimal? PreviousPrincipal { get; set; }
    public string PreviousPrincipalRaw { get; set; } = string.Empty;
    public decimal? PreviousPrincipalParsedValue { get; set; }
    public string PreviousPrincipalParseStatus { get; set; } = string.Empty;

    public decimal? PreviousProfit { get; set; }
    public string PreviousProfitRaw { get; set; } = string.Empty;
    public decimal? PreviousProfitParsedValue { get; set; }
    public string PreviousProfitParseStatus { get; set; } = string.Empty;
    public decimal? PreviousProfitUnderlyingNumericValue { get; set; }
    public string PreviousProfitNumberFormat { get; set; } = string.Empty;
    public string PreviousProfitBusinessStatus { get; set; } = string.Empty;

    public decimal? PreviousTotal { get; set; }
    public string PreviousTotalRaw { get; set; } = string.Empty;
    public decimal? PreviousTotalParsedValue { get; set; }
    public string PreviousTotalParseStatus { get; set; } = string.Empty;

    public decimal? CurrentPrincipal { get; set; }
    public string CurrentPrincipalRaw { get; set; } = string.Empty;
    public decimal? CurrentPrincipalParsedValue { get; set; }
    public string CurrentPrincipalParseStatus { get; set; } = string.Empty;

    public decimal? CurrentProfit { get; set; }
    public string CurrentProfitRaw { get; set; } = string.Empty;
    public decimal? CurrentProfitParsedValue { get; set; }
    public string CurrentProfitParseStatus { get; set; } = string.Empty;

    public decimal? CurrentTotal { get; set; }
    public string CurrentTotalRaw { get; set; } = string.Empty;
    public decimal? CurrentTotalParsedValue { get; set; }
    public string CurrentTotalParseStatus { get; set; } = string.Empty;

    public int OverdueDays { get; set; }
    public string OverdueDaysRaw { get; set; } = string.Empty;
    public int? OverdueDaysParsedValue { get; set; }
    public string OverdueDaysParseStatus { get; set; } = string.Empty;
}
