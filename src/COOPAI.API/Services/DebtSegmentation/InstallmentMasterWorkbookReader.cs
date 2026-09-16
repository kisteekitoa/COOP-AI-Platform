using System.Globalization;
using System.Text.RegularExpressions;
using OfficeOpenXml;

namespace COOPAI.API.Services.DebtSegmentation;

public sealed class InstallmentMasterWorkbookReader
{
    private const int ContractNumberColumn = 1; // A
    private const int DownPaymentColumn = 31; // AE
    private const int ContractualObligationColumn = 34; // AH
    private const int TotalInstallmentsColumn = 38; // AL
    private const int MonthlyInstallmentColumn = 39; // AM
    private const int HeaderlessSampleSize = 100;
    private const int MinimumHeaderlessSampleRows = 20;
    private const int MinimumStructuralCompatibilityPercent = 95;

    static InstallmentMasterWorkbookReader()
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Installment Master Preview");
    }

    public InstallmentMasterReadResult Read(string workbookPath, string worksheetName)
    {
        var file = new FileInfo(workbookPath);
        if (!file.Exists)
            throw new FileNotFoundException("Installment Master Preview workbook was not found.", workbookPath);

        using var package = new ExcelPackage(file);
        var sheet = package.Workbook.Worksheets[worksheetName]
            ?? throw new InvalidOperationException($"Required worksheet '{worksheetName}' was not found.");
        if (sheet.Dimension is null)
            throw new InvalidOperationException($"Worksheet '{sheet.Name}' is empty.");

        var schemaMode = DetectSchemaMode(sheet);
        var firstDataRow = schemaMode == InstallmentMasterSchemaModes.Headered ? 2 : 1;
        var rows = new List<ParsedRow>();
        var invalidAh = 0;
        var invalidAl = 0;
        var invalidAm = 0;
        for (var row = firstDataRow; row <= sheet.Dimension.End.Row; row++)
        {
            var contractNumber = NormalizeContractNumber(sheet.Cells[row, ContractNumberColumn]);
            var aeHasContent = HasContent(sheet.Cells[row, DownPaymentColumn]);
            var ahHasContent = HasContent(sheet.Cells[row, ContractualObligationColumn]);
            var alHasContent = HasContent(sheet.Cells[row, TotalInstallmentsColumn]);
            var amHasContent = HasContent(sheet.Cells[row, MonthlyInstallmentColumn]);
            if (string.IsNullOrWhiteSpace(contractNumber) && !aeHasContent && !ahHasContent && !alHasContent && !amHasContent)
                continue;

            var downPayment = ReadNonNegativeDecimal(sheet.Cells[row, DownPaymentColumn]);
            var contractualObligation = ReadPositiveDecimal(sheet.Cells[row, ContractualObligationColumn]);
            var totalInstallments = ReadPositiveInteger(sheet.Cells[row, TotalInstallmentsColumn]);
            var monthlyInstallment = ReadPositiveDecimal(sheet.Cells[row, MonthlyInstallmentColumn]);
            if (!contractualObligation.HasValue)
                invalidAh++;
            if (!totalInstallments.HasValue)
                invalidAl++;
            if (!monthlyInstallment.HasValue)
                invalidAm++;
            rows.Add(new ParsedRow(row, contractNumber, downPayment,
                aeHasContent, downPayment.HasValue, contractualObligation,
                totalInstallments, monthlyInstallment));
        }

        var warnings = new List<string>();
        var blankContractRows = rows.Count(x => string.IsNullOrWhiteSpace(x.ContractNumber));
        if (blankContractRows > 0)
            warnings.Add($"{blankContractRows} populated rows have a blank ContractNo and were excluded.");

        var groups = rows.Where(x => !string.IsNullOrWhiteSpace(x.ContractNumber))
            .GroupBy(x => x.ContractNumber, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var contracts = new List<InstallmentMasterContract>(groups.Length);
        var duplicateGroups = 0;
        foreach (var group in groups)
        {
            var items = group.ToArray();
            var totalValues = items.Select(x => x.TotalInstallments).Distinct().ToArray();
            var monthlyValues = items.Select(x => x.MonthlyInstallment).Distinct().ToArray();
            var obligationValues = items.Select(x => x.ContractualObligation).Distinct().ToArray();
            var downPaymentValues = items.Select(x => x.DownPayment).Distinct().ToArray();
            var duplicateStatus = InstallmentDuplicateStatuses.None;
            if (items.Length > 1)
            {
                duplicateGroups++;
                duplicateStatus = totalValues.Length == 1 && monthlyValues.Length == 1
                    ? obligationValues.Length == 1
                        ? InstallmentDuplicateStatuses.Identical
                        : InstallmentDuplicateStatuses.ConflictingContractualObligation
                    : totalValues.Length > 1 && monthlyValues.Length > 1
                        ? InstallmentDuplicateStatuses.ConflictingBoth
                        : totalValues.Length > 1
                            ? InstallmentDuplicateStatuses.ConflictingTotalInstallments
                            : InstallmentDuplicateStatuses.ConflictingMonthlyInstallment;
            }

            var total = totalValues.Length == 1 ? totalValues[0] : null;
            var monthly = monthlyValues.Length == 1 ? monthlyValues[0] : null;
            var contractualObligation = obligationValues.Length == 1 ? obligationValues[0] : null;
            var downPayment = downPaymentValues.Length == 1 ? downPaymentValues[0] : null;
            var downPaymentStatus = downPaymentValues.Length > 1
                ? DownPaymentDataStatuses.ConflictingDuplicate
                : items.All(x => x.DownPaymentValid) && downPayment.HasValue
                    ? DownPaymentDataStatuses.Available
                    : items.Any(x => x.DownPaymentHasContent)
                        ? DownPaymentDataStatuses.Invalid
                        : DownPaymentDataStatuses.Missing;
            var validationStatus = duplicateStatus is
                InstallmentDuplicateStatuses.ConflictingTotalInstallments or
                InstallmentDuplicateStatuses.ConflictingMonthlyInstallment or
                InstallmentDuplicateStatuses.ConflictingBoth or
                InstallmentDuplicateStatuses.ConflictingContractualObligation
                ? InstallmentValidationStatuses.ConflictingDuplicate
                : ValidationStatus(contractualObligation, total, monthly);
            var rowWarnings = new List<string>();
            if (!contractualObligation.HasValue)
                rowWarnings.Add("AH/LCONT_AMOUNT_SAL must be a positive number.");
            if (!total.HasValue)
                rowWarnings.Add("AL/LREG_MAX_INSTALL must be a positive integer.");
            if (!monthly.HasValue)
                rowWarnings.Add("AM/LREG_SALINT must be a positive number.");
            if (duplicateStatus == InstallmentDuplicateStatuses.Identical)
                rowWarnings.Add("Duplicate ContractNo rows have identical AH/AL/AM values.");
            else if (duplicateStatus != InstallmentDuplicateStatuses.None)
                rowWarnings.Add($"Duplicate ContractNo has {duplicateStatus} values and is unusable for AmountDue.");

            if (downPaymentStatus != DownPaymentDataStatuses.Available)
                rowWarnings.Add($"AE/LREC_SAL_ADVANCE is {downPaymentStatus}; DownPayment classification must remain unresolved when required.");
            contracts.Add(new InstallmentMasterContract(
                group.Key, total, monthly, validationStatus, duplicateStatus,
                rowWarnings, items.Min(x => x.SourceRowNumber))
            {
                DownPayment = downPayment,
                DownPaymentDataStatus = downPaymentStatus,
                ContractualObligation = contractualObligation
            });
        }

        if (duplicateGroups > 0)
            warnings.Add($"{duplicateGroups} duplicate ContractNo groups were classified without arbitrary row selection.");
        if (invalidAh > 0)
            warnings.Add($"{invalidAh} rows have invalid AH/LCONT_AMOUNT_SAL contractual obligations and fail closed for schedule reconstruction.");
        return new InstallmentMasterReadResult(
            file.Name,
            sheet.Name,
            rows.Count,
            contracts.Count,
            contracts.Count(x => x.IsUsable),
            contracts.Count(x => !x.IsUsable),
            duplicateGroups,
            invalidAl,
            invalidAm,
            contracts,
            warnings)
        {
            SchemaMode = schemaMode
        };
    }

    private static string DetectSchemaMode(ExcelWorksheet sheet)
    {
        var contractHeader = sheet.Cells[1, ContractNumberColumn].Text.Trim();
        var downPaymentHeader = sheet.Cells[1, DownPaymentColumn].Text.Trim();
        var contractualObligationHeader = sheet.Cells[1, ContractualObligationColumn].Text.Trim();
        var totalHeader = sheet.Cells[1, TotalInstallmentsColumn].Text.Trim();
        var monthlyHeader = sheet.Cells[1, MonthlyInstallmentColumn].Text.Trim();
        var headerMatches = new[]
        {
            string.Equals(contractHeader, "LCONT_ID", StringComparison.OrdinalIgnoreCase),
            string.Equals(downPaymentHeader, "LREC_SAL_ADVANCE", StringComparison.OrdinalIgnoreCase),
            string.Equals(contractualObligationHeader, "LCONT_AMOUNT_SAL", StringComparison.OrdinalIgnoreCase),
            string.Equals(totalHeader, "LREG_MAX_INSTALL", StringComparison.OrdinalIgnoreCase),
            string.Equals(monthlyHeader, "LREG_SALINT", StringComparison.OrdinalIgnoreCase)
        };
        if (headerMatches.All(value => value))
            return InstallmentMasterSchemaModes.Headered;
        if (headerMatches.Any(value => value))
        {
            ValidateHeaderedSchema(contractHeader, downPaymentHeader, contractualObligationHeader, totalHeader, monthlyHeader);
            throw new InstallmentMasterSchemaValidationException(
                InstallmentMasterSchemaModes.Headered,
                "Installment Master header validation failed unexpectedly.");
        }

        ValidateHeaderlessPositionalSchema(sheet);
        return InstallmentMasterSchemaModes.HeaderlessPositional;
    }

    private static void ValidateHeaderedSchema(
        string contractHeader,
        string downPaymentHeader,
        string contractualObligationHeader,
        string totalHeader,
        string monthlyHeader)
    {
        var errors = new List<string>();
        if (!string.Equals(contractHeader, "LCONT_ID", StringComparison.OrdinalIgnoreCase))
            errors.Add("A1 must be LCONT_ID.");
        if (!string.Equals(downPaymentHeader, "LREC_SAL_ADVANCE", StringComparison.OrdinalIgnoreCase))
            errors.Add("AE1 must be LREC_SAL_ADVANCE.");
        if (!string.Equals(contractualObligationHeader, "LCONT_AMOUNT_SAL", StringComparison.OrdinalIgnoreCase))
            errors.Add("AH1 must be LCONT_AMOUNT_SAL.");
        if (!string.Equals(totalHeader, "LREG_MAX_INSTALL", StringComparison.OrdinalIgnoreCase))
            errors.Add("AL1 must be LREG_MAX_INSTALL.");
        if (!string.Equals(monthlyHeader, "LREG_SALINT", StringComparison.OrdinalIgnoreCase))
            errors.Add("AM1 must be LREG_SALINT.");
        if (errors.Count > 0)
            throw new InstallmentMasterSchemaValidationException(
                InstallmentMasterSchemaModes.Headered,
                $"Installment Master schema validation failed: {string.Join(" ", errors)}");
    }

    private static void ValidateHeaderlessPositionalSchema(ExcelWorksheet sheet)
    {
        if (sheet.Dimension!.End.Column < MonthlyInstallmentColumn)
        {
            throw new InstallmentMasterSchemaValidationException(
                InstallmentMasterSchemaModes.HeaderlessPositional,
                "Installment Master HEADERLESS_POSITIONAL validation failed: required columns A through AM are not present.");
        }

        var sampleRows = Enumerable.Range(1, sheet.Dimension.End.Row)
            .Where(row => HasAnyRequiredContent(sheet, row))
            .Take(HeaderlessSampleSize)
            .ToArray();
        if (sampleRows.Length < MinimumHeaderlessSampleRows)
        {
            throw new InstallmentMasterSchemaValidationException(
                InstallmentMasterSchemaModes.HeaderlessPositional,
                $"Installment Master HEADERLESS_POSITIONAL validation failed: at least {MinimumHeaderlessSampleRows} populated sample rows are required.");
        }

        var errors = new List<string>();
        var populatedRows = Enumerable.Range(1, sheet.Dimension.End.Row)
            .Where(row => HasAnyRequiredContent(sheet, row))
            .ToArray();
        if (populatedRows.Any(row => !IsContractNumber(sheet.Cells[row, ContractNumberColumn])))
            errors.Add("column A does not consistently contain ContractNo values in the locked format.");
        if (!MeetsCompatibilityThreshold(sampleRows, row => IsNonNegativeDecimalOrBlank(sheet.Cells[row, DownPaymentColumn])))
            errors.Add("column AE does not behave like DownPayment/LREC_SAL_ADVANCE numeric-or-blank data.");
        if (!MeetsCompatibilityThreshold(sampleRows, row => ReadPositiveDecimal(sheet.Cells[row, ContractualObligationColumn]).HasValue))
            errors.Add("column AH does not behave like ContractualObligation/LCONT_AMOUNT_SAL positive-numeric data.");
        if (!MeetsCompatibilityThreshold(sampleRows, row => ReadPositiveInteger(sheet.Cells[row, TotalInstallmentsColumn]).HasValue))
            errors.Add("column AL does not behave like TotalInstallments/LREG_MAX_INSTALL positive-integer data.");
        if (!MeetsCompatibilityThreshold(sampleRows, row => ReadPositiveDecimal(sheet.Cells[row, MonthlyInstallmentColumn]).HasValue))
            errors.Add("column AM does not behave like MonthlyInstallment/LREG_SALINT positive-numeric data.");

        if (errors.Count > 0)
        {
            throw new InstallmentMasterSchemaValidationException(
                InstallmentMasterSchemaModes.HeaderlessPositional,
                $"Installment Master HEADERLESS_POSITIONAL structural validation failed: {string.Join(" ", errors)}");
        }
    }

    private static bool HasAnyRequiredContent(ExcelWorksheet sheet, int row) =>
        HasContent(sheet.Cells[row, ContractNumberColumn]) ||
        HasContent(sheet.Cells[row, DownPaymentColumn]) ||
        HasContent(sheet.Cells[row, ContractualObligationColumn]) ||
        HasContent(sheet.Cells[row, TotalInstallmentsColumn]) ||
        HasContent(sheet.Cells[row, MonthlyInstallmentColumn]);

    private static bool IsContractNumber(ExcelRange cell)
    {
        var value = NormalizeContractNumber(cell);
        return Regex.IsMatch(value, @"^[^-\s]+-\d{4}-\d{6}$", RegexOptions.CultureInvariant);
    }

    private static bool IsNonNegativeDecimalOrBlank(ExcelRange cell) =>
        !HasContent(cell) || ReadNonNegativeDecimal(cell).HasValue;

    private static bool MeetsCompatibilityThreshold(
        IReadOnlyCollection<int> rows,
        Func<int, bool> predicate) =>
        rows.Count(predicate) * 100 >= rows.Count * MinimumStructuralCompatibilityPercent;

    public static string NormalizeContractNumber(ExcelRange cell)
    {
        if (cell.Value is null || cell.Value is ExcelErrorValue)
            return string.Empty;
        return cell.Value switch
        {
            string text => text.Trim(),
            double value => value.ToString("G15", CultureInfo.InvariantCulture).Trim(),
            float value => value.ToString("G9", CultureInfo.InvariantCulture).Trim(),
            decimal value => value.ToString(CultureInfo.InvariantCulture).Trim(),
            _ => Convert.ToString(cell.Value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
        };
    }

    private static string ValidationStatus(decimal? contractualObligation, int? total, decimal? monthly) =>
        !contractualObligation.HasValue
            ? InstallmentValidationStatuses.InvalidContractualObligation
            : (!total.HasValue, !monthly.HasValue) switch
            {
                (false, false) => InstallmentValidationStatuses.Usable,
                (true, false) => InstallmentValidationStatuses.InvalidTotalInstallments,
                (false, true) => InstallmentValidationStatuses.InvalidMonthlyInstallment,
                _ => InstallmentValidationStatuses.InvalidTotalAndMonthlyInstallment
            };

    private static int? ReadPositiveInteger(ExcelRange cell)
    {
        var value = ReadDecimal(cell);
        if (!value.HasValue || value <= 0m || decimal.Truncate(value.Value) != value.Value || value > int.MaxValue)
            return null;
        return decimal.ToInt32(value.Value);
    }

    private static decimal? ReadPositiveDecimal(ExcelRange cell)
    {
        var value = ReadDecimal(cell);
        return value is > 0m ? value : null;
    }

    private static decimal? ReadNonNegativeDecimal(ExcelRange cell)
    {
        var value = ReadDecimal(cell);
        return value is >= 0m ? value : null;
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
                    cell.Text.Trim().Replace(",", string.Empty, StringComparison.Ordinal),
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

    private static bool HasContent(ExcelRange cell) =>
        cell.Value is not null && cell.Value is not ExcelErrorValue && !string.IsNullOrWhiteSpace(cell.Text);

    private sealed record ParsedRow(
        int SourceRowNumber,
        string ContractNumber,
        decimal? DownPayment,
        bool DownPaymentHasContent,
        bool DownPaymentValid,
        decimal? ContractualObligation,
        int? TotalInstallments,
        decimal? MonthlyInstallment);
}
