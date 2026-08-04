using System.Data;
using COOPAI.API.DTOs.Import;

namespace COOPAI.API.Services.Import;

public class ExcelImportService : IExcelImportService
{
    private readonly ExcelReader _reader;

    public ExcelImportService()
    {
        _reader = new ExcelReader();
    }

    public ImportResult Preview(string filePath)
    {
        var result = new ImportResult();

        try
        {
            DataTable table = _reader.Read(filePath);

            result.Success = true;
            result.TotalRows = table.Rows.Count;

            // ข้าม Header
            for (int row = 1; row < table.Rows.Count; row++)
            {
                if (table.Rows[row].ItemArray.All(x =>
                        string.IsNullOrWhiteSpace(x?.ToString())))
                {
                    continue;
                }

                result.ImportedRows++;
            }

            result.FailedRows = 0;
            result.UpdatedRows = 0;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }
}