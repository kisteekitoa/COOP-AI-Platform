using System;
using System.Collections.Generic;

namespace COOPAI.API.DTOs.Dashboard;

public class DashboardSummaryDto
{
    public int TotalContracts { get; set; }

    public int OutstandingContracts { get; set; }

    public int TotalMembers { get; set; }

    public decimal PrincipalBalance { get; set; }

    public decimal ProfitBalance { get; set; }

    public decimal TotalBalance { get; set; }

    public int ZeroBalanceContracts { get; set; }

    public DateTime GeneratedAt { get; set; }

    public List<DashboardContractTypeDto> ContractTypes { get; set; } = new();
}

public class DashboardContractTypeDto
{
    public string Prefix { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int ContractCount { get; set; }

    public int OutstandingContractCount { get; set; }

    public decimal PrincipalBalance { get; set; }

    public decimal ProfitBalance { get; set; }

    public decimal TotalBalance { get; set; }
}
