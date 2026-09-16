using COOPAI.API.Services.DebtSegmentation;
using OfficeOpenXml;

namespace COOPAI.API.Tests;

public sealed class OperationalDebtWorkbookReaderTests
{
    [Fact]
    public void Read_CurrentOperationalSheet_MapsJuneJulyAndDcWithoutClassifyingByDc()
    {
        var path = Path.Combine(Path.GetTempPath(), $"debt-preview-{Guid.NewGuid():N}.xlsx");
        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("COOP-AI tests");
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var sheet = package.Workbook.Worksheets.Add(OperationalDebtWorkbookReader.CurrentWorksheetName);
                sheet.Cells[1, 1].Value = "สหกรณ์ตัวอย่าง (สาขาเมืองปัตตานี)";
                sheet.Cells[2, 1].Value = "รายละเอียดรับชำระหนี้ ประจำปี 2569";
                sheet.Cells[5, 109].Value = "31 กรกฎาคม";
                sheet.Cells[7, 2].Value = "M001";
                sheet.Cells[7, 3].Value = "สมาชิกตัวอย่าง";
                sheet.Cells[7, 4].Value = "สม-2569-000001";
                sheet.Cells[7, 5].Value = new DateTime(2026, 1, 1);
                sheet.Cells[7, 6].Value = new DateTime(2027, 1, 1);
                sheet.Cells[7, 7].Value = 100m;
                SetMonth(sheet, 7, 6, 20m, 80m, 8m, 88m);
                SetMonth(sheet, 7, 7, 0m, 80m, 8m, 88m);
                sheet.Cells[7, 57].Value = 0.01m; // BE, July principal payment evidence
                sheet.Cells[7, 60].Formula = "SUM(80)"; // BH, preserve formula provenance
                sheet.Cells[7, 107].Value = "G-01";
                package.Workbook.Calculate();
                package.Save();
            }

            var result = new OperationalDebtWorkbookReader().Read(path);
            var contract = Assert.Single(result.Contracts);

            Assert.Equal(OperationalDebtWorkbookReader.CurrentWorksheetName, result.WorksheetName);
            Assert.Equal(new DateOnly(2026, 7, 1), result.DataThroughPeriod);
            Assert.Equal("G-01", contract.GroupCode);
            Assert.Equal("เมืองปัตตานี", contract.Branch);
            Assert.Equal(20m, contract.MonthlyStates[new DateOnly(2026, 6, 1)].PaymentAmount);
            var july = contract.MonthlyStates[new DateOnly(2026, 7, 1)];
            Assert.True(july.HasActualPayment);
            Assert.NotNull(july.Provenance);
            Assert.Equal(0.01m, july.Provenance.PrincipalPaymentInput);
            Assert.Equal("BH7", july.Provenance.PrincipalOutstanding.CellAddress);
            Assert.Equal("SUM(80)", july.Provenance.PrincipalOutstanding.Formula);
            Assert.True(july.Provenance.PrincipalOutstanding.IsFormulaDerived);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Read_LegacyJuneWorkbook_ReportsJuneAndExplicitDiagnosticWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"debt-preview-legacy-{Guid.NewGuid():N}.xlsx");
        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("COOP-AI tests");
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var sheet = package.Workbook.Worksheets.Add(OperationalDebtWorkbookReader.LegacyWorksheetName);
                sheet.Cells[2, 1].Value = "ข้อมูลประจำปี 2569";
                sheet.Cells[5, 109].Value = "30 มิถุนายน";
                package.Save();
            }

            var reader = new OperationalDebtWorkbookReader();
            var strictFailure = Assert.Throws<InvalidOperationException>(() => reader.Read(path));
            Assert.Contains(OperationalDebtWorkbookReader.CurrentWorksheetName, strictFailure.Message, StringComparison.Ordinal);
            var result = reader.Read(path, allowLegacyDiagnostic: true);

            Assert.Equal(new DateOnly(2026, 6, 1), result.DataThroughPeriod);
            Assert.Contains(result.Warnings, x => x.Contains(OperationalDebtWorkbookReader.CurrentWorksheetName, StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void SetMonth(
        ExcelWorksheet sheet,
        int row,
        int month,
        decimal payment,
        decimal principal,
        decimal profit,
        decimal total)
    {
        var start = 14 + ((month - 1) * 7);
        sheet.Cells[row, start + 3].Value = payment;
        sheet.Cells[row, start + 4].Value = principal;
        sheet.Cells[row, start + 5].Value = profit;
        sheet.Cells[row, start + 6].Value = total;
    }
}
