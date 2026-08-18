using COOPAI.API.Models.Import;
using COOPAI.API.Services.Import;
using Xunit;

namespace COOPAI.API.Tests;

public class ImportValidatorTests
{
    private const string AccountingZeroFormat = "_-* #,##0_-;\\-* #,##0_-;_-* \"-\"??_-;_-@_-";

    [Fact]
    public void Validate_PreviousHasProfit_Valid()
    {
        var record = CreateRecord();
        SetParsed(record, "PreviousPrincipal", 100000m);
        SetParsed(record, "PreviousProfit", 25000m);
        SetParsed(record, "PreviousTotal", 125000m);

        AssertValid(record);
    }

    [Fact]
    public void Validate_CurrentHasProfit_Valid_RealExcelRegression()
    {
        var record = CreateRecord("สห-2569-000600");
        SetParsed(record, "CurrentPrincipal", 123000m);
        SetParsed(record, "CurrentProfit", 25830m);
        SetParsed(record, "CurrentTotal", 148830m);

        AssertValid(record);
    }

    [Theory]
    [InlineData("PreviousPrincipal")]
    [InlineData("CurrentPrincipal")]
    public void Validate_ZeroPrincipal_IsParsedAndSelectsActiveSide(string principalField)
    {
        var record = CreateRecord();
        SetParsed(record, principalField, 0m);

        if (principalField == "PreviousPrincipal")
        {
            SetParsed(record, "PreviousProfit", 0m);
            SetParsed(record, "PreviousTotal", 0m);
        }
        else
        {
            SetParsed(record, "CurrentProfit", 0m);
            SetParsed(record, "CurrentTotal", 0m);
        }

        AssertValid(record);
    }

    [Fact]
    public void Validate_BothPrincipalsParsed_ReturnsConflict()
    {
        var record = CreateRecord();
        SetParsed(record, "PreviousPrincipal", 100m);
        SetParsed(record, "CurrentPrincipal", 200m);

        AssertError(record, "PrincipalBalance", "PrincipalBalanceConflict");
    }

    [Fact]
    public void Validate_BothPrincipalsBlank_ReturnsSkippedInactiveLoan()
    {
        var result = new ImportValidator().Validate(CreateRecord());

        Assert.False(result.IsValid);
        Assert.True(result.IsSkipped);
        Assert.Equal("SkippedInactiveLoan", result.Status);
        Assert.DoesNotContain(result.Details, detail => detail.ErrorType == "MissingPrincipalBalance");
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("\u0E2A\u0E21-2534-000005", true)]
    [InlineData("\u0E2A\u0E2D-2566-000415", true)]
    [InlineData("\u0E2A\u0E2D-2569-000001", false)]
    public void Validate_ApprovedNewPrefix_ValidatesProfitOnActiveSide(string contractNo, bool previous)
    {
        var record = CreateRecord(contractNo);
        SetActiveValues(record, previous, 100m, 20m, 120m);

        AssertValid(record);
    }

    [Fact]
    public void Validate_MonetaryRepresentationDifferenceAtTwoDecimals_IsValid()
    {
        var record = CreateRecord("\u0E2A\u0E21-2552-000036");
        SetParsed(record, "PreviousPrincipal", 11867.28m);
        SetParsed(record, "PreviousProfit", 3318.7200000000003m);
        SetParsed(record, "PreviousTotal", 15186m);

        AssertValid(record);
    }

    [Fact]
    public void Validate_MonetaryDifferenceOfOneCent_ReturnsMismatch()
    {
        var record = CreateRecord();
        SetActiveValues(record, true, 11867.28m, 3318.72m, 15186.01m);

        AssertError(record, "PreviousTotal", "TotalBalanceMismatch");
    }

