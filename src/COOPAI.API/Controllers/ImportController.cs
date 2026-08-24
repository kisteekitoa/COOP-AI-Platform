using COOPAI.API.Models.Import;
using COOPAI.API.Security;
using COOPAI.API.Services.Import;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Controllers;

[ApiController]
[Route("api/import")]
public sealed class ImportController(
    IExcelImportService service,
    IImportTempFileStore tempFileStore,
    IOptions<ImportUploadOptions> uploadOptions,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("ping")]
    [Authorize(Policy = CoopPolicies.ImportRead)]
    public IActionResult Ping() => Ok(new
    {
        success = true,
        message = "COOP-AI Import Engine Ready"
    });

    [HttpGet("preview")]
    [Authorize(Policy = CoopPolicies.ImportRead)]
    public IActionResult Preview() => Ok(new
    {
        success = true,
        message = "Upload a workbook to the validate endpoint for a safe preview.",
        allowedExtensions = uploadOptions.Value.AllowedExtensions,
        maxUploadBytes = uploadOptions.Value.MaxUploadBytes
    });

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = CoopPolicies.ImportExecute)]
    [EnableRateLimiting("Upload")]
    public async Task<IActionResult> Import(
        [FromForm] ImportFileRequest request,
        CancellationToken cancellationToken)
    {
        if (await AntiforgeryError.IsInvalidAsync(HttpContext, antiforgery))
            return BadRequest(UploadError("InvalidAntiforgeryToken", "The request is missing or has an invalid antiforgery token."));
        if (request.File is null)
            return BadRequest(UploadError("FileRequired", "A non-empty Excel workbook is required."));

        try
        {
            await using var upload = await tempFileStore.SaveAsync(request.File, cancellationToken);
            var result = await service.ImportAsync(upload.FilePath, new ImportExecutionOptions
            {
                ValidationFileHash = request.ValidationFileHash,
                ApprovedErrorSkips = request.SkipRows ?? []
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
        catch (ImportUploadException exception)
        {
            return UploadFailure(exception);
        }
    }

    [HttpPost("validate")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = CoopPolicies.ImportRead)]
    [EnableRateLimiting("Upload")]
    public async Task<IActionResult> Validate(
        [FromForm] ImportFileRequest request,
        CancellationToken cancellationToken)
    {
        if (await AntiforgeryError.IsInvalidAsync(HttpContext, antiforgery))
            return BadRequest(UploadError("InvalidAntiforgeryToken", "The request is missing or has an invalid antiforgery token."));
        if (request.File is null)
            return BadRequest(UploadError("FileRequired", "A non-empty Excel workbook is required."));

        try
        {
            await using var upload = await tempFileStore.SaveAsync(request.File, cancellationToken);
            var result = await service.ValidateOnlyAsync(upload.FilePath);

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
        catch (ImportUploadException exception)
        {
            return UploadFailure(exception);
        }
    }

    private IActionResult UploadFailure(ImportUploadException exception) =>
        exception.Code == "UploadTooLarge"
            ? StatusCode(StatusCodes.Status413PayloadTooLarge, UploadError(exception.Code, exception.Message))
            : BadRequest(UploadError(exception.Code, exception.Message));

    private static object UploadError(string code, string message) => new
    {
        success = false,
        code,
        message
    };
}
