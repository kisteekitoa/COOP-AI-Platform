using System.Globalization;
using COOPAI.API.Data;
using COOPAI.API.Models.Portfolio;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace COOPAI.API.Tests;

public sealed class PortfolioSnapshotRealFileValidationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ValidateOnly_ControlledLoanWorkbook_ReconcilesLockedBaselineWithoutWrites()
    {
        var filePath = Environment.GetEnvironmentVariable("COOPAI_SNAPSHOT_REAL_FILE");
        var connectionString = Environment.GetEnvironmentVariable("COOPAI_SNAPSHOT_REAL_CONNECTION");
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("SKIPPED_REAL_VALIDATION: environment variables were not supplied.");
            return;
        }

        var options = new DbContextOptionsBuilder<CoopDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(300))
            .Options;
        await using var dbContext = new CoopDbContext(options);
        var loanCountBefore = await dbContext.LoanContracts.AsNoTracking().CountAsync();
        var memberCountBefore = await dbContext.Members.AsNoTracking().CountAsync();
        var baseline = PortfolioSnapshotAcceptanceBaseline.June2026;
        var service = new PortfolioSnapshotDryRunService(
            dbContext,
            new ExcelPortfolioSnapshotSource());

        var result = await service.BuildAsync(filePath, baseline.AsOfDate, baseline);

        output.WriteLine($"ObservedPopulation=SourceRows:{result.Snapshot.TotalSourceRows},Contracts:{result.Snapshot.TotalContractCount},Placeholders:{result.Snapshot.PlaceholderRowCount},CanonicalMatched:{result.Snapshot.MatchedCanonicalCount},CanonicalMissing:{result.Snapshot.MissingCanonicalCount},UnresolvedMembers:{result.Snapshot.UnresolvedMemberContractCount},Warnings:{result.Snapshot.WarningRecordCount},Shadows:{result.Snapshot.ShadowExcludedCount}");
        output.WriteLine($"ObservedTerm=InTerm:{result.Snapshot.InTermContractCount},Expired:{result.Snapshot.ExpiredContractCount}");
        output.WriteLine($"ObservedBalance=Outstanding:{result.Snapshot.OutstandingContractCount},PaidOff:{result.Snapshot.PaidOffContractCount}");
        output.WriteLine($"ObservedCross=InTermOutstanding:{result.Snapshot.InTermOutstandingContractCount},InTermPaidOff:{result.Snapshot.InTermPaidOffContractCount},ExpiredOutstanding:{result.Snapshot.ExpiredOutstandingContractCount},ExpiredPaidOff:{result.Snapshot.ExpiredPaidOffContractCount}");
        output.WriteLine($"ObservedExpiredOutstandingTotal={result.Snapshot.ExpiredOutstandingTotal:F2}");
        output.WriteLine($"ObservedOpening={result.Snapshot.PrincipalOpening:F2},{result.Snapshot.ProfitOpening:F2},{result.Snapshot.TotalOpening:F2}");
        output.WriteLine($"ObservedRepayment={result.Snapshot.PrincipalRepayment:F2},{result.Snapshot.ProfitRepayment:F2},{result.Snapshot.TotalRepayment:F2}");
        output.WriteLine($"ObservedOutstanding={result.Snapshot.PrincipalOutstanding:F2},{result.Snapshot.ProfitOutstanding:F2},{result.Snapshot.TotalOutstanding:F2}");
        output.WriteLine($"ObservedRemainder={result.Snapshot.PrincipalDifference:F2},{result.Snapshot.ProfitDifference:F2},{result.Snapshot.TotalDifference:F2},{result.Snapshot.ComponentDifference:F2}");
        foreach (var group in result.Snapshot.Records
                     .SelectMany(record => System.Text.Json.JsonSerializer.Deserialize<string[]>(record.WarningCodesJson) ?? [])
                     .GroupBy(code => code, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"WarningCode={group.Key},Count={group.Count()}");
        }
        foreach (var group in result.Issues
                     .GroupBy(issue => issue.Code, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"IssueCode={group.Key},Count={group.Count()},Rows={string.Join(',', group.Select(issue => issue.SourceRowNumber).Where(row => row.HasValue).Select(row => row!.Value))}");
        }

        var loanCountAfter = await dbContext.LoanContracts.AsNoTracking().CountAsync();
        var memberCountAfter = await dbContext.Members.AsNoTracking().CountAsync();
        Assert.Equal(loanCountBefore, loanCountAfter);
        Assert.Equal(memberCountBefore, memberCountAfter);
        Assert.DoesNotContain(dbContext.ChangeTracker.Entries(), entry => entry.State != EntityState.Unchanged);
        output.WriteLine($"DatabaseCountsUnchanged=LoanContracts:{loanCountAfter},Members:{memberCountAfter}");

        Assert.True(result.IsValid, string.Join(", ", result.Issues.Select(x => x.Code)));
        Assert.Equal(PortfolioSnapshotStatus.Validated, result.Snapshot.Status);
        Assert.Equal(new DateOnly(2026, 6, 30), result.Snapshot.AsOfDate);
        Assert.Equal(baseline.TotalSourceRows, result.Snapshot.TotalSourceRows);
        Assert.Equal(baseline.TotalContractCount, result.Snapshot.TotalContractCount);
        Assert.Equal(baseline.PlaceholderRowCount, result.Snapshot.PlaceholderRowCount);
        Assert.Equal(baseline.MatchedCanonicalCount, result.Snapshot.MatchedCanonicalCount);
        Assert.Equal(baseline.MissingCanonicalCount, result.Snapshot.MissingCanonicalCount);
        Assert.Equal(baseline.UnresolvedMemberContractCount, result.Snapshot.UnresolvedMemberContractCount);
        Assert.Equal(baseline.WarningRecordCount, result.Snapshot.WarningRecordCount);
        Assert.Equal(baseline.ShadowExcludedCount, result.Snapshot.ShadowExcludedCount);
        Assert.Equal(baseline.InTermContractCount, result.Snapshot.InTermContractCount);
        Assert.Equal(baseline.ExpiredContractCount, result.Snapshot.ExpiredContractCount);
        Assert.Equal(baseline.OutstandingContractCount, result.Snapshot.OutstandingContractCount);
        Assert.Equal(baseline.PaidOffContractCount, result.Snapshot.PaidOffContractCount);
        Assert.Equal(baseline.InTermOutstandingContractCount, result.Snapshot.InTermOutstandingContractCount);
        Assert.Equal(baseline.InTermPaidOffContractCount, result.Snapshot.InTermPaidOffContractCount);
        Assert.Equal(baseline.ExpiredOutstandingContractCount, result.Snapshot.ExpiredOutstandingContractCount);
        Assert.Equal(baseline.ExpiredPaidOffContractCount, result.Snapshot.ExpiredPaidOffContractCount);
        Assert.Equal(baseline.ExpiredOutstandingTotal, result.Snapshot.ExpiredOutstandingTotal);
        Assert.Equal(baseline.TotalSourceRows, result.Snapshot.TotalContractCount + result.Snapshot.PlaceholderRowCount);
        Assert.Equal(baseline.TotalContractCount, result.Snapshot.InTermContractCount + result.Snapshot.ExpiredContractCount);
        Assert.Equal(baseline.TotalContractCount, result.Snapshot.OutstandingContractCount + result.Snapshot.PaidOffContractCount);
        Assert.Equal(baseline.TotalContractCount,
            result.Snapshot.InTermOutstandingContractCount + result.Snapshot.InTermPaidOffContractCount +
            result.Snapshot.ExpiredOutstandingContractCount + result.Snapshot.ExpiredPaidOffContractCount);
        Assert.Equal(baseline.TotalContractCount, result.Snapshot.MatchedCanonicalCount + result.Snapshot.MissingCanonicalCount);
        var unresolved = result.Snapshot.Records.Where(x => x.MemberMatchStatus == PortfolioMemberMatchStatus.Missing).ToArray();
        Assert.Equal(baseline.UnresolvedMemberContractCount, unresolved.Length);
        Assert.All(unresolved, record =>
        {
            Assert.Null(record.MemberId);
            Assert.Equal(PortfolioSnapshotInclusionStatus.IncludedWithWarning, record.InclusionStatus);
        });
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "MultipleInTermContractsForMember");
        Assert.Equal(baseline.Opening, Opening(result.Snapshot));
        Assert.Equal(baseline.Repayment, Repayment(result.Snapshot));
        Assert.Equal(baseline.Outstanding, Outstanding(result.Snapshot));
        Assert.Equal(0m, result.Snapshot.PrincipalDifference);
        Assert.Equal(0m, result.Snapshot.ProfitDifference);
        Assert.Equal(0m, result.Snapshot.TotalDifference);
        Assert.Equal(0m, result.Snapshot.ComponentDifference);
        output.WriteLine($"AsOfDateGregorian={result.Snapshot.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)},AsOfDateBuddhistEra=2569-06-30");
        output.WriteLine($"Counts={result.Snapshot.TotalSourceRows},{result.Snapshot.TotalContractCount},{result.Snapshot.PlaceholderRowCount},{result.Snapshot.MatchedCanonicalCount},{result.Snapshot.MissingCanonicalCount},{result.Snapshot.UnresolvedMemberContractCount},{result.Snapshot.ShadowExcludedCount},{result.Snapshot.WarningRecordCount}");
        output.WriteLine($"Opening={result.Snapshot.PrincipalOpening:F2},{result.Snapshot.ProfitOpening:F2},{result.Snapshot.TotalOpening:F2}");
        output.WriteLine($"Repayment={result.Snapshot.PrincipalRepayment:F2},{result.Snapshot.ProfitRepayment:F2},{result.Snapshot.TotalRepayment:F2}");
        output.WriteLine($"Outstanding={result.Snapshot.PrincipalOutstanding:F2},{result.Snapshot.ProfitOutstanding:F2},{result.Snapshot.TotalOutstanding:F2}");
        output.WriteLine($"Remainder={result.Snapshot.PrincipalDifference:F2},{result.Snapshot.ProfitDifference:F2},{result.Snapshot.TotalDifference:F2},{result.Snapshot.ComponentDifference:F2}");
        output.WriteLine($"SourceHash={result.Snapshot.SourceFileHash}");
        output.WriteLine($"SnapshotHash={result.Snapshot.SnapshotContentHash}");
    }

    private static PortfolioFinancialValues Opening(PortfolioSnapshot snapshot) =>
        new(snapshot.PrincipalOpening, snapshot.ProfitOpening, snapshot.TotalOpening);

    private static PortfolioFinancialValues Repayment(PortfolioSnapshot snapshot) =>
        new(snapshot.PrincipalRepayment, snapshot.ProfitRepayment, snapshot.TotalRepayment);

    private static PortfolioFinancialValues Outstanding(PortfolioSnapshot snapshot) =>
        new(snapshot.PrincipalOutstanding, snapshot.ProfitOutstanding, snapshot.TotalOutstanding);

}
