using COOPAI.API.Data;
using COOPAI.API.Models.Import;
using COOPAI.API.Services.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OfficeOpenXml;
using System.Security.Cryptography;

namespace COOPAI.API.Tests;

public class ExcelImportServiceTests
{
    private const string AccountingZeroFormat = "_-* #,##0_-;\\-* #,##0_-;_-* \"-\"??_-;_-@_-";

    [Fact]
    public async Task ValidateOnlyAsync_ValidExistingContract_IsUpdateCandidateAndDatabaseIsUnchanged()
    {
        const string contractNo = "\u0E2A\u0E2B-2569-000001";
        var execution = await ValidateOnlyAsync(
            new[] { ValidRow(contractNo) },
            new DryRunSeed(contractNo, 777777.77m, 1m, 2m, 3m));

        Assert.True(execution.Result.Success);
        Assert.Equal(1, execution.Result.TotalRows);
        Assert.Equal(1, execution.Result.ValidRows);
        Assert.Equal(1, execution.Result.UpdateCandidates);
        Assert.Equal(1, execution.Result.ExistingContracts);
        Assert.Equal(0, execution.Result.MissingContracts);
        Assert.Equal(0, execution.Result.SkippedRows);
        Assert.Equal(0, execution.Result.FailedRows);

        await using var verificationContext = new CoopDbContext(execution.Options);
        var contract = await verificationContext.LoanContracts.AsNoTracking().SingleAsync();
        Assert.Equal(777777.77m, contract.LoanAmount);
        Assert.Equal(1m, contract.PrincipalBalance);
        Assert.Equal(2m, contract.ProfitBalance);
        Assert.Equal(3m, contract.TotalBalance);
        Assert.Equal(execution.MemberId, contract.MemberId);
        await AssertNoDryRunAuditWritesAsync(verificationContext);
    }

    [Fact]
    public async Task ValidateOnlyAsync_ValidMissingContract_IsReportedWithoutCreatingContractOrMember()
    {
        const string contractNo = "\u0E2A\u0E2B-2569-009999";
        var execution = await ValidateOnlyAsync(new[] { ValidRow(contractNo, existing: false) });

        Assert.False(execution.Result.Success);
        Assert.Equal(1, execution.Result.TotalRows);
        Assert.Equal(1, execution.Result.ValidRows);
        Assert.Equal(0, execution.Result.UpdateCandidates);
        Assert.Equal(0, execution.Result.ExistingContracts);
        Assert.Equal(1, execution.Result.MissingContracts);
        Assert.Equal(0, execution.Result.FailedRows);
        var error = Assert.Single(execution.Result.ErrorDetails);
        Assert.Equal("ContractNo", error.FieldName);
        Assert.Equal("ContractNotFound", error.ErrorType);
        Assert.Equal(contractNo, error.ParsedValue);

        await using var verificationContext = new CoopDbContext(execution.Options);
        Assert.Empty(await verificationContext.LoanContracts.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationContext.Members.AsNoTracking().ToListAsync());
        await AssertNoDryRunAuditWritesAsync(verificationContext);
    }

    [Fact]
    public async Task ValidateOnlyAsync_InactiveContract_IsSkippedWithoutDatabaseWrites()
    {
        var execution = await ValidateOnlyAsync(new[] { InactiveRow() });

        Assert.True(execution.Result.Success);
        Assert.Equal(1, execution.Result.TotalRows);
        Assert.Equal(0, execution.Result.ValidRows);
        Assert.Equal(0, execution.Result.UpdateCandidates);
        Assert.Equal(1, execution.Result.SkippedRows);
        Assert.Equal(0, execution.Result.FailedRows);
        Assert.Equal("SkippedInactiveLoan", Assert.Single(execution.Result.SkipDetails).Status);

        await using var verificationContext = new CoopDbContext(execution.Options);
        Assert.Empty(await verificationContext.LoanContracts.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationContext.Members.AsNoTracking().ToListAsync());
        await AssertNoDryRunAuditWritesAsync(verificationContext);
    }

