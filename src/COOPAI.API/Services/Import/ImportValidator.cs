using System.Globalization;
using COOPAI.API.Models.Import;

namespace COOPAI.API.Services.Import;

public class ValidationError
{
    public string FieldName { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RawValue { get; set; } = string.Empty;
    public string ParsedValue { get; set; } = string.Empty;
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string Status { get; set; } = "Validated";
    public bool IsSkipped => Status == "SkippedInactiveLoan";
    public List<string> Errors { get; set; } = new();
    public List<ValidationError> Details { get; set; } = new();
}

public class ImportValidator
{
    public const string RefinancedNoProfitStatus = "RefinancedNoProfit";

    private readonly ContractNoClassifier _contractNoClassifier;

    public ImportValidator()
        : this(new ContractNoClassifier())
    {
    }

    public ImportValidator(ContractNoClassifier contractNoClassifier)
    {
        _contractNoClassifier = contractNoClassifier;
    }

    public ValidationResult Validate(ImportLoanRecord record)
    {
        var result = new ValidationResult();
        record.PreviousProfitBusinessStatus = string.Empty;

        ValidateRequired(record.MemberNo, "MemberNo", result);
        ValidateRequired(record.MemberName, "MemberName", result);

        var classification = _contractNoClassifier.Classify(record.ContractNo);
        if (!classification.IsValid)
        {
            AddError(result, "ContractNo", classification.ErrorType, classification.ErrorMessage,
                record.ContractNo ?? string.Empty, string.Empty);
        }

        var previousPrincipal = NumericField.Create("PreviousPrincipal", record.PreviousPrincipalRaw, record.PreviousPrincipalParsedValue, record.PreviousPrincipalParseStatus);
        var previousProfit = NumericField.Create("PreviousProfit", record.PreviousProfitRaw, record.PreviousProfitParsedValue, record.PreviousProfitParseStatus);
        var previousTotal = NumericField.Create("PreviousTotal", record.PreviousTotalRaw, record.PreviousTotalParsedValue, record.PreviousTotalParseStatus);
        var currentPrincipal = NumericField.Create("CurrentPrincipal", record.CurrentPrincipalRaw, record.CurrentPrincipalParsedValue, record.CurrentPrincipalParseStatus);
        var currentProfit = NumericField.Create("CurrentProfit", record.CurrentProfitRaw, record.CurrentProfitParsedValue, record.CurrentProfitParseStatus);
        var currentTotal = NumericField.Create("CurrentTotal", record.CurrentTotalRaw, record.CurrentTotalParsedValue, record.CurrentTotalParseStatus);

        if (IsRefinancedNoProfit(record, classification, previousPrincipal, previousTotal, currentPrincipal, currentProfit, currentTotal))
        {
            record.PreviousProfit = 0m;
            record.PreviousProfitParsedValue = 0m;
            record.PreviousProfitParseStatus = "Parsed";
            record.PreviousProfitBusinessStatus = RefinancedNoProfitStatus;
            previousProfit = NumericField.Create("PreviousProfit", record.PreviousProfitRaw, 0m, "Parsed");
        }

        var financialFields = new[]
        {
            previousPrincipal, previousProfit, previousTotal,
            currentPrincipal, currentProfit, currentTotal
        };

        foreach (var field in financialFields)
            ValidateNumericState(field, result);

        ValidateOptionalOverdueDays(record, result);

        var principalHasParseError = !previousPrincipal.CanDeterminePresence || !currentPrincipal.CanDeterminePresence;
        if (classification.IsValid && !principalHasParseError)
        {
            if (previousPrincipal.IsParsed && currentPrincipal.IsParsed)
            {
                AddError(result, "PrincipalBalance", "PrincipalBalanceConflict",
                    "พบเงินต้นทั้งปีก่อนหน้า G และปีปัจจุบัน J พร้อมกัน", JoinRaw(previousPrincipal, currentPrincipal), string.Empty);
            }
            else if (previousPrincipal.IsBlank && currentPrincipal.IsBlank)
            {
                result.Status = "SkippedInactiveLoan";
            }
            else if (previousPrincipal.IsParsed)
            {
                ValidateInactiveSide("ปีปัจจุบัน", currentProfit, currentTotal, result);
                ValidateActiveSide(classification.HasProfit, previousPrincipal, previousProfit, previousTotal, result);
            }
            else if (currentPrincipal.IsParsed)
            {
                ValidateInactiveSide("ปีก่อนหน้า", previousProfit, previousTotal, result);
                ValidateActiveSide(classification.HasProfit, currentPrincipal, currentProfit, currentTotal, result);
            }
        }

        result.IsValid = result.Errors.Count == 0 && !result.IsSkipped;
        return result;
    }

