// ==========================================================
// COOP-AI
// File        : ExcelImportService.cs
// Module      : Import Engine
// Version     : 0.3.000
// Description : Excel Preview Service
// ==========================================================

using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Models.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace COOPAI.API.Services.Import;

public class ExcelImportService : IExcelImportService
{
    private static readonly HashSet<string> SkippableValidationErrorTypes = new(StringComparer.Ordinal)
    {
        "Negative",
        "TotalBalanceMismatch"
    };

    private readonly ExcelReader _reader;
    private readonly ImportValidator _validator;
    private readonly LoanImporter _loanImporter;
    private readonly CoopDbContext _dbContext;

    public ExcelImportService(
        CoopDbContext dbContext,
        ExcelReader reader,
        ImportValidator validator,
        LoanImporter loanImporter)
    {
        _dbContext = dbContext;
        _reader = reader;
        _validator = validator;
        _loanImporter = loanImporter;
    }

    public ImportResult Preview(string filePath)
    {
        var result = new ImportResult();

        try
        {
            //-------------------------------------------------
            // Read Excel rows directly from EPPlus
            //-------------------------------------------------

            var previewData = _reader.ReadPreviewRows(filePath, 20);

            result.Success = true;
            result.TotalRows = previewData.TotalRows;
            result.ImportedRows = 0;
            result.UpdatedRows = 0;
            result.FailedRows = 0;
            result.Headers = previewData.Headers;
            result.PreviewRows = previewData.Rows;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    public async Task<ImportResult> ValidateOnlyAsync(string filePath)
    {
        var result = new ImportResult();

        try
        {
            result.FileHash = await ComputeFileHashAsync(filePath);
            var records = _reader.ReadRecords(filePath);
            result.TotalRows = records.Count;

            var validActiveRecords = new List<ImportLoanRecord>();

            foreach (var record in records)
            {
                var validation = _validator.Validate(record);

                if (validation.IsSkipped)
                {
                    result.SkippedRows++;
                    result.SkipDetails.Add(new ImportSkipDetail
                    {
                        RowNumber = record.RowNo,
                        ContractNo = record.ContractNo,
                        MemberName = record.MemberName,
                        Status = validation.Status,
                        Message = "Skipped because both previous and current principal balances are blank."
                    });
                    continue;
                }

                if (!validation.IsValid)
                {
                    var canSkipRow = AreAllValidationErrorsSkippable(validation);
                    result.FailedRows++;
                    result.Errors.AddRange(validation.Errors);
                    result.ErrorDetails.AddRange(validation.Details.Select(detail => CreateImportError(record, detail, canSkipRow)));
                    continue;
                }

                result.ValidRows++;
                validActiveRecords.Add(record);
            }

            if (validActiveRecords.Count > 0)
            {
                var existingContractNos = await _dbContext.LoanContracts
                    .AsNoTracking()
                    .Select(contract => contract.ContractNo)
                    .ToListAsync();

                var normalizedExistingContractNos = existingContractNos
                    .Select(NormalizeContractNo)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var record in validActiveRecords)
                {
                    if (normalizedExistingContractNos.Contains(NormalizeContractNo(record.ContractNo)))
                    {
                        result.ExistingContracts++;
                        result.UpdateCandidates++;
                        continue;
                    }

                    result.MissingContracts++;
                    var message = $"LoanContract '{record.ContractNo.Trim()}' was not found and cannot be updated.";
                    result.Errors.Add(message);
                    result.ErrorDetails.Add(new ImportError
                    {
                        RowNumber = record.RowNo,
                        ContractNo = record.ContractNo,
                        MemberNo = record.MemberNo,
                        MemberName = record.MemberName,
                        FieldName = "ContractNo",
                        RawValue = record.ContractNo,
                        ParsedValue = record.ContractNo.Trim(),
                        ErrorType = "ContractNotFound",
                        ErrorMessage = $"Row {record.RowNo}: {message}"
                    });
                }
            }

            result.Success = result.FailedRows == 0 && result.MissingContracts == 0;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    public async Task<ImportResult> ImportAsync(string filePath, ImportExecutionOptions? options = null)
    {
        var result = new ImportResult();
        var logs = new List<ImportLog>();
        IDbContextTransaction? transaction = null;

        try
        {
            result.FileHash = await ComputeFileHashAsync(filePath);
            var approvedSkipIdentities = BuildApprovedSkipIdentities(options?.ApprovedErrorSkips);

            if (approvedSkipIdentities.Count > 0 &&
                !string.Equals(options?.ValidationFileHash, result.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                AddValidationFileHashMismatch(result);
                return result;
            }

            var records = _reader.ReadRecords(filePath);
            result.TotalRows = records.Count;
            var updateRecords = await PreflightAsync(records, result, logs, approvedSkipIdentities);

            if (result.FailedRows > 0 || result.MissingContracts > 0)
            {
                result.Success = false;
                return result;
            }

            transaction = await _dbContext.Database.BeginTransactionAsync();

            var batch = new ImportBatch
            {
                FileName = Path.GetFileName(filePath),
                ImportDate = DateTime.UtcNow,
                Status = "Completed"
            };
            var batchId = Guid.NewGuid();

            foreach (var record in updateRecords)
            {
                record.BatchId = batchId;
                _dbContext.ImportLoanRecords.Add(record);

                var loanResult = await _loanImporter.ImportAsync(record);
                if (loanResult.Status == LoanImportStatus.ContractNotFound)
                {
                    result.ExistingContracts--;
                    result.UpdateCandidates--;
                    AddContractNotFoundFailure(record, result, logs, loanResult.ErrorMessage);
                    throw new InvalidOperationException(
                        $"LoanContract '{record.ContractNo.Trim()}' became unavailable after import preflight.");
                }

                record.IsValid = true;
                record.ErrorMessage = string.Empty;
                result.UpdatedRows++;
                logs.Add(CreateLog(record.RowNo, "Info", $"Updated contract {loanResult.Contract!.ContractNo}"));
            }

            batch.TotalRecords = result.TotalRows;
            batch.SuccessRecords = result.ImportedRows + result.UpdatedRows;
            batch.FailedRecords = 0;
            batch.ImportLogs = logs;

            _dbContext.ImportBatches.Add(batch);
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            result.BatchId = batch.Id;
            result.Success = true;
        }
        catch (Exception ex)
        {
            var rollbackSucceeded = false;
            Exception? rollbackException = null;

            if (transaction != null)
            {
                try
                {
                    await transaction.RollbackAsync();
                    rollbackSucceeded = true;
                }
                catch (Exception rollbackEx)
                {
                    rollbackException = rollbackEx;
                }
            }

            _dbContext.ChangeTracker.Clear();
            result.Success = false;
            result.BatchId = null;
            result.ImportedRows = 0;
            result.UpdatedRows = 0;

            var errorType = transaction == null
                ? "ImportFailedBeforeTransaction"
                : rollbackSucceeded
                    ? "ImportTransactionRolledBack"
                    : "ImportRollbackFailed";
            var message = transaction == null
                ? $"Import failed before database persistence began. No import changes were committed. {ex.Message}"
                : rollbackSucceeded
                    ? $"Import transaction was rolled back. No import changes were committed. {ex.Message}"
                    : $"Import failed and rollback also failed. Database state must be verified. {ex.Message}; rollback: {rollbackException!.Message}";

            result.Errors.Add(message);
            result.ErrorDetails.Add(new ImportError
            {
                ErrorType = errorType,
                ErrorMessage = message,
                CanSkip = false
            });
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }

        return result;
    }

    private async Task<List<ImportLoanRecord>> PreflightAsync(
        IReadOnlyCollection<ImportLoanRecord> records,
        ImportResult result,
        List<ImportLog> logs,
        HashSet<ErrorSkipIdentity> approvedSkipIdentities)
    {
        var validActiveRecords = new List<ImportLoanRecord>();

        foreach (var record in records)
        {
            var validation = _validator.Validate(record);

            if (validation.IsSkipped)
            {
                result.SkippedRows++;
                result.SkipDetails.Add(new ImportSkipDetail
                {
                    RowNumber = record.RowNo,
                    ContractNo = record.ContractNo,
                    MemberName = record.MemberName,
                    Status = validation.Status,
                    Message = "ข้ามรายการเนื่องจากไม่พบเงินต้นคงเหลือทั้งปีก่อนหน้าและปีปัจจุบัน"
                });
                logs.Add(CreateLog(record.RowNo, "Info", $"Skipped inactive contract {record.ContractNo}"));
                continue;
            }

            if (!validation.IsValid)
            {
                var canSkipRow = AreAllValidationErrorsSkippable(validation);
                var errorDetails = validation.Details
                    .Select(detail => CreateImportError(record, detail, canSkipRow))
                    .ToList();

                if (IsUserApprovedErrorSkip(record, validation, approvedSkipIdentities))
                {
                    foreach (var error in errorDetails)
                        error.WasUserSkipped = true;

                    result.UserSkippedErrorRows++;
                    result.ErrorDetails.AddRange(errorDetails);
                    result.UserSkippedErrorDetails.Add(new UserSkippedErrorDetail
                    {
                        RowNumber = record.RowNo,
                        ContractNo = record.ContractNo,
                        MemberNo = record.MemberNo,
                        MemberName = record.MemberName,
                        OriginalErrors = errorDetails
                    });
                    logs.Add(CreateLog(record.RowNo, "Warning", $"User approved validation-error skip for {record.ContractNo}"));
                    continue;
                }

                logs.Add(CreateLog(record.RowNo, "Error", $"Validation failed: {string.Join("; ", validation.Errors)}"));
                result.FailedRows++;
                result.Errors.AddRange(validation.Errors);
                result.ErrorDetails.AddRange(errorDetails);
                continue;
            }

            result.ValidRows++;
            validActiveRecords.Add(record);
        }

        if (validActiveRecords.Count == 0)
            return validActiveRecords;

        var existingContractNos = await _dbContext.LoanContracts
            .AsNoTracking()
            .Select(contract => contract.ContractNo)
            .ToListAsync();
        var normalizedExistingContractNos = existingContractNos
            .Select(NormalizeContractNo)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var record in validActiveRecords)
        {
            if (normalizedExistingContractNos.Contains(NormalizeContractNo(record.ContractNo)))
            {
                result.ExistingContracts++;
                result.UpdateCandidates++;
                continue;
            }

            AddContractNotFoundFailure(
                record,
                result,
                logs,
                $"LoanContract '{record.ContractNo.Trim()}' was not found and cannot be updated.");
        }

        return validActiveRecords;
    }

    private static void AddContractNotFoundFailure(
        ImportLoanRecord record,
        ImportResult result,
        List<ImportLog> logs,
        string message)
    {
        result.MissingContracts++;
        result.FailedRows++;
        result.Errors.Add(message);
        result.ErrorDetails.Add(new ImportError
        {
            RowNumber = record.RowNo,
            ContractNo = record.ContractNo,
            MemberNo = record.MemberNo,
            MemberName = record.MemberName,
            FieldName = "ContractNo",
            RawValue = record.ContractNo,
            ParsedValue = record.ContractNo.Trim(),
            ErrorType = "ContractNotFound",
            ErrorMessage = $"แถว {record.RowNo}: {message}"
        });
        logs.Add(CreateLog(record.RowNo, "Warning", message));
    }

    private static ImportLog CreateLog(int rowNumber, string level, string message)
    {
        return new ImportLog
        {
            RowNumber = rowNumber,
            Level = level,
            Message = message
        };
    }

    private static ImportError CreateImportError(
        ImportLoanRecord record,
        ValidationError detail,
        bool canSkipRow)
    {
        return new ImportError
        {
            RowNumber = record.RowNo,
            ContractNo = record.ContractNo,
            MemberNo = record.MemberNo,
            MemberName = record.MemberName,
            FieldName = detail.FieldName,
            RawValue = detail.RawValue,
            ParsedValue = detail.ParsedValue,
            ErrorType = detail.ErrorType,
            CanSkip = canSkipRow,
            SuggestedAction = canSkipRow ? GetSuggestedAction(detail.ErrorType) : string.Empty,
            ErrorMessage = $"แถว {record.RowNo}: {detail.Message}"
        };
    }

    private static bool IsUserApprovedErrorSkip(
        ImportLoanRecord record,
        ValidationResult validation,
        HashSet<ErrorSkipIdentity> approvedSkipIdentities)
    {
        if (!approvedSkipIdentities.Contains(ErrorSkipIdentity.Create(record.RowNo, record.ContractNo)))
            return false;

        return AreAllValidationErrorsSkippable(validation);
    }

    private static bool AreAllValidationErrorsSkippable(ValidationResult validation) =>
        validation.Details.Count > 0 &&
        validation.Details.All(detail => SkippableValidationErrorTypes.Contains(detail.ErrorType));

    private static HashSet<ErrorSkipIdentity> BuildApprovedSkipIdentities(
        IReadOnlyCollection<ApprovedErrorSkipSelection>? selections)
    {
        if (selections == null || selections.Count == 0)
            return new HashSet<ErrorSkipIdentity>();

        return selections
            .Select(selection => ErrorSkipIdentity.Create(selection.RowNumber, selection.ContractNo))
            .ToHashSet();
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static void AddValidationFileHashMismatch(ImportResult result)
    {
        const string message = "The uploaded file does not match the file that was validated. Error-skip approvals were not applied.";
        result.Success = false;
        result.Errors.Add(message);
        result.ErrorDetails.Add(new ImportError
        {
            FieldName = "ValidationFileHash",
            ErrorType = "ValidationFileHashMismatch",
            ErrorMessage = message,
            CanSkip = false
        });
    }

    private static string GetSuggestedAction(string errorType) => errorType switch
    {
        "Negative" => "แก้ไข Excel หรือข้ามรายการ",
        "TotalBalanceMismatch" => "ตรวจสอบยอดรวมใน Excel หรือข้ามรายการ",
        _ => string.Empty
    };

    private static string NormalizeContractNo(string? contractNo) =>
        (contractNo ?? string.Empty).Trim().ToLowerInvariant();

    private readonly record struct ErrorSkipIdentity(int RowNumber, string ContractNo)
    {
        public static ErrorSkipIdentity Create(int rowNumber, string? contractNo) =>
            new(rowNumber, NormalizeContractNo(contractNo));
    }

    private static string GetString(List<string> row, int index)
    {
        if (index >= row.Count)
            return string.Empty;

        return row[index].Trim();
    }

    private static decimal GetDecimal(List<string> row, int index)
    {
        if (index >= row.Count)
            return 0;

        decimal.TryParse(
            row[index].Replace(",", string.Empty),
            out decimal value);

        return value;
    }

    private static DateTime? GetDate(List<string> row, int index)
    {
        if (index >= row.Count)
            return null;

        if (DateTime.TryParse(row[index], out DateTime value))
            return value;

        return null;
    }
}