    [Fact]
    public async Task ValidateOnlyAsync_FourKnownDuplicateInactivePatterns_AreCandidatesAndSkips()
    {
        var contractNos = new[]
        {
            "\u0E2A\u0E2B-2569-000592",
            "\u0E2A\u0E2B-2569-000595",
            "\u0E2A\u0E2B-2569-000597",
            "\u0E2A\u0E2B-2569-000600"
        };
        var activeRows = contractNos
            .Select((contractNo, index) => new TestRow(
                $"M{index + 1:000}",
                $"Active Member {index + 1}",
                contractNo,
                null,
                null,
                null,
                100m,
                20m,
                120m,
                true));
        var inactiveRows = contractNos.Select(BlankMemberInactiveRow);
        var seeds = contractNos
            .Select(contractNo => new DryRunSeed(contractNo, 500m, 50m, 10m, 60m))
            .ToArray();

        var execution = await ValidateOnlyAsync(activeRows.Concat(inactiveRows).ToArray(), seeds);

        Assert.True(execution.Result.Success);
        Assert.Equal(8, execution.Result.TotalRows);
        Assert.Equal(4, execution.Result.ValidRows);
        Assert.Equal(4, execution.Result.UpdateCandidates);
        Assert.Equal(4, execution.Result.ExistingContracts);
        Assert.Equal(0, execution.Result.MissingContracts);
        Assert.Equal(4, execution.Result.SkippedRows);
        Assert.Equal(0, execution.Result.FailedRows);
        Assert.Equal(contractNos.OrderBy(value => value), execution.Result.SkipDetails.Select(detail => detail.ContractNo).OrderBy(value => value));
        Assert.All(execution.Result.SkipDetails, detail => Assert.Equal(string.Empty, detail.MemberName));

        await using var verificationContext = new CoopDbContext(execution.Options);
        Assert.Equal(4, await verificationContext.LoanContracts.AsNoTracking().CountAsync());
        Assert.All(
            await verificationContext.LoanContracts.AsNoTracking().ToListAsync(),
            contract =>
            {
                Assert.Equal(500m, contract.LoanAmount);
                Assert.Equal(50m, contract.PrincipalBalance);
                Assert.Equal(10m, contract.ProfitBalance);
                Assert.Equal(60m, contract.TotalBalance);
                Assert.Equal(execution.MemberId, contract.MemberId);
            });
        await AssertNoDryRunAuditWritesAsync(verificationContext);
    }

    [Fact]
    public async Task ValidateOnlyAsync_NegativeContract_IsFailedWithoutDatabaseWrites()
    {
        var execution = await ValidateOnlyAsync(new[] { NegativeRow() });

        Assert.False(execution.Result.Success);
        Assert.Equal(1, execution.Result.TotalRows);
        Assert.Equal(0, execution.Result.ValidRows);
        Assert.Equal(0, execution.Result.UpdateCandidates);
        Assert.Equal(0, execution.Result.SkippedRows);
        Assert.Equal(0, execution.Result.UserSkippedErrorRows);
        Assert.Equal(1, execution.Result.FailedRows);
        Assert.Equal(64, execution.Result.FileHash.Length);
        var error = Assert.Single(execution.Result.ErrorDetails, error => error.ErrorType == "Negative");
        Assert.Equal("M003", error.MemberNo);
        Assert.True(error.CanSkip);
        Assert.False(error.WasUserSkipped);
        Assert.Contains("Excel", error.SuggestedAction);

        await using var verificationContext = new CoopDbContext(execution.Options);
        Assert.Empty(await verificationContext.LoanContracts.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationContext.Members.AsNoTracking().ToListAsync());
        await AssertNoDryRunAuditWritesAsync(verificationContext);
    }

    [Fact]
    public async Task ValidateOnlyAsync_RefinancedNoProfitBatch_ReclassifiesSignaturesAndPreservesKnownErrors()
    {
        var refinanceRows = Enumerable.Range(1, 23)
            .Select(index => new TestRow(
                $"M{index:000}",
                index % 2 == 0 ? $"Refinanced Member {index} (R)" : $"Refinanced Member {index}",
                $"\u0E2A\u0E2B-2568-{index:000000}",
                85000m + index,
                0m,
                85000m + index,
                null,
                null,
                null,
                true)
            {
                PreviousProfitAccountingDash = true
            })
            .ToArray();
        var negativeRows = Enumerable.Range(1, 7)
            .Select(index => new TestRow(
                $"N{index:000}",
                $"Negative Member {index}",
                $"\u0E2A\u0E2B-2569-900{index:000}",
                100m,
                -1m,
                99m,
                null,
                null,
                null,
                false))
            .ToArray();
        var ordinaryHiddenFractionRow = new TestRow(
            "HF001",
            "Ordinary Hidden Fraction",
            "\u0E2A\u0E2B-2552-000036",
            11867m,
            3318.72m,
            15186m,
            null,
            null,
            null,
            true)
        {
            PreviousProfitNumberFormat = "#,##0"
        };
        var mismatchRow = new TestRow(
            "MM001",
            "Mismatch Member",
            "\u0E2A\u0E08-2563-000301",
            8151m,
            2349.50m,
            10500m,
            null,
            null,
            null,
            false)
        {
            PreviousProfitNumberFormat = "#,##0"
        };
        var seeds = refinanceRows
            .Select(row => new DryRunSeed(row.ContractNo, 777777.77m, 1m, 2m, 3m))
            .Append(new DryRunSeed(ordinaryHiddenFractionRow.ContractNo, 777777.77m, 1m, 2m, 3m))
            .ToArray();

        var execution = await ValidateOnlyAsync(
            refinanceRows.Concat(negativeRows).Append(ordinaryHiddenFractionRow).Append(mismatchRow).ToArray(),
            seeds);

        Assert.False(execution.Result.Success);
        Assert.Equal(32, execution.Result.TotalRows);
        Assert.Equal(24, execution.Result.ValidRows);
        Assert.Equal(24, execution.Result.UpdateCandidates);
        Assert.Equal(24, execution.Result.ExistingContracts);
        Assert.Equal(0, execution.Result.MissingContracts);
        Assert.Equal(0, execution.Result.SkippedRows);
        Assert.Equal(8, execution.Result.FailedRows);
        Assert.Equal(7, execution.Result.ErrorDetails.Count(error => error.ErrorType == "Negative"));
        var mismatchError = Assert.Single(execution.Result.ErrorDetails, error => error.ErrorType == "TotalBalanceMismatch");
        Assert.True(mismatchError.CanSkip);
        Assert.Contains("Excel", mismatchError.SuggestedAction);
        Assert.Contains("Profit=2350.00", mismatchError.ErrorMessage);
        Assert.Contains("Expected Total=10501.00", mismatchError.ErrorMessage);
        Assert.DoesNotContain(execution.Result.ErrorDetails, error => error.ErrorType == "InvalidNumeric");

        await using var verificationContext = new CoopDbContext(execution.Options);
        Assert.Equal(24, await verificationContext.LoanContracts.AsNoTracking().CountAsync());
        Assert.All(
            await verificationContext.LoanContracts.AsNoTracking().ToListAsync(),
            contract =>
            {
                Assert.Equal(777777.77m, contract.LoanAmount);
                Assert.Equal(1m, contract.PrincipalBalance);
                Assert.Equal(2m, contract.ProfitBalance);
                Assert.Equal(3m, contract.TotalBalance);
                Assert.Equal(execution.MemberId, contract.MemberId);
            });
        await AssertNoDryRunAuditWritesAsync(verificationContext);
    }