    private static bool IsRefinancedNoProfit(
        ImportLoanRecord record,
        ContractClassificationResult classification,
        NumericField previousPrincipal,
        NumericField previousTotal,
        NumericField currentPrincipal,
        NumericField currentProfit,
        NumericField currentTotal)
    {
        if (!classification.IsValid || !classification.HasProfit)
            return false;

        if (!previousPrincipal.IsParsed || previousPrincipal.Value is null ||
            !previousTotal.IsParsed || previousTotal.Value is null)
        {
            return false;
        }

        if (!currentPrincipal.IsBlank || !currentProfit.IsBlank || !currentTotal.IsBlank)
            return false;

        if (record.PreviousProfitUnderlyingNumericValue != 0m ||
            !string.Equals(record.PreviousProfitRaw?.Trim(), "-", StringComparison.Ordinal) ||
            !DisplaysAccountingZeroAsDash(record.PreviousProfitNumberFormat))
        {
            return false;
        }

        return NormalizeMoney(previousTotal.Value.Value) == NormalizeMoney(previousPrincipal.Value.Value);
    }

    private static bool DisplaysAccountingZeroAsDash(string? numberFormat)
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

    private static void ValidateActiveSide(bool hasProfit, NumericField principal, NumericField profit, NumericField total, ValidationResult result)
    {
        if (hasProfit)
        {
            ValidateRequiredActiveField(profit, result);
        }
        else if (profit.IsParsed)
        {
            AddError(result, profit.Name, "UnexpectedProfit",
                $"{profit.Name} มีค่า ทั้งที่สัญญาประเภท สศ ไม่มีผลตอบแทน",
                profit.RawValue, Format(profit.Value));
        }

        ValidateRequiredActiveField(total, result);

        if (!principal.IsParsed || !total.IsParsed || principal.Value is null || total.Value is null)
            return;

        if (hasProfit && (!profit.IsParsed || profit.Value is null))
            return;

        var normalizedPrincipal = NormalizeMoney(principal.Value.Value);
        var normalizedProfit = hasProfit ? NormalizeMoney(profit.Value!.Value) : 0m;
        var expectedTotal = NormalizeMoney(normalizedPrincipal + normalizedProfit);
        var actualTotal = NormalizeMoney(total.Value.Value);

        if (actualTotal == expectedTotal)
            return;

        var profitText = hasProfit ? FormatMoney(normalizedProfit) : "Blank (ไม่มีผลตอบแทน)";
        AddError(result, total.Name, "TotalBalanceMismatch",
            $"ยอดรวมไม่ถูกต้อง: Principal={FormatMoney(normalizedPrincipal)}, Profit={profitText}, Expected Total={FormatMoney(expectedTotal)}, Actual Total={FormatMoney(actualTotal)}",
            total.RawValue, Format(total.Value));
    }

    private static void ValidateInactiveSide(string sideName, NumericField profit, NumericField total, ValidationResult result)
    {
        foreach (var field in new[] { profit, total })
        {
            if (field.IsBlank)
                continue;

            AddError(result, field.Name, "InactiveSideData",
                $"พบข้อมูล {field.Name} ในฝั่ง{sideName} ทั้งที่สัญญา active อีกฝั่งหนึ่ง",
                field.RawValue, Format(field.Value));
        }
    }

