namespace COOPAI.API.Services.Import;

public interface IExcelImportService
{
    ImportResult Preview(string filePath);

    Task<ImportResult> ImportAsync(string filePath);
}