    [Fact]
    public async Task ValidateOnlyAsync_MixedBatch_HasExclusiveCountersAndLeavesDatabaseUnchanged()
    {
        const string existingContractNo = "\u0E2A\u0E2B-2569-000001";
        var rows = new[]
        {
            ValidRow(existingContractNo),
            ValidRow("\u0E2A\u0E2B-2569-009999", existing: false),
            InactiveRow(),
            NegativeRow()
        };
        var execution = await ValidateOnlyAsync(
            rows,
            new DryRunSeed(existingContractNo, 888888.88m, 11m, 22m, 33m));

        Assert.False(execution.Result.Success);
        Assert.Equal(4, execution.Result.TotalRows);
        Assert.Equal(2, execution.Result.ValidRows);
        Assert.Equal(1, execution.Result.UpdateCandidates);
        Assert.Equal(1, execution.Result.ExistingContracts);
        Assert.Equal(1, execution.Result.MissingContracts);
        Assert.Equal(1, execution.Result.SkippedRows);
        Assert.Equal(1, execution.Result.FailedRows);
        Assert.Equal(
            execution.Result.TotalRows,
            execution.Result.UpdateCandidates
            + execution.Result.MissingContracts
            + execution.Result.SkippedRows
            + execution.Result.FailedRows);

        await using var verificationContext = new CoopDbContext(execution.Options);
        var contract = await verificationContext.LoanContracts.AsNoTracking().SingleAsync();
        Assert.Equal(existingContractNo, contract.ContractNo);
        Assert.Equal(888888.88m, contract.LoanAmount);
        Assert.Equal(11m, contract.PrincipalBalance);
        Assert.Equal(22m, contract.ProfitBalance);
        Assert.Equal(33m, contract.TotalBalance);
        Assert.Equal(execution.MemberId, contract.MemberId);
        Assert.Single(await verificationContext.Members.AsNoTracking().ToListAsync());
        await AssertNoDryRunAuditWritesAsync(verificationContext);
    }

    [Fact]
    public async Task ValidateOnlyAsync_CurrentActiveRegression000600_IsCandidateButNotPersisted()
    {
        const string contractNo = "\u0E2A\u0E2B-2569-000600";
        var row = new TestRow(
            "UNRELIABLE-MEMBER",
            "Regression Member",
            contractNo,
            null,
            null,
            null,
            123000m,
            25830m,
            148830m,
            true);
        var execution = await ValidateOnlyAsync(
            new[] { row },
            new DryRunSeed(contractNo, 999999.99m, 41m, 42m, 83m));

        Assert.True(execution.Result.Success);
        Assert.Equal(1, execution.Result.UpdateCandidates);
        Assert.Equal(1, execution.Result.ExistingContracts);
        Assert.Equal(0, execution.Result.MissingContracts);

        await using var verificationContext = new CoopDbContext(execution.Options);
        var contract = await verificationContext.LoanContracts.AsNoTracking().SingleAsync();
        Assert.Equal(contractNo, contract.ContractNo);
        Assert.Equal(999999.99m, contract.LoanAmount);
        Assert.Equal(41m, contract.PrincipalBalance);
        Assert.Equal(42m, contract.ProfitBalance);
        Assert.Equal(83m, contract.TotalBalance);
        Assert.Equal(execution.MemberId, contract.MemberId);
        await AssertNoDryRunAuditWritesAsync(verificationContext);
    }

    [Fact]
    public async Task ImportAsync_PreviousActiveExistingRow_UpdatesWithoutCreatingMemberOrContract()
    {
        var result = await ImportAsync(ValidRow());

        Assert.True(result.Result.Success);
        Assert.Equal(1, result.Result.TotalRows);
        Assert.Equal(0, result.Result.ImportedRows);
        Assert.Equal(1, result.Result.UpdatedRows);
        Assert.Equal(0, result.Result.SkippedRows);
        Assert.Equal(0, result.Result.FailedRows);
        Assert.Single(result.Context.LoanContracts);
        Assert.Single(result.Context.Members);
        var contract = Assert.Single(result.Context.LoanContracts);
        Assert.Equal(100m, contract.PrincipalBalance);
        Assert.Equal(20m, contract.ProfitBalance);
        Assert.Equal(120m, contract.TotalBalance);
        Assert.Equal(777777.77m, contract.LoanAmount);
        Assert.Equal(1, contract.MemberId);
    }

