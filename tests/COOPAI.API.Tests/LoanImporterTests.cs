using System;
using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Models.Import;
using COOPAI.API.Services.Import;
using Microsoft.EntityFrameworkCore;
using Xunit;

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
    public async Task ImportAsync_NewContract_CreatesLoanContract()
    {
        await using var dbContext = CreateDbContext();

        var member = new Member
        {
            MemberNo = "M100",
            FirstName = "Bob",
            LastName = "Jones",
            FullName = "Bob Jones",
            IsActive = true
        };
        dbContext.Members.Add(member);
        await dbContext.SaveChangesAsync();

        var record = new ImportLoanRecord
        {
            ContractNo = "C100",
            MemberNo = "M100",
            MemberName = "Bob Jones",
            ContractDate = new DateTime(2026, 1, 1),
            ExpireDate = new DateTime(2026, 12, 31),
            LoanAmount = 1000m,
            PrincipalBalance = 500m,
            ProfitBalance = 50m,
            TotalBalance = 550m,
            OverdueDays = 5
        };

        var importer = new LoanImporter(dbContext);
        var result = await importer.ImportAsync(record, member);

        Assert.False(result.IsUpdated);
        Assert.Equal("C100", result.Contract.ContractNo);
        Assert.Equal(member.Id, result.Contract.MemberId);
        Assert.Equal(1000m, result.Contract.LoanAmount);
        Assert.Equal(5, result.Contract.OverdueDays);

        Assert.Equal(1, await dbContext.LoanContracts.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_ExistingContract_UpdatesLoanContract()
    {
        await using var dbContext = CreateDbContext();

        var member = new Member
        {
            MemberNo = "M100",
            FirstName = "Bob",
            LastName = "Jones",
            FullName = "Bob Jones",
            IsActive = true
        };
        dbContext.Members.Add(member);
        await dbContext.SaveChangesAsync();

        var initialContract = new LoanContract
        {
            ContractNo = "C100",
            MemberId = member.Id,
            LoanTypeId = 1,
            ContractDate = new DateTime(2026, 1, 1),
            ExpireDate = new DateTime(2026, 12, 31),
            LoanAmount = 1000m,
            PrincipalBalance = 500m,
            ProfitBalance = 50m,
            TotalBalance = 550m,
            OverdueDays = 5,
            IsClosed = false
        };
        dbContext.LoanContracts.Add(initialContract);
        dbContext.LoanTypes.Add(new LoanType
        {
            Code = "DEFAULT",
            Name = "Default Loan Type",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var record = new ImportLoanRecord
        {
            ContractNo = "C100",
            MemberNo = "M100",
            MemberName = "Bob Jones",
            ContractDate = new DateTime(2026, 1, 1),
            ExpireDate = new DateTime(2027, 1, 1),
            LoanAmount = 2000m,
            PrincipalBalance = 1500m,
            ProfitBalance = 100m,
            TotalBalance = 1600m,
            OverdueDays = 10
        };

        var importer = new LoanImporter(dbContext);
        var result = await importer.ImportAsync(record, member);

        Assert.True(result.IsUpdated);
        Assert.Equal("C100", result.Contract.ContractNo);
        Assert.Equal(2000m, result.Contract.LoanAmount);
        Assert.Equal(1500m, result.Contract.PrincipalBalance);
        Assert.Equal(100m, result.Contract.ProfitBalance);
        Assert.Equal(1600m, result.Contract.TotalBalance);
        Assert.Equal(10, result.Contract.OverdueDays);
        Assert.Equal(new DateTime(2027, 1, 1), result.Contract.ExpireDate);
    }
}
