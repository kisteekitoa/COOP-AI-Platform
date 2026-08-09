using System;

namespace COOPAI.API.DTOs.Dashboard;

public class DashboardSummaryDto
{
    public int TotalLoanContracts { get; set; }

    public decimal PrincipalBalance { get; set; }

    public decimal ProfitBalance { get; set; }

    public decimal TotalBalance { get; set; }

    public DateTime GeneratedAt { get; set; }
}
