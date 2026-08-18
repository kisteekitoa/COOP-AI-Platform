using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using COOPAI.API.Data;
using COOPAI.API.DTOs.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Services.Dashboard;

public class DashboardService : IDashboardService
{
    private static readonly DashboardLoanTypeClassifier LoanTypeClassifier = new();

    private readonly CoopDbContext _dbContext;

    public DashboardService(CoopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var contracts = await _dbContext.LoanContracts
            .AsNoTracking()
            .Where(contract => !contract.IsDeleted)
            .Select(contract => new DashboardContractSnapshot
            {
                ContractNo = contract.ContractNo,
                PrincipalBalance = contract.PrincipalBalance,
                ProfitBalance = contract.ProfitBalance,
                TotalBalance = contract.TotalBalance
            })
            .ToListAsync();

        var totalMembers = await _dbContext.Members
            .AsNoTracking()
            .CountAsync(member => !member.IsDeleted);

        return new DashboardSummaryDto
        {
            TotalContracts = contracts.Count,
            OutstandingContracts = contracts.Count(contract => contract.TotalBalance > 0m),
            TotalMembers = totalMembers,
            PrincipalBalance = contracts.Sum(contract => contract.PrincipalBalance),
            ProfitBalance = contracts.Sum(contract => contract.ProfitBalance),
            TotalBalance = contracts.Sum(contract => contract.TotalBalance),
            ZeroBalanceContracts = contracts.Count(contract => contract.TotalBalance == 0m),
            GeneratedAt = DateTime.Now,
            ContractTypes = BuildContractTypeBreakdown(contracts)
        };
    }

    private static List<DashboardContractTypeDto> BuildContractTypeBreakdown(
        IReadOnlyCollection<DashboardContractSnapshot> contracts)
    {
        var groups = new Dictionary<string, DashboardContractTypeDto>(StringComparer.Ordinal);

        foreach (var contract in contracts)
        {
            var classification = LoanTypeClassifier.Classify(contract.ContractNo);
            var prefix = classification.Prefix;
            var name = classification.Name;

            if (!groups.TryGetValue(prefix, out var group))
            {
                group = new DashboardContractTypeDto
                {
                    Prefix = prefix,
                    Name = name
                };
                groups.Add(prefix, group);
            }

            group.ContractCount++;
            if (contract.TotalBalance > 0m)
                group.OutstandingContractCount++;
            group.PrincipalBalance += contract.PrincipalBalance;
            group.ProfitBalance += contract.ProfitBalance;
            group.TotalBalance += contract.TotalBalance;
        }

        return groups.Values
            .OrderBy(group => group.Prefix == DashboardLoanTypeClassifier.UnknownPrefix)
            .ThenBy(group => group.Prefix, StringComparer.Ordinal)
            .ToList();
    }

    private sealed class DashboardContractSnapshot
    {
        public string ContractNo { get; init; } = string.Empty;

        public decimal PrincipalBalance { get; init; }

        public decimal ProfitBalance { get; init; }

        public decimal TotalBalance { get; init; }
    }
}
