using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Models.Import;
using COOPAI.API.Services.Import;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Tests;

public class LoanImporterTests
{
    private static CoopDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CoopDbContext>()
            .UseInMemoryDatabase(databaseName: $"LoanImporter_{Guid.NewGuid():N}")
            .Options;

        return new CoopDbContext(options);
    }

    [Fact]
    public async Task ImportAsync_PreviousActive_UpdatesBalancesAndPreservesProtectedFields()
    {
        await using var dbContext = CreateDbContext();
        var contract = await SeedContractAsync(dbContext, loanAmount: 750000.129m, memberId: 41);
        var originalDate = contract.ContractDate;
        var originalExpireDate = contract.ExpireDate;
        var record = PreviousActiveRecord(
            contractNo: "  สห-2568-000001  ",
            principal: 11867.285m,
            profit: 3318.724m,
            total: 15186.009m);
        record.LoanAmount = 123000m;
        record.PrincipalBalance = 999999m;
        record.ProfitBalance = 999999m;
        record.TotalBalance = 999999m;
        record.ContractDate = new DateTime(2030, 1, 1);
        record.ExpireDate = new DateTime(2031, 1, 1);
        record.OverdueDays = 99;

        var result = await new LoanImporter(dbContext).ImportAsync(record);

        Assert.Equal(LoanImportStatus.Updated, result.Status);
        Assert.True(result.IsUpdated);
        Assert.Same(contract, result.Contract);
        Assert.Equal(11867.29m, contract.PrincipalBalance);
        Assert.Equal(3318.72m, contract.ProfitBalance);
        Assert.Equal(15186.01m, contract.TotalBalance);
        Assert.Equal(750000.129m, contract.LoanAmount);
        Assert.Equal(41, contract.MemberId);
        Assert.Equal("สห-2568-000001", contract.ContractNo);
        Assert.Equal(originalDate, contract.ContractDate);
        Assert.Equal(originalExpireDate, contract.ExpireDate);
        Assert.Equal(7, contract.OverdueDays);
    }

    [Fact]
    public async Task ImportAsync_CurrentActive_Regression000600UpdatesBalancesAndPreservesLoanAmountAndMemberId()
    {
        await using var dbContext = CreateDbContext();
        var contract = await SeedContractAsync(
            dbContext,
            contractNo: "สห-2569-000600",
            loanAmount: 987654.32m,
            memberId: 77);
        var record = CurrentActiveRecord(
            "สห-2569-000600",
            principal: 123000m,
            profit: 25830m,
            total: 148830m);
        record.LoanAmount = 123000m;
        record.PrincipalBalance = 0m;

        var result = await new LoanImporter(dbContext).ImportAsync(record);

        Assert.Equal(LoanImportStatus.Updated, result.Status);
        Assert.Equal(123000m, contract.PrincipalBalance);
        Assert.Equal(25830m, contract.ProfitBalance);
        Assert.Equal(148830m, contract.TotalBalance);
        Assert.Equal(987654.32m, contract.LoanAmount);
        Assert.Equal(77, contract.MemberId);
    }

    [Fact]
    public async Task ImportAsync_ExistingLoanAmountZero_RemainsZero()
    {
        await using var dbContext = CreateDbContext();
        var contract = await SeedContractAsync(dbContext, loanAmount: 0m);
        var record = PreviousActiveRecord("สห-2568-000001", 100m, 20m, 120m);
        record.LoanAmount = 100m;

        await new LoanImporter(dbContext).ImportAsync(record);

        Assert.Equal(0m, contract.LoanAmount);
        Assert.Equal(100m, contract.PrincipalBalance);
    }

    [Fact]
    public async Task ImportAsync_ExistingPositiveLoanAmount_RemainsExactlyUnchanged()
    {
        await using var dbContext = CreateDbContext();
        var contract = await SeedContractAsync(dbContext, loanAmount: 456789.12m);
        var record = CurrentActiveRecord("สห-2568-000001", 200m, 30m, 230m);
        record.LoanAmount = 200m;

        await new LoanImporter(dbContext).ImportAsync(record);

        Assert.Equal(456789.12m, contract.LoanAmount);
        Assert.NotEqual(record.LoanAmount, contract.LoanAmount);
    }

    [Fact]
    public async Task ImportAsync_MissingContract_ReturnsStructuredNotFoundWithoutCreatingContractOrMember()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Members.Add(new Member
        {
            MemberNo = "EXISTING",
            FirstName = "Existing",
            LastName = "Member",
            FullName = "Existing Member",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var memberCount = await dbContext.Members.CountAsync();
        var record = PreviousActiveRecord("สห-2569-009999", 100m, 20m, 120m);

        var result = await new LoanImporter(dbContext).ImportAsync(record);

        Assert.Equal(LoanImportStatus.ContractNotFound, result.Status);
        Assert.False(result.IsUpdated);
        Assert.Null(result.Contract);
        Assert.Equal("ContractNotFound", result.ErrorType);
        Assert.Contains("was not found and was not updated", result.ErrorMessage);
        Assert.Empty(dbContext.LoanContracts);
        Assert.Equal(memberCount, await dbContext.Members.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_StagesTrackedBalanceChangesWithoutCommittingIndependently()
    {
        var options = new DbContextOptionsBuilder<CoopDbContext>()
            .UseInMemoryDatabase(databaseName: $"LoanImporter_NoCommit_{Guid.NewGuid():N}")
            .Options;

        await using (var seedContext = new CoopDbContext(options))
        {
            await SeedContractAsync(seedContext);
        }

        await using (var importContext = new CoopDbContext(options))
        {
            var result = await new LoanImporter(importContext).ImportAsync(
                PreviousActiveRecord("\u0E2A\u0E2B-2568-000001", 100m, 20m, 120m));

            Assert.Equal(LoanImportStatus.Updated, result.Status);
            Assert.Equal(100m, result.Contract!.PrincipalBalance);
        }

        await using var verificationContext = new CoopDbContext(options);
        var persisted = await verificationContext.LoanContracts.AsNoTracking().SingleAsync();
        Assert.Equal(1m, persisted.PrincipalBalance);
        Assert.Equal(2m, persisted.ProfitBalance);
        Assert.Equal(3m, persisted.TotalBalance);
    }

    private static async Task<LoanContract> SeedContractAsync(
        CoopDbContext dbContext,
        string contractNo = "สห-2568-000001",
        decimal loanAmount = 500000m,
        int memberId = 25)
    {
        var contract = new LoanContract
        {
            ContractNo = contractNo,
            MemberId = memberId,
            LoanTypeId = 9,
            ContractDate = new DateTime(2020, 1, 1),
            ExpireDate = new DateTime(2030, 1, 1),
            LoanAmount = loanAmount,
            PrincipalBalance = 1m,
            ProfitBalance = 2m,
            TotalBalance = 3m,
            OverdueDays = 7,
            IsClosed = false
        };
        dbContext.LoanContracts.Add(contract);
        await dbContext.SaveChangesAsync();
        return contract;
    }

    private static ImportLoanRecord PreviousActiveRecord(
        string contractNo,
        decimal principal,
        decimal profit,
        decimal total) =>
        new()
        {
            ContractNo = contractNo,
            MemberNo = "UNRELIABLE-EXCEL-MEMBER",
            MemberName = "Excel Member",
            PreviousPrincipal = principal,
            PreviousPrincipalRaw = principal.ToString(),
            PreviousPrincipalParsedValue = principal,
            PreviousPrincipalParseStatus = "Parsed",
            PreviousProfit = profit,
            PreviousProfitRaw = profit.ToString(),
            PreviousProfitParsedValue = profit,
            PreviousProfitParseStatus = "Parsed",
            PreviousTotal = total,
            PreviousTotalRaw = total.ToString(),
            PreviousTotalParsedValue = total,
            PreviousTotalParseStatus = "Parsed",
            CurrentPrincipalParseStatus = "Blank",
            CurrentProfitParseStatus = "Blank",
            CurrentTotalParseStatus = "Blank"
        };

    private static ImportLoanRecord CurrentActiveRecord(
        string contractNo,
        decimal principal,
        decimal profit,
        decimal total) =>
        new()
        {
            ContractNo = contractNo,
            MemberNo = "UNRELIABLE-EXCEL-MEMBER",
            MemberName = "Excel Member",
            PreviousPrincipalParseStatus = "Blank",
            PreviousProfitParseStatus = "Blank",
            PreviousTotalParseStatus = "Blank",
            CurrentPrincipal = principal,
            CurrentPrincipalRaw = principal.ToString(),
            CurrentPrincipalParsedValue = principal,
            CurrentPrincipalParseStatus = "Parsed",
            CurrentProfit = profit,
            CurrentProfitRaw = profit.ToString(),
            CurrentProfitParsedValue = profit,
            CurrentProfitParseStatus = "Parsed",
            CurrentTotal = total,
            CurrentTotalRaw = total.ToString(),
            CurrentTotalParsedValue = total,
            CurrentTotalParseStatus = "Parsed"
        };
}
