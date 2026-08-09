using System;
using System.Threading.Tasks;
using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Models.Import;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Services.Import;

public class LoanImporter
{
    private readonly CoopDbContext _dbContext;

    public LoanImporter(CoopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LoanImportResult> ImportAsync(ImportLoanRecord record, Member member)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        if (member == null)
            throw new ArgumentNullException(nameof(member));

        if (string.IsNullOrWhiteSpace(record.ContractNo))
            throw new ArgumentException("ContractNo is required.", nameof(record));

        var contractNo = record.ContractNo.Trim();

        var lowerContractNo = contractNo.ToLowerInvariant();

        var existingContract = await _dbContext.LoanContracts
            .FirstOrDefaultAsync(x => x.ContractNo.ToLower() == lowerContractNo);

        if (existingContract != null)
        {
            existingContract.LoanAmount = record.LoanAmount;
            existingContract.PrincipalBalance = record.PrincipalBalance;
            existingContract.ProfitBalance = record.ProfitBalance;
            existingContract.TotalBalance = record.TotalBalance;
            existingContract.OverdueDays = record.OverdueDays;

            if (record.ContractDate.HasValue)
                existingContract.ContractDate = record.ContractDate.Value;

            existingContract.ExpireDate = record.ExpireDate;

            _dbContext.LoanContracts.Update(existingContract);
            await _dbContext.SaveChangesAsync();

            return new LoanImportResult
            {
                Contract = existingContract,
                IsUpdated = true
            };
        }

        var loanType = await _dbContext.LoanTypes.FirstOrDefaultAsync();
        if (loanType == null)
        {
            loanType = new LoanType
            {
                Code = "DEFAULT",
                Name = "Default Loan Type",
                IsActive = true
            };

            _dbContext.LoanTypes.Add(loanType);
            await _dbContext.SaveChangesAsync();
        }

        var newContract = new LoanContract
        {
            ContractNo = contractNo,
            LoanTypeId = loanType.Id,
            MemberId = member.Id,
            Member = member,
            ContractDate = record.ContractDate ?? DateTime.UtcNow,
            ExpireDate = record.ExpireDate,
            LoanAmount = record.LoanAmount,
            PrincipalBalance = record.PrincipalBalance,
            ProfitBalance = record.ProfitBalance,
            TotalBalance = record.TotalBalance,
            OverdueDays = record.OverdueDays,
            IsClosed = false
        };

        _dbContext.LoanContracts.Add(newContract);
        await _dbContext.SaveChangesAsync();

        return new LoanImportResult
        {
            Contract = newContract,
            IsUpdated = false
        };
    }
}

public class LoanImportResult
{
    public LoanContract Contract { get; set; } = null!;

    public bool IsUpdated { get; set; }
}
