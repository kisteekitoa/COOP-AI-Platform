namespace COOPAI.API.Services.Import;

public interface IExcelImportService
{
    ImportResult Preview(string filePath);
}