    [Theory]
    [InlineData("Borrower Without Marker")]
    [InlineData("Borrower (R)")]
    [InlineData("Borrower \u00AE")]
    public void Validate_RefinancedNoProfitSignature_IsValidRegardlessOfMemberNameMarker(string memberName)
    {
        var record = CreateRecord();
        record.MemberName = memberName;
        SetRefinancedNoProfitEvidence(record, 85000m, 85000m);

        var result = new ImportValidator().Validate(record);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.DoesNotContain(result.Details, detail => detail.ErrorType is "InvalidNumeric" or "Missing");
        Assert.Equal(ImportValidator.RefinancedNoProfitStatus, record.PreviousProfitBusinessStatus);
        Assert.Equal(0m, record.PreviousProfitParsedValue);
    }

    [Fact]
    public void Validate_MemberNameRWithPositiveProfit_RemainsOrdinaryHasProfit()
    {
        var record = CreateRecord("\u0E2A\u0E21-2559-000665");
        record.MemberName = "Borrower (R)";
        SetActiveValues(record, true, 17516m, 9184m, 26700m);

        AssertValid(record);
        Assert.Equal(string.Empty, record.PreviousProfitBusinessStatus);
    }

    [Fact]
    public void Validate_LiteralPreviousProfitDash_IsNotRefinancedNoProfit()
    {
        var record = CreateRecord();
        SetParsed(record, "PreviousPrincipal", 85000m);
        SetInvalid(record, "PreviousProfit", "-");
        SetParsed(record, "PreviousTotal", 85000m);
        record.PreviousProfitNumberFormat = AccountingZeroFormat;

        AssertError(record, "PreviousProfit", "InvalidNumeric");
        Assert.Equal(string.Empty, record.PreviousProfitBusinessStatus);
    }

    [Fact]
    public void Validate_OrdinaryNumericZeroDisplayedAsZero_IsNotRefinancedNoProfit()
    {
        var record = CreateRecord();
        SetActiveValues(record, true, 85000m, 0m, 85000m);
        record.PreviousProfitUnderlyingNumericValue = 0m;
        record.PreviousProfitNumberFormat = "0.00";

        AssertValid(record);
        Assert.Equal(string.Empty, record.PreviousProfitBusinessStatus);
    }

    [Fact]
    public void Validate_AccountingZeroDashWithDifferentTotal_RemainsMismatch()
    {
        var record = CreateRecord();
        SetRefinancedNoProfitEvidence(record, 85000m, 85000.01m);

        AssertError(record, "PreviousTotal", "TotalBalanceMismatch");
        Assert.Equal(string.Empty, record.PreviousProfitBusinessStatus);
    }

    [Fact]
    public void Validate_CurrentAccountingZeroDash_DoesNotApplyPreviousRefinanceStatus()
    {
        var record = CreateRecord();
        SetActiveValues(record, false, 85000m, 0m, 85000m);
        record.CurrentProfitRaw = "-";

        AssertValid(record);
        Assert.Equal(string.Empty, record.PreviousProfitBusinessStatus);
    }

    [Theory]
    [InlineData(true, 100000, 25000, 120000, "PreviousTotal")]
    [InlineData(false, 123000, 25000, 148830, "CurrentTotal")]
    public void Validate_HasProfit_TotalMismatch(bool previous, decimal principal, decimal profit, decimal total, string totalField)
    {
        var record = CreateRecord();
        SetActiveValues(record, previous, principal, profit, total);

        var error = AssertError(record, totalField, "TotalBalanceMismatch");
        Assert.Contains("Principal=", error.Message);
        Assert.Contains("Profit=", error.Message);
        Assert.Contains("Expected Total=", error.Message);
        Assert.Contains("Actual Total=", error.Message);
    }

    [Theory]
    [InlineData(true, "PreviousProfit")]
    [InlineData(true, "PreviousTotal")]
    [InlineData(false, "CurrentProfit")]
    [InlineData(false, "CurrentTotal")]
    public void Validate_HasProfit_MissingActiveField(bool previous, string missingField)
    {
        var record = CreateRecord();
        SetActiveValues(record, previous, previous ? 100000m : 123000m, previous ? 25000m : 25830m, previous ? 125000m : 148830m);
        SetBlank(record, missingField);

        AssertError(record, missingField, "Missing");
    }

