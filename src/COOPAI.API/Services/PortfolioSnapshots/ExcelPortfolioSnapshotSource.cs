using System.Globalization;
using System.Security.Cryptography;
using COOPAI.API.Models.Portfolio;
using OfficeOpenXml;

namespace COOPAI.API.Services.PortfolioSnapshots;

public sealed class ExcelPortfolioSnapshotSource : IPortfolioSnapshotSource
{
    private const string WorksheetName = "\u0E1B\u0E49\u0E2D\u0E19\u0E1B\u0E23\u0E30\u0E08\u0E33\u0E27\u0E31\u0E19";

    private const int MemberNoColumn = 1;
    private const int ContractNoColumn = 4;
    private const int ContractDateColumn = 5;
    private const int ExpireDateColumn = 6;
    private const int PreviousPrincipalColumn = 7;
    private const int PreviousProfitColumn = 8;
    private const int PreviousTotalColumn = 9;
    private const int CurrentPrincipalColumn = 10;
    private const int CurrentProfitColumn = 11;
    private const int CurrentTotalColumn = 12;
    private const int RepaymentPrincipalColumn = 99; // CU
    private const int RepaymentProfitColumn = 100;   // CV
    private const int RepaymentTotalColumn = 101;    // CW
    private const int OutstandingPrincipalColumn = 104; // CZ
    private const int OutstandingProfitColumn = 105;    // DA
    private const int OutstandingTotalColumn = 106;     // DB

