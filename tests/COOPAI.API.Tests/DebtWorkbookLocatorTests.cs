using COOPAI.API.Services.DebtSegmentation;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OfficeOpenXml;

namespace COOPAI.API.Tests;

public sealed class DebtWorkbookLocatorTests
{
    [Fact]
    public void Locate_SelectsNewestOperationalSchemaAndDoesNotDependOnFilename()
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI tests");
        var directory = Path.Combine(Path.GetTempPath(), $"debt-locator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var validPath = Path.Combine(directory, "cooperative-report-arbitrary-name.xlsx");
            using (var package = new ExcelPackage(new FileInfo(validPath)))
            {
                var sheet = package.Workbook.Worksheets.Add(OperationalDebtWorkbookReader.CurrentWorksheetName);
                sheet.Cells[2, 1].Value = "รายละเอียดรับชำระหนี้ ประจำปี 2569";
                sheet.Cells[3, 107].Value = "รหัสกลุ่ม";
                sheet.Cells[5, 109].Value = "31 กรกฎาคม";
                package.Save();
            }
            File.SetLastWriteTimeUtc(validPath, DateTime.UtcNow.AddMinutes(-2));

            var newerInvalidPath = Path.Combine(directory, "Loan-latest.xlsx");
            using (var package = new ExcelPackage(new FileInfo(newerInvalidPath)))
            {
                package.Workbook.Worksheets.Add("unrelated").Cells[1, 1].Value = "not operational";
                package.Save();
            }
            File.SetLastWriteTimeUtc(newerInvalidPath, DateTime.UtcNow.AddMinutes(-1));

            var options = Options.Create(new DebtSegmentationPreviewOptions { WorkbookPath = directory });
            var locator = new DebtWorkbookLocator(
                options, new TestHostEnvironment(directory), new OperationalDebtWorkbookReader());

            var result = locator.Locate();

            Assert.True(result.Found);
            Assert.Equal(Path.GetFullPath(validPath), result.Path);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "COOPAI.API.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