    [Theory]
    [InlineData(true, 100000, 100000)]
    [InlineData(false, 123000, 123000)]
    public void Validate_NoProfit_BlankProfitAndMatchingTotal_IsValid(bool previous, decimal principal, decimal total)
    {
        var record = CreateRecord("สศ-2569-000001");
        SetActiveValues(record, previous, principal, null, total);

        AssertValid(record);
    }

    [Theory]
    [InlineData(true, 100000, 90000, "PreviousTotal")]
    [InlineData(false, 123000, 120000, "CurrentTotal")]
    public void Validate_NoProfit_TotalMismatch(bool previous, decimal principal, decimal total, string totalField)
    {
        var record = CreateRecord("สศ-2569-000001");
        SetActiveValues(record, previous, principal, null, total);

        AssertError(record, totalField, "TotalBalanceMismatch");
    }

    [Theory]
    [InlineData(true, "PreviousProfit")]
    [InlineData(false, "CurrentProfit")]
    public void Validate_NoProfit_ParsedProfit_ReturnsUnexpectedProfit(bool previous, string profitField)
    {
        var record = CreateRecord("สศ-2569-000001");
        SetActiveValues(record, previous, previous ? 100000m : 123000m, 5000m, previous ? 105000m : 128000m);

        AssertError(record, profitField, "UnexpectedProfit");
    }

    [Theory]
    [InlineData(true, "CurrentProfit")]
    [InlineData(true, "CurrentTotal")]
    [InlineData(false, "PreviousProfit")]
    [InlineData(false, "PreviousTotal")]
    public void Validate_InactiveSideContainsData_ReturnsInactiveSideData(bool previousActive, string inactiveField)
    {
        var record = CreateRecord();
        SetActiveValues(record, previousActive, 100m, 20m, 120m);
        SetParsed(record, inactiveField, 1m);

        AssertError(record, inactiveField, "InactiveSideData");
    }

    [Theory]
    [InlineData("PreviousPrincipal")]
    [InlineData("PreviousProfit")]
    [InlineData("PreviousTotal")]
    [InlineData("CurrentPrincipal")]
    [InlineData("CurrentProfit")]
    [InlineData("CurrentTotal")]
    public void Validate_EachFinancialFieldNegative_ReturnsNegative(string fieldName)
    {
        var record = CreateRecord();
        var previous = fieldName.StartsWith("Previous", StringComparison.Ordinal);
        SetActiveValues(record, previous, 100m, 20m, 120m);
        SetParsed(record, fieldName, -1m);

        var error = AssertError(record, fieldName, "Negative");
        Assert.Contains("ข้อมูล Excel ไม่ถูกต้อง", error.Message);
        Assert.Contains("ข้ามรายการนี้", error.Message);
        Assert.Equal("-1", error.RawValue);
        Assert.Equal("-1", error.ParsedValue);
    }

    [Theory]
    [InlineData("PreviousPrincipal")]
    [InlineData("PreviousProfit")]
    [InlineData("PreviousTotal")]
    [InlineData("CurrentPrincipal")]
    [InlineData("CurrentProfit")]
    [InlineData("CurrentTotal")]
    public void Validate_EachFinancialFieldInvalidNumeric_ReturnsInvalidNumeric(string fieldName)
    {
        var record = CreateRecord();
        var previous = fieldName.StartsWith("Previous", StringComparison.Ordinal);
        SetActiveValues(record, previous, 100m, 20m, 120m);
        SetInvalid(record, fieldName, $"invalid-{fieldName}");

        var error = AssertError(record, fieldName, "InvalidNumeric");
        Assert.Equal($"invalid-{fieldName}", error.RawValue);
        Assert.Equal(string.Empty, error.ParsedValue);
    }

