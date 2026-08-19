using COOPAI.API.Services.PortfolioSnapshots;
using OfficeOpenXml;

namespace COOPAI.API.Tests;

public sealed class ExcelPortfolioSnapshotSourceTests
{
    [Fact]
    public async Task ReadAsync_ExtractsOpeningRepaymentAndAuthoritativeOutstandingColumns()
    {
        var rows = new[]
        {
            SnapshotWorkbookRow.ContractPrevious("M001", "\u0E2A\u0E21-2569-000001", 100m, 20m, 120m, 10m, 2m, 12m, 90m, 18m, 108m),
            SnapshotWorkbookRow.ContractCurrent("M002", "\u0E2A\u0E08-2569-000002", 200m, 40m, 240m, 30m, 6m, 36m, 170m, 34m, 204m),
            SnapshotWorkbookRow.TemplatePlaceholder("M003", "\u0E2A\u0E2B-2569-000003")
        };
        var path = CreateWorkbook(rows);

        try
        {
            var result = await new ExcelPortfolioSnapshotSource().ReadAsync(path, new DateOnly(2026, 6, 30));

            Assert.Empty(result.Issues);
            Assert.Equal(3, result.Rows.Count);
            Assert.Equal(2, result.Rows.Count(x => x.SourceRowKind == COOPAI.API.Models.Portfolio.PortfolioSourceRowKind.Contract));
            Assert.Single(result.Rows.Where(x => x.SourceRowKind == COOPAI.API.Models.Portfolio.PortfolioSourceRowKind.TemplatePlaceholder));
            Assert.Equal(COOPAI.API.Models.Portfolio.PortfolioOpeningSide.Previous, result.Rows[0].OpeningSide);
            Assert.Equal(COOPAI.API.Models.Portfolio.PortfolioOpeningSide.Current, result.Rows[1].OpeningSide);
            Assert.Equal(new DateOnly(2026, 1, 1), result.Rows[0].ContractDate);
            Assert.Equal(new DateOnly(2026, 6, 30), result.Rows[0].ExpireDate);
            Assert.Equal(new PortfolioFinancialValues(100m, 20m, 120m), result.Rows[0].Opening);
            Assert.Equal(new PortfolioFinancialValues(10m, 2m, 12m), result.Rows[0].Repayment);
            Assert.Equal(new PortfolioFinancialValues(90m, 18m, 108m), result.Rows[0].Outstanding);
            Assert.Equal(new PortfolioFinancialValues(200m, 40m, 240m), result.Rows[1].Opening);
            Assert.Equal(new PortfolioFinancialValues(30m, 6m, 36m), result.Rows[1].Repayment);
            Assert.Equal(new PortfolioFinancialValues(170m, 34m, 204m), result.Rows[1].Outstanding);
            Assert.Equal(new PortfolioFinancialValues(300m, 60m, 360m), result.ReportSummary!.Opening);
            Assert.Equal(new PortfolioFinancialValues(40m, 8m, 48m), result.ReportSummary.Repayment);
            Assert.Equal(new PortfolioFinancialValues(260m, 52m, 312m), result.ReportSummary.Outstanding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_BothOpeningPrincipalSidesPopulated_IsBlocking()
    {
        var row = SnapshotWorkbookRow.ContractPrevious(
            "M001", "\u0E2A\u0E21-2569-000001", 100m, 20m, 120m, 0m, 0m, 0m, 100m, 20m, 120m);
        var path = CreateWorkbook([row], sheet => sheet.Cells[7, 10].Value = 100m);

        try
        {
            var result = await new ExcelPortfolioSnapshotSource().ReadAsync(path, new DateOnly(2026, 6, 30));

            Assert.Contains(result.Issues, x => x.Code == "BothOpeningSidesPopulated");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_FormulaWithoutCachedValue_IsBlocking()
    {
        var row = SnapshotWorkbookRow.ContractPrevious(
            "M001", "\u0E2A\u0E21-2569-000001", 100m, 20m, 120m, 0m, 0m, 0m, 100m, 20m, 120m);
        var path = CreateWorkbook([row], sheet => sheet.Cells[7, 104].Formula = "G7-CU7");

        try
        {
            var result = await new ExcelPortfolioSnapshotSource().ReadAsync(path, new DateOnly(2026, 6, 30));

            Assert.Contains(result.Issues, x => x.Code == "FormulaCacheUnavailable");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_SubCentValue_IsBlockingInsteadOfSilentlyRounded()
    {
        var row = SnapshotWorkbookRow.ContractPrevious(
            "M001", "\u0E2A\u0E21-2569-000001", 100m, 20m, 120m, 0m, 0m, 0m, 100m, 20m, 120m);
        var path = CreateWorkbook([row], sheet => sheet.Cells[7, 104].Value = 100.001m);

        try
        {
            var result = await new ExcelPortfolioSnapshotSource().ReadAsync(path, new DateOnly(2026, 6, 30));

            Assert.Contains(result.Issues, x => x.Code == "MoneyPrecisionExceeded");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_BuddhistCalendarText_IsConvertedExplicitlyToGregorianDates()
    {
        var row = SnapshotWorkbookRow.ContractPrevious(
            "M001", "\u0E2A\u0E21-2569-000001", 100m, 20m, 120m, 0m, 0m, 0m, 100m, 20m, 120m);
        var path = CreateWorkbook([row], sheet =>
        {
            sheet.Cells[7, 5].Value = "1/1/2569";
            sheet.Cells[7, 6].Value = "30/6/2569";
        });

        try
        {
            var result = await new ExcelPortfolioSnapshotSource().ReadAsync(path, new DateOnly(2026, 6, 30));

            Assert.Equal(new DateOnly(2026, 1, 1), result.Rows[0].ContractDate);
            Assert.Equal(new DateOnly(2026, 6, 30), result.Rows[0].ExpireDate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("  \u0E2A\u0E21\u20112569\u2011000001  ", "\u0E2A\u0E21-2569-000001", "\u0E2A\u0E21")]
    [InlineData("\u0E2A\u0E08-2569-1", "\u0E2A\u0E08-2569-1", "\u0E2A\u0E08")]
    [InlineData("\u0E2A\u0E22-2569-000001", "\u0E2A\u0E22-2569-000001", "\u0E2A\u0E22")]
    public void Normalize_ApprovedFormalContract_UsesCanonicalHyphen(
        string input,
        string expectedContractNo,
        string expectedPrefix)
    {
        var result = new SnapshotContractNoNormalizer().Normalize(input);

        Assert.True(result.IsValid);
        Assert.Equal(expectedContractNo, result.NormalizedContractNo);
        Assert.Equal(expectedPrefix, result.LoanTypePrefix);
    }

    [Theory]
    [InlineData("Person Name")]
    [InlineData("XX-2569-000001")]
    [InlineData("\u0E2A\u0E21-69-000001")]
    [InlineData("\u0E2A\u0E21-2569-A")]
    [InlineData("")]
    public void Normalize_MalformedOrUnsupportedContract_IsRejected(string input)
    {
        Assert.False(new SnapshotContractNoNormalizer().Normalize(input).IsValid);
    }

    private static string CreateWorkbook(
        IReadOnlyList<SnapshotWorkbookRow> rows,
        Action<ExcelWorksheet>? mutate = null)
    {
        ExcelPackage.License.SetNonCommercialPersonal("COOP-AI Snapshot Tests");
        var path = Path.Combine(Path.GetTempPath(), $"portfolio_snapshot_{Guid.NewGuid():N}.xlsx");
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("\u0E1B\u0E49\u0E2D\u0E19\u0E1B\u0E23\u0E30\u0E08\u0E33\u0E27\u0E31\u0E19");
        sheet.Cells[1, 1].Value = "MemberNo";
        sheet.Cells[1, 4].Value = "ContractNo";

        for (var index = 0; index < rows.Count; index++)
        {
            var source = rows[index];
            var row = 7 + index;
            sheet.Cells[row, 1].Value = source.MemberNo;
            sheet.Cells[row, 4].Value = source.ContractNo;
            if (source.SourceRowKind == COOPAI.API.Models.Portfolio.PortfolioSourceRowKind.Contract)
            {
                sheet.Cells[row, 5].Value = source.ContractDate.ToDateTime(TimeOnly.MinValue);
                sheet.Cells[row, 6].Value = source.ExpireDate.ToDateTime(TimeOnly.MinValue);
            }

            if (source.OpeningSide == COOPAI.API.Models.Portfolio.PortfolioOpeningSide.Previous)
            {
                WriteGroup(sheet, row, 7, source.Opening);
            }
            else if (source.OpeningSide == COOPAI.API.Models.Portfolio.PortfolioOpeningSide.Current)
            {
                WriteGroup(sheet, row, 10, source.Opening);
            }

            if (source.SourceRowKind == COOPAI.API.Models.Portfolio.PortfolioSourceRowKind.Contract)
            {
                WriteGroup(sheet, row, 99, source.Repayment);
                WriteGroup(sheet, row, 104, source.Outstanding);
            }
        }

        var summaryRow = 8 + rows.Count;
        var previousRows = rows.Where(x => x.OpeningSide == COOPAI.API.Models.Portfolio.PortfolioOpeningSide.Previous).ToArray();
        var currentRows = rows.Where(x => x.OpeningSide == COOPAI.API.Models.Portfolio.PortfolioOpeningSide.Current).ToArray();
        WriteGroup(sheet, summaryRow, 7, Sum(previousRows, x => x.Opening));
        WriteGroup(sheet, summaryRow, 10, Sum(currentRows, x => x.Opening));
        WriteGroup(sheet, summaryRow, 99, Sum(rows.Where(x => x.SourceRowKind == COOPAI.API.Models.Portfolio.PortfolioSourceRowKind.Contract), x => x.Repayment));
        WriteGroup(sheet, summaryRow, 104, Sum(rows.Where(x => x.SourceRowKind == COOPAI.API.Models.Portfolio.PortfolioSourceRowKind.Contract), x => x.Outstanding));
        mutate?.Invoke(sheet);
        package.SaveAs(new FileInfo(path));
        return path;
    }

    private static void WriteGroup(ExcelWorksheet sheet, int row, int firstColumn, PortfolioFinancialValues values)
    {
        sheet.Cells[row, firstColumn].Value = values.Principal;
        sheet.Cells[row, firstColumn + 1].Value = values.Profit;
        sheet.Cells[row, firstColumn + 2].Value = values.Total;
    }

    private static PortfolioFinancialValues Sum(
        IEnumerable<SnapshotWorkbookRow> rows,
        Func<SnapshotWorkbookRow, PortfolioFinancialValues> selector) => new(
        rows.Sum(x => selector(x).Principal),
        rows.Sum(x => selector(x).Profit),
        rows.Sum(x => selector(x).Total));

    private sealed record SnapshotWorkbookRow(
        string MemberNo,
        string ContractNo,
        COOPAI.API.Models.Portfolio.PortfolioSourceRowKind SourceRowKind,
        COOPAI.API.Models.Portfolio.PortfolioOpeningSide OpeningSide,
        DateOnly ContractDate,
        DateOnly ExpireDate,
        PortfolioFinancialValues Opening,
        PortfolioFinancialValues Repayment,
        PortfolioFinancialValues Outstanding)
    {
        public static SnapshotWorkbookRow ContractPrevious(
            string memberNo, string contractNo,
            decimal openingPrincipal, decimal openingProfit, decimal openingTotal,
            decimal repaymentPrincipal, decimal repaymentProfit, decimal repaymentTotal,
            decimal outstandingPrincipal, decimal outstandingProfit, decimal outstandingTotal) =>
            new(
                memberNo, contractNo,
                COOPAI.API.Models.Portfolio.PortfolioSourceRowKind.Contract,
                COOPAI.API.Models.Portfolio.PortfolioOpeningSide.Previous,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 6, 30),
                new PortfolioFinancialValues(openingPrincipal, openingProfit, openingTotal),
                new PortfolioFinancialValues(repaymentPrincipal, repaymentProfit, repaymentTotal),
                new PortfolioFinancialValues(outstandingPrincipal, outstandingProfit, outstandingTotal));

        public static SnapshotWorkbookRow ContractCurrent(
            string memberNo, string contractNo,
            decimal openingPrincipal, decimal openingProfit, decimal openingTotal,
            decimal repaymentPrincipal, decimal repaymentProfit, decimal repaymentTotal,
            decimal outstandingPrincipal, decimal outstandingProfit, decimal outstandingTotal) =>
            ContractPrevious(
                memberNo, contractNo,
                openingPrincipal, openingProfit, openingTotal,
                repaymentPrincipal, repaymentProfit, repaymentTotal,
                outstandingPrincipal, outstandingProfit, outstandingTotal) with
            {
                OpeningSide = COOPAI.API.Models.Portfolio.PortfolioOpeningSide.Current
            };

        public static SnapshotWorkbookRow TemplatePlaceholder(string memberNo, string contractNo) =>
            new(
                memberNo, contractNo,
                COOPAI.API.Models.Portfolio.PortfolioSourceRowKind.TemplatePlaceholder,
                COOPAI.API.Models.Portfolio.PortfolioOpeningSide.Unknown,
                default,
                default,
                new PortfolioFinancialValues(0m, 0m, 0m),
                new PortfolioFinancialValues(0m, 0m, 0m),
                new PortfolioFinancialValues(0m, 0m, 0m));
    }
}
