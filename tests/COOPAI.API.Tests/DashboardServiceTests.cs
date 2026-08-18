using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Services.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Tests;

public class DashboardServiceTests
{
    [Theory]
    [InlineData("สม", "สินเชื่อสามัญทั่วไป (รุ่นเก่า)")]
    [InlineData("สจ", "สินเชื่อสามัญเพื่อรถจักรยานยนต์")]
    [InlineData("สท", "สินเชื่อสามัญประกันด้วยอสังหาริมทรัพย์")]
    [InlineData("สป", "สินเชื่อสามัญประกันด้วยเงินฝาก,สมาชิก,อสังหาริมทรัพย์")]
    [InlineData("สห", "สินเชื่อสามัญประกันด้วยสมาชิก วงเงิน 100% ของทุนเรือนหุ้น")]
    [InlineData("สอ", "สินเชื่อฮัจย์และอุมเราะห์")]
    [InlineData("สศ", "สินเชื่อสามัญเพื่อเหตุจำเป็น")]
    [InlineData("สย", "สินเชื่อสามัญเพื่อผู้ประกอบการรายย่อย")]
    public async Task GetSummaryAsync_ApprovedPrefixUsesApprovedThaiDisplayName(
        string prefix,
        string expectedName)
    {
        await using var dbContext = CreateDbContext();
        AddContract(dbContext, $"{prefix}-2569-000001", 100m, 20m, 120m);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        var contractType = Assert.Single(summary.ContractTypes);
        Assert.Equal(prefix, contractType.Prefix);
        Assert.Equal(expectedName, contractType.Name);
    }

    [Fact]
    public async Task GetSummaryAsync_SharedClassifierOnlyPrefixIsUnknownForDashboard()
    {
        await using var dbContext = CreateDbContext();
        AddContract(dbContext, "สฉ-2569-000001", 100m, 20m, 120m);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        var contractType = Assert.Single(summary.ContractTypes);
        Assert.Equal("Unknown", contractType.Prefix);
        Assert.Equal("ไม่ทราบประเภท", contractType.Name);
    }

    [Fact]
    public async Task GetSummaryAsync_UnsupportedPrefixIsUnknown()
    {
        await using var dbContext = CreateDbContext();
        AddContract(dbContext, "XX-2569-000001", 100m, 20m, 120m);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        Assert.Equal("Unknown", Assert.Single(summary.ContractTypes).Prefix);
    }

    [Fact]
    public async Task GetSummaryAsync_MalformedContractNoIsUnknown()
    {
        await using var dbContext = CreateDbContext();
        AddContract(dbContext, "สย-BAD-000001", 100m, 20m, 120m);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        Assert.Equal("Unknown", Assert.Single(summary.ContractTypes).Prefix);
    }

    [Fact]
    public async Task GetSummaryAsync_NormalizesLeadingAndTrailingContractNoWhitespace()
    {
        await using var dbContext = CreateDbContext();
        AddContract(dbContext, "  สย-2569-000001  ", 100m, 20m, 120m);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        var contractType = Assert.Single(summary.ContractTypes);
        Assert.Equal("สย", contractType.Prefix);
        Assert.Equal("สินเชื่อสามัญเพื่อผู้ประกอบการรายย่อย", contractType.Name);
    }

    [Fact]
    public async Task GetSummaryAsync_ExcludesDeletedContractsFromCountsBalancesAndBreakdown()
    {
        await using var dbContext = CreateDbContext();
        AddContract(dbContext, "\u0E2A\u0E2B-2569-000001", 100m, 20m, 120m);
        AddContract(dbContext, "\u0E2A\u0E1B-2569-000002", 900m, 90m, 990m, isDeleted: true);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        Assert.Equal(1, summary.TotalContracts);
        Assert.Equal(1, summary.OutstandingContracts);
        Assert.Equal(100m, summary.PrincipalBalance);
        Assert.Equal(20m, summary.ProfitBalance);
        Assert.Equal(120m, summary.TotalBalance);
        var contractType = Assert.Single(summary.ContractTypes);
        Assert.Equal("\u0E2A\u0E2B", contractType.Prefix);
        Assert.Equal(1, contractType.ContractCount);
        Assert.Equal(1, contractType.OutstandingContractCount);
    }

    [Fact]
    public async Task GetSummaryAsync_PositiveZeroAndNegativeBalancesFollowApprovedCountRules()
    {
        await using var dbContext = CreateDbContext();
        AddContract(dbContext, "\u0E2A\u0E2B-2569-000001", 10m, 5m, 15m);
        AddContract(dbContext, "\u0E2A\u0E2B-2569-000002", 0m, 0m, 0m);
        AddContract(dbContext, "\u0E2A\u0E2B-2569-000003", -3m, -1m, -4m);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        Assert.Equal(3, summary.TotalContracts);
        Assert.Equal(1, summary.OutstandingContracts);
        Assert.Equal(1, summary.ZeroBalanceContracts);
        Assert.Equal(7m, summary.PrincipalBalance);
        Assert.Equal(4m, summary.ProfitBalance);
        Assert.Equal(11m, summary.TotalBalance);
        var contractType = Assert.Single(summary.ContractTypes);
        Assert.Equal(3, contractType.ContractCount);
        Assert.Equal(1, contractType.OutstandingContractCount);
    }

