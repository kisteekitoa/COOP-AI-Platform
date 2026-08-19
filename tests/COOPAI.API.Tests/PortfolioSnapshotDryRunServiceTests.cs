using System.Text.Json;
using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Models.Portfolio;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace COOPAI.API.Tests;

public sealed class PortfolioSnapshotDryRunServiceTests
{
    [Fact]
    public async Task BuildAsync_MatchesCanonicalContractAndMemberWithoutWriting()
    {
        await using var dbContext = CreateDbContext();
        var member = AddMember(dbContext, "M001");
        await dbContext.SaveChangesAsync();
        AddContract(dbContext, "\u0E2A\u0E21-2569-000001", member.Id);
        await dbContext.SaveChangesAsync();
        var row = Row("M001", "\u0E2A\u0E21-2569-000001", 100m, 20m, 120m, 10m, 2m, 12m, 90m, 18m, 108m);
        var source = Source([row]);
        var service = CreateService(dbContext, source);
        var loansBefore = await dbContext.LoanContracts.CountAsync();

        var result = await service.BuildAsync("controlled-copy.xlsx", source.AsOfDate);

        Assert.True(result.IsValid);
        Assert.Equal(PortfolioSnapshotStatus.Validated, result.Snapshot.Status);
        var record = Assert.Single(result.Snapshot.Records);
        Assert.NotNull(record.LoanContractId);
        Assert.Equal(member.Id, record.MemberId);
        Assert.Equal(PortfolioCanonicalMatchStatus.Matched, record.CanonicalMatchStatus);
        Assert.Equal(PortfolioMemberMatchStatus.Matched, record.MemberMatchStatus);
        Assert.Equal(PortfolioTermStatus.Expired, record.TermStatus);
        Assert.Equal(PortfolioBalanceStatus.Outstanding, record.BalanceStatus);
        Assert.Equal(0m, result.Snapshot.TotalDifference);
        Assert.Equal(loansBefore, await dbContext.LoanContracts.CountAsync());
        Assert.DoesNotContain(dbContext.ChangeTracker.Entries(), x => x.State == EntityState.Added);
    }

    [Fact]
    public async Task BuildAsync_SevenMissingCanonicalContractsAreRetainedWithWarnings()
    {
        await using var dbContext = CreateDbContext();
        AddMember(dbContext, "M001");
        await dbContext.SaveChangesAsync();
        var rows = Enumerable.Range(1, 7)
            .Select(index => Row(
                "M001",
                $"\u0E2A\u0E21-2569-{index:000000}",
                -100m, 0m, -100m,
                0m, 0m, 0m,
                -100m, 0m, -100m))
            .ToArray();
        var source = Source(rows);

        var result = await CreateService(dbContext, source).BuildAsync("controlled-copy.xlsx", source.AsOfDate);

        Assert.True(result.IsValid);
        Assert.Equal(7, result.Snapshot.MissingCanonicalCount);
        Assert.Equal(7, result.Snapshot.Records.Count);
        Assert.All(result.Snapshot.Records, record =>
        {
            Assert.Null(record.LoanContractId);
            Assert.Null(record.MemberId);
            Assert.Equal(PortfolioCanonicalMatchStatus.Missing, record.CanonicalMatchStatus);
            Assert.Equal(PortfolioMemberMatchStatus.Missing, record.MemberMatchStatus);
            Assert.Equal(PortfolioSnapshotInclusionStatus.IncludedWithWarning, record.InclusionStatus);
            Assert.Contains(PortfolioSnapshotCodes.MissingCanonicalContract, WarningCodes(record));
            Assert.Contains(PortfolioSnapshotCodes.Negative, WarningCodes(record));
        });
        Assert.Empty(dbContext.LoanContracts);
    }

