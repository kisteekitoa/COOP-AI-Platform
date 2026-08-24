using COOPAI.API.Data;
using COOPAI.API.DTOs.Dashboard;
using COOPAI.API.Models.Portfolio;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Services.Dashboard;

public sealed class DashboardService(CoopDbContext dbContext) : IDashboardService
{
    public const string PublishedSnapshotDataSource = "Published Portfolio Snapshot";
    private static readonly DashboardLoanTypeClassifier LoanTypeClassifier = new();

    public async Task<DashboardSummaryDto> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var summary = await dbContext.PortfolioSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Status == PortfolioSnapshotStatus.Published)
            .Select(snapshot => new DashboardSummaryDto
            {
                HasPublishedSnapshot = true,
                DataSource = PublishedSnapshotDataSource,
                SnapshotId = snapshot.Id,
                AsOfDate = snapshot.AsOfDate,
                PublishedAt = snapshot.PublishedAt,
                GeneratedAt = DateTime.UtcNow,
                IsReconciled =
                    snapshot.TotalSourceRows == snapshot.TotalContractCount + snapshot.PlaceholderRowCount &&
                    snapshot.TotalContractCount == snapshot.InTermContractCount + snapshot.ExpiredContractCount &&
                    snapshot.TotalContractCount == snapshot.OutstandingContractCount + snapshot.PaidOffContractCount &&
                    snapshot.TotalContractCount == snapshot.InTermOutstandingContractCount +
                        snapshot.InTermPaidOffContractCount + snapshot.ExpiredOutstandingContractCount +
                        snapshot.ExpiredPaidOffContractCount &&
                    snapshot.PrincipalOutstanding + snapshot.ProfitOutstanding == snapshot.TotalOutstanding &&
                    snapshot.PrincipalDifference == 0m && snapshot.ProfitDifference == 0m &&
                    snapshot.TotalDifference == 0m && snapshot.ComponentDifference == 0m,
                TotalSourceRows = snapshot.TotalSourceRows,
                TotalContracts = snapshot.TotalContractCount,
                TemplatePlaceholderRows = snapshot.PlaceholderRowCount,
                InTermContracts = snapshot.InTermContractCount,
                ExpiredContracts = snapshot.ExpiredContractCount,
                OutstandingContracts = snapshot.OutstandingContractCount,
                PaidOffContracts = snapshot.PaidOffContractCount,
                InTermOutstandingContracts = snapshot.InTermOutstandingContractCount,
                InTermPaidOffContracts = snapshot.InTermPaidOffContractCount,
                ExpiredOutstandingContracts = snapshot.ExpiredOutstandingContractCount,
                ExpiredPaidOffContracts = snapshot.ExpiredPaidOffContractCount,
                WarningContracts = snapshot.WarningRecordCount,
                UnresolvedMemberContracts = snapshot.UnresolvedMemberContractCount,
                ShadowExcludedContracts = snapshot.ShadowExcludedCount,
                CanonicalMatchedContracts = snapshot.MatchedCanonicalCount,
                CanonicalMissingContracts = snapshot.MissingCanonicalCount,
                PrincipalOutstanding = snapshot.PrincipalOutstanding,
                ProfitOutstanding = snapshot.ProfitOutstanding,
                TotalOutstanding = snapshot.TotalOutstanding,
                ExpiredOutstandingBalance = snapshot.ExpiredOutstandingTotal
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (summary is null)
        {
            return new DashboardSummaryDto
            {
                HasPublishedSnapshot = false,
                GeneratedAt = DateTime.UtcNow
            };
        }

        var snapshotId = summary.SnapshotId!.Value;
        var records = await dbContext.PortfolioSnapshotRecords
            .AsNoTracking()
            .Where(record => record.PortfolioSnapshotId == snapshotId)
            .Select(record => new
            {
                record.LoanTypePrefix,
                record.TermStatus,
                record.BalanceStatus,
                record.PrincipalOutstanding,
                record.ProfitOutstanding,
                record.TotalOutstanding
            })
            .ToListAsync(cancellationToken);

        var groups = records
            .GroupBy(record => record.LoanTypePrefix, StringComparer.Ordinal)
            .Select(group => new ContractTypeAggregate
            {
                Prefix = group.Key,
                ContractCount = group.Count(),
                OutstandingContractCount = group.Count(record =>
                    record.BalanceStatus == PortfolioBalanceStatus.Outstanding),
                PaidOffContractCount = group.Count(record =>
                    record.BalanceStatus == PortfolioBalanceStatus.PaidOff),
                InTermCount = group.Count(record => record.TermStatus == PortfolioTermStatus.InTerm),
                ExpiredCount = group.Count(record => record.TermStatus == PortfolioTermStatus.Expired),
                PrincipalOutstanding = group.Sum(record => record.PrincipalOutstanding),
                ProfitOutstanding = group.Sum(record => record.ProfitOutstanding),
                TotalOutstanding = group.Sum(record => record.TotalOutstanding)
            })
            .ToList();

        summary.ContractTypes = groups
            .Select(MapContractType)
            .GroupBy(group => new { group.Prefix, group.Name })
            .Select(group => new DashboardContractTypeDto
            {
                Prefix = group.Key.Prefix,
                Name = group.Key.Name,
                ContractCount = group.Sum(x => x.ContractCount),
                OutstandingContractCount = group.Sum(x => x.OutstandingContractCount),
                PaidOffContractCount = group.Sum(x => x.PaidOffContractCount),
                InTermCount = group.Sum(x => x.InTermCount),
                ExpiredCount = group.Sum(x => x.ExpiredCount),
                PrincipalOutstanding = group.Sum(x => x.PrincipalOutstanding),
                ProfitOutstanding = group.Sum(x => x.ProfitOutstanding),
                TotalOutstanding = group.Sum(x => x.TotalOutstanding)
            })
            .OrderBy(group => group.Prefix == DashboardLoanTypeClassifier.UnknownPrefix)
            .ThenBy(group => group.Prefix, StringComparer.Ordinal)
            .ToList();

        return summary;
    }

    private static DashboardContractTypeDto MapContractType(ContractTypeAggregate aggregate)
    {
        var classification = LoanTypeClassifier.ClassifyPrefix(aggregate.Prefix);
        return new DashboardContractTypeDto
        {
            Prefix = classification.Prefix,
            Name = classification.Name,
            ContractCount = aggregate.ContractCount,
            OutstandingContractCount = aggregate.OutstandingContractCount,
            PaidOffContractCount = aggregate.PaidOffContractCount,
            InTermCount = aggregate.InTermCount,
            ExpiredCount = aggregate.ExpiredCount,
            PrincipalOutstanding = aggregate.PrincipalOutstanding,
            ProfitOutstanding = aggregate.ProfitOutstanding,
            TotalOutstanding = aggregate.TotalOutstanding
        };
    }

    private sealed class ContractTypeAggregate
    {
        public string Prefix { get; set; } = string.Empty;
        public int ContractCount { get; set; }
        public int OutstandingContractCount { get; set; }
        public int PaidOffContractCount { get; set; }
        public int InTermCount { get; set; }
        public int ExpiredCount { get; set; }
        public decimal PrincipalOutstanding { get; set; }
        public decimal ProfitOutstanding { get; set; }
        public decimal TotalOutstanding { get; set; }
    }
}
