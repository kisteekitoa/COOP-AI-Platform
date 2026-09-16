using System.Globalization;
using System.Text.RegularExpressions;
using OfficeOpenXml;

namespace COOPAI.API.Services.DebtSegmentation;

public sealed record DebtWorkbookReadResult(
    string SourceFileName,
    string WorksheetName,
    DateOnly? DataThroughPeriod,
    int WorksheetRowCount,
    int ExcludedContractRows,
    IReadOnlyList<DebtContractHistory> Contracts,
    IReadOnlyList<string> Warnings);

public sealed record DebtWorkbookProbeResult(
    bool IsMatch,
    string FilePath,
    string? WorksheetName,
    IReadOnlyList<string> Issues);

public sealed class OperationalDebtWorkbookReader
{
    public const string CurrentWorksheetName = "อัพเดทล่าสุด";
    public const string LegacyWorksheetName = "ป้อนประจำวัน";

    private const int MemberCodeColumn = 2; // B
    private const int MemberNameColumn = 3; // C
    private const int ContractNumberColumn = 4; // D
    private const int ContractDateColumn = 5; // E
    private const int ExpireDateColumn = 6; // F
    private const int PreviousOpeningPrincipalColumn = 7; // G
    private const int CurrentOpeningPrincipalColumn = 10; // J
    private const int FirstMonthStartColumn = 14; // N
    private const int ColumnsPerMonth = 7;
    private const int GroupCodeColumn = 107; // DC

    private static readonly IReadOnlyDictionary<string, int> ThaiMonths = new Dictionary<string, int>
    {
        ["มกราคม"] = 1,
        ["กุมภาพันธ์"] = 2,
        ["มีนาคม"] = 3,
        ["เมษายน"] = 4,
        ["พฤษภาคม"] = 5,
        ["มิถุนายน"] = 6,
        ["กรกฎาคม"] = 7,
        ["สิงหาคม"] = 8,
        ["กันยายน"] = 9,
        ["ตุลาคม"] = 10,
        ["พฤศจิกายน"] = 11,
        ["ธันวาคม"] = 12
    };