    private static void ValidateRequiredActiveField(NumericField field, ValidationResult result)
    {
        if (!field.IsBlank)
            return;

        AddError(result, field.Name, "Missing", $"{field.Name} ต้องระบุสำหรับฝั่งที่ active",
            field.RawValue, string.Empty);
    }

    private static void ValidateNumericState(NumericField field, ValidationResult result)
    {
        if (field.IsInvalid)
        {
            AddError(result, field.Name, "InvalidNumeric", $"{field.Name} มีรูปแบบตัวเลขไม่ถูกต้อง",
                field.RawValue, string.Empty);
            return;
        }

        if (field.IsParsed && field.Value is null)
        {
            AddError(result, field.Name, "InvalidNumeric", $"{field.Name} ระบุสถานะ Parsed แต่ไม่มีค่าตัวเลข",
                field.RawValue, string.Empty);
            return;
        }

        if (field.Value is < 0)
        {
            AddError(result, field.Name, "Negative",
                $"ข้อมูล Excel ไม่ถูกต้อง: {field.Name} มีค่าติดลบ กรุณาแก้ไขข้อมูลหรือข้ามรายการนี้",
                field.RawValue, Format(field.Value));
        }
    }

    private static void ValidateOptionalOverdueDays(ImportLoanRecord record, ValidationResult result)
    {
        var status = NormalizeStatus(record.OverdueDaysParseStatus, record.OverdueDaysParsedValue);
        if (status == "Blank")
            return;

        if (status == "InvalidNumeric")
        {
            AddError(result, "OverdueDays", "InvalidNumeric", "OverdueDays มีรูปแบบตัวเลขไม่ถูกต้อง",
                record.OverdueDaysRaw, string.Empty);
            return;
        }

        var value = record.OverdueDaysParsedValue;
        if (value is < 0)
        {
            AddError(result, "OverdueDays", "Negative", $"OverdueDays ติดลบ ({value.Value})",
                record.OverdueDaysRaw, value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void ValidateRequired(string? value, string fieldName, ValidationResult result)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return;

        AddError(result, fieldName, "Required", "ข้อมูลต้องระบุ", value ?? string.Empty, string.Empty);
    }

    private static void AddError(ValidationResult result, string fieldName, string errorType, string message, string rawValue, string parsedValue)
    {
        result.Errors.Add(message);
        result.Details.Add(new ValidationError
        {
            FieldName = fieldName,
            ErrorType = errorType,
            Message = message,
            RawValue = rawValue,
            ParsedValue = parsedValue
        });
    }

    private static string NormalizeStatus(string status, decimal? value) =>
        string.IsNullOrWhiteSpace(status) ? (value.HasValue ? "Parsed" : "Blank") : status;

    private static string NormalizeStatus(string status, int? value) =>
        string.IsNullOrWhiteSpace(status) ? (value.HasValue ? "Parsed" : "Blank") : status;

    private static string Format(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static decimal NormalizeMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FormatMoney(decimal value) =>
        value.ToString("F2", CultureInfo.InvariantCulture);

    private static string JoinRaw(NumericField first, NumericField second) =>
        $"G={first.RawValue}; J={second.RawValue}";

    private readonly record struct NumericField(string Name, string RawValue, decimal? Value, string Status)
    {
        public bool IsParsed => Status == "Parsed";
        public bool IsBlank => Status == "Blank";
        public bool IsInvalid => Status == "InvalidNumeric";
        public bool CanDeterminePresence => IsParsed || IsBlank;

        public static NumericField Create(string name, string rawValue, decimal? value, string status) =>
            new(name, rawValue ?? string.Empty, value, NormalizeStatus(status, value));
    }
}
