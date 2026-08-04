namespace COOPAI.API.DTOs.Import;

public class LoanImportRow
{
    public string MemberNo { get; set; } = "";

    public string FullName { get; set; } = "";

    public string ContractNo { get; set; } = "";

    public DateTime? ContractDate { get; set; }

    public DateTime? ExpireDate { get; set; }

    public decimal LoanAmount { get; set; }

    public decimal PrincipalBalance { get; set; }

    public decimal ProfitBalance { get; set; }

    public decimal TotalBalance { get; set; }

    public int OverdueDays { get; set; }
}