using COOPAI.API.Models.Import;
using COOPAI.API.Services.Import;
using Microsoft.AspNetCore.Mvc;

namespace COOPAI.API.Controllers;

[ApiController]
[Route("api/import")]
public class ImportController : ControllerBase
{
    private readonly IExcelImportService _service;

    public ImportController(IExcelImportService service)
    {
        _service = service;
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new
        {
            success = true,
            message = "COOP-AI Import Engine Ready"
        });
    }

    [HttpGet("preview")]
    public IActionResult Preview()
    {
        var filePath = @"D:\Import\Loan.xlsx";

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(new
            {
                success = false,
                message = $"File not found : {filePath}"
            });
        }

        var result = _service.Preview(filePath);

        return Ok(result);
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Import([FromForm] ImportFileRequest request)
    {
        if (request.File == null)
        {
            return BadRequest(new
            {
                success = false,
                message = "File is required."
            });
        }

        var file = request.File;

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();

        if (extension != ".xlsx" && extension != ".xls")
        {
            return BadRequest(new
            {
                success = false,
                message = "Only .xlsx and .xls files are supported."
            });
        }

        var tempFolder = Path.Combine(Path.GetTempPath(), "COOPAI-Imports");
        Directory.CreateDirectory(tempFolder);

        var tempFileName = Path.Combine(
            tempFolder,
            $"import_{Guid.NewGuid()}{extension}");

        await using (var stream = System.IO.File.Create(tempFileName))
        {
            await file.CopyToAsync(stream);
        }

        var result = await _service.ImportAsync(tempFileName);

        return Ok(new
        {
            success = result.Success,
            importedRows = result.ImportedRows,
            updatedRows = result.UpdatedRows,
            failedRows = result.FailedRows,
            batchId = result.BatchId,
            errors = result.Errors
        });
    }
}