    [Theory]
    [InlineData("สป", true)]
    [InlineData("สฉ", true)]
    [InlineData("สห", true)]
    [InlineData("สท", true)]
    [InlineData("สศ", false)]
    [InlineData("สจ", true)]
    public void Validate_ApprovedContractPrefixes_ApplyHasProfitRule(string prefix, bool hasProfit)
    {
        var record = CreateRecord($"{prefix}-2569-000001");
        SetParsed(record, "CurrentPrincipal", 100m);
        if (hasProfit)
        {
            SetParsed(record, "CurrentProfit", 20m);
            SetParsed(record, "CurrentTotal", 120m);
        }
        else
        {
            SetParsed(record, "CurrentTotal", 100m);
        }

        AssertValid(record);
    }

    [Fact]
    public void Validate_UnknownContractPrefix_UsesClassifierError()
    {
        var record = CreateRecord("XX-2569-000001");
        SetActiveValues(record, false, 100m, 20m, 120m);

        AssertError(record, "ContractNo", "UnknownContractType");
    }

    [Theory]
    [InlineData(null, "MissingContractNo")]
    [InlineData("สห 2569-000001", "InvalidContractNo")]
    [InlineData("สห-69-000001", "InvalidContractYear")]
    [InlineData("สห-2569-ABC", "InvalidRunningNumber")]
    public void Validate_InvalidContract_UsesClassifierClassification(string? contractNo, string errorType)
    {
        var record = CreateRecord(contractNo);
        SetActiveValues(record, false, 100m, 20m, 120m);

        AssertError(record, "ContractNo", errorType);
    }

    [Fact]
    public void Validate_BlankOverdueDays_DoesNotReturnMissing()
    {
        var record = CreateValidCurrentRecord();
        record.OverdueDaysRaw = string.Empty;
        record.OverdueDaysParsedValue = null;
        record.OverdueDaysParseStatus = "Blank";

        AssertValid(record);
    }

    [Theory]
    [InlineData("InvalidNumeric", null, "InvalidNumeric")]
    [InlineData("Parsed", -1, "Negative")]
    public void Validate_OverdueDaysStillValidatesProvidedSource(string status, int? value, string errorType)
    {
        var record = CreateValidCurrentRecord();
        record.OverdueDaysRaw = value?.ToString() ?? "invalid-days";
        record.OverdueDaysParsedValue = value;
        record.OverdueDaysParseStatus = status;

        AssertError(record, "OverdueDays", errorType);
    }

    private static ImportLoanRecord CreateRecord(string? contractNo = "สห-2569-000001") => new()
    {
        MemberNo = "M001",
        MemberName = "Jane Doe",
        ContractNo = contractNo!,
        PreviousPrincipalParseStatus = "Blank",
        PreviousProfitParseStatus = "Blank",
        PreviousTotalParseStatus = "Blank",
        CurrentPrincipalParseStatus = "Blank",
        CurrentProfitParseStatus = "Blank",
        CurrentTotalParseStatus = "Blank",
        OverdueDaysParseStatus = "Blank"
    };

    private static ImportLoanRecord CreateValidCurrentRecord()
    {
        var record = CreateRecord();
        SetActiveValues(record, false, 100m, 20m, 120m);
        return record;
    }

    private static void SetActiveValues(ImportLoanRecord record, bool previous, decimal principal, decimal? profit, decimal total)
    {
        var side = previous ? "Previous" : "Current";
        SetParsed(record, $"{side}Principal", principal);
        if (profit.HasValue)
            SetParsed(record, $"{side}Profit", profit.Value);
        SetParsed(record, $"{side}Total", total);
    }