    [Fact]
    public async Task ImportAsync_InactiveRow_IsSkippedAndDoesNotInvokeImporters()
    {
        var result = await ImportAsync(InactiveRow());

        Assert.True(result.Result.Success);
        Assert.Equal(1, result.Result.SkippedRows);
        Assert.Equal(0, result.Result.UserSkippedErrorRows);
        Assert.Equal(0, result.Result.FailedRows);
        Assert.Equal(0, result.Result.ImportedRows);
        Assert.Empty(result.Context.Members);
        Assert.Empty(result.Context.LoanContracts);
        Assert.Empty(result.Context.ImportLoanRecords);
    }

    [Fact]
    public async Task ImportAsync_InactiveRowWithBlankMember_IsSkippedBeforePersistence()
    {
        var result = await ImportAsync(BlankMemberInactiveRow("\u0E2A\u0E2B-2569-000900"));

        Assert.True(result.Result.Success);
        Assert.Equal(1, result.Result.TotalRows);
        Assert.Equal(1, result.Result.SkippedRows);
        Assert.Equal(0, result.Result.FailedRows);
        Assert.Equal(0, result.Result.UpdatedRows);
        Assert.Empty(result.Context.Members);
        Assert.Empty(result.Context.LoanContracts);
        Assert.Empty(result.Context.ImportLoanRecords);
    }

    [Fact]
    public async Task ImportAsync_ValidAndInactiveRows_UpdatesValidAndSkipsInactive()
    {
        var result = await ImportAsync(ValidRow(), InactiveRow());

        Assert.True(result.Result.Success);
        Assert.Equal(2, result.Result.TotalRows);
        Assert.Equal(0, result.Result.ImportedRows);
        Assert.Equal(1, result.Result.UpdatedRows);
        Assert.Equal(1, result.Result.SkippedRows);
        Assert.Equal(0, result.Result.FailedRows);
        Assert.Single(result.Context.LoanContracts);
    }

    [Fact]
    public async Task ImportAsync_InactiveAndNegativeRows_SeparatesSkippedFromFailed()
    {
        var result = await ImportAsync(InactiveRow(), NegativeRow());

        Assert.False(result.Result.Success);
        Assert.Equal(1, result.Result.SkippedRows);
        Assert.Equal(1, result.Result.FailedRows);
        Assert.Equal(0, result.Result.ImportedRows);
    }

    [Fact]
    public async Task ImportAsync_InactiveRow_PreservesSkipAuditInformation()
    {
        var result = await ImportAsync(InactiveRow());

        var detail = Assert.Single(result.Result.SkipDetails);
        Assert.Equal(2, detail.RowNumber);
        Assert.Equal("\u0E2A\u0E2B-2569-000900", detail.ContractNo);
        Assert.Equal("Inactive Member", detail.MemberName);
        Assert.Equal("SkippedInactiveLoan", detail.Status);
        Assert.Contains("ไม่พบเงินต้นคงเหลือ", detail.Message);
    }

    [Fact]
    public async Task ImportAsync_OnlySkippedRows_IsSuccessfulWithoutValidationFailure()
    {
        var result = await ImportAsync(InactiveRow());

        Assert.True(result.Result.Success);
        Assert.Equal(1, result.Result.SkippedRows);
        Assert.Equal(0, result.Result.FailedRows);
        Assert.Empty(result.Result.Errors);
        Assert.Empty(result.Result.ErrorDetails);
    }

    [Fact]
    public async Task ImportAsync_NegativeRow_IsFailedNotSkipped()
    {
        var result = await ImportAsync(NegativeRow());

        Assert.Equal(1, result.Result.FailedRows);
        Assert.Equal(0, result.Result.SkippedRows);
        Assert.Equal(0, result.Result.UserSkippedErrorRows);
        Assert.Contains(result.Result.ErrorDetails, error => error.ErrorType == "Negative");
        Assert.Empty(result.Context.Members);
        Assert.Empty(result.Context.LoanContracts);
    }

    [Fact]
    public async Task ImportAsync_NegativeRowExplicitlySelected_IsUserSkippedWithoutUpdatingContract()
    {
        var row = NegativeRow(existing: true);
        var result = await ImportWithSkipApprovalsAsync(
            new[] { row },
            new ApprovedErrorSkipSelection { RowNumber = 2, ContractNo = row.ContractNo });

        Assert.True(result.Result.Success);
        Assert.Equal(0, result.Result.FailedRows);
        Assert.Equal(0, result.Result.SkippedRows);
        Assert.Equal(1, result.Result.UserSkippedErrorRows);
        Assert.Equal(0, result.Result.UpdatedRows);
        Assert.Empty(result.Result.Errors);
        Assert.Empty(result.Context.ImportLoanRecords);

        var contract = Assert.Single(result.Context.LoanContracts);
        Assert.Equal(1m, contract.PrincipalBalance);
        Assert.Equal(2m, contract.ProfitBalance);
        Assert.Equal(3m, contract.TotalBalance);

        var error = Assert.Single(result.Result.ErrorDetails);
        Assert.Equal("Negative", error.ErrorType);
        Assert.True(error.CanSkip);
        Assert.True(error.WasUserSkipped);
        var skip = Assert.Single(result.Result.UserSkippedErrorDetails);
        Assert.Equal("UserApprovedValidationError", skip.SkipReason);
        Assert.Equal(row.ContractNo, skip.ContractNo);
        Assert.Equal("M003", skip.MemberNo);
        Assert.Same(error, Assert.Single(skip.OriginalErrors));
    }