    [Fact]
    public async Task GetSummaryAsync_AggregatesCurrentBalancesAndNeverUsesLoanAmount()
    {
        await using var dbContext = CreateDbContext();
        AddContract(dbContext, "\u0E2A\u0E2B-2569-000001", 100m, 25m, 125m, loanAmount: 900000m);
        AddContract(dbContext, "\u0E2A\u0E2B-2569-000002", 200m, 50m, 250m, loanAmount: 800000m);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        Assert.Equal(300m, summary.PrincipalBalance);
        Assert.Equal(75m, summary.ProfitBalance);
        Assert.Equal(375m, summary.TotalBalance);
        Assert.NotEqual(1700000m, summary.PrincipalBalance);
    }

    [Fact]
    public async Task GetSummaryAsync_CountsOnlyNonDeletedMembers()
    {
        await using var dbContext = CreateDbContext();
        AddMember(dbContext, "M001");
        AddMember(dbContext, "M002");
        AddMember(dbContext, "M003", isDeleted: true);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        Assert.Equal(2, summary.TotalMembers);
    }

    [Fact]
    public async Task GetSummaryAsync_GroupsMultipleKnownPrefixesAndMalformedContractsAsUnknown()
    {
        await using var dbContext = CreateDbContext();
        AddContract(dbContext, "\u0E2A\u0E2B-2569-000001", 100m, 20m, 120m);
        AddContract(dbContext, "\u0E2A\u0E2B-2569-000002", 200m, 40m, 240m);
        AddContract(dbContext, "\u0E2A\u0E21-2569-000003", 300m, 60m, 360m);
        AddContract(dbContext, "BAD-CONTRACT", 400m, 80m, 480m);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        Assert.Equal(3, summary.ContractTypes.Count);

        var firstKnownType = Assert.Single(summary.ContractTypes, type => type.Prefix == "\u0E2A\u0E2B");
        Assert.Equal(2, firstKnownType.ContractCount);
        Assert.Equal(2, firstKnownType.OutstandingContractCount);
        Assert.Equal(300m, firstKnownType.PrincipalBalance);
        Assert.Equal(60m, firstKnownType.ProfitBalance);
        Assert.Equal(360m, firstKnownType.TotalBalance);

        var secondKnownType = Assert.Single(summary.ContractTypes, type => type.Prefix == "\u0E2A\u0E21");
        Assert.Equal(1, secondKnownType.ContractCount);

        var unknownType = Assert.Single(summary.ContractTypes, type => type.Prefix == "Unknown");
        Assert.Equal("ไม่ทราบประเภท", unknownType.Name);
        Assert.Equal(1, unknownType.ContractCount);
        Assert.Equal(480m, unknownType.TotalBalance);
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyDatabaseReturnsZeroValuesAndEmptyBreakdown()
    {
        await using var dbContext = CreateDbContext();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        Assert.Equal(0, summary.TotalContracts);
        Assert.Equal(0, summary.OutstandingContracts);
        Assert.Equal(0, summary.TotalMembers);
        Assert.Equal(0m, summary.PrincipalBalance);
        Assert.Equal(0m, summary.ProfitBalance);
        Assert.Equal(0m, summary.TotalBalance);
        Assert.Equal(0, summary.ZeroBalanceContracts);
        Assert.Empty(summary.ContractTypes);
    }

    [Fact]
    public async Task GetSummaryAsync_GroupTotalsReconcileWithOverallTotals()
    {
        await using var dbContext = CreateDbContext();
        AddContract(dbContext, "สจ-2569-000001", 100m, 20m, 120m);
        AddContract(dbContext, "สย-2569-000002", 200m, 40m, 240m);
        AddContract(dbContext, "XX-2569-000003", -10m, -2m, -12m);
        await dbContext.SaveChangesAsync();

        var summary = await new DashboardService(dbContext).GetSummaryAsync();

        Assert.Equal(summary.TotalContracts, summary.ContractTypes.Sum(type => type.ContractCount));
        Assert.Equal(
            summary.OutstandingContracts,
            summary.ContractTypes.Sum(type => type.OutstandingContractCount));
        Assert.Equal(
            summary.PrincipalBalance,
            summary.ContractTypes.Sum(type => type.PrincipalBalance));
        Assert.Equal(
            summary.ProfitBalance,
            summary.ContractTypes.Sum(type => type.ProfitBalance));
        Assert.Equal(
            summary.TotalBalance,
            summary.ContractTypes.Sum(type => type.TotalBalance));
    }

    private static CoopDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CoopDbContext>()
            .UseInMemoryDatabase($"DashboardService_{Guid.NewGuid():N}")
            .Options;

        return new CoopDbContext(options);
    }

    private static void AddContract(
        CoopDbContext dbContext,
        string contractNo,
        decimal principalBalance,
        decimal profitBalance,
        decimal totalBalance,
        bool isDeleted = false,
        decimal loanAmount = 999999m)
    {
        dbContext.LoanContracts.Add(new LoanContract
        {
            ContractNo = contractNo,
            ContractDate = new DateTime(2026, 1, 1),
            MemberId = 1,
            LoanTypeId = 1,
            LoanAmount = loanAmount,
            PrincipalBalance = principalBalance,
            ProfitBalance = profitBalance,
            TotalBalance = totalBalance,
            IsDeleted = isDeleted
        });
    }

    private static void AddMember(CoopDbContext dbContext, string memberNo, bool isDeleted = false)
    {
        dbContext.Members.Add(new Member
        {
            MemberNo = memberNo,
            FirstName = "Test",
            LastName = "Member",
            FullName = $"Test Member {memberNo}",
            IsDeleted = isDeleted
        });
    }
}