    private static void SetParsed(ImportLoanRecord record, string fieldName, decimal value)
    {
        var raw = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        switch (fieldName)
        {
            case "PreviousPrincipal": record.PreviousPrincipalRaw = raw; record.PreviousPrincipalParsedValue = value; record.PreviousPrincipalParseStatus = "Parsed"; break;
            case "PreviousProfit": record.PreviousProfitRaw = raw; record.PreviousProfitParsedValue = value; record.PreviousProfitParseStatus = "Parsed"; break;
            case "PreviousTotal": record.PreviousTotalRaw = raw; record.PreviousTotalParsedValue = value; record.PreviousTotalParseStatus = "Parsed"; break;
            case "CurrentPrincipal": record.CurrentPrincipalRaw = raw; record.CurrentPrincipalParsedValue = value; record.CurrentPrincipalParseStatus = "Parsed"; break;
            case "CurrentProfit": record.CurrentProfitRaw = raw; record.CurrentProfitParsedValue = value; record.CurrentProfitParseStatus = "Parsed"; break;
            case "CurrentTotal": record.CurrentTotalRaw = raw; record.CurrentTotalParsedValue = value; record.CurrentTotalParseStatus = "Parsed"; break;
            default: throw new ArgumentOutOfRangeException(nameof(fieldName));
        }
    }

    private static void SetRefinancedNoProfitEvidence(ImportLoanRecord record, decimal principal, decimal total)
    {
        SetParsed(record, "PreviousPrincipal", principal);
        SetParsed(record, "PreviousProfit", 0m);
        SetParsed(record, "PreviousTotal", total);
        record.PreviousProfitRaw = "-";
        record.PreviousProfitUnderlyingNumericValue = 0m;
        record.PreviousProfitNumberFormat = AccountingZeroFormat;
    }

    private static void SetBlank(ImportLoanRecord record, string fieldName)
    {
        switch (fieldName)
        {
            case "PreviousProfit": record.PreviousProfitRaw = string.Empty; record.PreviousProfitParsedValue = null; record.PreviousProfitParseStatus = "Blank"; break;
            case "PreviousTotal": record.PreviousTotalRaw = string.Empty; record.PreviousTotalParsedValue = null; record.PreviousTotalParseStatus = "Blank"; break;
            case "CurrentProfit": record.CurrentProfitRaw = string.Empty; record.CurrentProfitParsedValue = null; record.CurrentProfitParseStatus = "Blank"; break;
            case "CurrentTotal": record.CurrentTotalRaw = string.Empty; record.CurrentTotalParsedValue = null; record.CurrentTotalParseStatus = "Blank"; break;
            default: throw new ArgumentOutOfRangeException(nameof(fieldName));
        }
    }

    private static void SetInvalid(ImportLoanRecord record, string fieldName, string raw)
    {
        switch (fieldName)
        {
            case "PreviousPrincipal": record.PreviousPrincipalRaw = raw; record.PreviousPrincipalParsedValue = null; record.PreviousPrincipalParseStatus = "InvalidNumeric"; break;
            case "PreviousProfit": record.PreviousProfitRaw = raw; record.PreviousProfitParsedValue = null; record.PreviousProfitParseStatus = "InvalidNumeric"; break;
            case "PreviousTotal": record.PreviousTotalRaw = raw; record.PreviousTotalParsedValue = null; record.PreviousTotalParseStatus = "InvalidNumeric"; break;
            case "CurrentPrincipal": record.CurrentPrincipalRaw = raw; record.CurrentPrincipalParsedValue = null; record.CurrentPrincipalParseStatus = "InvalidNumeric"; break;
            case "CurrentProfit": record.CurrentProfitRaw = raw; record.CurrentProfitParsedValue = null; record.CurrentProfitParseStatus = "InvalidNumeric"; break;
            case "CurrentTotal": record.CurrentTotalRaw = raw; record.CurrentTotalParsedValue = null; record.CurrentTotalParseStatus = "InvalidNumeric"; break;
            default: throw new ArgumentOutOfRangeException(nameof(fieldName));
        }
    }

    private static void AssertValid(ImportLoanRecord record)
    {
        var result = new ImportValidator().Validate(record);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Empty(result.Details);
    }

    private static ValidationError AssertError(ImportLoanRecord record, string fieldName, string errorType)
    {
        var result = new ImportValidator().Validate(record);
        Assert.False(result.IsValid);
        return Assert.Single(result.Details.Where(detail => detail.FieldName == fieldName && detail.ErrorType == errorType));
    }
}
