// ==========================================================
// COOP-AI
// File        : ExcelImportService.cs
// Module      : Import Engine
// Version     : 0.3.000
// Description : Excel Preview Service
// ==========================================================

using System.Data;
using COOPAI.API.DTOs.Import;

namespace COOPAI.API.Services.Import;

public class ExcelImportService : IExcelImportService
{
    private readonly ExcelReader _reader;
    private readonly WorksheetScanner _scanner;

    public ExcelImportService()
    {
        _reader = new ExcelReader();
        _scanner = new WorksheetScanner();
    }

    public ImportResult Preview(string filePath)
    {
        var result = new ImportResult();

        try
        {
            //-------------------------------------------------
            // Read Excel
            //-------------------------------------------------

            DataTable table = _reader.Read(filePath);

            //-------------------------------------------------
            // Scan Worksheet
            //-------------------------------------------------

            var rows = _scanner.Scan(table);

            result.Success = true;
            result.TotalRows = rows.Count;
            result.ImportedRows = 0;
            result.UpdatedRows = 0;
            result.FailedRows = 0;

            //-------------------------------------------------
            // Information
            //-------------------------------------------------

            result.Headers.Add($"Rows : {rows.Count}");
            result.Headers.Add($"Columns : {table.Columns.Count}");

            //-------------------------------------------------
            // Excel Header
            //-------------------------------------------------

            if (rows.Count >= 5)
            {
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    string h1 = rows[3].Count > c ? rows[3][c] : "";
                    string h2 = rows[4].Count > c ? rows[4][c] : "";

                    result.Headers.Add($"{h1} {h2}".Trim());
                }
            }

            //-------------------------------------------------
            // Data Start
            // Excel Row 7
            //-------------------------------------------------

            const int DATA_START_ROW = 6;

            for (int r = DATA_START_ROW; r < rows.Count; r++)
            {
                var row = rows[r];

                if (row.Count < 9)
                    continue;

                if (string.IsNullOrWhiteSpace(row[1]))
                    continue;

                var loan = new LoanImportRow
{
    MemberNo = GetString(row, 1),

    FullName = GetString(row, 2),

    ContractNo = GetString(row, 3),

    ContractDate = GetDate(row, 4),

    ExpireDate = GetDate(row, 5),

    PrincipalBalance = GetDecimal(row, 6),

    ProfitBalance = GetDecimal(row, 7),

    TotalBalance = GetDecimal(row, 8)
};

var preview = new Dictionary<string, string>
{
    ["MemberNo"] = loan.MemberNo,
    ["MemberName"] = loan.FullName,
    ["ContractNo"] = loan.ContractNo,
    ["LoanDate"] = loan.ContractDate?.ToString("dd/MM/yyyy") ?? "",
    ["ExpireDate"] = loan.ExpireDate?.ToString("dd/MM/yyyy") ?? "",
    ["Principal"] = loan.PrincipalBalance.ToString("N2"),
    ["Profit"] = loan.ProfitBalance.ToString("N2"),
    ["Total"] = loan.TotalBalance.ToString("N2")
};

                result.PreviewRows.Add(preview);

                result.ImportedRows++;

                if (result.PreviewRows.Count >= 20)
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    // ======================================================
    // Helper
    // ======================================================

    private static string GetString(List<string> row, int index)
    {
        if (index >= row.Count)
            return "";

        return row[index].Trim();
    }

    private static decimal GetDecimal(List<string> row, int index)
    {
        if (index >= row.Count)
            return 0;

        decimal.TryParse(
            row[index].Replace(",", ""),
            out decimal value);

        return value;
    }

    private static DateTime? GetDate(List<string> row, int index)
    {
        if (index >= row.Count)
            return null;

        if (DateTime.TryParse(row[index], out DateTime d))
            return d;

        return null;
    }
}