using System;
using System.Threading.Tasks;
using COOPAI.API.Data;
using COOPAI.API.Models.Import;
using COOPAI.API.Models;
using Microsoft.EntityFrameworkCore;

namespace COOPAI.API.Services.Import;

public class LoanImporter
{
    private readonly CoopDbContext _dbContext;

    public LoanImporter(CoopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public virtual async Task<LoanImportResult> ImportAsync(ImportLoanRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        if (string.IsNullOrWhiteSpace(record.ContractNo))
            throw new ArgumentException("ContractNo is required.", nameof(record));

        var normalizedContractNo = NormalizeContractNo(record.ContractNo);

        var existingContract = await _dbContext.LoanContracts
            .FirstOrDefaultAsync(x => x.ContractNo.Trim().ToLower() == normalizedContractNo);

        if (existingContract == null)
        {
            return new LoanImportResult
            {
                Status = LoanImportStatus.ContractNotFound,
                ErrorType = "ContractNotFound",
                ErrorMessage = $"LoanContract '{record.ContractNo.Trim()}' was not found and was not updated."
            };
        }

        var balances = GetValidatedActiveBalances(record);

        existingContract.PrincipalBalance = NormalizeMoney(balances.Principal);
        existingContract.ProfitBalance = NormalizeMoney(balances.Profit);
        existingContract.TotalBalance = NormalizeMoney(balances.Total);

        return new LoanImportResult
        {
            Contract = existingContract,
            Status = LoanImportStatus.Updated
        };
    }

    private static string NormalizeContractNo(string contractNo) =>
        contractNo.Trim().ToLowerInvariant();

    private static ActiveBalances GetValidatedActiveBalances(ImportLoanRecord record)
    {
        var previousActive = IsParsed(record.PreviousPrincipalParseStatus, record.PreviousPrincipalParsedValue);
        var currentActive = IsParsed(record.CurrentPrincipalParseStatus, record.CurrentPrincipalParsedValue);

        if (previousActive == currentActive)
            throw new InvalidOperationException("ImportLoanRecord must have exactly one validated active balance side.");

        if (previousActive)
        {
            return new ActiveBalances(
                RequireValue(record.PreviousPrincipalParsedValue, "PreviousPrincipal"),
                record.PreviousProfitParsedValue ?? 0m,
                RequireValue(record.PreviousTotalParsedValue, "PreviousTotal"));
        }

        return new ActiveBalances(
            RequireValue(record.CurrentPrincipalParsedValue, "CurrentPrincipal"),
            record.CurrentProfitParsedValue ?? 0m,
            RequireValue(record.CurrentTotalParsedValue, "CurrentTotal"));
    }

    private static bool IsParsed(string status, decimal? value) =>
        status == "Parsed" && value.HasValue;

    private static decimal RequireValue(decimal? value, string fieldName) =>
        value ?? throw new InvalidOperationException($"Validated field {fieldName} has no value.");

    private static decimal NormalizeMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private readonly record struct ActiveBalances(decimal Principal, decimal Profit, decimal Total);
}

public enum LoanImportStatus
{
    Updated,
    ContractNotFound
}

public class LoanImportResult
{
    public LoanContract? Contract { get; set; }

    public LoanImportStatus Status { get; set; }

    public bool IsUpdated => Status == LoanImportStatus.Updated;

    public string ErrorType { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;
}
