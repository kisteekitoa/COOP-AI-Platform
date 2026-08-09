using System;
using COOPAI.API.Data;
using COOPAI.API.Models.Import;
using COOPAI.API.Services.Import;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace COOPAI.API.Tests;

public class MemberImporterTests
{
    private static CoopDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CoopDbContext>()
            .UseInMemoryDatabase(databaseName: $"MemberImporter_{Guid.NewGuid():N}")
            .Options;

        return new CoopDbContext(options);
    }

    [Fact]
    public async Task ImportAsync_NewMember_CreatesMember()
    {
        await using var dbContext = CreateDbContext();
        var importer = new MemberImporter(dbContext);

        var record = new ImportLoanRecord
        {
            MemberNo = "M123",
            MemberName = "Alice Smith"
        };

        var member = await importer.ImportAsync(record);

        Assert.NotNull(member);
        Assert.Equal("M123", member.MemberNo);
        Assert.Equal("Alice Smith", member.FullName);
        Assert.True(member.Id > 0);

        var storedMember = await dbContext.Members.FirstOrDefaultAsync(x => x.MemberNo == "M123");
        Assert.NotNull(storedMember);
    }

    [Fact]
    public async Task ImportAsync_ExistingMember_ReturnsSameMemberWithoutDuplicate()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Members.Add(new COOPAI.API.Models.Member
        {
            MemberNo = "M123",
            FullName = "Alice Smith",
            FirstName = "Alice",
            LastName = "Smith",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var importer = new MemberImporter(dbContext);
        var record = new ImportLoanRecord
        {
            MemberNo = "M123",
            MemberName = "Alice Smith"
        };

        var member = await importer.ImportAsync(record);

        Assert.NotNull(member);
        Assert.Equal("M123", member.MemberNo);
        Assert.Equal(1, await dbContext.Members.CountAsync());
    }
}