    [Fact]
    public async Task BuildAsync_MalformedDatabaseShadowsAreExcludedWithoutPersistingTheirText()
    {
        await using var dbContext = CreateDbContext();
        var member = AddMember(dbContext, "M001");
        await dbContext.SaveChangesAsync();
        AddContract(dbContext, "\u0E2A\u0E21-2569-000001", member.Id);
        for (var index = 1; index <= 327; index++)
            AddContract(dbContext, $"Malformed Person {index}", member.Id);
        await dbContext.SaveChangesAsync();
        var source = Source([Row("M001", "\u0E2A\u0E21-2569-000001")]);

        var result = await CreateService(dbContext, source).BuildAsync("controlled-copy.xlsx", source.AsOfDate);

        Assert.True(result.IsValid);
        Assert.Equal(327, result.Snapshot.ShadowExcludedCount);
        Assert.Equal(327, result.Snapshot.Exclusions.Count);
        Assert.All(result.Snapshot.Exclusions, exclusion =>
            Assert.Equal(PortfolioSnapshotCodes.MalformedShadowContractNo, exclusion.ReasonCode));
        Assert.DoesNotContain(
            typeof(PortfolioSnapshotExclusion).GetProperties(),
            property => property.Name.Contains("ContractNo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_EightReviewedWarningsRemainIncludedInCountsAndTotals()
    {
        await using var dbContext = CreateDbContext();
        var member = AddMember(dbContext, "M001");
        await dbContext.SaveChangesAsync();
        var rows = new List<PortfolioSourceRow>();
        for (var index = 1; index <= 8; index++)
        {
            var contractNo = $"\u0E2A\u0E21-2569-{index:000000}";
            AddContract(dbContext, contractNo, member.Id);
            rows.Add(index <= 7
                ? Row("M001", contractNo, -100m, 0m, -100m, 0m, 0m, 0m, -100m, 0m, -100m)
                : Row(
                    "M001", contractNo,
                    100m, 20m, 120m,
                    10m, 2m, 12m,
                    90m, 18m, 108m,
                    displayedOpening: new PortfolioFinancialValues(100m, 20m, 121m)));
        }
        await dbContext.SaveChangesAsync();
        var source = Source(rows);

        var result = await CreateService(dbContext, source).BuildAsync("controlled-copy.xlsx", source.AsOfDate);

        Assert.True(result.IsValid);
        Assert.Equal(8, result.Snapshot.WarningRecordCount);
        Assert.Equal(8, result.Snapshot.TotalContractCount);
        Assert.All(result.Snapshot.Records, record =>
            Assert.Equal(PortfolioSnapshotInclusionStatus.IncludedWithWarning, record.InclusionStatus));
        Assert.Equal(-600m, result.Snapshot.PrincipalOpening);
        Assert.Equal(-610m, result.Snapshot.PrincipalOutstanding);
    }

    [Fact]
    public async Task BuildAsync_DuplicateSourceContractNo_IsBlocking()
    {
        await using var dbContext = CreateDbContext();
        var member = AddMember(dbContext, "M001");
        await dbContext.SaveChangesAsync();
        AddContract(dbContext, "\u0E2A\u0E21-2569-000001", member.Id);
        await dbContext.SaveChangesAsync();
        var source = Source(
        [
            Row("M001", "\u0E2A\u0E21-2569-000001"),
            Row("M001", " \u0E2A\u0E21\u20112569\u2011000001 ")
        ]);

        var result = await CreateService(dbContext, source).BuildAsync("controlled-copy.xlsx", source.AsOfDate);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, x => x.Code == "DuplicateSourceContractNo");
    }

    [Fact]
    public async Task BuildAsync_MalformedSourceContractNo_IsBlocking()
    {
        await using var dbContext = CreateDbContext();
        AddMember(dbContext, "M001");
        await dbContext.SaveChangesAsync();
        var source = Source([Row("M001", "Person Name")]);

        var result = await CreateService(dbContext, source).BuildAsync("controlled-copy.xlsx", source.AsOfDate);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, x => x.Code == "MalformedSourceContractNo");
        Assert.Empty(result.Snapshot.Records);
    }

    [Fact]
    public async Task BuildAsync_MoreThanOneInTermContractForStableMember_IsBlocking()
    {
        await using var dbContext = CreateDbContext();
        var member1 = AddMember(dbContext, " M001 ");
        await dbContext.SaveChangesAsync();
        var rows = new[]
        {
            Row("M001", "\u0E2A\u0E21-2569-000001", expireDate: new DateOnly(2026, 6, 30)),
            Row("M001", "\u0E2A\u0E21-2569-000002", expireDate: new DateOnly(2026, 7, 1))
        };
        AddContract(dbContext, rows[0].ContractNo, member1.Id);
        AddContract(dbContext, rows[1].ContractNo, member1.Id);
        await dbContext.SaveChangesAsync();
        var source = Source(rows);

        var result = await CreateService(dbContext, source).BuildAsync("controlled-copy.xlsx", source.AsOfDate);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues.Where(x => x.Code == "MultipleInTermContractsForMember"));
        Assert.Contains($"MemberId {member1.Id}", issue.Message);
        Assert.DoesNotContain("Test Member", issue.Message);
    }

