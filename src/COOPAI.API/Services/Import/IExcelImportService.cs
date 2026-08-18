using COOPAI.API.Models.Import;

namespace COOPAI.API.Services.Import;

public interface IExcelImportService
{
    ImportResult Preview(string filePath);

    Task<ImportResult> ValidateOnlyAsync(string filePath);

    Task<ImportResult> ImportAsync(string filePath, ImportExecutionOptions? options = null);
}
