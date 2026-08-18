// ==========================================================
// COOP-AI
// File        : ExcelReader.cs
// Module      : Loan Import
// Version     : 0.4.000
// Description : Read Excel File using EPPlus
// ==========================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OfficeOpenXml;
using COOPAI.API.Models.Import;

namespace COOPAI.API.Services.Import;

public class ExcelReader
{
    static ExcelReader()
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Service");
    }

    public (List<LoanImportRow> Rows, List<string> Headers) ReadRowsWithHeaders(string filePath)
    {
        using var package = new ExcelPackage(new FileInfo(filePath));
        var worksheet = GetWorksheet(package);

        var workbookDimension = worksheet.Dimension;
        if (workbookDimension == null)
            return (new List<LoanImportRow>(), GetDefaultHeaders(10));

        var columns = Math.Min(workbookDimension.End.Column, 12);
        var rows = new List<LoanImportRow>();
        var dataStartRow = DetectFirstDataRow(worksheet, workbookDimension.Start.Row, workbookDimension.End.Row, columns);
        var headerRows = DetectHeaderRows(worksheet, workbookDimension.Start.Row, dataStartRow - 1, columns);
        var headers = BuildHeaders(worksheet, headerRows, columns);

        if (headers.Count == 0)
            headers = GetDefaultHeaders(columns);

        var mapping = BuildColumnMapping(headers);

        for (var row = dataStartRow; row <= workbookDimension.End.Row; row++)
        {
            var rowText = GetRowText(worksheet, row, columns);
            if (IsEmptyRow(rowText))
                continue;

            if (!IsContractSourceRow(rowText, mapping.ContractNo))
                continue;

            var loanRow = MapToLoanImportRow(rowText, mapping);
            ApplyPreviousProfitSourceEvidence(worksheet.Cells[row, mapping.PreviousProfit + 1], loanRow);
            rows.Add(loanRow);
        }

        return (rows, headers);
    }

    public List<LoanImportRow> ReadRows(string filePath)
    {
        return ReadRowsWithHeaders(filePath).Rows;
    }

    public (List<Dictionary<string, string>> Rows, List<string> Headers, int TotalRows, int HeaderStartRow, int DataStartRow) ReadPreviewRows(string filePath, int maxRows = 20)
    {
        using var package = new ExcelPackage(new FileInfo(filePath));
        var worksheet = GetWorksheet(package);

        var workbookDimension = worksheet.Dimension;
        if (workbookDimension == null)
            return (new List<Dictionary<string, string>>(), GetDefaultHeaders(10), 0, workbookDimension?.Start.Row ?? 1, workbookDimension?.End.Row ?? 0);

        var columns = Math.Min(workbookDimension.End.Column, 12);
        var dataStartRow = DetectFirstDataRow(worksheet, workbookDimension.Start.Row, workbookDimension.End.Row, columns);
        var headerRows = DetectHeaderRows(worksheet, workbookDimension.Start.Row, dataStartRow - 1, columns);
        var headerStartRow = headerRows.Any() ? headerRows.First().RowIndex : workbookDimension.Start.Row;
        var headers = BuildHeaders(worksheet, headerRows, columns);

        if (headers.Count == 0)
            headers = GetDefaultHeaders(columns);

        var mapping = BuildColumnMapping(headers);
        var previewRows = new List<Dictionary<string, string>>();
        var totalRows = 0;

        for (var row = dataStartRow; row <= workbookDimension.End.Row; row++)
        {
            var rowText = GetRowText(worksheet, row, columns);
            if (IsEmptyRow(rowText))
                continue;

            if (!IsContractSourceRow(rowText, mapping.ContractNo))
                continue;

            totalRows++;
            if (previewRows.Count < maxRows)
            {
                previewRows.Add(BuildPreviewRow(headers, rowText));
            }
        }

        Console.WriteLine($"Detected header start row: {headerStartRow}");
        Console.WriteLine($"Detected data start row: {dataStartRow}");
        Console.WriteLine($"Final header names: {string.Join(", ", headers)}");
        Console.WriteLine($"Number of columns: {headers.Count}");

        return (previewRows, headers, totalRows, headerStartRow, dataStartRow);
    }

    public List<ImportLoanRecord> ReadRecords(string filePath)
    {
        using var package = new ExcelPackage(new FileInfo(filePath));
        var worksheet = GetWorksheet(package);

        var workbookDimension = worksheet.Dimension;
        if (workbookDimension == null)
            return new List<ImportLoanRecord>();

        var records = new List<ImportLoanRecord>();
        var columns = Math.Min(workbookDimension.End.Column, 12);
        var dataStartRow = DetectFirstDataRow(worksheet, workbookDimension.Start.Row, workbookDimension.End.Row, columns);
        var headerRows = DetectHeaderRows(worksheet, workbookDimension.Start.Row, dataStartRow - 1, columns);
        var headers = BuildHeaders(worksheet, headerRows, columns);

        if (headers.Count == 0)
            headers = GetDefaultHeaders(columns);

        var mapping = BuildColumnMapping(headers);

        for (var row = dataStartRow; row <= workbookDimension.End.Row; row++)
        {
            var rowText = GetRowText(worksheet, row, columns);
            if (IsEmptyRow(rowText))
                continue;

            if (!IsContractSourceRow(rowText, mapping.ContractNo))
                continue;

            var loanRow = MapToLoanImportRow(rowText, mapping);
            ApplyPreviousProfitSourceEvidence(worksheet.Cells[row, mapping.PreviousProfit + 1], loanRow);
            records.Add(MapToImportLoanRecord(row, loanRow));
        }

        return records;
    }

    private static ExcelWorksheet GetWorksheet(ExcelPackage package)
    {
        var worksheet = package.Workbook.Worksheets["ป้อนประจำวัน"];

        if (worksheet == null)
            throw new InvalidOperationException("Worksheet 'ป้อนประจำวัน' not found.");

        return worksheet;
    }

    private static int DetectFirstDataRow(ExcelWorksheet worksheet, int startRow, int endRow, int columns)
    {
        for (var row = startRow; row <= endRow; row++)
        {
            var rowText = GetRowText(worksheet, row, columns);
            if (IsLikelyDataStartRow(rowText))
                return row;
        }

        return endRow + 1;
    }

    private static List<string> GetRowText(ExcelWorksheet worksheet, int row, int columns)
    {
        var values = new List<string>(columns);

        for (var col = 1; col <= columns; col++)
        {
            values.Add(worksheet.Cells[row, col].Text?.Trim() ?? string.Empty);
        }

        return values;
    }

    private static List<(int RowIndex, List<string> Values)> DetectHeaderRows(ExcelWorksheet worksheet, int startRow, int endRow, int columns)
    {
        var headerRows = new List<(int, List<string>)>();
        var foundHeaderSection = false;

        for (var row = endRow; row >= startRow; row--)
        {
            var rowText = GetRowText(worksheet, row, columns);
            if (IsPotentialHeaderRow(rowText))
            {
                headerRows.Add((row, rowText));
                foundHeaderSection = true;
            }
            else if (foundHeaderSection)
            {
                break;
            }
        }

        headerRows.Reverse();
        return headerRows;
    }

    private static List<string> BuildHeaders(ExcelWorksheet worksheet, List<(int RowIndex, List<string> Values)> headerRows, int columns)
    {
        if (!headerRows.Any())
            return new List<string>();

        var columnHeaders = new string[columns];
        var inheritedHeaders = new string[columns];

        for (var col = 0; col < columns; col++)
        {
            var names = new List<string>();
            for (var rowIndex = 0; rowIndex < headerRows.Count; rowIndex++)
            {
                var (sheetRowIndex, rowText) = headerRows[rowIndex];
                var cellValue = GetHeaderCellText(worksheet, sheetRowIndex, col + 1, rowText[col]);
                if (string.IsNullOrWhiteSpace(cellValue))
                {
                    cellValue = inheritedHeaders[col];
                }
                else
                {
                    inheritedHeaders[col] = cellValue;
                }

                if (!string.IsNullOrWhiteSpace(cellValue))
                {
                    if (!names.Any() || names.Last() != cellValue)
                    {
                        names.Add(cellValue);
                    }
                }
            }

            columnHeaders[col] = names.Any() ? string.Join("_", names) : $"Column{col + 1}";
        }

        var result = new List<string>(columns);
        for (var i = 0; i < columnHeaders.Length; i++)
        {
            result.Add(MakeHeaderUnique(columnHeaders[i], columnHeaders, i));
        }

        return result;
    }

    private static string GetHeaderCellText(ExcelWorksheet worksheet, int row, int col, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var mergedAddress in worksheet.MergedCells)
        {
            if (string.IsNullOrWhiteSpace(mergedAddress))
                continue;

            var mergedRange = worksheet.Cells[mergedAddress];
            if (mergedRange.Start.Row <= row && row <= mergedRange.End.Row &&
                mergedRange.Start.Column <= col && col <= mergedRange.End.Column)
            {
                return mergedRange.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string MakeHeaderUnique(string header, string[] existingHeaders, int currentIndex)
    {
        var uniqueHeader = header;
        var suffix = 1;
        while (existingHeaders.Select((h, index) => (h, index)).Any(pair => pair.h.Equals(uniqueHeader, StringComparison.OrdinalIgnoreCase) && pair.index != currentIndex))
        {
            suffix++;
            uniqueHeader = $"{header}_{suffix}";
        }

        return uniqueHeader;
    }

    private static Dictionary<string, string> BuildPreviewRow(List<string> headers, List<string> rowText)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Count; index++)
        {
            var header = string.IsNullOrWhiteSpace(headers[index]) ? $"Column{index + 1}" : headers[index];
            result[header] = index < rowText.Count ? rowText[index] : string.Empty;
        }

        return result;
    }

    private static List<string> GetDefaultHeaders(int columns)
    {
        var headers = new List<string>(columns);
        for (var i = 1; i <= columns; i++)
        {
            headers.Add($"Column {i}");
        }

        return headers;
    }

    private static bool IsPotentialHeaderRow(List<string> row)
    {
        if (row.All(string.IsNullOrWhiteSpace))
            return false;

        var nonEmpty = row.Count(value => !string.IsNullOrWhiteSpace(value));
        return nonEmpty >= 3;
    }

    private static bool IsEmptyRow(List<string> row)
    {
        return row.All(string.IsNullOrWhiteSpace);
    }

    private static bool IsLikelyDataStartRow(List<string> row)
    {
        if (row.Any(HasContractNumberShape))
            return true;

        if (row.Count < 3)
            return false;

        if (string.IsNullOrWhiteSpace(row[0]) || string.IsNullOrWhiteSpace(row[1]) || string.IsNullOrWhiteSpace(row[2]))
            return false;

        var filledCount = row.Count(value => !string.IsNullOrWhiteSpace(value));
        if (filledCount < 4)
            return false;

        var numericOrDateCount = 0;

        for (var index = 3; index < row.Count && index < 10; index++)
        {
            var text = row[index];
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (TryParseDate(text, out _) || decimal.TryParse(text.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out _) || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                numericOrDateCount++;
            }
        }

        return numericOrDateCount >= 1;
    }

    private static bool IsContractSourceRow(List<string> row, int contractNoIndex)
    {
        return contractNoIndex >= 0
            && contractNoIndex < row.Count
            && !string.IsNullOrWhiteSpace(row[contractNoIndex]);
    }

    private static bool HasContractNumberShape(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var segments = value.Split('-', StringSplitOptions.None);
        return segments.Length == 3
            && segments[0].Length > 0
            && segments[1].Length == 4
            && segments[1].All(character => character is >= '0' and <= '9')
            && segments[2].Length > 0
            && segments[2].All(character => character is >= '0' and <= '9');
    }

    private static LoanImportRow MapToLoanImportRow(List<string> row, (int MemberNo, int MemberName, int ContractNo, int ContractDate, int ExpireDate, int LoanAmount, int PrincipalBalance, int ProfitBalance, int TotalBalance, int OverdueDays, int PreviousPrincipal, int PreviousProfit, int PreviousTotal, int CurrentPrincipal, int CurrentProfit, int CurrentTotal) mapping)
    {
        var loanAmount = ParseDecimal(GetText(row, mapping.LoanAmount));
        var principalBalance = ParseDecimal(GetText(row, mapping.PrincipalBalance));
        var profitBalance = ParseDecimal(GetText(row, mapping.ProfitBalance));
        var totalBalance = ParseDecimal(GetText(row, mapping.TotalBalance));
        var previousPrincipal = ParseDecimal(GetText(row, mapping.PreviousPrincipal));
        var previousProfit = ParseDecimal(GetText(row, mapping.PreviousProfit));
        var previousTotal = ParseDecimal(GetText(row, mapping.PreviousTotal));
        var currentPrincipal = ParseDecimal(GetText(row, mapping.CurrentPrincipal));
        var currentProfit = ParseDecimal(GetText(row, mapping.CurrentProfit));
        var currentTotal = ParseDecimal(GetText(row, mapping.CurrentTotal));
        var overdueDays = ParseInt(GetText(row, mapping.OverdueDays));

        var mappedRow = new LoanImportRow
        {
            MemberNo = GetText(row, mapping.MemberNo),
            FullName = GetText(row, mapping.MemberName),
            ContractNo = GetText(row, mapping.ContractNo),
            ContractDate = ParseDate(GetText(row, mapping.ContractDate)),
            ExpireDate = ParseDate(GetText(row, mapping.ExpireDate)),
            LoanAmountRaw = loanAmount.RawValue,
            LoanAmountParsedValue = loanAmount.Value,
            LoanAmountParseStatus = loanAmount.Status,
            PrincipalBalanceRaw = principalBalance.RawValue,
            PrincipalBalanceParsedValue = principalBalance.Value,
            PrincipalBalanceParseStatus = principalBalance.Status,
            ProfitBalanceRaw = profitBalance.RawValue,
            ProfitBalanceParsedValue = profitBalance.Value,
            ProfitBalanceParseStatus = profitBalance.Status,
            TotalBalanceRaw = totalBalance.RawValue,
            TotalBalanceParsedValue = totalBalance.Value,
            TotalBalanceParseStatus = totalBalance.Status,
            PreviousPrincipal = previousPrincipal.Value,
            PreviousPrincipalRaw = previousPrincipal.RawValue,
            PreviousPrincipalParsedValue = previousPrincipal.Value,
            PreviousPrincipalParseStatus = previousPrincipal.Status,
            PreviousProfit = previousProfit.Value,
            PreviousProfitRaw = previousProfit.RawValue,
            PreviousProfitParsedValue = previousProfit.Value,
            PreviousProfitParseStatus = previousProfit.Status,
            PreviousTotal = previousTotal.Value,
            PreviousTotalRaw = previousTotal.RawValue,
            PreviousTotalParsedValue = previousTotal.Value,
            PreviousTotalParseStatus = previousTotal.Status,
            CurrentPrincipal = currentPrincipal.Value,
            CurrentPrincipalRaw = currentPrincipal.RawValue,
            CurrentPrincipalParsedValue = currentPrincipal.Value,
            CurrentPrincipalParseStatus = currentPrincipal.Status,
            CurrentProfit = currentProfit.Value,
            CurrentProfitRaw = currentProfit.RawValue,
            CurrentProfitParsedValue = currentProfit.Value,
            CurrentProfitParseStatus = currentProfit.Status,
            CurrentTotal = currentTotal.Value,
            CurrentTotalRaw = currentTotal.RawValue,
            CurrentTotalParsedValue = currentTotal.Value,
            CurrentTotalParseStatus = currentTotal.Status,
            OverdueDaysRaw = overdueDays.RawValue,
            OverdueDaysParsedValue = overdueDays.Value,
            OverdueDaysParseStatus = overdueDays.Status
        };

        if (loanAmount.Value.HasValue)
            mappedRow.LoanAmount = loanAmount.Value.Value;

        if (principalBalance.Value.HasValue)
            mappedRow.PrincipalBalance = principalBalance.Value.Value;

        if (profitBalance.Value.HasValue)
            mappedRow.ProfitBalance = profitBalance.Value.Value;

        if (totalBalance.Value.HasValue)
            mappedRow.TotalBalance = totalBalance.Value.Value;

        if (overdueDays.Value.HasValue)
            mappedRow.OverdueDays = overdueDays.Value.Value;

        return mappedRow;
    }

    private static ImportLoanRecord MapToImportLoanRecord(int rowNo, LoanImportRow row)
    {
        return new ImportLoanRecord
        {
            BatchId = Guid.Empty,
            RowNo = rowNo,
            MemberNo = row.MemberNo,
            MemberName = row.FullName,
            ContractNo = row.ContractNo,
            ContractDate = row.ContractDate,
            ExpireDate = row.ExpireDate,
            LoanAmount = row.LoanAmount,
            LoanAmountRaw = row.LoanAmountRaw,
            LoanAmountParsedValue = row.LoanAmountParsedValue,
            LoanAmountParseStatus = row.LoanAmountParseStatus,
            PrincipalBalance = row.PrincipalBalance,
            PrincipalBalanceRaw = row.PrincipalBalanceRaw,
            PrincipalBalanceParsedValue = row.PrincipalBalanceParsedValue,
            PrincipalBalanceParseStatus = row.PrincipalBalanceParseStatus,
            ProfitBalance = row.ProfitBalance,
            ProfitBalanceRaw = row.ProfitBalanceRaw,
            ProfitBalanceParsedValue = row.ProfitBalanceParsedValue,
            ProfitBalanceParseStatus = row.ProfitBalanceParseStatus,
            TotalBalance = row.TotalBalance,
            TotalBalanceRaw = row.TotalBalanceRaw,
            TotalBalanceParsedValue = row.TotalBalanceParsedValue,
            TotalBalanceParseStatus = row.TotalBalanceParseStatus,
            PreviousPrincipal = row.PreviousPrincipal,
            PreviousPrincipalRaw = row.PreviousPrincipalRaw,
            PreviousPrincipalParsedValue = row.PreviousPrincipalParsedValue,
            PreviousPrincipalParseStatus = row.PreviousPrincipalParseStatus,
            PreviousProfit = row.PreviousProfit,
            PreviousProfitRaw = row.PreviousProfitRaw,
            PreviousProfitParsedValue = row.PreviousProfitParsedValue,
            PreviousProfitParseStatus = row.PreviousProfitParseStatus,
            PreviousProfitUnderlyingNumericValue = row.PreviousProfitUnderlyingNumericValue,
            PreviousProfitNumberFormat = row.PreviousProfitNumberFormat,
            PreviousProfitBusinessStatus = row.PreviousProfitBusinessStatus,
            PreviousTotal = row.PreviousTotal,
            PreviousTotalRaw = row.PreviousTotalRaw,
            PreviousTotalParsedValue = row.PreviousTotalParsedValue,
            PreviousTotalParseStatus = row.PreviousTotalParseStatus,
            CurrentPrincipal = row.CurrentPrincipal,
            CurrentPrincipalRaw = row.CurrentPrincipalRaw,
            CurrentPrincipalParsedValue = row.CurrentPrincipalParsedValue,
            CurrentPrincipalParseStatus = row.CurrentPrincipalParseStatus,
            CurrentProfit = row.CurrentProfit,
            CurrentProfitRaw = row.CurrentProfitRaw,
            CurrentProfitParsedValue = row.CurrentProfitParsedValue,
            CurrentProfitParseStatus = row.CurrentProfitParseStatus,
            CurrentTotal = row.CurrentTotal,
            CurrentTotalRaw = row.CurrentTotalRaw,
            CurrentTotalParsedValue = row.CurrentTotalParsedValue,
            CurrentTotalParseStatus = row.CurrentTotalParseStatus,
            OverdueDays = row.OverdueDays,
            OverdueDaysRaw = row.OverdueDaysRaw,
            OverdueDaysParsedValue = row.OverdueDaysParsedValue,
            OverdueDaysParseStatus = row.OverdueDaysParseStatus,
            IsValid = true,
            ErrorMessage = string.Empty,
            ImportedAt = DateTime.UtcNow
        };
    }

    private static string GetText(List<string> row, int index)
    {
        if (index >= row.Count)
            return string.Empty;

        return row[index]?.Trim() ?? string.Empty;
    }

    private static void ApplyPreviousProfitSourceEvidence(ExcelRangeBase cell, LoanImportRow row)
    {
        row.PreviousProfitRaw = cell.Text?.Trim() ?? string.Empty;
        row.PreviousProfitNumberFormat = cell.Style.Numberformat.Format ?? string.Empty;
        row.PreviousProfitUnderlyingNumericValue = GetUnderlyingNumericValue(cell.Value);

        if (row.PreviousProfitUnderlyingNumericValue != 0m ||
            !string.Equals(row.PreviousProfitRaw, "-", StringComparison.Ordinal) ||
            !DisplaysAccountingZeroAsDash(row.PreviousProfitNumberFormat))
        {
            return;
        }

        row.PreviousProfit = 0m;
        row.PreviousProfitParsedValue = 0m;
        row.PreviousProfitParseStatus = "Parsed";
    }

    private static bool DisplaysAccountingZeroAsDash(string numberFormat)
    {
        if (string.IsNullOrWhiteSpace(numberFormat))
            return false;

        var sections = numberFormat.Split(';');
        if (sections.Length < 3)
            return false;

        var zeroSection = sections[2].Trim();
        return zeroSection.Contains("\"-\"", StringComparison.Ordinal) ||
               zeroSection is "-" or "\\-";
    }

    private static decimal? GetUnderlyingNumericValue(object? value)
    {
        if (value is not (byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal))
            return null;

        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static (int MemberNo, int MemberName, int ContractNo, int ContractDate, int ExpireDate, int LoanAmount, int PrincipalBalance, int ProfitBalance, int TotalBalance, int OverdueDays, int PreviousPrincipal, int PreviousProfit, int PreviousTotal, int CurrentPrincipal, int CurrentProfit, int CurrentTotal) BuildColumnMapping(List<string> headers)
    {
        return (
            MemberNo: GetHeaderIndex(headers, 0, "MemberNo", "Member No", "รหัสสมาชิก"),
            MemberName: GetHeaderIndex(headers, 1, "MemberName", "Member Name", "ชื่อ - สกุล", "ชื่อ สกุล"),
            ContractNo: GetHeaderIndex(headers, 2, "ContractNo", "Contract No", "เลขที่สัญญา", "เลขที่ สัญญา"),
            ContractDate: GetHeaderIndex(headers, 3, "ContractDate", "Contract Date", "วันที่สัญญา"),
            ExpireDate: GetHeaderIndex(headers, 4, "ExpireDate", "Expire Date", "วันที่ครบกำหนด", "วันครบกำหนด"),
            LoanAmount: GetGroupedHeaderIndex(headers, 5, "เพิ่มระหว่างปี", "ลูกหนี้", "LoanAmount", "Loan Amount", "จำนวนเงิน"),
            PrincipalBalance: GetHeaderIndex(headers, 6, "PrincipalBalance", "Principal Balance", "เงินต้น"),
            ProfitBalance: GetGroupedHeaderIndex(headers, 7, "เพิ่มระหว่างปี", "ผลตอบแทนฯ", "ProfitBalance", "Profit Balance", "ดอกเบี้ย"),
            TotalBalance: GetGroupedHeaderIndex(headers, 8, "เพิ่มระหว่างปี", "รวม", "TotalBalance", "Total Balance", "ยอดรวม"),
            OverdueDays: GetHeaderIndex(headers, 9, "OverdueDays", "Overdue Days", "วันคงค้าง"),
            PreviousPrincipal: 6,
            PreviousProfit: 7,
            PreviousTotal: 8,
            CurrentPrincipal: 9,
            CurrentProfit: 10,
            CurrentTotal: 11
        );
    }

    private static int GetHeaderIndex(List<string> headers, int fallbackIndex, params string[] candidates)
    {
        var normalizedCandidates = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(NormalizeHeader)
            .ToList();

        for (var index = 0; index < headers.Count; index++)
        {
            var normalizedHeader = NormalizeHeader(headers[index]);
            if (normalizedCandidates.Any(candidate =>
                    normalizedHeader.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                    normalizedHeader.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
                    candidate.Contains(normalizedHeader, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return fallbackIndex < headers.Count ? fallbackIndex : headers.Count - 1;
    }

    private static int GetGroupedHeaderIndex(List<string> headers, int fallbackIndex, string group, string field, params string[] candidates)
    {
        var normalizedGroup = NormalizeHeader(group);
        var normalizedField = NormalizeHeader(field);

        for (var index = 0; index < headers.Count; index++)
        {
            var normalizedHeader = NormalizeHeader(headers[index]);
            if (normalizedHeader.Contains(normalizedGroup, StringComparison.OrdinalIgnoreCase) &&
                normalizedHeader.Contains(normalizedField, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return GetHeaderIndex(headers, fallbackIndex, candidates);
    }

    private static string NormalizeHeader(string header)
    {
        return header?.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Replace("\u00A0", string.Empty).ToLowerInvariant() ?? string.Empty;
    }

    private static NumericParseResult<decimal> ParseDecimal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return NumericParseResult<decimal>.Blank(text);

        if (decimal.TryParse(text.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
            return NumericParseResult<decimal>.Parsed(text, result);

        return NumericParseResult<decimal>.Invalid(text);
    }

    private static NumericParseResult<int> ParseInt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return NumericParseResult<int>.Blank(text);

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return NumericParseResult<int>.Parsed(text, result);

        if (decimal.TryParse(text.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalResult))
            return NumericParseResult<int>.Parsed(text, Convert.ToInt32(decimalResult));

        return NumericParseResult<int>.Invalid(text);
    }

    private static DateTime? ParseDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ||
            DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out result))
        {
            return result;
        }

        return null;
    }

    private static bool TryParseDate(string text, out DateTime? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate) ||
            DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsedDate))
        {
            result = parsedDate;
            return true;
        }

        return false;
    }

    private readonly record struct NumericParseResult<T>(string RawValue, T? Value, string Status)
        where T : struct
    {
        public static NumericParseResult<T> Blank(string rawValue) => new(rawValue, null, "Blank");

        public static NumericParseResult<T> Invalid(string rawValue) => new(rawValue, null, "InvalidNumeric");

        public static NumericParseResult<T> Parsed(string rawValue, T value) => new(rawValue, value, "Parsed");
    }
}
