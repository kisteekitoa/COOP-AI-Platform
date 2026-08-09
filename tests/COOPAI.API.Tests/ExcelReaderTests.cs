using System.Globalization;
using System.IO;
using COOPAI.API.Services.Import;
using OfficeOpenXml;
using Xunit;

namespace COOPAI.API.Tests;

public class ExcelReaderTests
{
    [Fact]
    public void ReadRecords_WhenFileContainsOneValidRow_ReturnsImportLoanRecord()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"excelreader_{Guid.NewGuid():N}.xlsx");

        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Import Tests");

            using (var package = new ExcelPackage(new FileInfo(tempFile)))
            {
                var sheet = package.Workbook.Worksheets.Add("Sheet1");
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
}