    [Fact]
    public async Task ImportAsync_TotalBalanceMismatchExplicitlySelected_IsUserSkippedWithoutPersistence()
    {
        var row = MismatchRow(existing: true);
        var result = await ImportWithSkipApprovalsAsync(
            new[] { row },
            new ApprovedErrorSkipSelection { RowNumber = 2, ContractNo = row.ContractNo });

        Assert.True(result.Result.Success);
        Assert.Equal(0, result.Result.FailedRows);
        Assert.Equal(1, result.Result.UserSkippedErrorRows);
        Assert.Equal(0, result.Result.UpdatedRows);
        Assert.Empty(result.Context.ImportLoanRecords);
        var error = Assert.Single(result.Result.ErrorDetails);
        Assert.Equal("TotalBalanceMismatch", error.ErrorType);
        Assert.True(error.CanSkip);
        Assert.True(error.WasUserSkipped);
        var contract = Assert.Single(result.Context.LoanContracts);
        Assert.Equal(1m, contract.PrincipalBalance);
        Assert.Equal(2m, contract.ProfitBalance);
        Assert.Equal(3m, contract.TotalBalance);
    }

    [Fact]
    public async Task ImportAsync_ValidRowListedForSkip_ProcessesNormally()
    {
        var row = ValidRow();
        var result = await ImportWithSkipApprovalsAsync(
            new[] { row },
            new ApprovedErrorSkipSelection { RowNumber = 2, ContractNo = row.ContractNo });

        Assert.True(result.Result.Success);
        Assert.Equal(0, result.Result.UserSkippedErrorRows);
        Assert.Equal(0, result.Result.FailedRows);
        Assert.Equal(1, result.Result.UpdatedRows);
        var contract = Assert.Single(result.Context.LoanContracts);
        Assert.Equal(100m, contract.PrincipalBalance);
        Assert.Equal(20m, contract.ProfitBalance);
        Assert.Equal(120m, contract.TotalBalance);
    }

    [Fact]
    public async Task ImportAsync_NonSkippableErrorSelected_RemainsFailed()
    {
        var row = ValidRow("XX-2569-000001", existing: true);
        var result = await ImportWithSkipApprovalsAsync(
            new[] { row },
            new ApprovedErrorSkipSelection { RowNumber = 2, ContractNo = row.ContractNo });

        Assert.False(result.Result.Success);
        Assert.Equal(1, result.Result.FailedRows);
        Assert.Equal(0, result.Result.UserSkippedErrorRows);
        Assert.Equal(0, result.Result.UpdatedRows);
        var error = Assert.Single(result.Result.ErrorDetails);
        Assert.Equal("UnknownContractType", error.ErrorType);
        Assert.False(error.CanSkip);
        Assert.False(error.WasUserSkipped);
        var contract = Assert.Single(result.Context.LoanContracts);
        Assert.Equal(1m, contract.PrincipalBalance);
        Assert.Equal(2m, contract.ProfitBalance);
        Assert.Equal(3m, contract.TotalBalance);
    }

    [Theory]
    [InlineData(3, "\u0E2A\u0E2B-2569-000901")]
    [InlineData(2, "\u0E2A\u0E2B-2569-999999")]
    public async Task ImportAsync_SkipIdentityMismatch_DoesNotApplyApproval(int rowNumber, string contractNo)
    {
        var row = NegativeRow(existing: true);
        var result = await ImportWithSkipApprovalsAsync(
            new[] { row },
            new ApprovedErrorSkipSelection { RowNumber = rowNumber, ContractNo = contractNo });

        Assert.False(result.Result.Success);
        Assert.Equal(1, result.Result.FailedRows);
        Assert.Equal(0, result.Result.UserSkippedErrorRows);
        Assert.False(Assert.Single(result.Result.ErrorDetails).WasUserSkipped);
        var contract = Assert.Single(result.Context.LoanContracts);
        Assert.Equal(1m, contract.PrincipalBalance);
        Assert.Equal(2m, contract.ProfitBalance);
        Assert.Equal(3m, contract.TotalBalance);
    }

