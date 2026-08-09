using System;
using System.Threading.Tasks;
using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Models.Import;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Services.Import;

public class MemberImporter
{
    private readonly CoopDbContext _dbContext;

    public MemberImporter(CoopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Member> ImportAsync(ImportLoanRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        if (string.IsNullOrWhiteSpace(record.MemberNo))
            throw new ArgumentException("MemberNo is required.", nameof(record));

        var normalizedMemberNo = record.MemberNo.Trim();

        var lowerMemberNo = normalizedMemberNo.ToLowerInvariant();

        var existingMember = await _dbContext.Members
            .FirstOrDefaultAsync(x => x.MemberNo.ToLower() == lowerMemberNo);

        if (existingMember != null)
            return existingMember;

        var member = new Member
        {
            MemberNo = normalizedMemberNo,
            FullName = record.MemberName?.Trim() ?? normalizedMemberNo,
            FirstName = record.MemberName?.Trim() ?? normalizedMemberNo,
            LastName = record.MemberName?.Trim() ?? normalizedMemberNo,
            IsActive = true
        };

        _dbContext.Members.Add(member);
        await _dbContext.SaveChangesAsync();

        return member;
    }
}
