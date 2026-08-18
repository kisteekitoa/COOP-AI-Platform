using System.Linq;
using COOPAI.API.Models.Import;
using COOPAI.API.Services.Import;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;

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
        var logs = new List<string>();

        var fileInfo = new System.IO.FileInfo(filePath);
        logs.Add($"Uploaded filename: {fileInfo.Name}");
        logs.Add($"Full temporary file path: {fileInfo.FullName}");
        logs.Add($"File exists: {fileInfo.Exists}");
        logs.Add(fileInfo.Exists ? $"File size: {fileInfo.Length}" : "File size: 0");

        if (!fileInfo.Exists)
        {
            return NotFound(new
            {
                success = false,
                message = $"File not found : {filePath}",
                logs
            });
        }

        using var package = new ExcelPackage(fileInfo);
        var worksheets = package.Workbook.Worksheets;

        logs.Add($"Workbook.Worksheets.Count: {worksheets.Count}");

        var worksheetDetails = new List<object>();
        for (var index = 0; index < worksheets.Count; index++)
        {
            var worksheet = worksheets[index];
            worksheetDetails.Add(new
            {
                Index = index,
                Name = worksheet.Name
            });
            logs.Add($"Worksheet {index}: {worksheet.Name}");
        }

        var targetWorksheet = worksheets
            .FirstOrDefault(x =>
                x.Name.Trim().Equals("ป้อนประจำวัน", StringComparison.OrdinalIgnoreCase));

        if (targetWorksheet == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Worksheet 'ป้อนประจำวัน' not found.",
                logs,
                worksheets = worksheetDetails
            });
        }

        var previewResult = _service.Preview(filePath);

        return Ok(new
        {
            success = previewResult.Success,
            totalRows = previewResult.TotalRows,
            headers = previewResult.Headers,
            previewRows = previewResult.PreviewRows,
            errors = previewResult.Errors,
            logs,
            worksheet = new
            {
                Name = targetWorksheet.Name,
                StartRow = targetWorksheet.Dimension?.Start.Row,
                StartColumn = targetWorksheet.Dimension?.Start.Column,
                EndRow = targetWorksheet.Dimension?.End.Row,
                EndColumn = targetWorksheet.Dimension?.End.Column
            }
        });
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

        var result = await _service.ImportAsync(tempFileName, new ImportExecutionOptions
        {
            ValidationFileHash = request.ValidationFileHash,
            ApprovedErrorSkips = request.SkipRows ?? new List<ApprovedErrorSkipSelection>()
        });

        return Ok(new
        {
            success = result.Success,
            fileHash = result.FileHash,
            totalRows = result.TotalRows,
            importedRows = result.ImportedRows,
            updatedRows = result.UpdatedRows,
            skippedRows = result.SkippedRows,
            userSkippedErrorRows = result.UserSkippedErrorRows,
            failedRows = result.FailedRows,
            batchId = result.BatchId,
            errors = result.Errors,
            errorDetails = result.ErrorDetails,
            skipDetails = result.SkipDetails,
            userSkippedErrorDetails = result.UserSkippedErrorDetails
        });
    }

    [HttpPost("validate")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Validate([FromForm] ImportFileRequest request)
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

        var tempFolder = Path.Combine(Path.GetTempPath(), "COOPAI-Validations");
        Directory.CreateDirectory(tempFolder);

        var tempFileName = Path.Combine(
            tempFolder,
            $"validate_{Guid.NewGuid()}{extension}");

        try
        {
            await using (var stream = System.IO.File.Create(tempFileName))
            {
                await file.CopyToAsync(stream);
            }

            var result = await _service.ValidateOnlyAsync(tempFileName);

            return Ok(new
            {
                success = result.Success,
                fileHash = result.FileHash,
                totalRows = result.TotalRows,
                validRows = result.ValidRows,
                updateCandidates = result.UpdateCandidates,
                existingContracts = result.ExistingContracts,
                missingContracts = result.MissingContracts,
                skippedRows = result.SkippedRows,
                userSkippedErrorRows = result.UserSkippedErrorRows,
                failedRows = result.FailedRows,
                errors = result.Errors,
                errorDetails = result.ErrorDetails,
                skipDetails = result.SkipDetails
            });
        }
        finally
        {
            if (System.IO.File.Exists(tempFileName))
                System.IO.File.Delete(tempFileName);
        }
    }
}