    [Fact]
    public async Task ImportAsync_MixedBatch_WithUnresolvedFailureBlocksAllUpdateCandidates()
    {
        var valid = ValidRow();
        var inactive = InactiveRow();
        var negative = NegativeRow(existing: true);
        var mismatch = MismatchRow(existing: true);
        var rows = new[] { valid, inactive, negative, mismatch };
        var result = await ImportWithSkipApprovalsAsync(
            rows,
            new ApprovedErrorSkipSelection { RowNumber = 4, ContractNo = negative.ContractNo });

        Assert.False(result.Result.Success);
        Assert.Equal(4, result.Result.TotalRows);
        Assert.Equal(0, result.Result.UpdatedRows);
        Assert.Equal(1, result.Result.SkippedRows);
        Assert.Equal(1, result.Result.UserSkippedErrorRows);
        Assert.Equal(1, result.Result.FailedRows);
        Assert.Equal(1, result.Result.UpdateCandidates);
        Assert.Equal(
            result.Result.TotalRows,
            result.Result.UpdateCandidates + result.Result.SkippedRows +
            result.Result.UserSkippedErrorRows + result.Result.FailedRows);
        Assert.Single(result.Result.SkipDetails, detail => detail.Status == "SkippedInactiveLoan");
        Assert.Single(result.Result.UserSkippedErrorDetails);
        Assert.Single(result.Result.ErrorDetails, error => error.WasUserSkipped && error.ErrorType == "Negative");
        Assert.Single(result.Result.ErrorDetails, error => !error.WasUserSkipped && error.ErrorType == "TotalBalanceMismatch");

        var validContract = Assert.Single(
            result.Context.LoanContracts,
            contract => contract.ContractNo == valid.ContractNo);
        Assert.Equal(1m, validContract.PrincipalBalance);
        Assert.Equal(2m, validContract.ProfitBalance);
        Assert.Equal(3m, validContract.TotalBalance);

        var unchangedContracts = result.Context.LoanContracts
            .Where(contract => contract.ContractNo == negative.ContractNo || contract.ContractNo == mismatch.ContractNo)
            .ToList();
        Assert.All(unchangedContracts, contract =>
        {
            Assert.Equal(1m, contract.PrincipalBalance);
            Assert.Equal(2m, contract.ProfitBalance);
            Assert.Equal(3m, contract.TotalBalance);
        });
    }

    [Fact]
    public async Task ImportAsync_ChangedFileHash_RejectsAllSkipApprovalsBeforeProcessing()
    {
        var row = NegativeRow(existing: true);
        var result = await ImportWithSkipApprovalsAsync(
            new[] { row },
            false,
            new ApprovedErrorSkipSelection { RowNumber = 2, ContractNo = row.ContractNo });

        Assert.False(result.Result.Success);
        Assert.Equal(0, result.Result.TotalRows);
        Assert.Equal(0, result.Result.UserSkippedErrorRows);
        Assert.Equal("ValidationFileHashMismatch", Assert.Single(result.Result.ErrorDetails).ErrorType);
        Assert.Empty(result.Context.ImportBatches);
        Assert.Empty(result.Context.ImportLogs);
        Assert.Empty(result.Context.ImportLoanRecords);
        var contract = Assert.Single(result.Context.LoanContracts);
        Assert.Equal(1m, contract.PrincipalBalance);
        Assert.Equal(2m, contract.ProfitBalance);
        Assert.Equal(3m, contract.TotalBalance);
    }

    [Fact]
    public async Task ImportAsync_MissingContract_ReturnsStructuredFailureWithoutCreatingMemberOrContract()
    {
        var result = await ImportAsync(ValidRow("สห-2569-009999", existing: false));

        Assert.False(result.Result.Success);
        Assert.Equal(0, result.Result.ImportedRows);
        Assert.Equal(0, result.Result.UpdatedRows);
        Assert.Equal(1, result.Result.FailedRows);
        Assert.Empty(result.Context.Members);
        Assert.Empty(result.Context.LoanContracts);
        var error = Assert.Single(result.Result.ErrorDetails);
        Assert.Equal("ContractNo", error.FieldName);
        Assert.Equal("ContractNotFound", error.ErrorType);
        Assert.Equal("สห-2569-009999", error.ParsedValue);
    }

    [Fact]
    public async Task ImportAsync_CurrentActiveRegression000600_UpdatesBalancesAndPreservesProtectedFields()
    {
        var result = await ImportAsync(CurrentRegressionRow());

        Assert.True(result.Result.Success);
        Assert.Equal(0, result.Result.ImportedRows);
        Assert.Equal(1, result.Result.UpdatedRows);
        var contract = Assert.Single(result.Context.LoanContracts);
        Assert.Equal("สห-2569-000600", contract.ContractNo);
        Assert.Equal(123000m, contract.PrincipalBalance);
        Assert.Equal(25830m, contract.ProfitBalance);
        Assert.Equal(148830m, contract.TotalBalance);
        Assert.Equal(777777.77m, contract.LoanAmount);
        Assert.Equal(1, contract.MemberId);
    }

    [Fact]
    public async Task ImportAsync_UnknownContractType_IsFailedNotSkipped()
    {
        var result = await ImportAsync(ValidRow(contractNo: "XX-2569-000001", existing: false));

        Assert.Equal(1, result.Result.FailedRows);
        Assert.Equal(0, result.Result.SkippedRows);
        Assert.Contains(result.Result.ErrorDetails, error => error.ErrorType == "UnknownContractType");
        Assert.Empty(result.Context.LoanContracts);
    }

