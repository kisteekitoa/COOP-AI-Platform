using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Models.Portfolio;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace COOPAI.API.Tests;

public sealed class PortfolioSnapshotGate2ARealFileRehearsalTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RealLoanWorkbook_ValidateCreateReviewAndValidateDraft_UsesIsolatedDatabaseOnly()
    {
        var filePath = Environment.GetEnvironmentVariable("COOPAI_SNAPSHOT_REAL_FILE");
        var productionConnection = Environment.GetEnvironmentVariable("COOPAI_SNAPSHOT_REAL_CONNECTION");
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(productionConnection))
        {
            output.WriteLine("SKIPPED_REAL_GATE2A_REHEARSAL: environment variables were not supplied.");
            return;
        }

        var productionOptions = new DbContextOptionsBuilder<CoopDbContext>()
            .UseSqlServer(productionConnection, sql => sql.CommandTimeout(300))
            .Options;
        await using var production = new CoopDbContext(productionOptions);
        var productionLoanCountBefore = await production.LoanContracts.AsNoTracking().CountAsync();
        var productionMemberCountBefore = await production.Members.AsNoTracking().CountAsync();
        var productionMembers = await production.Members
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new { x.Id, x.MemberNo })
            .ToListAsync();
        var productionContracts = await production.LoanContracts
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new { x.ContractNo, x.MemberId })
            .ToListAsync();

        await using var isolatedConnection = new SqliteConnection("Data Source=:memory:");
        await isolatedConnection.OpenAsync();
        var isolatedOptions = new DbContextOptionsBuilder<CoopDbContext>()
            .UseSqlite(isolatedConnection)
            .Options;
        await using var isolated = new CoopDbContext(isolatedOptions);
        await isolated.Database.EnsureCreatedAsync();
        var loanType = new LoanType { Code = "ISO", Name = "Isolated rehearsal", IsActive = true };
        isolated.LoanTypes.Add(loanType);
        var memberMap = new Dictionary<int, Member>();
        foreach (var sourceMember in productionMembers)
        {
            var isolatedMember = new Member
            {
                MemberNo = sourceMember.MemberNo,
                FirstName = "Isolated",
                LastName = "Rehearsal",
                FullName = "Isolated Rehearsal"
            };
            memberMap.Add(sourceMember.Id, isolatedMember);
            isolated.Members.Add(isolatedMember);
        }
        await isolated.SaveChangesAsync();
        foreach (var sourceContract in productionContracts)
        {
            isolated.LoanContracts.Add(new LoanContract
            {
                ContractNo = sourceContract.ContractNo,
                ContractDate = new DateTime(2025, 1, 1),
                MemberId = memberMap[sourceContract.MemberId].Id,
                LoanTypeId = loanType.Id
            });
        }
        await isolated.SaveChangesAsync();

        var baseline = PortfolioSnapshotAcceptanceBaseline.June2026;
        var service = new PortfolioSnapshotWorkflowService(isolated, new ExcelPortfolioSnapshotSource());
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
        Assert.Equal(baseline.ShadowExcludedCount, validation.Counts.ShadowExcludedContracts);
        Assert.Equal(baseline.WarningRecordCount, validation.Counts.WarningContracts);
        Assert.Equal(baseline.UnresolvedMemberContractCount, validation.Counts.UnresolvedMemberContracts);
        Assert.Equal(baseline.Outstanding.Principal, validation.Financial.PrincipalOutstanding);
        Assert.Equal(baseline.Outstanding.Profit, validation.Financial.ProfitOutstanding);
        Assert.Equal(baseline.Outstanding.Total, validation.Financial.TotalOutstanding);
        Assert.Equal(baseline.ExpiredOutstandingTotal, validation.Financial.ExpiredOutstandingTotal);
        Assert.True(validation.Quality.Reconciled);

        var draft = await service.CreateDraftAsync(
            filePath,
            "Loan.xlsx",
            baseline.AsOfDate,
            validation.SourceFileHash,
            validation.SnapshotContentHash);
        Assert.Equal("Draft", draft.Status);
        Assert.False(draft.WasExisting);
        var review = await service.GetReviewAsync(draft.Id);
        Assert.NotNull(review);
        Assert.Equal(baseline.TotalContractCount, review.Counts.TotalContracts);
        Assert.Equal(baseline.WarningRecordCount, review.Counts.WarningContracts);
        Assert.Equal(baseline.UnresolvedMemberContractCount, review.UnresolvedMembers.Count);
        Assert.Contains(review.WarningSummary, x => x.Code == PortfolioSnapshotCodes.Negative && x.Count == 7);
        Assert.Contains(review.WarningSummary, x => x.Code == PortfolioSnapshotCodes.TotalBalanceMismatch && x.Count == 1);
        Assert.Contains(review.Exclusions, x =>
            x.ReasonCode == PortfolioSnapshotCodes.MalformedShadowContractNo &&
            x.Count == baseline.ShadowExcludedCount);
        Assert.False(review.PublishingEnabled);
        var validated = await service.ValidateDraftAsync(draft.Id);
        Assert.Equal("Validated", validated.Status);
        Assert.Equal(baseline.TotalContractCount, await isolated.PortfolioSnapshotRecords.CountAsync());
        Assert.Equal(baseline.ShadowExcludedCount, await isolated.PortfolioSnapshotExclusions.CountAsync());
        Assert.Equal(PortfolioSnapshotStatus.Validated, await isolated.PortfolioSnapshots
            .Where(x => x.Id == draft.Id)
            .Select(x => x.Status)
            .SingleAsync());

        var productionLoanCountAfter = await production.LoanContracts.AsNoTracking().CountAsync();
        var productionMemberCountAfter = await production.Members.AsNoTracking().CountAsync();
        Assert.Equal(productionLoanCountBefore, productionLoanCountAfter);
        Assert.Equal(productionMemberCountBefore, productionMemberCountAfter);
        output.WriteLine($"IsolatedDraftId={draft.Id},Status={validated.Status}");
        output.WriteLine($"Population={validation.Counts.SourceRows},{validation.Counts.TotalContracts},{validation.Counts.PlaceholderRows}");
        output.WriteLine($"Term={validation.Counts.InTermContracts},{validation.Counts.ExpiredContracts}");
        output.WriteLine($"Balance={validation.Counts.OutstandingContracts},{validation.Counts.PaidOffContracts}");
        output.WriteLine($"Cross={validation.Counts.InTermOutstandingContracts},{validation.Counts.InTermPaidOffContracts},{validation.Counts.ExpiredOutstandingContracts},{validation.Counts.ExpiredPaidOffContracts}");
        output.WriteLine($"Financial={validation.Financial.PrincipalOutstanding:F2},{validation.Financial.ProfitOutstanding:F2},{validation.Financial.TotalOutstanding:F2},{validation.Financial.ExpiredOutstandingTotal:F2}");
        output.WriteLine($"Quality={validation.Counts.WarningContracts},{validation.Counts.UnresolvedMemberContracts},{validation.Counts.ShadowExcludedContracts}");
        output.WriteLine($"ProductionCountsUnchanged=LoanContracts:{productionLoanCountAfter},Members:{productionMemberCountAfter}");
    }
}
