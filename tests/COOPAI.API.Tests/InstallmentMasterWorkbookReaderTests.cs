using COOPAI.API.Services.DebtSegmentation;
using OfficeOpenXml;

namespace COOPAI.API.Tests;

public sealed class InstallmentMasterWorkbookReaderTests
{
    [Fact]
    public void ReadsAhAsAuthoritativeContractualObligation_InHeaderedMode()
    {
        var path = CreateWorkbook([("C1", 12m, 100m)]);
        try
        {
            var result = new InstallmentMasterWorkbookReader().Read(path, "2534-2569");

            Assert.Equal(InstallmentMasterSchemaModes.Headered, result.SchemaMode);
            Assert.Equal(1_200m, result.Contracts.Single().ContractualObligation);
            Assert.True(result.Contracts.Single().IsUsable);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidAhFailsClosedAtRowLevel()
    {
        var path = CreateWorkbook([("C1", 12m, 100m)]);
        try
        {
            SetCell(path, 2, 34, 0m);

            var result = new InstallmentMasterWorkbookReader().Read(path, "2534-2569");
            var contract = Assert.Single(result.Contracts);

            Assert.False(contract.IsUsable);
            Assert.Null(contract.ContractualObligation);
            Assert.Equal(InstallmentValidationStatuses.InvalidContractualObligation, contract.ValidationStatus);
            Assert.Contains(contract.ValidationWarnings,
                warning => warning.Contains("AH/LCONT_AMOUNT_SAL", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadsAeAsAuthoritativeDownPayment_AndDoesNotTurnMissingIntoZero()
    {
        var path = CreateWorkbook([("SJ1", 12m, 100m), ("SJ2", 12m, 100m)]);
        try
        {
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var sheet = package.Workbook.Worksheets["2534-2569"];
                sheet.Cells[2, 31].Value = 750m;
                sheet.Cells[3, 31].Value = null;
                package.Save();
            }

            var result = new InstallmentMasterWorkbookReader().Read(path, "2534-2569");

            Assert.Equal(InstallmentMasterSchemaModes.Headered, result.SchemaMode);
            Assert.Equal(750m, result.Contracts.Single(x => x.ContractNumber == "SJ1").DownPayment);
            Assert.Equal(DownPaymentDataStatuses.Available,
                result.Contracts.Single(x => x.ContractNumber == "SJ1").DownPaymentDataStatus);
            Assert.Null(result.Contracts.Single(x => x.ContractNumber == "SJ2").DownPayment);
            Assert.Equal(DownPaymentDataStatuses.Missing,
                result.Contracts.Single(x => x.ContractNumber == "SJ2").DownPaymentDataStatus);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadsOwnerConfirmedColumnsAndClassifiesDuplicateAndInvalidRows()
    {
        var path = CreateWorkbook([
            ("C1", 12m, 100m),
            ("C1", 12m, 100m),
            ("C2", 12m, 100m),
            ("C2", 13m, 100m),
            ("C3", 12m, 100m),
            ("C3", 12m, 101m),
            ("C4", 12m, 100m),
            ("C4", 13m, 101m),
            ("C5", null, 100m),
            ("C6", 12m, 0m)
        ]);
        try
        {
            var result = new InstallmentMasterWorkbookReader().Read(path, "2534-2569");

            Assert.Equal(10, result.SourceRowCount);
            Assert.Equal(6, result.ContractCount);
            Assert.Equal(1, result.ValidContractCount);
            Assert.Equal(5, result.InvalidContractCount);
            Assert.Equal(4, result.DuplicateContractNumberGroups);
            Assert.Equal(1, result.InvalidTotalInstallmentRows);
            Assert.Equal(1, result.InvalidMonthlyInstallmentRows);
            Assert.Equal(InstallmentDuplicateStatuses.Identical,
                result.Contracts.Single(x => x.ContractNumber == "C1").DuplicateStatus);
            Assert.Equal(InstallmentDuplicateStatuses.ConflictingTotalInstallments,
                result.Contracts.Single(x => x.ContractNumber == "C2").DuplicateStatus);
            Assert.Equal(InstallmentDuplicateStatuses.ConflictingMonthlyInstallment,
                result.Contracts.Single(x => x.ContractNumber == "C3").DuplicateStatus);
            Assert.Equal(InstallmentDuplicateStatuses.ConflictingBoth,
                result.Contracts.Single(x => x.ContractNumber == "C4").DuplicateStatus);
            Assert.All(result.Contracts, contract =>
            {
                Assert.Equal(0m, contract.DownPayment);
                Assert.Equal(DownPaymentDataStatuses.Available, contract.DownPaymentDataStatus);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnsafeSchema_IsRejectedAtWorkbookLevel()
    {
        var path = CreateWorkbook([("C1", 12m, 100m)]);
        try
        {
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                package.Workbook.Worksheets["2534-2569"].Cells[1, 38].Value = "WRONG";
                package.Save();
            }

            var exception = Assert.Throws<InstallmentMasterSchemaValidationException>(() =>
                new InstallmentMasterWorkbookReader().Read(path, "2534-2569"));
            Assert.Contains("AL1", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ValidHeaderlessPositionalWorkbook_ImportsWithoutDiscardingRowOne()
    {
        var path = CreateHeaderlessWorkbook();
        try
        {
            var result = new InstallmentMasterWorkbookReader().Read(path, "รายละเอียดลูกหนี้ 34-69");

            Assert.Equal(InstallmentMasterSchemaModes.HeaderlessPositional, result.SchemaMode);
            Assert.Equal(25, result.SourceRowCount);
            Assert.Equal(25, result.ContractCount);
            Assert.Contains(result.Contracts, item =>
                item.ContractNumber == "สจ-2569-000001" && item.SourceRowNumber == 1 &&
                item.ContractualObligation == 100_000m);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HeaderlessShiftedRequiredColumns_FailsClosed()
    {
        var path = CreateHeaderlessWorkbook();
        try
        {
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var sheet = package.Workbook.Worksheets["รายละเอียดลูกหนี้ 34-69"];
                for (var row = 1; row <= 25; row++)
                {
                    var contract = sheet.Cells[row, 1].Value;
                    var downPayment = sheet.Cells[row, 31].Value;
                    var total = sheet.Cells[row, 38].Value;
                    var monthly = sheet.Cells[row, 39].Value;
                    sheet.Cells[row, 1].Value = null;
                    sheet.Cells[row, 2].Value = contract;
                    sheet.Cells[row, 31].Value = null;
                    sheet.Cells[row, 32].Value = downPayment;
                    sheet.Cells[row, 38].Value = null;
                    sheet.Cells[row, 39].Value = total;
                    sheet.Cells[row, 40].Value = monthly;
                }
                package.Save();
            }

            var exception = Assert.Throws<InstallmentMasterSchemaValidationException>(() =>
                new InstallmentMasterWorkbookReader().Read(path, "รายละเอียดลูกหนี้ 34-69"));
            Assert.Contains("HEADERLESS_POSITIONAL", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HeaderlessInvalidContractNumberStructure_FailsClosed()
    {
        var path = CreateHeaderlessWorkbook();
        try
        {
            SetCell(path, 1, 1, "NOT-A-CONTRACT");

            var exception = Assert.Throws<InstallmentMasterSchemaValidationException>(() =>
                new InstallmentMasterWorkbookReader().Read(path, "รายละเอียดลูกหนี้ 34-69"));
            Assert.Contains("column A", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(34, "column AH")]
    [InlineData(38, "column AL")]
    [InlineData(39, "column AM")]
    public void HeaderlessInvalidInstallmentColumnStructure_FailsClosed(int column, string expectedMessage)
    {
        var path = CreateHeaderlessWorkbook();
        try
        {
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var sheet = package.Workbook.Worksheets["รายละเอียดลูกหนี้ 34-69"];
                for (var row = 1; row <= 25; row++)
                    sheet.Cells[row, column].Value = "INVALID";
                package.Save();
            }

            var exception = Assert.Throws<InstallmentMasterSchemaValidationException>(() =>
                new InstallmentMasterWorkbookReader().Read(path, "รายละเอียดลูกหนี้ 34-69"));
            Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HeaderlessMalformedAe_RemainsAVisibleRowLevelValidationIssue()
    {
        var path = CreateHeaderlessWorkbook();
        try
        {
            SetCell(path, 1, 31, "INVALID-AE");

            var result = new InstallmentMasterWorkbookReader().Read(path, "รายละเอียดลูกหนี้ 34-69");
            var contract = result.Contracts.Single(item => item.ContractNumber == "สจ-2569-000001");

            Assert.Equal(InstallmentMasterSchemaModes.HeaderlessPositional, result.SchemaMode);
            Assert.Null(contract.DownPayment);
            Assert.Equal(DownPaymentDataStatuses.Invalid, contract.DownPaymentDataStatus);
            Assert.Contains(contract.ValidationWarnings,
                warning => warning.Contains("AE/LREC_SAL_ADVANCE is Invalid", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HeaderlessDuplicateConflictRules_RemainUnchanged()
    {
        var path = CreateHeaderlessWorkbook();
        try
        {
            using (var package = new ExcelPackage(new FileInfo(path)))
            {
                var sheet = package.Workbook.Worksheets["รายละเอียดลูกหนี้ 34-69"];
                sheet.Cells[2, 1].Value = sheet.Cells[1, 1].Value;
                sheet.Cells[2, 31].Value = 500m;
                sheet.Cells[2, 38].Value = 24m;
                sheet.Cells[2, 39].Value = 200m;
                package.Save();
            }

            var result = new InstallmentMasterWorkbookReader().Read(path, "รายละเอียดลูกหนี้ 34-69");
            var duplicate = result.Contracts.Single(item => item.ContractNumber == "สจ-2569-000001");

            Assert.Equal(1, result.DuplicateContractNumberGroups);
            Assert.Equal(InstallmentDuplicateStatuses.ConflictingBoth, duplicate.DuplicateStatus);
            Assert.Equal(InstallmentValidationStatuses.ConflictingDuplicate, duplicate.ValidationStatus);
            Assert.Equal(DownPaymentDataStatuses.ConflictingDuplicate, duplicate.DownPaymentDataStatus);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RealStagedWorkbook_UsesConfirmedSheetAndColumns_WhenAvailable()
    {
        var workspace = FindWorkspaceRoot();
        var path = workspace is null ? string.Empty : Path.Combine(
            workspace, "artifacts", "installment-master-inspection", "2534-2569.staged.xlsx");
        if (!File.Exists(path))
            return;

        var result = new InstallmentMasterWorkbookReader().Read(path, "2534-2569");

        Assert.Equal("2534-2569", result.WorksheetName);
        Assert.True(result.SourceRowCount > 4_500);
        Assert.True(result.ContractCount > 4_500);
        Assert.Equal(result.ContractCount, result.ValidContractCount + result.InvalidContractCount);
    }

    internal static string CreateWorkbook(IEnumerable<(string Contract, decimal? Al, decimal? Am)> rows)
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI tests");
        var path = Path.Combine(Path.GetTempPath(), $"installment-{Guid.NewGuid():N}.xlsx");
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("2534-2569");
        sheet.Cells[1, 1].Value = "LCONT_ID";
        sheet.Cells[1, 31].Value = "LREC_SAL_ADVANCE";
        sheet.Cells[1, 34].Value = "LCONT_AMOUNT_SAL";
        sheet.Cells[1, 38].Value = "LREG_MAX_INSTALL";
        sheet.Cells[1, 39].Value = "LREG_SALINT";
        var row = 2;
        foreach (var item in rows)
        {
            sheet.Cells[row, 1].Value = item.Contract;
            sheet.Cells[row, 31].Value = 0m;
            sheet.Cells[row, 34].Value = item.Al is > 0m && item.Am is > 0m
                ? item.Al.Value * item.Am.Value
                : 1_200m;
            sheet.Cells[row, 38].Value = item.Al;
            sheet.Cells[row, 39].Value = item.Am;
            row++;
        }
        package.SaveAs(new FileInfo(path));
        return path;
    }

    internal static string CreateHeaderlessWorkbook(int rowCount = 25)
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI tests");
        var path = Path.Combine(Path.GetTempPath(), $"installment-headerless-{Guid.NewGuid():N}.xlsx");
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("รายละเอียดลูกหนี้ 34-69");
        for (var row = 1; row <= rowCount; row++)
        {
            sheet.Cells[row, 1].Value = $"สจ-2569-{row:000000}";
            sheet.Cells[row, 2].Value = $"{row:00000}";
            sheet.Cells[row, 4].Value = 45000 + row;
            sheet.Cells[row, 5].Value = 45030 + row;
            sheet.Cells[row, 6].Value = 47000 + row;
            sheet.Cells[row, 31].Value = 0m;
            sheet.Cells[row, 34].Value = 100_000m;
            sheet.Cells[row, 35].Value = 20_000m;
            sheet.Cells[row, 36].Value = 0m;
            sheet.Cells[row, 37].Value = 80_000m;
            sheet.Cells[row, 38].Value = 36m;
            sheet.Cells[row, 39].Value = 3_000m;
        }
        package.SaveAs(new FileInfo(path));
        return path;
    }

    private static void SetCell(string path, int row, int column, object value)
    {
        using var package = new ExcelPackage(new FileInfo(path));
        package.Workbook.Worksheets.First().Cells[row, column].Value = value;
        package.Save();
    }

    private static string? FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "COOPAI.API")))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }
}