    [Fact]
    public async Task ImportAsync_NegativeProfitBalance_ReturnsErrorWithExcelRowAndSourceData()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"import_{Guid.NewGuid():N}.xlsx");
        try
        {
            CreateWorkbook(filePath);
            var options = new DbContextOptionsBuilder<CoopDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            await using var dbContext = new CoopDbContext(options);
            var service = new ExcelImportService(dbContext, new ExcelReader(), new ImportValidator(), new LoanImporter(dbContext));

            var result = await service.ImportAsync(filePath);

            var error = Assert.Single(result.ErrorDetails);
            Assert.Equal(1, result.FailedRows);
            Assert.Equal(0, result.SkippedRows);
            Assert.Equal(2, error.RowNumber);
            Assert.Equal("สห-2569-000001", error.ContractNo);
            Assert.Equal("Jane Doe", error.MemberName);
            Assert.Equal("PreviousProfit", error.FieldName);
            Assert.Equal("-87", error.RawValue);
            Assert.Equal("-87", error.ParsedValue);
            Assert.Equal("Negative", error.ErrorType);
            Assert.Contains("แถว 2", error.ErrorMessage);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private static Task<(ImportResult Result, CoopDbContext Context)> ImportAsync(params TestRow[] rows) =>
        ImportCoreAsync(rows, Array.Empty<ApprovedErrorSkipSelection>(), true);

    private static Task<(ImportResult Result, CoopDbContext Context)> ImportWithSkipApprovalsAsync(
        IReadOnlyList<TestRow> rows,
        params ApprovedErrorSkipSelection[] approvedSkips) =>
        ImportCoreAsync(rows, approvedSkips, true);

    private static Task<(ImportResult Result, CoopDbContext Context)> ImportWithSkipApprovalsAsync(
        IReadOnlyList<TestRow> rows,
        bool useMatchingFileHash,
        params ApprovedErrorSkipSelection[] approvedSkips) =>
        ImportCoreAsync(rows, approvedSkips, useMatchingFileHash);

    private static async Task<(ImportResult Result, CoopDbContext Context)> ImportCoreAsync(
        IReadOnlyList<TestRow> rows,
        IReadOnlyCollection<ApprovedErrorSkipSelection> approvedSkips,
        bool useMatchingFileHash)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"import_{Guid.NewGuid():N}.xlsx");
        CreateWorkbook(filePath, rows);
        var options = new DbContextOptionsBuilder<CoopDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var dbContext = new CoopDbContext(options);
        try
        {
            var existingRows = rows.Where(row => row.Existing).ToList();
            if (existingRows.Count > 0)
            {
                var member = new COOPAI.API.Models.Member
                {
                    MemberNo = "DB-MEMBER",
                    FirstName = "Database",
                    LastName = "Member",
                    FullName = "Database Member",
                    IsActive = true
                };
                dbContext.Members.Add(member);
                await dbContext.SaveChangesAsync();

                foreach (var row in existingRows.GroupBy(row => row.ContractNo).Select(group => group.First()))
                {
                    dbContext.LoanContracts.Add(new COOPAI.API.Models.LoanContract
                    {
                        ContractNo = row.ContractNo,
                        MemberId = member.Id,
                        LoanTypeId = 1,
                        ContractDate = new DateTime(2020, 1, 1),
                        ExpireDate = new DateTime(2030, 1, 1),
                        LoanAmount = 777777.77m,
                        PrincipalBalance = 1m,
                        ProfitBalance = 2m,
                        TotalBalance = 3m,
                        OverdueDays = 4,
                        IsClosed = false
                    });
                }

                await dbContext.SaveChangesAsync();
            }

            var service = new ExcelImportService(dbContext, new ExcelReader(), new ImportValidator(), new LoanImporter(dbContext));
            ImportExecutionOptions? executionOptions = null;
            if (approvedSkips.Count > 0)
            {
                executionOptions = new ImportExecutionOptions
                {
                    ValidationFileHash = useMatchingFileHash ? ComputeFileHash(filePath) : new string('0', 64),
                    ApprovedErrorSkips = approvedSkips
                };
            }

            var result = await service.ImportAsync(filePath, executionOptions);
            return (result, dbContext);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private static async Task<DryRunExecution> ValidateOnlyAsync(
        IReadOnlyList<TestRow> rows,
        params DryRunSeed[] existingContracts)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"validate_{Guid.NewGuid():N}.xlsx");
        CreateWorkbook(filePath, rows);
        var options = new DbContextOptionsBuilder<CoopDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var memberId = 0;

        try
        {
            if (existingContracts.Length > 0)
            {
                await using var seedContext = new CoopDbContext(options);
                var member = new COOPAI.API.Models.Member
                {
                    MemberNo = "DB-MEMBER",
                    FirstName = "Database",
                    LastName = "Member",
                    FullName = "Database Member",
                    IsActive = true
                };
                seedContext.Members.Add(member);
                await seedContext.SaveChangesAsync();
                memberId = member.Id;

                foreach (var seed in existingContracts)
                {
                    seedContext.LoanContracts.Add(new COOPAI.API.Models.LoanContract
                    {
                        ContractNo = seed.ContractNo,
                        MemberId = memberId,
                        LoanTypeId = 1,
                        ContractDate = new DateTime(2020, 1, 1),
                        ExpireDate = new DateTime(2030, 1, 1),
                        LoanAmount = seed.LoanAmount,
                        PrincipalBalance = seed.PrincipalBalance,
                        ProfitBalance = seed.ProfitBalance,
                        TotalBalance = seed.TotalBalance,
                        OverdueDays = 4,
                        IsClosed = false
                    });
                }

                await seedContext.SaveChangesAsync();
            }

            await using var validationContext = new CoopDbContext(options);
            var service = new ExcelImportService(
                validationContext,
                new ExcelReader(),
                new ImportValidator(),
                new LoanImporter(validationContext));
            var result = await service.ValidateOnlyAsync(filePath);
            return new DryRunExecution(result, options, memberId);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private static async Task AssertNoDryRunAuditWritesAsync(CoopDbContext context)
    {
        Assert.Empty(await context.ImportBatches.AsNoTracking().ToListAsync());
        Assert.Empty(await context.ImportLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await context.ImportLoanRecords.AsNoTracking().ToListAsync());
    }

    private static TestRow ValidRow(string contractNo = "\u0E2A\u0E2B-2569-000001", bool existing = true) =>
        new("M001", "Valid Member", contractNo, 100m, 20m, 120m, null, null, null, existing);

    private static TestRow CurrentRegressionRow() =>
        new("UNRELIABLE-MEMBER", "Regression Member", "สห-2569-000600", null, null, null, 123000m, 25830m, 148830m, true);

    private static TestRow InactiveRow() =>
        new("M002", "Inactive Member", "\u0E2A\u0E2B-2569-000900", null, null, null, null, null, 0m, false);

    private static TestRow BlankMemberInactiveRow(string contractNo) =>
        new(string.Empty, string.Empty, contractNo, null, null, null, null, null, 0m, false);

    private static TestRow NegativeRow(bool existing = false) =>
        new("M003", "Negative Member", "\u0E2A\u0E2B-2569-000901", 100m, -1m, 99m, null, null, null, existing);

    private static TestRow MismatchRow(bool existing = false) =>
        new("M004", "Mismatch Member", "\u0E2A\u0E2B-2569-000902", 100m, 20m, 119m, null, null, null, existing);

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void CreateWorkbook(string filePath)
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Tests");
        using var package = new ExcelPackage(new FileInfo(filePath));
        var sheet = package.Workbook.Worksheets.Add("ป้อนประจำวัน");
        var headers = new[] { "MemberNo", "MemberName", "ContractNo", "ContractDate", "ExpireDate", "OverdueDays", "PreviousPrincipal", "PreviousProfit", "PreviousTotal", "CurrentPrincipal", "CurrentProfit", "CurrentTotal" };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cells[1, column + 1].Value = headers[column];

        var values = new object?[] { "M001", "Jane Doe", "สห-2569-000001", "2026-01-01", "2026-12-31", 0, 100m, -87m, 13m, null, null, null };
        for (var column = 0; column < values.Length; column++)
            sheet.Cells[2, column + 1].Value = values[column];

        package.Save();
    }

    private static void CreateWorkbook(string filePath, IReadOnlyList<TestRow> rows)
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Tests");
        using var package = new ExcelPackage(new FileInfo(filePath));
        var sheet = package.Workbook.Worksheets.Add("\u0E1B\u0E49\u0E2D\u0E19\u0E1B\u0E23\u0E30\u0E08\u0E33\u0E27\u0E31\u0E19");
        var headers = new[] { "MemberNo", "MemberName", "ContractNo", "ContractDate", "ExpireDate", "OverdueDays", "PreviousPrincipal", "PreviousProfit", "PreviousTotal", "CurrentPrincipal", "CurrentProfit", "CurrentTotal" };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cells[1, column + 1].Value = headers[column];

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var values = new object?[] { row.MemberNo, row.MemberName, row.ContractNo, "2026-01-01", "2026-12-31", 0, row.G, row.H, row.I, row.J, row.K, row.L };
            for (var column = 0; column < values.Length; column++)
                sheet.Cells[index + 2, column + 1].Value = values[column];

            if (row.PreviousProfitAccountingDash)
                sheet.Cells[index + 2, 8].Style.Numberformat.Format = AccountingZeroFormat;
            else if (!string.IsNullOrWhiteSpace(row.PreviousProfitNumberFormat))
                sheet.Cells[index + 2, 8].Style.Numberformat.Format = row.PreviousProfitNumberFormat;
        }

        package.Save();
    }

    private sealed record TestRow(
        string MemberNo,
        string MemberName,
        string ContractNo,
        decimal? G,
        decimal? H,
        decimal? I,
        decimal? J,
        decimal? K,
        decimal? L,
        bool Existing)
    {
        public bool PreviousProfitAccountingDash { get; init; }
        public string? PreviousProfitNumberFormat { get; init; }
    }

    private sealed record DryRunSeed(
        string ContractNo,
        decimal LoanAmount,
        decimal PrincipalBalance,
        decimal ProfitBalance,
        decimal TotalBalance);

    private sealed record DryRunExecution(
        ImportResult Result,
        DbContextOptions<CoopDbContext> Options,
        int MemberId);
}