    static OperationalDebtWorkbookReader()
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Debt Segmentation Preview");
    }

    public DebtWorkbookReadResult Read(string workbookPath, bool allowLegacyDiagnostic = false)
    {
        var file = new FileInfo(workbookPath);
        if (!file.Exists)
            throw new FileNotFoundException("Debt segmentation Preview workbook was not found.", workbookPath);

        using var package = new ExcelPackage(file);
        var sheet = package.Workbook.Worksheets[CurrentWorksheetName];
        if (sheet is null && allowLegacyDiagnostic)
            sheet = package.Workbook.Worksheets[LegacyWorksheetName];
        if (sheet is null)
            throw new InvalidOperationException(
                $"Required worksheet '{CurrentWorksheetName}' was not found. Legacy fallback is disabled for July Preview.");
        if (sheet.Dimension is null)
            throw new InvalidOperationException($"Worksheet '{sheet.Name}' is empty.");

        var warnings = new List<string>();
        if (!string.Equals(sheet.Name, CurrentWorksheetName, StringComparison.Ordinal))
            warnings.Add($"Current worksheet '{CurrentWorksheetName}' is absent; using the configured operational worksheet '{sheet.Name}'.");

        var dataThroughPeriod = DetectDataThroughPeriod(sheet);
        if (!dataThroughPeriod.HasValue)
            warnings.Add("The workbook data-through period could not be proven from its operational header or payment facts.");

        var workbookYear = DetectWorkbookYear(sheet) ?? dataThroughPeriod?.Year ?? DateTime.UtcNow.Year;
        var branch = DetectBranch(sheet);
        var contracts = new List<DebtContractHistory>();
        var candidateContractRows = 0;
        for (var row = 1; row <= sheet.Dimension.End.Row; row++)
        {
            var contractNumber = CellText(sheet.Cells[row, ContractNumberColumn]);
            var memberCode = CellText(sheet.Cells[row, MemberCodeColumn]);
            if (string.IsNullOrWhiteSpace(contractNumber))
                continue;
            candidateContractRows++;

            var previousOpening = ReadDecimal(sheet.Cells[row, PreviousOpeningPrincipalColumn]);
            var currentOpening = ReadDecimal(sheet.Cells[row, CurrentOpeningPrincipalColumn]);
            if (previousOpening.HasValue == currentOpening.HasValue)
                continue;

            var states = new Dictionary<DateOnly, DebtMonthlyState>();
            for (var month = 1; month <= 12; month++)
            {
                var startColumn = FirstMonthStartColumn + ((month - 1) * ColumnsPerMonth);
                var paymentPrincipal = ReadDecimal(sheet.Cells[row, startColumn + 1]) ?? 0m;
                var paymentProfit = ReadDecimal(sheet.Cells[row, startColumn + 2]) ?? 0m;
                var paymentTotal = ReadDecimal(sheet.Cells[row, startColumn + 3]);
                var principal = ReadDecimal(sheet.Cells[row, startColumn + 4]);
                var profit = ReadDecimal(sheet.Cells[row, startColumn + 5]);
                var total = ReadDecimal(sheet.Cells[row, startColumn + 6]);
                if (!principal.HasValue || !profit.HasValue || !total.HasValue)
                    continue;

                var period = new DateOnly(workbookYear, month, 1);
                var paymentEvidence = paymentTotal is > 0m
                    ? paymentTotal.Value
                    : Math.Max(0m, paymentPrincipal) + Math.Max(0m, paymentProfit);
                states[period] = new DebtMonthlyState(
                    period,
                    paymentEvidence,
                    principal.Value,
                    profit.Value,
                    total.Value)
                {
                    Provenance = new DebtMonthlyStateProvenance(
                        paymentPrincipal,
                        paymentProfit,
                        paymentTotal,
                        CellProvenance(sheet.Cells[row, startColumn + 1]),
                        CellProvenance(sheet.Cells[row, startColumn + 2]),
                        CellProvenance(sheet.Cells[row, startColumn + 3]),
                        CellProvenance(sheet.Cells[row, startColumn + 4]),
                        CellProvenance(sheet.Cells[row, startColumn + 5]),
                        CellProvenance(sheet.Cells[row, startColumn + 6]))
                };
            }

            if (states.Count == 0)
                continue;

            contracts.Add(new DebtContractHistory
            {
                SourceRowNumber = row,
                MemberCode = memberCode,
                MemberName = CellText(sheet.Cells[row, MemberNameColumn]),
                ContractNumber = contractNumber.Trim(),
                LoanType = LoanType(contractNumber),
                ContractDate = ReadDate(sheet.Cells[row, ContractDateColumn]),
                ExpireDate = ReadDate(sheet.Cells[row, ExpireDateColumn]),
                GroupCode = ReadUsableText(sheet.Cells[row, GroupCodeColumn]),
                Branch = branch,
                MonthlyStates = states
            });
        }

        return new DebtWorkbookReadResult(
            file.Name,
            sheet.Name,
            dataThroughPeriod,
            sheet.Dimension.End.Row,
            candidateContractRows - contracts.Count,
            contracts,
            warnings);
    }

    public DebtWorkbookProbeResult Probe(string workbookPath)
    {
        var issues = new List<string>();
        var file = new FileInfo(workbookPath);
        if (!file.Exists)
            return new DebtWorkbookProbeResult(false, file.FullName, null, ["File not found."]);

        try
        {
            using var package = new ExcelPackage(file);
            var sheet = package.Workbook.Worksheets[CurrentWorksheetName];
            if (sheet?.Dimension is null)
                return new DebtWorkbookProbeResult(false, file.FullName, null, [$"Worksheet '{CurrentWorksheetName}' is missing or empty."]);

            var marker = string.Join(" ", Enumerable.Range(1, Math.Min(6, sheet.Dimension.End.Row))
                .Select(row => CellText(sheet.Cells[row, GroupCodeColumn])))
                .Replace(" ", string.Empty, StringComparison.Ordinal);
            if (!marker.Contains("กลุ่ม", StringComparison.Ordinal))
                issues.Add("Column DC does not contain the required group-code marker.");

            if (sheet.Dimension.End.Column < GroupCodeColumn)
                issues.Add("Worksheet does not extend through column DC.");
            if (!DetectWorkbookYear(sheet).HasValue)
                issues.Add("Workbook year was not recognized from the operational title.");
            if (!DetectDataThroughPeriod(sheet).HasValue)
                issues.Add("Latest completed data period was not recognized.");

            return new DebtWorkbookProbeResult(issues.Count == 0, file.FullName, sheet.Name, issues);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DebtWorkbookProbeResult(false, file.FullName, null, [$"Workbook could not be inspected: {exception.Message}"]);
        }
    }

    private static DateOnly? DetectDataThroughPeriod(ExcelWorksheet sheet)
    {
        var year = DetectWorkbookYear(sheet);
        var header = CellText(sheet.Cells[5, 109]); // DE: current operational month header
        if (year.HasValue)
        {
            var month = ThaiMonths.FirstOrDefault(x => header.Contains(x.Key, StringComparison.Ordinal)).Value;
            if (month > 0)
                return new DateOnly(year.Value, month, 1);
        }

        if (!year.HasValue)
            return null;
        for (var month = 12; month >= 1; month--)
        {
            var totalPaymentColumn = FirstMonthStartColumn + ((month - 1) * ColumnsPerMonth) + 3;
            for (var row = 7; row <= sheet.Dimension.End.Row; row++)
            {
                if (ReadDecimal(sheet.Cells[row, totalPaymentColumn]) > 0m)
                    return new DateOnly(year.Value, month, 1);
            }
        }

        return null;
    }

    private static int? DetectWorkbookYear(ExcelWorksheet sheet)
    {
        var title = CellText(sheet.Cells[2, 1]);
        var match = Regex.Match(title, @"(?<!\d)(25\d{2}|20\d{2})(?!\d)", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Value, CultureInfo.InvariantCulture, out var year))
            return null;
        return year >= 2400 ? year - 543 : year;
    }

    private static string? DetectBranch(ExcelWorksheet sheet)
    {
        var rows = Math.Min(6, sheet.Dimension?.End.Row ?? 0);
        var columns = Math.Min(20, sheet.Dimension?.End.Column ?? 0);
        for (var row = 1; row <= rows; row++)
        {
            for (var column = 1; column <= columns; column++)
            {
                var text = CellText(sheet.Cells[row, column]);
                var match = Regex.Match(text, @"สาขา\s*([^)]+)", RegexOptions.CultureInvariant);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private static DateOnly? ReadDate(ExcelRange cell)
    {
        if (cell.Value is DateTime dateTime)
            return DateOnly.FromDateTime(dateTime);
        if (cell.Value is double serial)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        if (cell.Value is decimal decimalSerial)
            return DateOnly.FromDateTime(DateTime.FromOADate((double)decimalSerial));

        var text = CellText(cell);
        foreach (var format in new[] { "d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd" })
        {
            if (!DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                continue;
            if (parsed.Year >= 2400)
                parsed = parsed.AddYears(-543);
            return DateOnly.FromDateTime(parsed);
        }

        return null;
    }

    private static decimal? ReadDecimal(ExcelRange cell)
    {
        if (cell.Value is null || cell.Value is ExcelErrorValue)
            return null;
        try
        {
            return cell.Value switch
            {
                decimal value => value,
                double value => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                float value => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                int value => value,
                long value => value,
                _ when decimal.TryParse(
                    CellText(cell).Replace(",", string.Empty, StringComparison.Ordinal),
                    NumberStyles.Number | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var parsed) => parsed,
                _ => null
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static string? ReadUsableText(ExcelRange cell)
    {
        if (cell.Value is null || cell.Value is ExcelErrorValue)
            return null;
        var value = CellText(cell).Trim();
        return string.IsNullOrWhiteSpace(value) || value.StartsWith('#') ? null : value;
    }

    private static string CellText(ExcelRange cell) => cell.Text?.Trim() ?? string.Empty;

    private static DebtCellProvenance CellProvenance(ExcelRange cell) => new(
        cell.Address,
        string.IsNullOrWhiteSpace(cell.Formula) ? null : cell.Formula,
        string.IsNullOrWhiteSpace(cell.FormulaR1C1) ? null : cell.FormulaR1C1);

    private static string LoanType(string contractNumber)
    {
        var separator = contractNumber.IndexOf('-');
        return separator > 0 ? contractNumber[..separator].Trim() : contractNumber.Trim();
    }
}