    [Fact]
    public async Task BuildAsync_MultipleExpiredContractsForStableMember_AreAllowed()
    {
        await using var dbContext = CreateDbContext();
        var member = AddMember(dbContext, "M001");
        await dbContext.SaveChangesAsync();
        var rows = new[]
        {
            Row("M001", "\u0E2A\u0E21-2568-000001"),
            Row("M001", "\u0E2A\u0E21-2568-000002")
        };
        AddContract(dbContext, rows[0].ContractNo, member.Id);
        AddContract(dbContext, rows[1].ContractNo, member.Id);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, Source(rows)).BuildAsync(
            "controlled-copy.xlsx",
            new DateOnly(2026, 6, 30));

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Snapshot.ExpiredContractCount);
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "MultipleInTermContractsForMember");
    }

    [Fact]
    public async Task BuildAsync_ClassifiesAllFourIndependentTermAndBalanceCombinations()
    {
        await using var dbContext = CreateDbContext();
        var asOfDate = new DateOnly(2026, 6, 30);
        var rows = new[]
        {
            Row("M001", "\u0E2A\u0E21-2569-000001", 10m, 0m, 10m, 0m, 0m, 0m, 10m, 0m, 10m, expireDate: asOfDate),
            Row("M002", "\u0E2A\u0E21-2569-000002", 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, expireDate: asOfDate.AddDays(1)),
            Row("M003", "\u0E2A\u0E21-2568-000003", 20m, 0m, 20m, 0m, 0m, 0m, 20m, 0m, 20m, expireDate: asOfDate.AddDays(-1)),
            Row("M004", "\u0E2A\u0E21-2568-000004", 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, expireDate: asOfDate.AddDays(-1))
        };
        for (var index = 0; index < rows.Length; index++)
        {
            var member = AddMember(dbContext, $"M{index + 1:000}");
            await dbContext.SaveChangesAsync();
            AddContract(dbContext, rows[index].ContractNo, member.Id);
        }
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, Source(rows)).BuildAsync("controlled-copy.xlsx", asOfDate);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Snapshot.InTermContractCount);
        Assert.Equal(2, result.Snapshot.ExpiredContractCount);
        Assert.Equal(2, result.Snapshot.OutstandingContractCount);
        Assert.Equal(2, result.Snapshot.PaidOffContractCount);
        Assert.Equal(1, result.Snapshot.InTermOutstandingContractCount);
        Assert.Equal(1, result.Snapshot.InTermPaidOffContractCount);
        Assert.Equal(1, result.Snapshot.ExpiredOutstandingContractCount);
        Assert.Equal(1, result.Snapshot.ExpiredPaidOffContractCount);
    }

    [Fact]
    public async Task BuildAsync_WarningDoesNotChangeTermOrBalanceStatus()
    {
        await using var dbContext = CreateDbContext();
        var member = AddMember(dbContext, "M001");
        await dbContext.SaveChangesAsync();
        var asOfDate = new DateOnly(2026, 6, 30);
        var row = Row(
            "M001", "\u0E2A\u0E21-2569-000001",
            -1m, 0m, -1m,
            -11m, 0m, -11m,
            10m, 0m, 10m,
            expireDate: asOfDate);
        AddContract(dbContext, row.ContractNo, member.Id);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, Source([row])).BuildAsync("controlled-copy.xlsx", asOfDate);

        Assert.True(result.IsValid);
        var record = Assert.Single(result.Snapshot.Records);
        Assert.Equal(PortfolioSnapshotInclusionStatus.IncludedWithWarning, record.InclusionStatus);
        Assert.Equal(PortfolioTermStatus.InTerm, record.TermStatus);
        Assert.Equal(PortfolioBalanceStatus.Outstanding, record.BalanceStatus);
        Assert.Contains(PortfolioSnapshotCodes.Negative, WarningCodes(record));
    }

    [Fact]
    public async Task BuildAsync_DoesNotUseMemberNameForLinkage()
    {
        await using var dbContext = CreateDbContext();
        var member = AddMember(dbContext, "M001");
        await dbContext.SaveChangesAsync();
        var row = Row("Test Member", "\u0E2A\u0E21-2569-000001");
        AddContract(dbContext, row.ContractNo, member.Id);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, Source([row])).BuildAsync(
            "controlled-copy.xlsx",
            new DateOnly(2026, 6, 30));

        Assert.True(result.IsValid);
        var record = Assert.Single(result.Snapshot.Records);
        Assert.Null(record.MemberId);
        Assert.Equal(PortfolioMemberMatchStatus.Missing, record.MemberMatchStatus);
        Assert.Equal(PortfolioSnapshotInclusionStatus.IncludedWithWarning, record.InclusionStatus);
    }

    [Fact]
    public void ContractStatusClassifier_UsesSnapshotAsOfDateInsteadOfCurrentSystemDate()
    {
        var expiry = new DateOnly(2026, 6, 30);

        Assert.Equal(
            PortfolioTermStatus.InTerm,
            PortfolioContractStatusClassifier.ClassifyTerm(expiry, new DateOnly(2026, 6, 30)));
        Assert.Equal(
            PortfolioTermStatus.Expired,
            PortfolioContractStatusClassifier.ClassifyTerm(expiry, new DateOnly(2026, 7, 1)));
        Assert.Equal(PortfolioTermStatus.Unknown, PortfolioContractStatusClassifier.ClassifyTerm(null, expiry));
        Assert.Equal(PortfolioBalanceStatus.Outstanding, PortfolioContractStatusClassifier.ClassifyBalance(0.01m));
        Assert.Equal(PortfolioBalanceStatus.PaidOff, PortfolioContractStatusClassifier.ClassifyBalance(0m));
        Assert.Equal(PortfolioBalanceStatus.Unknown, PortfolioContractStatusClassifier.ClassifyBalance(null));
        Assert.Equal(PortfolioBalanceStatus.Unknown, PortfolioContractStatusClassifier.ClassifyBalance(-0.01m));
    }

    [Fact]
    public void ContentHash_IsDeterministicAndExcludesIdsTimestampsAndRecordOrder()
    {
        var first = HashSnapshot([HashRecord("B-2569-2", 99, 98), HashRecord("A-2569-1", 1, 2)], DateTime.UtcNow);
        var second = HashSnapshot([HashRecord("A-2569-1", 501, 502), HashRecord("B-2569-2", 599, 598)], DateTime.UtcNow.AddDays(1));

        Assert.Equal(first, second);
    }

    [Fact]
    public void EfModel_HasRequiredUniqueIndexesAndNoActionForeignKeys()
    {
        using var dbContext = CreateDbContext();
        var snapshot = dbContext.Model.FindEntityType(typeof(PortfolioSnapshot))!;
        var record = dbContext.Model.FindEntityType(typeof(PortfolioSnapshotRecord))!;
        var exclusion = dbContext.Model.FindEntityType(typeof(PortfolioSnapshotExclusion))!;

        Assert.Contains(snapshot.GetIndexes(), index =>
            index.IsUnique && PropertyNames(index).SequenceEqual(["AsOfDate", "Revision"]));
        Assert.Contains(record.GetIndexes(), index =>
            index.IsUnique && PropertyNames(index).SequenceEqual(["PortfolioSnapshotId", "NormalizedContractNo"]));
        Assert.Contains(exclusion.GetIndexes(), index =>
            index.IsUnique && PropertyNames(index).SequenceEqual(["PortfolioSnapshotId", "LoanContractId", "ReasonCode"]));
        Assert.All(record.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
        Assert.All(exclusion.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));

        var financialProperties = snapshot.GetProperties()
            .Where(property => property.ClrType == typeof(decimal) &&
                               (property.Name.Contains("Opening", StringComparison.Ordinal) ||
                               property.Name.Contains("Repayment", StringComparison.Ordinal) ||
                               property.Name.Contains("Outstanding", StringComparison.Ordinal) ||
                               property.Name.Contains("Difference", StringComparison.Ordinal)));
        Assert.All(financialProperties, property =>
        {
            Assert.Equal(19, property.GetPrecision());
            Assert.Equal(2, property.GetScale());
        });
    }

    [Fact]
    public async Task BuildAsync_LockedJune2026Baseline_ReconcilesExactly()
    {
        await using var dbContext = CreateDbContext();
        var baseline = PortfolioSnapshotAcceptanceBaseline.June2026;
        var rows = new List<PortfolioSourceRow>(baseline.TotalSourceRows);
        var members = new List<Member>(baseline.MatchedCanonicalCount);
        for (var memberIndex = 1; memberIndex <= baseline.MatchedCanonicalCount; memberIndex++)
            members.Add(AddMember(dbContext, $"M{memberIndex:0000}"));
        await dbContext.SaveChangesAsync();

        for (var index = 0; index < baseline.TotalContractCount; index++)
        {
            var contractNo = $"\u0E2A\u0E21-2569-{index + 1:000000}";
            var memberNo = index < baseline.MatchedCanonicalCount
                ? $"M{index + 1:0000}"
                : $"UNRESOLVED{index + 1:0000}";
            var inTerm = index < baseline.InTermContractCount;
            var outstanding = index < baseline.InTermOutstandingContractCount ||
                              index >= baseline.InTermContractCount &&
                              index < baseline.InTermContractCount + baseline.ExpiredOutstandingContractCount;
            var outstandingValues = index switch
            {
                0 => new PortfolioFinancialValues(173_024_475.55m, 85_068_837.45m, 258_093_313.00m),
                2_559 => new PortfolioFinancialValues(46_519_013.00m, 0m, 46_519_013.00m),
                _ when outstanding => new PortfolioFinancialValues(1m, 0m, 1m),
                _ => new PortfolioFinancialValues(0m, 0m, 0m)
            };
            var repaymentValues = index switch
            {
                0 => new PortfolioFinancialValues(34_253_700.60m, 8_344_017.40m, 42_597_718.00m),
                >= 4_447 => new PortfolioFinancialValues(-100m, 0m, -100m),
                _ => new PortfolioFinancialValues(0m, 0m, 0m)
            };
            var openingValues = new PortfolioFinancialValues(
                outstandingValues.Principal + repaymentValues.Principal,
                outstandingValues.Profit + repaymentValues.Profit,
                outstandingValues.Total + repaymentValues.Total);
            var displayedOpening = index == 1
                ? openingValues with { Total = openingValues.Total + 1m }
                : openingValues;
            var row = Row(
                memberNo,
                contractNo,
                openingValues.Principal,
                openingValues.Profit,
                openingValues.Total,
                repaymentValues.Principal,
                repaymentValues.Profit,
                repaymentValues.Total,
                outstandingValues.Principal,
                outstandingValues.Profit,
                outstandingValues.Total,
                displayedOpening,
                expireDate: inTerm ? baseline.AsOfDate : baseline.AsOfDate.AddDays(-1));

            rows.Add(row);
            if (index < baseline.MatchedCanonicalCount)
                AddContract(dbContext, contractNo, members[index].Id);
        }

        for (var index = 0; index < baseline.PlaceholderRowCount; index++)
            rows.Add(PlaceholderRow(index));
        for (var index = 0; index < baseline.ShadowExcludedCount; index++)
            AddContract(dbContext, $"Malformed Person {index + 1}", members[0].Id);
        await dbContext.SaveChangesAsync();

        var source = Source(rows, new PortfolioReportSummary(baseline.Opening, baseline.Repayment, baseline.Outstanding));
        var result = await CreateService(dbContext, source).BuildAsync(
            "controlled-copy.xlsx",
            baseline.AsOfDate,
            baseline);

        Assert.True(result.IsValid, string.Join(", ", result.Issues.Select(x => x.Code)));
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
        Assert.Equal(baseline.Opening, SnapshotOpening(result.Snapshot));
        Assert.Equal(baseline.Repayment, SnapshotRepayment(result.Snapshot));
        Assert.Equal(baseline.Outstanding, SnapshotOutstanding(result.Snapshot));
        Assert.Equal(0m, result.Snapshot.PrincipalDifference);
        Assert.Equal(0m, result.Snapshot.ProfitDifference);
        Assert.Equal(0m, result.Snapshot.TotalDifference);
        Assert.Equal(0m, result.Snapshot.ComponentDifference);
    }

    private static CoopDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CoopDbContext>()
            .UseInMemoryDatabase($"portfolio-snapshot-{Guid.NewGuid():N}")
            .Options;
        return new CoopDbContext(options);
    }

    private static PortfolioSnapshotDryRunService CreateService(
        CoopDbContext dbContext,
        PortfolioSourceReadResult source) =>
        new(dbContext, new FakePortfolioSnapshotSource(source));

    private static Member AddMember(CoopDbContext dbContext, string memberNo)
    {
        var member = new Member
        {
            MemberNo = memberNo,
            FirstName = "Test",
            LastName = "Member",
            FullName = "Test Member"
        };
        dbContext.Members.Add(member);
        return member;
    }

    private static void AddContract(CoopDbContext dbContext, string contractNo, int memberId)
    {
        dbContext.LoanContracts.Add(new LoanContract
        {
            ContractNo = contractNo,
            ContractDate = new DateTime(2026, 1, 1),
            MemberId = memberId,
            LoanTypeId = 1
        });
    }

    private static PortfolioSourceRow Row(
        string memberNo,
        string contractNo,
        decimal openingPrincipal = 100m,
        decimal openingProfit = 20m,
        decimal openingTotal = 120m,
        decimal repaymentPrincipal = 10m,
        decimal repaymentProfit = 2m,
        decimal repaymentTotal = 12m,
        decimal outstandingPrincipal = 90m,
        decimal outstandingProfit = 18m,
        decimal outstandingTotal = 108m,
        PortfolioFinancialValues? displayedOpening = null,
        DateOnly? contractDate = null,
        DateOnly? expireDate = null) => new()
    {
        SourceRowNumber = Random.Shared.Next(7, 1_000_000),
        SourceRecordKey = $"key:{Guid.NewGuid():N}",
        MemberNo = memberNo,
        ContractNo = contractNo,
        ContractDate = contractDate ?? new DateOnly(2025, 1, 1),
        ExpireDate = expireDate ?? new DateOnly(2025, 12, 31),
        SourceRowKind = PortfolioSourceRowKind.Contract,
        OpeningSide = PortfolioOpeningSide.Previous,
        HasValidOpeningValues = true,
        HasValidRepaymentValues = true,
        HasValidOutstandingValues = true,
        Opening = new PortfolioFinancialValues(openingPrincipal, openingProfit, openingTotal),
        Repayment = new PortfolioFinancialValues(repaymentPrincipal, repaymentProfit, repaymentTotal),
        Outstanding = new PortfolioFinancialValues(outstandingPrincipal, outstandingProfit, outstandingTotal),
        DisplayedOpening = displayedOpening ?? new PortfolioFinancialValues(openingPrincipal, openingProfit, openingTotal)
    };

    private static PortfolioSourceRow PlaceholderRow(int index) => new()
    {
        SourceRowNumber = 10_000 + index,
        SourceRecordKey = $"placeholder:{index}",
        MemberNo = $"I{index:0000}",
        ContractNo = $"\u0E2A\u0E21-2568-{index + 1:000000}",
        SourceRowKind = PortfolioSourceRowKind.TemplatePlaceholder,
        OpeningSide = PortfolioOpeningSide.Unknown
    };

    private static PortfolioSourceReadResult Source(
        IReadOnlyList<PortfolioSourceRow> rows,
        PortfolioReportSummary? reportSummary = null)
    {
        reportSummary ??= new PortfolioReportSummary(
            new PortfolioFinancialValues(rows.Where(IsContract).Sum(x => x.Opening.Principal), rows.Where(IsContract).Sum(x => x.Opening.Profit), rows.Where(IsContract).Sum(x => x.Opening.Total)),
            new PortfolioFinancialValues(rows.Where(IsContract).Sum(x => x.Repayment.Principal), rows.Where(IsContract).Sum(x => x.Repayment.Profit), rows.Where(IsContract).Sum(x => x.Repayment.Total)),
            new PortfolioFinancialValues(rows.Where(IsContract).Sum(x => x.Outstanding.Principal), rows.Where(IsContract).Sum(x => x.Outstanding.Profit), rows.Where(IsContract).Sum(x => x.Outstanding.Total)));
        return new PortfolioSourceReadResult
        {
            AsOfDate = new DateOnly(2026, 6, 30),
            SourceFileName = "controlled-copy.xlsx",
            SourceFileHash = new string('A', 64),
            SourceFileSizeBytes = 123,
            SourceRetrievedAt = DateTime.UtcNow,
            SourceDataThroughDate = new DateOnly(2026, 6, 30),
            Rows = rows,
            ReportSummary = reportSummary
        };
    }

    private static bool IsContract(PortfolioSourceRow row) =>
        row.SourceRowKind == PortfolioSourceRowKind.Contract;

    private static string[] WarningCodes(PortfolioSnapshotRecord record) =>
        JsonSerializer.Deserialize<string[]>(record.WarningCodesJson)!;

    private static string HashSnapshot(IEnumerable<PortfolioSnapshotRecord> records, DateTime createdAt)
    {
        var snapshot = new PortfolioSnapshot
        {
            AsOfDate = new DateOnly(2026, 6, 30),
            DefinitionVersion = 1,
            CurrencyCode = "THB",
            CreatedAt = createdAt,
            Records = records.ToList()
        };
        return new PortfolioSnapshotContentHasher().Compute(snapshot);
    }

    private static PortfolioSnapshotRecord HashRecord(string contractNo, long id, int databaseId) => new()
    {
        Id = id,
        PortfolioSnapshotId = databaseId,
        SourceRecordKey = Guid.NewGuid().ToString("N"),
        NormalizedContractNo = contractNo,
        LoanContractId = databaseId,
        MemberId = databaseId,
        PrincipalOpening = 100m,
        ProfitOpening = 20m,
        TotalOpening = 120m,
        PrincipalRepayment = 10m,
        ProfitRepayment = 2m,
        TotalRepayment = 12m,
        PrincipalOutstanding = 90m,
        ProfitOutstanding = 18m,
        TotalOutstanding = 108m,
        WarningCodesJson = "[\"B\",\"A\"]",
        InclusionStatus = PortfolioSnapshotInclusionStatus.IncludedWithWarning,
        CanonicalMatchStatus = PortfolioCanonicalMatchStatus.Matched,
        MemberMatchStatus = PortfolioMemberMatchStatus.Matched
    };

    private static string[] PropertyNames(IIndex index) =>
        index.Properties.Select(property => property.Name).ToArray();

    private static PortfolioFinancialValues SnapshotOpening(PortfolioSnapshot snapshot) =>
        new(snapshot.PrincipalOpening, snapshot.ProfitOpening, snapshot.TotalOpening);

    private static PortfolioFinancialValues SnapshotRepayment(PortfolioSnapshot snapshot) =>
        new(snapshot.PrincipalRepayment, snapshot.ProfitRepayment, snapshot.TotalRepayment);

    private static PortfolioFinancialValues SnapshotOutstanding(PortfolioSnapshot snapshot) =>
        new(snapshot.PrincipalOutstanding, snapshot.ProfitOutstanding, snapshot.TotalOutstanding);

    private sealed class FakePortfolioSnapshotSource(PortfolioSourceReadResult result) : IPortfolioSnapshotSource
    {
        public Task<PortfolioSourceReadResult> ReadAsync(
            string controlledCopyPath,
            DateOnly asOfDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
