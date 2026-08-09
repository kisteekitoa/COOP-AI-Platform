// ==========================================================
// COOP-AI
// File        : ExcelReader.cs
// Module      : Loan Import
// Version     : 0.3.000
// Description : Read Excel File using EPPlus
// ==========================================================

using System.Data;
using System.Globalization;
using OfficeOpenXml;
using COOPAI.API.Models.Import;

namespace COOPAI.API.Services.Import;

public class ExcelReader
{
    static ExcelReader()
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Service");
    }

    public DataTable Read(string filePath)
    {
        using var package = new ExcelPackage(new FileInfo(filePath));
        var worksheet = GetFirstWorksheet(package);

        var table = new DataTable();
        var columns = worksheet.Dimension?.Columns ?? 0;

        if (columns == 0)
            return table;

        for (var col = 1; col <= columns; col++)
        {
            var headerValue = GetString(worksheet.Cells[1, col].Value);
            var columnName = string.IsNullOrWhiteSpace(headerValue)
                ? $"Column{col}"
                : headerValue;

            table.Columns.Add(columnName);
        }

        var rows = worksheet.Dimension?.Rows ?? 1;

        for (var row = 2; row <= rows; row++)
        {
            if (IsEmptyRow(worksheet, row, columns))
                continue;

            var dataRow = table.NewRow();

            for (var col = 1; col <= columns; col++)
            {
                dataRow[col - 1] = worksheet.Cells[row, col].Value ?? string.Empty;
            }

            table.Rows.Add(dataRow);
        }

        return table;
    }

    public List<ImportLoanRecord> ReadRecords(string filePath)
    {
        using var package = new ExcelPackage(new FileInfo(filePath));
        var worksheet = GetFirstWorksheet(package);

        var records = new List<ImportLoanRecord>();
        var rows = worksheet.Dimension?.Rows ?? 1;
        var columns = worksheet.Dimension?.Columns ?? 0;

        for (var row = 2; row <= rows; row++)
        {
            if (IsEmptyRow(worksheet, row, columns))
                continue;

            var record = new ImportLoanRecord
            {
                RowNo = row,
                MemberNo = GetString(worksheet.Cells[row, 1].Value),
                MemberName = GetString(worksheet.Cells[row, 2].Value),
                ContractNo = GetString(worksheet.Cells[row, 3].Value),
                ContractDate = ParseDateOrNull(worksheet.Cells[row, 4].Value),
                ExpireDate = ParseDateOrNull(worksheet.Cells[row, 5].Value),
                LoanAmount = ParseDecimal(worksheet.Cells[row, 6].Value, out var loanAmountError),
                PrincipalBalance = ParseDecimal(worksheet.Cells[row, 7].Value, out var principalError),
                ProfitBalance = ParseDecimal(worksheet.Cells[row, 8].Value, out var profitError),
                TotalBalance = ParseDecimal(worksheet.Cells[row, 9].Value, out var totalError),
                OverdueDays = ParseInt(worksheet.Cells[row, 10].Value, out var overdueError),
                ImportedAt = DateTime.UtcNow
            };

            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(record.MemberNo))
                errors.Add("MemberNo is required.");

            if (string.IsNullOrWhiteSpace(record.MemberName))
                errors.Add("MemberName is required.");

            if (string.IsNullOrWhiteSpace(record.ContractNo))
                errors.Add("ContractNo is required.");

            if (!TryParseDate(worksheet.Cells[row, 4].Value, out var contractDate, out var contractDateError))
                errors.Add(contractDateError);
            else
                record.ContractDate = contractDate;

            if (!TryParseDate(worksheet.Cells[row, 5].Value, out var expireDate, out var expireDateError))
                errors.Add(expireDateError);
            else
                record.ExpireDate = expireDate;

            if (!string.IsNullOrWhiteSpace(loanAmountError))
                errors.Add(loanAmountError);

            if (!string.IsNullOrWhiteSpace(principalError))
                errors.Add(principalError);

            if (!string.IsNullOrWhiteSpace(profitError))
                errors.Add(profitError);

            if (!string.IsNullOrWhiteSpace(totalError))
                errors.Add(totalError);

            if (!string.IsNullOrWhiteSpace(overdueError))
                errors.Add(overdueError);

            record.IsValid = errors.Count == 0;
            record.ErrorMessage = record.IsValid ? string.Empty : string.Join("; ", errors);

            records.Add(record);
        }

        return records;
    }

    private static ExcelWorksheet GetFirstWorksheet(ExcelPackage package)
    {
        if (package.Workbook.Worksheets.Count == 0)
            throw new InvalidOperationException("Excel file contains no worksheet.");

        return package.Workbook.Worksheets[0];
    }

    private static bool IsEmptyRow(ExcelWorksheet worksheet, int row, int columns)
    {
        for (var col = 1; col <= columns; col++)
        {
            if (!string.IsNullOrWhiteSpace(GetString(worksheet.Cells[row, col].Value)))
                return false;
        }

        return true;
    }

    private static string GetString(object? value)
    {
        return value?.ToString()?.Trim() ?? string.Empty;
    }

    private static decimal ParseDecimal(object? value, out string? error)
    {
        error = null;

        if (value == null)
            return 0m;

        if (value is decimal d)
            return d;

        if (value is double db)
            return Convert.ToDecimal(db);

        if (value is float f)
            return Convert.ToDecimal(f);

        var text = GetString(value).Replace(",", string.Empty);

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
            return result;

        error = $"Invalid decimal value '{text}' in the row.";
        return 0m;
    }

    private static int ParseInt(object? value, out string? error)
    {
        error = null;

        if (value == null)
            return 0;

        if (value is int i)
            return i;

        if (value is double db)
            return Convert.ToInt32(db);

        if (value is float f)
            return Convert.ToInt32(f);

        var text = GetString(value);

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return result;

        error = $"Invalid integer value '{text}' in the row.";
        return 0;
    }

    private static bool TryParseDate(object? value, out DateTime? result, out string error)
    {
        error = string.Empty;
        result = null;

        if (value == null)
            return true;

        if (value is DateTime date)
        {
            result = date;
            return true;
        }

        var text = GetString(value);
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
            DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
        {
            result = date;
            return true;
        }

        error = $"Invalid date value '{text}' in the row.";
        return false;
    }

    private static DateTime? ParseDateOrNull(object? value)
    {
        if (TryParseDate(value, out var result, out _))
            return result;

        return null;
    }
}
