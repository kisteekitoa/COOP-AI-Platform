using COOPAI.API.Data;
using COOPAI.API.DTOs.Dashboard;
using COOPAI.API.Models;
using COOPAI.API.Models.Portfolio;
using COOPAI.API.Services.Dashboard;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Tests;

public sealed class DashboardServiceTests
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
    public void DashboardLoanTypeClassifier_ApprovedPrefixUsesApprovedThaiDisplayName(
        string prefix,
        string expectedName)
    {
        var classification = new DashboardLoanTypeClassifier().Classify($"{prefix}-2569-000001");

        Assert.Equal(prefix, classification.Prefix);
        Assert.Equal(expectedName, classification.Name);
    }

    [Fact]
    public void DashboardLoanTypeClassifier_SharedClassifierOnlyPrefixIsUnknown()
    {
        var classification = new DashboardLoanTypeClassifier().Classify("สฉ-2569-000001");

        Assert.Equal(DashboardLoanTypeClassifier.UnknownPrefix, classification.Prefix);
        Assert.Equal(DashboardLoanTypeClassifier.UnknownTypeName, classification.Name);
    }

    [Fact]
    public void DashboardLoanTypeClassifier_UnsupportedPrefixIsUnknown()
    {
        var classification = new DashboardLoanTypeClassifier().Classify("XX-2569-000001");

        Assert.Equal(DashboardLoanTypeClassifier.UnknownPrefix, classification.Prefix);
    }

    [Fact]
    public void DashboardLoanTypeClassifier_MalformedContractNoIsUnknown()
    {
        var classification = new DashboardLoanTypeClassifier().Classify("สย-BAD-000001");

        Assert.Equal(DashboardLoanTypeClassifier.UnknownPrefix, classification.Prefix);
    }

    [Fact]
    public void DashboardLoanTypeClassifier_NormalizesOuterWhitespace()
    {
        var classification = new DashboardLoanTypeClassifier().Classify("  สย-2569-000001  ");

        Assert.Equal("สย", classification.Prefix);
        Assert.Equal("สินเชื่อสามัญเพื่อผู้ประกอบการรายย่อย", classification.Name);
    }

    [Fact]
    public async Task GetSummaryAsync_NoSnapshots_ReturnsExplicitNoPublishedState()
    {
        await using var database = await DashboardDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var summary = await new DashboardService(context).GetSummaryAsync(DashboardDataModes.Published);

        Assert.False(summary.HasPublishedSnapshot);
        Assert.Null(summary.DataSource);
        Assert.Null(summary.SnapshotId);
        Assert.Null(summary.TotalContracts);
        Assert.Null(summary.PrincipalOutstanding);
        Assert.Empty(summary.ContractTypes);
        Assert.NotEqual(default, summary.GeneratedAt);
    }

    [Theory]
    [InlineData(PortfolioSnapshotStatus.Draft)]
    [InlineData(PortfolioSnapshotStatus.Validated)]
    [InlineData(PortfolioSnapshotStatus.Rejected)]
    [InlineData(PortfolioSnapshotStatus.Superseded)]
    public async Task GetSummaryAsync_NonPublishedLifecycleNeverBecomesDashboardSource(
        PortfolioSnapshotStatus status)
    {
        await using var database = await DashboardDatabase.CreateAsync();
        await database.SeedSnapshotAsync(status, "A");
        await using var context = database.CreateContext();

        var summary = await new DashboardService(context).GetSummaryAsync(DashboardDataModes.Published);

        Assert.False(summary.HasPublishedSnapshot);
        Assert.Null(summary.TotalContracts);
        Assert.Empty(summary.ContractTypes);
    }

    [Fact]
    public async Task GetSummaryAsync_PublishedSnapshotReturnsLockedHeaderBaselineAndMetadata()
    {
        await using var database = await DashboardDatabase.CreateAsync();
        var publishedAt = new DateTime(2026, 8, 24, 3, 30, 0, DateTimeKind.Utc);
        var id = await database.SeedSnapshotAsync(
            PortfolioSnapshotStatus.Published,
            "B",
            publishedAt: publishedAt,
            records:
            [
                Record("สม", 1, PortfolioTermStatus.InTerm, PortfolioBalanceStatus.Outstanding, 80m, 20m),
                Record("สม", 2, PortfolioTermStatus.Expired, PortfolioBalanceStatus.PaidOff, 0m, 0m)
            ]);
        await using var context = database.CreateContext();

        var summary = await new DashboardService(context).GetSummaryAsync(DashboardDataModes.Published);

        Assert.True(summary.HasPublishedSnapshot);
        Assert.Equal(DashboardService.PublishedSnapshotDataSource, summary.DataSource);
        Assert.Equal(id, summary.SnapshotId);
        Assert.Equal(new DateOnly(2026, 6, 30), summary.AsOfDate);
        Assert.Equal(publishedAt, summary.PublishedAt);
        Assert.True(summary.IsReconciled);
        Assert.Equal(5_084, summary.TotalSourceRows);
        Assert.Equal(4_454, summary.TotalContracts);
        Assert.Equal(630, summary.TemplatePlaceholderRows);
        Assert.Equal(2_559, summary.InTermContracts);
        Assert.Equal(1_895, summary.ExpiredContracts);
        Assert.Equal(3_897, summary.OutstandingContracts);
        Assert.Equal(557, summary.PaidOffContracts);
        Assert.Equal(2_394, summary.InTermOutstandingContracts);
        Assert.Equal(165, summary.InTermPaidOffContracts);
        Assert.Equal(1_503, summary.ExpiredOutstandingContracts);
        Assert.Equal(392, summary.ExpiredPaidOffContracts);
        Assert.Equal(8, summary.WarningContracts);
        Assert.Equal(7, summary.UnresolvedMemberContracts);
        Assert.Equal(327, summary.ShadowExcludedContracts);
        Assert.Equal(4_447, summary.CanonicalMatchedContracts);
        Assert.Equal(7, summary.CanonicalMissingContracts);
        Assert.Equal(219_547_383.55m, summary.PrincipalOutstanding);
        Assert.Equal(85_068_837.45m, summary.ProfitOutstanding);
        Assert.Equal(304_616_221.00m, summary.TotalOutstanding);
        Assert.Equal(46_520_515.00m, summary.ExpiredOutstandingBalance);
        Assert.Equal(summary.TotalOutstanding,
            summary.PrincipalOutstanding + summary.ProfitOutstanding);
    }

    [Fact]
    public async Task GetSummaryAsync_PublishedAndSuperseded_SelectsOnlyCurrentPublished()
    {
        await using var database = await DashboardDatabase.CreateAsync();
        await database.SeedSnapshotAsync(
            PortfolioSnapshotStatus.Superseded,
            "C",
            asOfDate: new DateOnly(2026, 5, 31),
            totalContracts: 999);
        var publishedId = await database.SeedSnapshotAsync(
            PortfolioSnapshotStatus.Published,
            "D",
            totalContracts: 4_454);
        await using var context = database.CreateContext();

        var summary = await new DashboardService(context).GetSummaryAsync(DashboardDataModes.Published);

        Assert.True(summary.HasPublishedSnapshot);
        Assert.Equal(publishedId, summary.SnapshotId);
        Assert.Equal(4_454, summary.TotalContracts);
        Assert.NotEqual(999, summary.TotalContracts);
    }

    [Fact]
    public async Task GetSummaryAsync_NeverUsesLoanContractBalancesOrMembersCount()
    {
        await using var database = await DashboardDatabase.CreateAsync();
        await using (var setup = database.CreateContext())
        {
            var member = new Member
            {
                MemberNo = "M999",
                FirstName = "Protected",
                LastName = "Member",
                FullName = "Protected Member"
            };
            var loanType = new LoanType
            {
                Code = "TEST",
                Name = "Legacy test loan"
            };
            setup.LoanContracts.Add(new LoanContract
            {
                ContractNo = "สม-2569-999999",
                ContractDate = new DateTime(2026, 1, 1),
                Member = member,
                LoanType = loanType,
                PrincipalBalance = 999_999_999m,
                ProfitBalance = 888_888_888m,
                TotalBalance = 1_888_888_887m
            });
            await setup.SaveChangesAsync();
        }
        await using var noPublishedContext = database.CreateContext();
        var noPublished = await new DashboardService(noPublishedContext).GetSummaryAsync(DashboardDataModes.Published);
        Assert.False(noPublished.HasPublishedSnapshot);
        Assert.Null(noPublished.PrincipalOutstanding);

        await database.SeedSnapshotAsync(PortfolioSnapshotStatus.Published, "E");
        await using var publishedContext = database.CreateContext();
        var published = await new DashboardService(publishedContext).GetSummaryAsync(DashboardDataModes.Published);

        Assert.Equal(219_547_383.55m, published.PrincipalOutstanding);
        Assert.Equal(304_616_221m, published.TotalOutstanding);
        Assert.DoesNotContain(
            typeof(COOPAI.API.DTOs.Dashboard.DashboardSummaryDto).GetProperties(),
            property => property.Name.Contains("Member", StringComparison.Ordinal) &&
                        property.Name != "UnresolvedMemberContracts");
    }

    [Fact]
    public async Task GetSummaryAsync_ContractTypeBreakdownUsesOnlyPublishedRecords()
    {
        await using var database = await DashboardDatabase.CreateAsync();
        await database.SeedSnapshotAsync(
            PortfolioSnapshotStatus.Superseded,
            "F",
            asOfDate: new DateOnly(2026, 5, 31),
            records: [Record("สย", 9, PortfolioTermStatus.InTerm, PortfolioBalanceStatus.Outstanding, 9_000m, 900m)]);
        await database.SeedSnapshotAsync(
            PortfolioSnapshotStatus.Published,
            "G",
            records:
            [
                Record("สม", 1, PortfolioTermStatus.InTerm, PortfolioBalanceStatus.Outstanding, 100m, 20m),
                Record("สม", 2, PortfolioTermStatus.Expired, PortfolioBalanceStatus.PaidOff, 0m, 0m),
                Record("สจ", 3, PortfolioTermStatus.Expired, PortfolioBalanceStatus.Outstanding, 200m, 40m),
                Record("XX", 4, PortfolioTermStatus.InTerm, PortfolioBalanceStatus.Outstanding, 50m, 10m)
            ]);
        await using var context = database.CreateContext();

        var summary = await new DashboardService(context).GetSummaryAsync(DashboardDataModes.Published);

        Assert.Equal(3, summary.ContractTypes.Count);
        var general = Assert.Single(summary.ContractTypes, type => type.Prefix == "สม");
        Assert.Equal("สินเชื่อสามัญทั่วไป (รุ่นเก่า)", general.Name);
        Assert.Equal(2, general.ContractCount);
        Assert.Equal(1, general.OutstandingContractCount);
        Assert.Equal(1, general.PaidOffContractCount);
        Assert.Equal(1, general.InTermCount);
        Assert.Equal(1, general.ExpiredCount);
        Assert.Equal(100m, general.PrincipalOutstanding);
        Assert.Equal(20m, general.ProfitOutstanding);
        Assert.Equal(120m, general.TotalOutstanding);
        var motorcycle = Assert.Single(summary.ContractTypes, type => type.Prefix == "สจ");
        Assert.Equal(1, motorcycle.ExpiredCount);
        Assert.Equal(240m, motorcycle.TotalOutstanding);
        var unknown = Assert.Single(summary.ContractTypes, type => type.Prefix == "Unknown");
        Assert.Equal("ไม่ทราบประเภท", unknown.Name);
        Assert.Equal(60m, unknown.TotalOutstanding);
        Assert.DoesNotContain(summary.ContractTypes, type => type.Prefix == "สย");
        Assert.Equal(4, summary.ContractTypes.Sum(type => type.ContractCount));
        Assert.Equal(420m, summary.ContractTypes.Sum(type => type.TotalOutstanding));
    }

    [Fact]
    public async Task GetSummaryAsync_IsReadOnlyAndDoesNotMutateSnapshotGovernance()
    {
        await using var database = await DashboardDatabase.CreateAsync();
        var publishedAt = new DateTime(2026, 8, 24, 4, 0, 0, DateTimeKind.Utc);
        var id = await database.SeedSnapshotAsync(
            PortfolioSnapshotStatus.Published,
            "H",
            publishedAt: publishedAt,
            records: [Record("สห", 1, PortfolioTermStatus.InTerm, PortfolioBalanceStatus.Outstanding, 80m, 20m)]);
        await using var context = database.CreateContext();
        var before = await GovernanceAsync(context, id);

        _ = await new DashboardService(context).GetSummaryAsync(DashboardDataModes.Published);
        var after = await GovernanceAsync(context, id);

        Assert.Equal(before, after);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static async Task<(PortfolioSnapshotStatus Status, DateTime? PublishedAt, long Version, int Records)>
        GovernanceAsync(CoopDbContext context, int id)
    {
        var result = await context.PortfolioSnapshots
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Status,
                x.PublishedAt,
                Version = x.ConcurrencyVersion,
                Records = x.Records.Count
            })
            .SingleAsync();

        return (result.Status, result.PublishedAt, result.Version, result.Records);
    }

    private static PortfolioSnapshotRecord Record(
        string prefix,
        int number,
        PortfolioTermStatus term,
        PortfolioBalanceStatus balance,
        decimal principal,
        decimal profit) => new()
    {
        SourceRowNumber = number,
        SourceRecordKey = $"row:{number}",
        NormalizedContractNo = $"{(prefix == "XX" ? "สม" : prefix)}-2569-{number:000000}",
        LoanTypePrefix = prefix,
        SourceRowKind = PortfolioSourceRowKind.Contract,
        OpeningSide = PortfolioOpeningSide.Previous,
        TermStatus = term,
        BalanceStatus = balance,
        CanonicalMatchStatus = PortfolioCanonicalMatchStatus.Matched,
        MemberMatchStatus = PortfolioMemberMatchStatus.Matched,
        InclusionStatus = PortfolioSnapshotInclusionStatus.Included,
        WarningCodesJson = "[]",
        PrincipalOutstanding = principal,
        ProfitOutstanding = profit,
        TotalOutstanding = principal + profit
    };

    private sealed class DashboardDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly DbContextOptions<CoopDbContext> _options;

        private DashboardDatabase()
        {
            _options = new DbContextOptionsBuilder<CoopDbContext>()
                .UseSqlite(_connection)
                .Options;
        }

        public static async Task<DashboardDatabase> CreateAsync()
        {
            var database = new DashboardDatabase();
            await database._connection.OpenAsync();
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public CoopDbContext CreateContext() => new(_options);

        public async Task<int> SeedSnapshotAsync(
            PortfolioSnapshotStatus status,
            string seed,
            DateOnly? asOfDate = null,
            int totalContracts = 4_454,
            DateTime? publishedAt = null,
            IReadOnlyCollection<PortfolioSnapshotRecord>? records = null)
        {
            await using var context = CreateContext();
            var snapshot = BaselineSnapshot(status, seed, asOfDate, totalContracts, publishedAt);
            if (records is not null)
                snapshot.Records = records.ToList();
            context.PortfolioSnapshots.Add(snapshot);
            await context.SaveChangesAsync();
            return snapshot.Id;
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private static PortfolioSnapshot BaselineSnapshot(
        PortfolioSnapshotStatus status,
        string seed,
        DateOnly? asOfDate,
        int totalContracts,
        DateTime? publishedAt) => new()
    {
        AsOfDate = asOfDate ?? new DateOnly(2026, 6, 30),
        Revision = 1,
        Status = status,
        SourceFileName = $"{seed}.xlsx",
        SourceFileHash = new string(seed[0], 64),
        SnapshotContentHash = new string(seed[0], 64),
        SourceFileSizeBytes = 1,
        SourceRetrievedAt = DateTime.UtcNow,
        PublishedAt = status == PortfolioSnapshotStatus.Published
            ? publishedAt ?? DateTime.UtcNow
            : null,
        TotalSourceRows = totalContracts == 4_454 ? 5_084 : totalContracts,
        TotalContractCount = totalContracts,
        PlaceholderRowCount = totalContracts == 4_454 ? 630 : 0,
        MatchedCanonicalCount = totalContracts == 4_454 ? 4_447 : totalContracts,
        MissingCanonicalCount = totalContracts == 4_454 ? 7 : 0,
        UnresolvedMemberContractCount = totalContracts == 4_454 ? 7 : 0,
        WarningRecordCount = totalContracts == 4_454 ? 8 : 0,
        ShadowExcludedCount = totalContracts == 4_454 ? 327 : 0,
        InTermContractCount = totalContracts == 4_454 ? 2_559 : totalContracts,
        ExpiredContractCount = totalContracts == 4_454 ? 1_895 : 0,
        OutstandingContractCount = totalContracts == 4_454 ? 3_897 : totalContracts,
        PaidOffContractCount = totalContracts == 4_454 ? 557 : 0,
        InTermOutstandingContractCount = totalContracts == 4_454 ? 2_394 : totalContracts,
        InTermPaidOffContractCount = totalContracts == 4_454 ? 165 : 0,
        ExpiredOutstandingContractCount = totalContracts == 4_454 ? 1_503 : 0,
        ExpiredPaidOffContractCount = totalContracts == 4_454 ? 392 : 0,
        PrincipalOutstanding = totalContracts == 4_454 ? 219_547_383.55m : 0m,
        ProfitOutstanding = totalContracts == 4_454 ? 85_068_837.45m : 0m,
        TotalOutstanding = totalContracts == 4_454 ? 304_616_221m : 0m,
        ExpiredOutstandingTotal = totalContracts == 4_454 ? 46_520_515m : 0m
    };
}
