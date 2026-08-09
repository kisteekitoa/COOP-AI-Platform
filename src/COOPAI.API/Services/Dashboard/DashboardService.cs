using System;
using System.Threading.Tasks;
using COOPAI.API.Data;
using COOPAI.API.DTOs.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Services.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly CoopDbContext _dbContext;

    public DashboardService(CoopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var totalLoanContracts = await _dbContext.LoanContracts.CountAsync();
        var principalBalance = await _dbContext.LoanContracts.SumAsync(x => x.PrincipalBalance);
        var profitBalance = await _dbContext.LoanContracts.SumAsync(x => x.ProfitBalance);
        var totalBalance = await _dbContext.LoanContracts.SumAsync(x => x.TotalBalance);

        return new DashboardSummaryDto
        {
            TotalLoanContracts = totalLoanContracts,
            PrincipalBalance = principalBalance,
            ProfitBalance = profitBalance,
            TotalBalance = totalBalance,
            GeneratedAt = DateTime.Now
        };
    }
}
