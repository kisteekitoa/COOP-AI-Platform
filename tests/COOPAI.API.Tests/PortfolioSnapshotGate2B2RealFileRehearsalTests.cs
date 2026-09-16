using System.Text;
using COOPAI.API.Data;
using COOPAI.API.DTOs.Dashboard;
using COOPAI.API.Models;
using COOPAI.API.Models.Auth;
using COOPAI.API.Models.Portfolio;
using COOPAI.API.Services.PortfolioSnapshots;
using COOPAI.API.Services.Dashboard;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace COOPAI.API.Tests;

public sealed class PortfolioSnapshotGate2B2RealFileRehearsalTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LocalLoanWorkbook_IsolatedFirstPublishAndSupersede_ReconcilesLockedBaseline()
    {
        var filePath = FindRepositoryFile("Loan.xlsx");
        Assert.True(File.Exists(filePath), "The verified local Loan.xlsx rehearsal file is required.");
        var source = new ExcelPortfolioSnapshotSource();
        var baseline = PortfolioSnapshotAcceptanceBaseline.June2026;
        var sourceRead = await source.ReadAsync(filePath, baseline.AsOfDate);
        var normalizer = new SnapshotContractNoNormalizer();
        var contracts = sourceRead.Rows
            .Where(x => x.SourceRowKind == PortfolioSourceRowKind.Contract)
            .Select(row => new { Row = row, Contract = normalizer.Normalize(row.ContractNo) })
            .Where(x => x.Contract.IsValid)
            .OrderBy(x => x.Contract.NormalizedContractNo, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(baseline.TotalContractCount, contracts.Length);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CoopDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var isolated = new CoopDbContext(options);
        await isolated.Database.EnsureCreatedAsync();

        var loanType = new LoanType { Code = "ISO", Name = "Isolated rehearsal", IsActive = true };
        isolated.LoanTypes.Add(loanType);
        var memberNos = contracts
            .Take(baseline.MatchedCanonicalCount)
            .Select(x => NormalizeMemberNo(x.Row.MemberNo))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var members = memberNos.ToDictionary(
            memberNo => memberNo,
            memberNo => new Member
            {
                MemberNo = memberNo,
                FirstName = "Isolated",
                LastName = "Rehearsal",
                FullName = "Isolated Rehearsal"
            },
            StringComparer.Ordinal);
        isolated.Members.AddRange(members.Values);
        await isolated.SaveChangesAsync();

        isolated.LoanContracts.AddRange(contracts
            .Take(baseline.MatchedCanonicalCount)
            .Select(x => new LoanContract
            {
                ContractNo = x.Contract.NormalizedContractNo,
                ContractDate = new DateTime(2025, 1, 1),
                MemberId = members[NormalizeMemberNo(x.Row.MemberNo)].Id,
                LoanTypeId = loanType.Id
            }));
        var shadowMember = members.Values.First();
        isolated.LoanContracts.AddRange(Enumerable.Range(1, baseline.ShadowExcludedCount)
            .Select(index => new LoanContract
            {
                ContractNo = $"Shadow {index:000}",
                ContractDate = new DateTime(2025, 1, 1),
                MemberId = shadowMember.Id,
                LoanTypeId = loanType.Id
            }));
        var publisher = new CoopUser
        {
            UserName = "isolated.manager",
            NormalizedUserName = "ISOLATED.MANAGER",
            DisplayName = "Isolated Manager",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };
        isolated.Users.Add(publisher);
        await isolated.SaveChangesAsync();

        var service = EnabledService(isolated, source);
        var validation = await service.ValidateAsync(filePath, "Loan.xlsx", baseline.AsOfDate);
        Assert.True(validation.IsValid);
        Assert.Equal(baseline.TotalSourceRows, validation.Counts.SourceRows);
        Assert.Equal(baseline.TotalContractCount, validation.Counts.TotalContracts);
        Assert.Equal(baseline.PlaceholderRowCount, validation.Counts.PlaceholderRows);
        Assert.Equal(baseline.InTermContractCount, validation.Counts.InTermContracts);
        Assert.Equal(baseline.ExpiredContractCount, validation.Counts.ExpiredContracts);
        Assert.Equal(baseline.OutstandingContractCount, validation.Counts.OutstandingContracts);
        Assert.Equal(baseline.PaidOffContractCount, validation.Counts.PaidOffContracts);
        Assert.Equal(baseline.InTermOutstandingContractCount, validation.Counts.InTermOutstandingContracts);
        Assert.Equal(baseline.InTermPaidOffContractCount, validation.Counts.InTermPaidOffContracts);
        Assert.Equal(baseline.ExpiredOutstandingContractCount, validation.Counts.ExpiredOutstandingContracts);
        Assert.Equal(baseline.ExpiredPaidOffContractCount, validation.Counts.ExpiredPaidOffContracts);
        Assert.Equal(baseline.MatchedCanonicalCount, validation.Counts.CanonicalMatchedContracts);
        Assert.Equal(baseline.MissingCanonicalCount, validation.Counts.MissingCanonicalContracts);
        Assert.Equal(baseline.UnresolvedMemberContractCount, validation.Counts.UnresolvedMemberContracts);
        Assert.Equal(baseline.WarningRecordCount, validation.Counts.WarningContracts);
        Assert.Equal(baseline.ShadowExcludedCount, validation.Counts.ShadowExcludedContracts);
        Assert.Equal(baseline.Outstanding.Principal, validation.Financial.PrincipalOutstanding);
        Assert.Equal(baseline.Outstanding.Profit, validation.Financial.ProfitOutstanding);
        Assert.Equal(baseline.Outstanding.Total, validation.Financial.TotalOutstanding);
        Assert.Equal(baseline.ExpiredOutstandingTotal, validation.Financial.ExpiredOutstandingTotal);

        var draft = await service.CreateDraftAsync(
            filePath,
            "Loan.xlsx",
            baseline.AsOfDate,
            validation.SourceFileHash,
            validation.SnapshotContentHash);
        await service.ValidateDraftAsync(draft.Id);
        var firstPublish = await service.PublishAsync(
            draft.Id,
            validation.SnapshotContentHash,
            publisher.Id);
        Assert.Equal("Published", firstPublish.Status);
        Assert.Null(firstPublish.PreviousSupersededSnapshotId);

        var dashboard = await new DashboardService(isolated).GetSummaryAsync(DashboardDataModes.Published);
        Assert.True(dashboard.HasPublishedSnapshot);
        Assert.Equal(draft.Id, dashboard.SnapshotId);
        Assert.Equal(baseline.AsOfDate, dashboard.AsOfDate);
        Assert.Equal(baseline.TotalContractCount, dashboard.TotalContracts);
        Assert.Equal(baseline.InTermContractCount, dashboard.InTermContracts);
        Assert.Equal(baseline.ExpiredContractCount, dashboard.ExpiredContracts);
        Assert.Equal(baseline.OutstandingContractCount, dashboard.OutstandingContracts);
        Assert.Equal(baseline.PaidOffContractCount, dashboard.PaidOffContracts);
        Assert.Equal(baseline.InTermOutstandingContractCount, dashboard.InTermOutstandingContracts);
        Assert.Equal(baseline.InTermPaidOffContractCount, dashboard.InTermPaidOffContracts);
        Assert.Equal(baseline.ExpiredOutstandingContractCount, dashboard.ExpiredOutstandingContracts);
        Assert.Equal(baseline.ExpiredPaidOffContractCount, dashboard.ExpiredPaidOffContracts);
        Assert.Equal(baseline.Outstanding.Principal, dashboard.PrincipalOutstanding);
        Assert.Equal(baseline.Outstanding.Profit, dashboard.ProfitOutstanding);
        Assert.Equal(baseline.Outstanding.Total, dashboard.TotalOutstanding);
        Assert.Equal(baseline.ExpiredOutstandingTotal, dashboard.ExpiredOutstandingBalance);
        Assert.Equal(baseline.WarningRecordCount, dashboard.WarningContracts);
        Assert.Equal(baseline.UnresolvedMemberContractCount, dashboard.UnresolvedMemberContracts);
        Assert.Equal(baseline.ShadowExcludedCount, dashboard.ShadowExcludedContracts);
        Assert.True(dashboard.IsReconciled);
        Assert.Equal(baseline.TotalContractCount,
            dashboard.ContractTypes.Sum(type => type.ContractCount));
        Assert.Equal(baseline.InTermContractCount,
            dashboard.ContractTypes.Sum(type => type.InTermCount));
        Assert.Equal(baseline.ExpiredContractCount,
            dashboard.ContractTypes.Sum(type => type.ExpiredCount));
        Assert.Equal(baseline.OutstandingContractCount,
            dashboard.ContractTypes.Sum(type => type.OutstandingContractCount));
        Assert.Equal(baseline.PaidOffContractCount,
            dashboard.ContractTypes.Sum(type => type.PaidOffContractCount));
        Assert.Equal(baseline.Outstanding.Principal,
            dashboard.ContractTypes.Sum(type => type.PrincipalOutstanding));
        Assert.Equal(baseline.Outstanding.Profit,
            dashboard.ContractTypes.Sum(type => type.ProfitOutstanding));
        Assert.Equal(baseline.Outstanding.Total,
            dashboard.ContractTypes.Sum(type => type.TotalOutstanding));

        isolated.ChangeTracker.Clear();
        var first = await isolated.PortfolioSnapshots
            .Include(x => x.Records)
            .Include(x => x.Exclusions)
            .SingleAsync(x => x.Id == draft.Id);
        var second = CloneAsNewerValidated(isolated, first);
        isolated.PortfolioSnapshots.Add(second);
        await isolated.SaveChangesAsync();
        isolated.ChangeTracker.Clear();

        var secondPublish = await EnabledService(isolated, source).PublishAsync(
            second.Id,
            second.SnapshotContentHash,
            publisher.Id);
        Assert.Equal(first.Id, secondPublish.PreviousSupersededSnapshotId);
        isolated.ChangeTracker.Clear();
        var persistedFirst = await isolated.PortfolioSnapshots.SingleAsync(x => x.Id == first.Id);
        var persistedSecond = await isolated.PortfolioSnapshots.SingleAsync(x => x.Id == second.Id);
        Assert.Equal(PortfolioSnapshotStatus.Superseded, persistedFirst.Status);
        Assert.Equal(second.Id, persistedFirst.SupersededBySnapshotId);
        Assert.Equal(PortfolioSnapshotStatus.Published, persistedSecond.Status);
        Assert.Equal(publisher.Id, persistedSecond.PublishedByUserId);
        Assert.Single(await isolated.PortfolioSnapshots
            .Where(x => x.Status == PortfolioSnapshotStatus.Published)
            .ToListAsync());

        output.WriteLine($"RealFile={Path.GetFileName(filePath)},Contracts={validation.Counts.TotalContracts},Warnings={validation.Counts.WarningContracts},Unresolved={validation.Counts.UnresolvedMemberContracts},Shadows={validation.Counts.ShadowExcludedContracts}");
        output.WriteLine($"FirstPublish={firstPublish.Id}:{firstPublish.Status},SecondPublish={secondPublish.Id}:{secondPublish.Status},Superseded={secondPublish.PreviousSupersededSnapshotId}");
        output.WriteLine("Database=isolated SQLite; production connection was not opened and production writes were not performed.");
    }

    private static PortfolioSnapshotWorkflowService EnabledService(
        CoopDbContext context,
        IPortfolioSnapshotSource source) =>
        new(
            context,
            source,
            options: Options.Create(new PortfolioSnapshotOptions
            {
                PublishingEnabled = true
            }));

    private static PortfolioSnapshot CloneAsNewerValidated(
        CoopDbContext context,
        PortfolioSnapshot first)
    {
        var second = new PortfolioSnapshot();
        context.Entry(second).CurrentValues.SetValues(first);
        second.Id = 0;
        second.AsOfDate = first.AsOfDate.AddDays(1);
        second.Revision = 1;
        second.Status = PortfolioSnapshotStatus.Validated;
        second.CreatedAt = DateTime.UtcNow;
        second.ValidatedAt = DateTime.UtcNow;
        second.PublishedAt = null;
        second.PublishedByUserId = null;
        second.SupersededAt = null;
        second.SupersededBySnapshotId = null;
        second.ConcurrencyVersion = 0;
        second.Records = first.Records.Select(record =>
        {
            var clone = new PortfolioSnapshotRecord();
            context.Entry(clone).CurrentValues.SetValues(record);
            clone.Id = 0;
            clone.PortfolioSnapshotId = 0;
            clone.PortfolioSnapshot = second;
            return clone;
        }).ToList();
        second.Exclusions = first.Exclusions.Select(exclusion =>
        {
            var clone = new PortfolioSnapshotExclusion();
            context.Entry(clone).CurrentValues.SetValues(exclusion);
            clone.Id = 0;
            clone.PortfolioSnapshotId = 0;
            clone.PortfolioSnapshot = second;
            return clone;
        }).ToList();
        second.SnapshotContentHash = new PortfolioSnapshotContentHasher().Compute(second);
        return second;
    }

    private static string NormalizeMemberNo(string? memberNo) =>
        (memberNo ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);

    private static string FindRepositoryFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, fileName);
    }
}
