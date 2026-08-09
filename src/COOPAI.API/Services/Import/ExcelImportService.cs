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
using System.Threading.Tasks;
using COOPAI.API.Data;
using COOPAI.API.Models;
using COOPAI.API.Models.Import;

namespace COOPAI.API.Services.Import;

public class ExcelImportService : IExcelImportService
{
    private readonly ExcelReader _reader;
    private readonly ImportValidator _validator;
    private readonly MemberImporter _memberImporter;
    private readonly LoanImporter _loanImporter;
    private readonly CoopDbContext _dbContext;

    public ExcelImportService(
        CoopDbContext dbContext,
        ExcelReader reader,
        ImportValidator validator,
        MemberImporter memberImporter,
        LoanImporter loanImporter)
    {
        _dbContext = dbContext;
        _reader = reader;
        _validator = validator;
        _memberImporter = memberImporter;
        _loanImporter = loanImporter;
    }

    public ImportResult Preview(string filePath)
    {
        var result = new ImportResult();

        try
        {
            //-------------------------------------------------
            // Read Excel
            //-------------------------------------------------

            DataTable table = _reader.Read(filePath);

            //-------------------------------------------------
            // Scan Worksheet
            //-------------------------------------------------

            var rows = new WorksheetScanner().Scan(table);

            result.Success = true;
            result.TotalRows = rows.Count;
            result.ImportedRows = 0;
            result.UpdatedRows = 0;
            result.FailedRows = 0;

            //-------------------------------------------------
            // Information
            //-------------------------------------------------

            result.Headers.Add($"Rows : {rows.Count}");
            result.Headers.Add($"Columns : {table.Columns.Count}");

            //-------------------------------------------------
            // Excel Header
            //-------------------------------------------------

            if (rows.Count >= 5)
            {
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    string h1 = rows[3].Count > c ? rows[3][c] : "";
                    string h2 = rows[4].Count > c ? rows[4][c] : "";

                    result.Headers.Add($"{h1} {h2}".Trim());
                }
            }

            //-------------------------------------------------
            // Data Start
            // Excel Row 7
            //-------------------------------------------------

            const int DATA_START_ROW = 6;

            for (int r = DATA_START_ROW; r < rows.Count; r++)
            {
                var row = rows[r];

                if (row.Count < 9)
                    continue;

                if (string.IsNullOrWhiteSpace(row[1]))
                    continue;

                var loan = new LoanImportRow
                {
                    MemberNo = GetString(row, 1),
                    FullName = GetString(row, 2),
                    ContractNo = GetString(row, 3),
                    ContractDate = GetDate(row, 4),
                    ExpireDate = GetDate(row, 5),
                    PrincipalBalance = GetDecimal(row, 6),
                    ProfitBalance = GetDecimal(row, 7),
                    TotalBalance = GetDecimal(row, 8)
                };

                var preview = new Dictionary<string, string>
                {
                    ["MemberNo"] = loan.MemberNo,
                    ["MemberName"] = loan.FullName,
                    ["ContractNo"] = loan.ContractNo,
                    ["LoanDate"] = loan.ContractDate?.ToString("dd/MM/yyyy") ?? "",
                    ["ExpireDate"] = loan.ExpireDate?.ToString("dd/MM/yyyy") ?? "",
                    ["Principal"] = loan.PrincipalBalance.ToString("N2"),
                    ["Profit"] = loan.ProfitBalance.ToString("N2"),
                    ["Total"] = loan.TotalBalance.ToString("N2")
                };

                result.PreviewRows.Add(preview);
                result.ImportedRows++;

                if (result.PreviewRows.Count >= 20)
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    public async Task<ImportResult> ImportAsync(string filePath)
    {
        var result = new ImportResult();
        var logs = new List<ImportLog>();
        var batch = new ImportBatch
        {
            FileName = Path.GetFileName(filePath),
            ImportDate = DateTime.UtcNow,
            Status = "Pending"
        };

        var batchId = Guid.NewGuid();

        try
        {
            var records = _reader.ReadRecords(filePath);
            result.TotalRows = records.Count;

            foreach (var record in records)
            {
                record.BatchId = batchId;
                await ProcessRecordAsync(record, result, logs);
            }

            batch.TotalRecords = result.TotalRows;
            batch.SuccessRecords = result.ImportedRows + result.UpdatedRows;
            batch.FailedRecords = result.FailedRows;
            batch.Status = result.Errors.Any() ? "CompletedWithErrors" : "Completed";
            batch.ErrorMessage = result.Errors.Count > 0 ? string.Join("; ", result.Errors) : null;
            batch.ImportLogs = logs;

            _dbContext.ImportBatches.Add(batch);
            await _dbContext.SaveChangesAsync();

            result.BatchId = batch.Id;
            result.Success = !result.Errors.Any();
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);

            batch.Status = "Failed";
            batch.ErrorMessage = ex.Message;
            batch.ImportLogs = logs;
            _dbContext.ImportBatches.Add(batch);
            await _dbContext.SaveChangesAsync();
        }

        return result;
    }

    private async Task ProcessRecordAsync(ImportLoanRecord record, ImportResult result, List<ImportLog> logs)
    {
        var validation = _validator.Validate(record);

        if (!validation.IsValid)
        {
            record.IsValid = false;
            record.ErrorMessage = string.Join("; ", validation.Errors);
            _dbContext.ImportLoanRecords.Add(record);
            logs.Add(CreateLog(record.RowNo, "Error", $"Validation failed: {record.ErrorMessage}"));
            result.FailedRows++;
            result.Errors.AddRange(validation.Errors);
            await _dbContext.SaveChangesAsync();
            return;
        }

        _dbContext.ImportLoanRecords.Add(record);

        var member = await _memberImporter.ImportAsync(record);
        var loanResult = await _loanImporter.ImportAsync(record, member);

        record.IsValid = true;
        record.ErrorMessage = string.Empty;

        if (loanResult.IsUpdated)
        {
            result.UpdatedRows++;
            logs.Add(CreateLog(record.RowNo, "Info", $"Updated contract {loanResult.Contract.ContractNo}"));
        }
        else
        {
            result.ImportedRows++;
            logs.Add(CreateLog(record.RowNo, "Info", $"Imported contract {loanResult.Contract.ContractNo}"));
        }

        await _dbContext.SaveChangesAsync();
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