    static ExcelPortfolioSnapshotSource()
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Snapshot Validation");
    }

    public async Task<PortfolioSourceReadResult> ReadAsync(
        string controlledCopyPath,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlledCopyPath);

        var file = new FileInfo(controlledCopyPath);
        if (!file.Exists)
            throw new FileNotFoundException("The controlled snapshot source copy was not found.", controlledCopyPath);

        var initialLength = file.Length;
        var initialLastWriteUtc = file.LastWriteTimeUtc;
        string sourceHash;
        await using (var stream = new FileStream(
                         file.FullName,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite,
                         1024 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        var issues = new List<PortfolioValidationIssue>();
        var rows = new List<PortfolioSourceRow>();
        PortfolioReportSummary? reportSummary = null;

        using (var package = new ExcelPackage(file))
        {
            var worksheet = package.Workbook.Worksheets[WorksheetName];
            if (worksheet?.Dimension is null)
            {
                issues.Add(new PortfolioValidationIssue(
                    "WorksheetMissing",
                    "The required snapshot worksheet is missing or empty."));
            }
            else
            {
                var dataStartRow = DetectFirstDataRow(worksheet);
                if (dataStartRow is null)
                {
                    issues.Add(new PortfolioValidationIssue(
                        "SourceRowsMissing",
                        "No portfolio source rows were detected."));
                }
                else
                {
                    var rowNumber = dataStartRow.Value;
                    for (; rowNumber <= worksheet.Dimension.End.Row; rowNumber++)
                    {
                        var memberNo = GetDisplayedText(worksheet.Cells[rowNumber, MemberNoColumn]);
                        var contractNo = GetDisplayedText(worksheet.Cells[rowNumber, ContractNoColumn]);
                        if (string.IsNullOrWhiteSpace(memberNo) && string.IsNullOrWhiteSpace(contractNo))
                            break;

                        rows.Add(ReadSourceRow(worksheet, rowNumber, memberNo, contractNo, issues));
                    }

                    reportSummary = ReadReportSummary(worksheet, rowNumber + 1, issues);
                }
            }
        }

        file.Refresh();
        if (file.Length != initialLength || file.LastWriteTimeUtc != initialLastWriteUtc)
        {
            issues.Add(new PortfolioValidationIssue(
                "SourceChangedDuringRead",
                "The controlled source copy changed while it was being read."));
        }

        return new PortfolioSourceReadResult
        {
            AsOfDate = asOfDate,
            SourceFileName = file.Name,
            SourceFileHash = sourceHash,
            SourceFileSizeBytes = initialLength,
            SourceRetrievedAt = DateTime.UtcNow,
            SourceDataThroughDate = asOfDate,
            Rows = rows,
            ReportSummary = reportSummary,
            Issues = issues
        };
    }

    private static int? DetectFirstDataRow(ExcelWorksheet worksheet)
    {
        var normalizer = new SnapshotContractNoNormalizer();
        for (var row = worksheet.Dimension.Start.Row; row <= worksheet.Dimension.End.Row; row++)
        {
            var memberNo = GetDisplayedText(worksheet.Cells[row, MemberNoColumn]);
            var contractNo = GetDisplayedText(worksheet.Cells[row, ContractNoColumn]);
            if (!string.IsNullOrWhiteSpace(memberNo) && normalizer.Normalize(contractNo).IsValid)
                return row;
        }

        return null;
    }

    private static PortfolioSourceRow ReadSourceRow(
        ExcelWorksheet worksheet,
        int rowNumber,
        string memberNo,
        string contractNo,
        ICollection<PortfolioValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(memberNo))
            issues.Add(RowIssue("MemberNoMissing", "MemberNo is blank on a source row.", rowNumber));
        if (string.IsNullOrWhiteSpace(contractNo))
            issues.Add(RowIssue("ContractNoMissing", "ContractNo is blank on a source row.", rowNumber));

        var previousPrincipal = ReadMoneyCell(
            worksheet.Cells[rowNumber, PreviousPrincipalColumn], rowNumber, "G", issues);
        var currentPrincipal = ReadMoneyCell(
            worksheet.Cells[rowNumber, CurrentPrincipalColumn], rowNumber, "J", issues);

        if (previousPrincipal.State == MoneyCellState.Invalid || currentPrincipal.State == MoneyCellState.Invalid)
        {
            return UnknownSourceRow(rowNumber, memberNo, contractNo);
        }

        var previousPopulated = previousPrincipal.State == MoneyCellState.Numeric;
        var currentPopulated = currentPrincipal.State == MoneyCellState.Numeric;
        if (!previousPopulated && !currentPopulated)
            return PlaceholderSourceRow(rowNumber, memberNo, contractNo);

        if (previousPopulated && currentPopulated)
        {
            issues.Add(RowIssue(
                "BothOpeningSidesPopulated",
                "Both opening principal sides G and J are populated.",
                rowNumber));
            return UnknownSourceRow(rowNumber, memberNo, contractNo);
        }

        var contractDate = ReadDateCell(
            worksheet.Cells[rowNumber, ContractDateColumn], rowNumber, "ContractDate", issues);
        var expireDate = ReadDateCell(
            worksheet.Cells[rowNumber, ExpireDateColumn], rowNumber, "ExpireDate", issues);

        var openingPrincipalColumn = previousPopulated ? PreviousPrincipalColumn : CurrentPrincipalColumn;
        var openingProfitColumn = previousPopulated ? PreviousProfitColumn : CurrentProfitColumn;
        var openingTotalColumn = previousPopulated ? PreviousTotalColumn : CurrentTotalColumn;
        var opening = ReadFinancialGroup(
            worksheet,
            rowNumber,
            openingPrincipalColumn,
            openingProfitColumn,
            openingTotalColumn,
            issues,
            out var openingIsValid);
        var repayment = ReadFinancialGroup(
            worksheet,
            rowNumber,
            RepaymentPrincipalColumn,
            RepaymentProfitColumn,
            RepaymentTotalColumn,
            issues,
            out var repaymentIsValid);
        var outstanding = ReadFinancialGroup(
            worksheet,
            rowNumber,
            OutstandingPrincipalColumn,
            OutstandingProfitColumn,
            OutstandingTotalColumn,
            issues,
            out var outstandingIsValid);

        var displayedOpening = ReadDisplayedFinancialGroup(
            worksheet,
            rowNumber,
            openingPrincipalColumn,
            openingProfitColumn,
            openingTotalColumn);

        return new PortfolioSourceRow
        {
            SourceRowNumber = rowNumber,
            SourceRecordKey = $"row:{rowNumber}",
            MemberNo = memberNo.Trim(),
            ContractNo = contractNo,
            ContractDate = contractDate,
            ExpireDate = expireDate,
            SourceRowKind = PortfolioSourceRowKind.Contract,
            OpeningSide = previousPopulated
                ? PortfolioOpeningSide.Previous
                : PortfolioOpeningSide.Current,
            HasValidOpeningValues = openingIsValid,
            HasValidRepaymentValues = repaymentIsValid,
            HasValidOutstandingValues = outstandingIsValid,
            Opening = opening,
            Repayment = repayment,
            Outstanding = outstanding,
            DisplayedOpening = displayedOpening
        };
    }

    private static PortfolioSourceRow PlaceholderSourceRow(int rowNumber, string memberNo, string contractNo) => new()
    {
        SourceRowNumber = rowNumber,
        SourceRecordKey = $"row:{rowNumber}",
        MemberNo = memberNo.Trim(),
        ContractNo = contractNo,
        SourceRowKind = PortfolioSourceRowKind.TemplatePlaceholder,
        OpeningSide = PortfolioOpeningSide.Unknown
    };

    private static PortfolioSourceRow UnknownSourceRow(int rowNumber, string memberNo, string contractNo) => new()
    {
        SourceRowNumber = rowNumber,
        SourceRecordKey = $"row:{rowNumber}",
        MemberNo = memberNo.Trim(),
        ContractNo = contractNo,
        SourceRowKind = PortfolioSourceRowKind.Unknown,
        OpeningSide = PortfolioOpeningSide.Unknown
    };

    private static PortfolioFinancialValues ReadFinancialGroup(
        ExcelWorksheet worksheet,
        int rowNumber,
        int principalColumn,
        int profitColumn,
        int totalColumn,
        ICollection<PortfolioValidationIssue> issues,
        out bool isValid)
    {
        var principal = ReadMoneyCell(
            worksheet.Cells[rowNumber, principalColumn], rowNumber, GetColumnName(principalColumn), issues);
        var profit = ReadMoneyCell(
            worksheet.Cells[rowNumber, profitColumn], rowNumber, GetColumnName(profitColumn), issues);
        var total = ReadMoneyCell(
            worksheet.Cells[rowNumber, totalColumn], rowNumber, GetColumnName(totalColumn), issues);

        isValid = principal.State == MoneyCellState.Numeric &&
                  profit.State == MoneyCellState.Numeric &&
                  total.State == MoneyCellState.Numeric;
        if (!isValid)
        {
            issues.Add(RowIssue(
                "RequiredFinancialValueMissing",
                $"Required numeric values are unavailable in columns {GetColumnName(principalColumn)}:{GetColumnName(totalColumn)}.",
                rowNumber));
        }

        return new PortfolioFinancialValues(
            principal.Value ?? 0m,
            profit.Value ?? 0m,
            total.Value ?? 0m);
    }

    private static PortfolioFinancialValues? ReadDisplayedFinancialGroup(
        ExcelWorksheet worksheet,
        int rowNumber,
        int principalColumn,
        int profitColumn,
        int totalColumn)
    {
        if (!TryParseDisplayedMoney(worksheet.Cells[rowNumber, principalColumn].Text, out var principal) ||
            !TryParseDisplayedMoney(worksheet.Cells[rowNumber, profitColumn].Text, out var profit) ||
            !TryParseDisplayedMoney(worksheet.Cells[rowNumber, totalColumn].Text, out var total))
        {
            return null;
        }

        return new PortfolioFinancialValues(principal, profit, total);
    }

    private static PortfolioReportSummary? ReadReportSummary(
        ExcelWorksheet worksheet,
        int startRow,
        ICollection<PortfolioValidationIssue> issues)
    {
        for (var row = startRow; row <= worksheet.Dimension.End.Row; row++)
        {
            if (!HasNumericValue(worksheet.Cells[row, RepaymentPrincipalColumn]) ||
                !HasNumericValue(worksheet.Cells[row, RepaymentProfitColumn]) ||
                !HasNumericValue(worksheet.Cells[row, RepaymentTotalColumn]) ||
                !HasNumericValue(worksheet.Cells[row, OutstandingPrincipalColumn]) ||
                !HasNumericValue(worksheet.Cells[row, OutstandingProfitColumn]) ||
                !HasNumericValue(worksheet.Cells[row, OutstandingTotalColumn]))
            {
                continue;
            }

            var previous = ReadFinancialGroup(
                worksheet, row,
                PreviousPrincipalColumn, PreviousProfitColumn, PreviousTotalColumn,
                issues,
                out _);
            var current = ReadFinancialGroup(
                worksheet, row,
                CurrentPrincipalColumn, CurrentProfitColumn, CurrentTotalColumn,
                issues,
                out _);
            var repayment = ReadFinancialGroup(
                worksheet, row,
                RepaymentPrincipalColumn, RepaymentProfitColumn, RepaymentTotalColumn,
                issues,
                out _);
            var outstanding = ReadFinancialGroup(
                worksheet, row,
                OutstandingPrincipalColumn, OutstandingProfitColumn, OutstandingTotalColumn,
                issues,
                out _);

            return new PortfolioReportSummary(
                new PortfolioFinancialValues(
                    previous.Principal + current.Principal,
                    previous.Profit + current.Profit,
                    previous.Total + current.Total),
                repayment,
                outstanding);
        }

        issues.Add(new PortfolioValidationIssue(
            "ReportSummaryUnavailable",
            "A report summary with cached numeric repayment and outstanding totals was not found."));
        return null;
    }

    private static DateOnly? ReadDateCell(
        ExcelRangeBase cell,
        int rowNumber,
        string fieldName,
        ICollection<PortfolioValidationIssue> issues)
    {
        if (cell.Value is null || cell.Value is string text && string.IsNullOrWhiteSpace(text))
        {
            issues.Add(RowIssue(
                $"Required{fieldName}Missing",
                $"{fieldName} is required for a contract source row.",
                rowNumber));
            return null;
        }

        if (TryConvertDate(cell.Value, cell.Text, out var date))
            return date;

        issues.Add(RowIssue(
            $"Invalid{fieldName}",
            $"{fieldName} is not a valid Excel/Gregorian/Buddhist-calendar date.",
            rowNumber));
        return null;
    }

    private static bool TryConvertDate(object value, string displayedText, out DateOnly date)
    {
        switch (value)
        {
            case DateOnly dateOnly:
                date = NormalizeCalendarYear(dateOnly);
                return true;
            case DateTime dateTime:
                date = NormalizeCalendarYear(DateOnly.FromDateTime(dateTime));
                return true;
            case double serial when double.IsFinite(serial):
                return TryConvertOaDate(serial, out date);
            case float serial when float.IsFinite(serial):
                return TryConvertOaDate(serial, out date);
            case decimal serial:
                return TryConvertOaDate((double)serial, out date);
        }

        return TryParseDateText(value.ToString(), out date) ||
               TryParseDateText(displayedText, out date);
    }

    private static bool TryConvertOaDate(double serial, out DateOnly date)
    {
        try
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }
        catch (ArgumentException)
        {
            date = default;
            return false;
        }
    }

    private static bool TryParseDateText(string? text, out DateOnly date)
    {
        var normalized = text?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            date = default;
            return false;
        }

        string[] formats =
        [
            "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy",
            "yyyy/M/d", "yyyy-MM-dd", "d.M.yyyy", "dd.MM.yyyy"
        ];
        if (!DateOnly.TryParseExact(
                normalized,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            date = default;
            return false;
        }

        date = NormalizeCalendarYear(parsed);
        return true;
    }

    private static DateOnly NormalizeCalendarYear(DateOnly date) =>
        date.Year is >= 2400 and <= 3000
            ? new DateOnly(date.Year - 543, date.Month, date.Day)
            : date;

    private static MoneyCellValue ReadMoneyCell(
        ExcelRangeBase cell,
        int rowNumber,
        string columnName,
        ICollection<PortfolioValidationIssue> issues)
    {
        if (cell.Value is null || cell.Value is string text && string.IsNullOrWhiteSpace(text))
        {
            if (!string.IsNullOrWhiteSpace(cell.Formula))
            {
                issues.Add(RowIssue(
                    "FormulaCacheUnavailable",
                    $"Formula cache is unavailable in column {columnName}.",
                    rowNumber));
                return new MoneyCellValue(MoneyCellState.Invalid, null);
            }

            return new MoneyCellValue(MoneyCellState.Blank, null);
        }

        if (!TryConvertNumeric(cell.Value, out var rawValue))
        {
            issues.Add(RowIssue(
                "InvalidFinancialValue",
                $"A non-numeric value was found in column {columnName}.",
                rowNumber));
            return new MoneyCellValue(MoneyCellState.Invalid, null);
        }

        var normalized = decimal.Round(rawValue, 2, MidpointRounding.AwayFromZero);
        // Excel stores numeric cells as binary floating point. Ignore only representation
        // noise far below a cent; a real sub-cent business value remains blocking.
        if (decimal.Abs(rawValue - normalized) > 0.0000001m)
        {
            issues.Add(RowIssue(
                "MoneyPrecisionExceeded",
                $"A value in column {columnName} has more than two decimal places.",
                rowNumber));
        }

        return new MoneyCellValue(MoneyCellState.Numeric, normalized);
    }

    private static bool HasNumericValue(ExcelRangeBase cell) =>
        cell.Value is not null && TryConvertNumeric(cell.Value, out _);

    private static bool TryConvertNumeric(object value, out decimal result)
    {
        switch (value)
        {
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case double doubleValue when double.IsFinite(doubleValue):
                result = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                return true;
            case float floatValue when float.IsFinite(floatValue):
                result = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            default:
                result = 0m;
                return false;
        }
    }

    private static bool TryParseDisplayedMoney(string text, out decimal value)
    {
        var normalized = text.Trim();
        if (normalized is "" or "-")
        {
            value = 0m;
            return true;
        }

        return decimal.TryParse(
            normalized,
            NumberStyles.Number | NumberStyles.AllowParentheses,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static string GetDisplayedText(ExcelRangeBase cell) =>
        cell.Text?.Trim() ?? string.Empty;

    private static PortfolioValidationIssue RowIssue(string code, string message, int rowNumber) =>
        new(code, message, rowNumber);

    private static string GetColumnName(int columnNumber)
    {
        var dividend = columnNumber;
        var name = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            name = Convert.ToChar('A' + modulo) + name;
            dividend = (dividend - modulo) / 26;
        }

        return name;
    }

    private enum MoneyCellState
    {
        Blank,
        Numeric,
        Invalid
    }

    private readonly record struct MoneyCellValue(MoneyCellState State, decimal? Value);
}
