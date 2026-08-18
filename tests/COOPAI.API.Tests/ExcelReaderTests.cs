using System.Globalization;
using System.IO;
using COOPAI.API.Models.Import;
using COOPAI.API.Services.Import;
using OfficeOpenXml;
using Xunit;

namespace COOPAI.API.Tests;

public class ExcelReaderTests
{
    private const string AccountingZeroFormat = "_-* #,##0_-;\\-* #,##0_-;_-* \"-\"??_-;_-@_-";

    [Fact]
    public void ReadRecords_NumericCellStates_PreservesParseStatusAndValidationOutcome()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"excelreader_numeric_{Guid.NewGuid():N}.xlsx");

        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Tests");
            using (var package = new ExcelPackage(new FileInfo(tempFile)))
            {
                var sheet = package.Workbook.Worksheets.Add("ป้อนประจำวัน");
                CreateMultiLevelHeaders(sheet);

                AddMultiLevelDataRow(sheet, 3, "สห-2569-000001", null, null, null, 500m, 0m, 500m);
                AddMultiLevelDataRow(sheet, 4, "สห-2569-000002", null, null, null, 500m, 25m, 525m);
                AddMultiLevelDataRow(sheet, 5, "สห-2569-000003", null, null, null, 500m, -87m, 413m);
                AddMultiLevelDataRow(sheet, 6, "สห-2569-000004", null, null, null, 500m, null, 500m);
                AddMultiLevelDataRow(sheet, 7, "สห-2569-000005", null, null, null, 500m, "-", 500m);
                package.Save();
            }

            var records = new ExcelReader().ReadRecords(tempFile);
            var validator = new ImportValidator();

            Assert.Equal(5, records.Count);
            Assert.Equal("Parsed", records[0].PreviousProfitParseStatus);
            Assert.Equal(0m, records[0].PreviousProfitParsedValue);
            Assert.True(validator.Validate(records[0]).IsValid);

            Assert.Equal("Parsed", records[1].PreviousProfitParseStatus);
            Assert.Equal(25m, records[1].PreviousProfitParsedValue);
            Assert.True(validator.Validate(records[1]).IsValid);

            Assert.Equal("Parsed", records[2].PreviousProfitParseStatus);
            Assert.Equal(-87m, records[2].PreviousProfitParsedValue);
            Assert.Equal("Negative", Assert.Single(validator.Validate(records[2]).Details).ErrorType);

            Assert.Equal("Blank", records[3].PreviousProfitParseStatus);
            Assert.Null(records[3].PreviousProfitParsedValue);
            Assert.Equal("Missing", Assert.Single(validator.Validate(records[3]).Details).ErrorType);

            Assert.Equal("InvalidNumeric", records[4].PreviousProfitParseStatus);
            Assert.Null(records[4].PreviousProfitParsedValue);
            Assert.Equal("InvalidNumeric", Assert.Single(validator.Validate(records[4]).Details).ErrorType);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadRecords_PreviousProfitSourceEvidence_DistinguishesAccountingZeroFromLiteralDashAndOrdinaryZero()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"excelreader_refinance_{Guid.NewGuid():N}.xlsx");

        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Tests");
            using (var package = new ExcelPackage(new FileInfo(tempFile)))
            {
                var sheet = package.Workbook.Worksheets.Add("\u0E1B\u0E49\u0E2D\u0E19\u0E1B\u0E23\u0E30\u0E08\u0E33\u0E27\u0E31\u0E19");
                CreateMultiLevelHeaders(sheet);

                AddMultiLevelDataRow(sheet, 3, "\u0E2A\u0E2B-2569-000101", null, null, null, 85000m, 0m, 85000m);
                sheet.Cells[3, 8].Style.Numberformat.Format = AccountingZeroFormat;

                AddMultiLevelDataRow(sheet, 4, "\u0E2A\u0E2B-2569-000102", null, null, null, 85000m, "-", 85000m);
                sheet.Cells[4, 8].Style.Numberformat.Format = AccountingZeroFormat;

                AddMultiLevelDataRow(sheet, 5, "\u0E2A\u0E2B-2569-000103", null, null, null, 85000m, 0m, 85000m);

                AddMultiLevelDataRow(sheet, 6, "\u0E2A\u0E2B-2569-000104", 85000m, 0m, 85000m, null, null, null);
                sheet.Cells[6, 11].Style.Numberformat.Format = AccountingZeroFormat;

                AddMultiLevelDataRow(sheet, 7, "\u0E2A\u0E2B-2552-000036", null, null, null, 11867m, 3318.72m, 15186m);
                sheet.Cells[7, 8].Style.Numberformat.Format = "#,##0";

                AddMultiLevelDataRow(sheet, 8, "\u0E2A\u0E08-2563-000301", null, null, null, 8151m, 2349.50m, 10500m);
                sheet.Cells[8, 8].Style.Numberformat.Format = "#,##0";
                package.Save();
            }

            var records = new ExcelReader().ReadRecords(tempFile);
            var validator = new ImportValidator();

            var accountingZero = records.Single(record => record.RowNo == 3);
            Assert.Equal("-", accountingZero.PreviousProfitRaw);
            Assert.Equal(0m, accountingZero.PreviousProfitUnderlyingNumericValue);
            Assert.Equal(AccountingZeroFormat, accountingZero.PreviousProfitNumberFormat);
            Assert.Equal(("Parsed", (decimal?)0m), (accountingZero.PreviousProfitParseStatus, accountingZero.PreviousProfitParsedValue));
            Assert.True(validator.Validate(accountingZero).IsValid);
            Assert.Equal(ImportValidator.RefinancedNoProfitStatus, accountingZero.PreviousProfitBusinessStatus);

            var literalDash = records.Single(record => record.RowNo == 4);
            Assert.Equal("-", literalDash.PreviousProfitRaw);
            Assert.Null(literalDash.PreviousProfitUnderlyingNumericValue);
            Assert.Equal("InvalidNumeric", literalDash.PreviousProfitParseStatus);
            Assert.Equal("InvalidNumeric", Assert.Single(validator.Validate(literalDash).Details).ErrorType);
            Assert.Equal(string.Empty, literalDash.PreviousProfitBusinessStatus);

            var ordinaryZero = records.Single(record => record.RowNo == 5);
            Assert.Equal("0", ordinaryZero.PreviousProfitRaw);
            Assert.Equal(0m, ordinaryZero.PreviousProfitUnderlyingNumericValue);
            Assert.Equal("Parsed", ordinaryZero.PreviousProfitParseStatus);
            Assert.True(validator.Validate(ordinaryZero).IsValid);
            Assert.Equal(string.Empty, ordinaryZero.PreviousProfitBusinessStatus);

            var currentAccountingZero = records.Single(record => record.RowNo == 6);
            var currentValidation = validator.Validate(currentAccountingZero);
            Assert.Contains(currentValidation.Details, detail => detail.FieldName == "CurrentProfit" && detail.ErrorType == "InvalidNumeric");
            Assert.Equal(string.Empty, currentAccountingZero.PreviousProfitBusinessStatus);

            var ordinaryHiddenFraction = records.Single(record => record.RowNo == 7);
            Assert.Equal("3,319", ordinaryHiddenFraction.PreviousProfitRaw);
            Assert.Equal(3318.72m, ordinaryHiddenFraction.PreviousProfitUnderlyingNumericValue);
            Assert.Equal(("Parsed", (decimal?)3319m), (ordinaryHiddenFraction.PreviousProfitParseStatus, ordinaryHiddenFraction.PreviousProfitParsedValue));
            Assert.True(validator.Validate(ordinaryHiddenFraction).IsValid);
            Assert.Equal(string.Empty, ordinaryHiddenFraction.PreviousProfitBusinessStatus);

            var row948Style = records.Single(record => record.RowNo == 8);
            Assert.Equal("2,350", row948Style.PreviousProfitRaw);
            Assert.Equal(2349.50m, row948Style.PreviousProfitUnderlyingNumericValue);
            Assert.Equal(("Parsed", (decimal?)2350m), (row948Style.PreviousProfitParseStatus, row948Style.PreviousProfitParsedValue));
            var row948Error = Assert.Single(validator.Validate(row948Style).Details);
            Assert.Equal("TotalBalanceMismatch", row948Error.ErrorType);
            Assert.Contains("Profit=2350.00", row948Error.Message);
            Assert.Contains("Expected Total=10501.00", row948Error.Message);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadRecords_InactiveContractWithBlankMember_IsIncludedAndFooterWithoutContractIsExcluded()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"excelreader_inactive_{Guid.NewGuid():N}.xlsx");

        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Tests");
            using (var package = new ExcelPackage(new FileInfo(tempFile)))
            {
                var sheet = package.Workbook.Worksheets.Add("ป้อนประจำวัน");
                CreateMultiLevelHeaders(sheet);

                AddMultiLevelDataRow(
                    sheet,
                    3,
                    "สห-2569-000900",
                    null,
                    null,
                    0m,
                    null,
                    null,
                    null);
                sheet.Cells[3, 1].Value = null;
                sheet.Cells[3, 2].Value = null;

                sheet.Cells[4, 1].Value = "Total";
                sheet.Cells[4, 7].Value = 1000m;
                sheet.Cells[4, 9].Value = 1000m;
                package.Save();
            }

            var reader = new ExcelReader();
            var records = reader.ReadRecords(tempFile);
            var rows = reader.ReadRows(tempFile);
            var preview = reader.ReadPreviewRows(tempFile);

            var record = Assert.Single(records);
            Assert.Single(rows);
            Assert.Equal(1, preview.TotalRows);
            Assert.Equal(3, record.RowNo);
            Assert.Equal("สห-2569-000900", record.ContractNo);
            Assert.Equal(string.Empty, record.MemberNo);
            Assert.Equal(string.Empty, record.MemberName);
            Assert.Equal(("Blank", (decimal?)null), (record.PreviousPrincipalParseStatus, record.PreviousPrincipalParsedValue));
            Assert.Equal(("Blank", (decimal?)null), (record.CurrentPrincipalParseStatus, record.CurrentPrincipalParsedValue));
            Assert.Equal(("Parsed", (decimal?)0m), (record.CurrentTotalParseStatus, record.CurrentTotalParsedValue));

            var validation = new ImportValidator().Validate(record);
            Assert.True(validation.IsSkipped);
            Assert.Equal("SkippedInactiveLoan", validation.Status);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadRecords_FourKnownDuplicateInactivePatterns_ReturnsActiveAndInactiveOccurrences()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"excelreader_duplicate_inactive_{Guid.NewGuid():N}.xlsx");
        var contractNos = new[]
        {
            "สห-2569-000592",
            "สห-2569-000595",
            "สห-2569-000597",
            "สห-2569-000600"
        };

        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Tests");
            using (var package = new ExcelPackage(new FileInfo(tempFile)))
            {
                var sheet = package.Workbook.Worksheets.Add("ป้อนประจำวัน");
                CreateMultiLevelHeaders(sheet);

                for (var index = 0; index < contractNos.Length; index++)
                {
                    AddMultiLevelDataRow(sheet, index + 3, contractNos[index], 100m, 20m, 120m, null, null, null);
                    AddMultiLevelDataRow(sheet, index + 7, contractNos[index], null, null, 0m, null, null, null);
                    sheet.Cells[index + 7, 1].Value = null;
                    sheet.Cells[index + 7, 2].Value = null;
                }

                package.Save();
            }

            var records = new ExcelReader().ReadRecords(tempFile);
            var validator = new ImportValidator();

            Assert.Equal(8, records.Count);
            foreach (var contractNo in contractNos)
            {
                var occurrences = records.Where(record => record.ContractNo == contractNo).ToList();
                Assert.Equal(2, occurrences.Count);
                Assert.Single(occurrences, record => validator.Validate(record).IsValid);
                Assert.Single(occurrences, record => validator.Validate(record).IsSkipped);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadRecords_WhenFileContainsOneValidRow_ReturnsImportLoanRecord()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"excelreader_{Guid.NewGuid():N}.xlsx");

        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Tests");

            using (var package = new ExcelPackage(new FileInfo(tempFile)))
            {
                var sheet = package.Workbook.Worksheets.Add("ป้อนประจำวัน");
                sheet.Cells[1, 1].Value = "MemberNo";
                sheet.Cells[1, 2].Value = "MemberName";
                sheet.Cells[1, 3].Value = "ContractNo";
                sheet.Cells[1, 4].Value = "ContractDate";
                sheet.Cells[1, 5].Value = "ExpireDate";
                sheet.Cells[1, 6].Value = "LoanAmount";
                sheet.Cells[1, 7].Value = "PrincipalBalance";
                sheet.Cells[1, 8].Value = "ProfitBalance";
                sheet.Cells[1, 9].Value = "TotalBalance";
                sheet.Cells[1, 10].Value = "OverdueDays";

                sheet.Cells[2, 1].Value = "M001";
                sheet.Cells[2, 2].Value = "John Doe";
                sheet.Cells[2, 3].Value = "C001";
                sheet.Cells[2, 4].Value = "2026-01-01";
                sheet.Cells[2, 5].Value = "2026-12-31";
                sheet.Cells[2, 6].Value = 1000.50m;
                sheet.Cells[2, 7].Value = 500.00m;
                sheet.Cells[2, 8].Value = 50.00m;
                sheet.Cells[2, 9].Value = 550.00m;
                sheet.Cells[2, 10].Value = 0;

                package.Save();
            }

            var reader = new ExcelReader();
            var records = reader.ReadRecords(tempFile);

            Assert.Single(records);
            var record = Assert.Single(records);
            Assert.Equal("M001", record.MemberNo);
            Assert.Equal("John Doe", record.MemberName);
            Assert.Equal("C001", record.ContractNo);
            Assert.Equal(1000.50m, record.LoanAmount);
            Assert.Equal(500.00m, record.PrincipalBalance);
            Assert.Equal(50.00m, record.ProfitBalance);
            Assert.Equal(550.00m, record.TotalBalance);
            Assert.Equal(0, record.OverdueDays);
            Assert.True(record.IsValid);
            Assert.Equal(string.Empty, record.ErrorMessage);
            Assert.Equal(new DateTime(2026, 1, 1), record.ContractDate);
            Assert.Equal(new DateTime(2026, 12, 31), record.ExpireDate);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadRecords_MultiLevelMergedHeaders_MapsIncreaseDuringYearColumnsJToL()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"excelreader_multilevel_{Guid.NewGuid():N}.xlsx");

        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Tests");
            using (var package = new ExcelPackage(new FileInfo(tempFile)))
            {
                var sheet = package.Workbook.Worksheets.Add("ป้อนประจำวัน");
                CreateMultiLevelHeaders(sheet);

                AddMultiLevelDataRow(sheet, 3, "สห-2569-000001", 0m, 0m, 0m, 0m, 0m, 0m);
                AddMultiLevelDataRow(sheet, 4, "สห-2569-000002", 123000m, 25830m, 148830m, 901m, 902m, 903m);
                AddMultiLevelDataRow(sheet, 5, "สห-2569-000003", -123000m, -25830m, -148830m, -901m, -902m, -903m);
                AddMultiLevelDataRow(sheet, 6, "สห-2569-000004", null, null, null, 100000m, 25000m, 125000m);
                AddMultiLevelDataRow(sheet, 7, "สห-2569-000005", "invalid-loan", "invalid-profit", "invalid-total", 901m, 902m, 903m);
                AddMultiLevelDataRow(sheet, 8, "สห-2569-000006", "invalid-current-principal", "invalid-current-profit", "invalid-current-total", "invalid-previous-principal", "invalid-previous-profit", "invalid-previous-total");
                AddMultiLevelDataRow(sheet, 4460, "สห-2569-000600", 123000m, 25830m, 148830m, null, null, null);
                package.Save();
            }

            var reader = new ExcelReader();
            var (rows, headers) = reader.ReadRowsWithHeaders(tempFile);
            var records = reader.ReadRecords(tempFile);
            var validator = new ImportValidator();

            Assert.Contains("เพิ่มระหว่างปี2569", headers[9].Replace(" ", string.Empty));
            Assert.Contains("ลูกหนี้", headers[9]);
            Assert.Contains("ผลตอบแทนฯ", headers[10]);
            Assert.Contains("รวม", headers[11]);
            Assert.Equal(7, rows.Count);

            AssertNumericState(records.Single(record => record.RowNo == 3), "Parsed", 0m, "Parsed", 0m, "Parsed", 0m);
            AssertNumericState(records.Single(record => record.RowNo == 4), "Parsed", 123000m, "Parsed", 25830m, "Parsed", 148830m);

            var negativeRecord = records.Single(record => record.RowNo == 5);
            AssertNumericState(negativeRecord, "Parsed", -123000m, "Parsed", -25830m, "Parsed", -148830m);
            var negativeErrors = validator.Validate(negativeRecord).Details.Where(detail => detail.ErrorType == "Negative").ToList();
            Assert.Equal(6, negativeErrors.Count);

            AssertNumericState(records.Single(record => record.RowNo == 6), "Blank", null, "Blank", null, "Blank", null);
            AssertNumericState(records.Single(record => record.RowNo == 7), "InvalidNumeric", null, "InvalidNumeric", null, "InvalidNumeric", null);

            AssertSixColumnState(
                records.Single(record => record.RowNo == 3),
                ("Parsed", 0m), ("Parsed", 0m), ("Parsed", 0m),
                ("Parsed", 0m), ("Parsed", 0m), ("Parsed", 0m));

            AssertSixColumnState(
                records.Single(record => record.RowNo == 4),
                ("Parsed", 901m), ("Parsed", 902m), ("Parsed", 903m),
                ("Parsed", 123000m), ("Parsed", 25830m), ("Parsed", 148830m));

            AssertSixColumnState(
                records.Single(record => record.RowNo == 5),
                ("Parsed", -901m), ("Parsed", -902m), ("Parsed", -903m),
                ("Parsed", -123000m), ("Parsed", -25830m), ("Parsed", -148830m));

            AssertSixColumnState(
                records.Single(record => record.RowNo == 6),
                ("Parsed", 100000m), ("Parsed", 25000m), ("Parsed", 125000m),
                ("Blank", null), ("Blank", null), ("Blank", null));

            var previousYearRecord = records.Single(record => record.RowNo == 6);
            Assert.Equal("100000", previousYearRecord.PreviousPrincipalRaw);
            Assert.Equal("25000", previousYearRecord.PreviousProfitRaw);
            Assert.Equal("125000", previousYearRecord.PreviousTotalRaw);
            Assert.Equal(string.Empty, previousYearRecord.CurrentPrincipalRaw);
            Assert.Equal(string.Empty, previousYearRecord.CurrentProfitRaw);
            Assert.Equal(string.Empty, previousYearRecord.CurrentTotalRaw);

            var invalidRecord = records.Single(record => record.RowNo == 8);
            AssertSixColumnState(
                invalidRecord,
                ("InvalidNumeric", null), ("InvalidNumeric", null), ("InvalidNumeric", null),
                ("InvalidNumeric", null), ("InvalidNumeric", null), ("InvalidNumeric", null));
            Assert.Equal("invalid-previous-principal", invalidRecord.PreviousPrincipalRaw);
            Assert.Equal("invalid-previous-profit", invalidRecord.PreviousProfitRaw);
            Assert.Equal("invalid-previous-total", invalidRecord.PreviousTotalRaw);
            Assert.Equal("invalid-current-principal", invalidRecord.CurrentPrincipalRaw);
            Assert.Equal("invalid-current-profit", invalidRecord.CurrentProfitRaw);
            Assert.Equal("invalid-current-total", invalidRecord.CurrentTotalRaw);

            var regressionRecord = records.Single(record => record.RowNo == 4460);
            Assert.Equal("สห-2569-000600", regressionRecord.ContractNo);
            AssertNumericState(regressionRecord, "Parsed", 123000m, "Parsed", 25830m, "Parsed", 148830m);
            AssertSixColumnState(
                regressionRecord,
                ("Blank", null), ("Blank", null), ("Blank", null),
                ("Parsed", 123000m), ("Parsed", 25830m), ("Parsed", 148830m));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private static void AddDataRow(ExcelWorksheet sheet, int row, string contractNo, object? profitBalance)
    {
        var values = new object?[] { $"M{row:000}", "Jane Doe", contractNo, "2026-01-01", "2026-12-31", 1000m, 500m, profitBalance, 500m, 0 };
        for (var column = 0; column < values.Length; column++)
            sheet.Cells[row, column + 1].Value = values[column];
    }

    private static void CreateMultiLevelHeaders(ExcelWorksheet sheet)
    {
        var fixedHeaders = new[] { "MemberNo", "MemberName", "ContractNo", "ContractDate", "ExpireDate" };
        for (var column = 0; column < fixedHeaders.Length; column++)
            sheet.Cells[1, column + 1].Value = fixedHeaders[column];

        sheet.Cells[1, 6].Value = "OverdueDays";
        sheet.Cells[1, 7].Value = "ยอดยกมา";
        sheet.Cells[1, 10].Value = "เพิ่มระหว่างปี 2569";
        sheet.Cells[1, 7, 1, 9].Merge = true;
        sheet.Cells[1, 10, 1, 12].Merge = true;

        var balanceHeaders = new[] { "ลูกหนี้", "ผลตอบแทนฯ", "รวม" };
        for (var column = 0; column < balanceHeaders.Length; column++)
        {
            sheet.Cells[2, column + 7].Value = balanceHeaders[column];
            sheet.Cells[2, column + 10].Value = balanceHeaders[column];
        }
    }

    private static void AddMultiLevelDataRow(ExcelWorksheet sheet, int row, string contractNo, object? loanAmount, object? profitBalance, object? totalBalance, object? openingLoanAmount, object? openingProfitBalance, object? openingTotalBalance)
    {
        var values = new object?[]
        {
            $"M{row:0000}", "Jane Doe", contractNo, "2026-01-01", "2026-12-31",
            0, openingLoanAmount, openingProfitBalance, openingTotalBalance,
            loanAmount, profitBalance, totalBalance
        };

        for (var column = 0; column < values.Length; column++)
            sheet.Cells[row, column + 1].Value = values[column];
    }

    private static void AssertNumericState(ImportLoanRecord record, string loanStatus, decimal? loanValue, string profitStatus, decimal? profitValue, string totalStatus, decimal? totalValue)
    {
        Assert.Equal(loanStatus, record.LoanAmountParseStatus);
        Assert.Equal(loanValue, record.LoanAmountParsedValue);
        Assert.Equal(profitStatus, record.ProfitBalanceParseStatus);
        Assert.Equal(profitValue, record.ProfitBalanceParsedValue);
        Assert.Equal(totalStatus, record.TotalBalanceParseStatus);
        Assert.Equal(totalValue, record.TotalBalanceParsedValue);
    }

    private static void AssertSixColumnState(
        ImportLoanRecord record,
        (string Status, decimal? Value) previousPrincipal,
        (string Status, decimal? Value) previousProfit,
        (string Status, decimal? Value) previousTotal,
        (string Status, decimal? Value) currentPrincipal,
        (string Status, decimal? Value) currentProfit,
        (string Status, decimal? Value) currentTotal)
    {
        Assert.Equal(previousPrincipal, (record.PreviousPrincipalParseStatus, record.PreviousPrincipalParsedValue));
        Assert.Equal(previousProfit, (record.PreviousProfitParseStatus, record.PreviousProfitParsedValue));
        Assert.Equal(previousTotal, (record.PreviousTotalParseStatus, record.PreviousTotalParsedValue));
        Assert.Equal(currentPrincipal, (record.CurrentPrincipalParseStatus, record.CurrentPrincipalParsedValue));
        Assert.Equal(currentProfit, (record.CurrentProfitParseStatus, record.CurrentProfitParsedValue));
        Assert.Equal(currentTotal, (record.CurrentTotalParseStatus, record.CurrentTotalParsedValue));
    }
}
