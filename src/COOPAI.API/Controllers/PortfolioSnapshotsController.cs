using COOPAI.API.DTOs.PortfolioSnapshots;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.AspNetCore.Mvc;

namespace COOPAI.API.Controllers;

[ApiController]
[Route("api/portfolio-snapshots")]
public sealed class PortfolioSnapshotsController(
    IPortfolioSnapshotWorkflowService workflowService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortfolioSnapshotListItemDto>>> List(
        CancellationToken cancellationToken) =>
        Ok(await workflowService.ListAsync(cancellationToken));

    [HttpPost("validate")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Validate(
        [FromForm] PortfolioSnapshotValidateRequest request,
        CancellationToken cancellationToken)
    {
        var requestError = ValidateUpload(request.File, request.AsOfDate);
        if (requestError is not null)
            return BadRequest(requestError);

        var tempPath = await CopyToControlledTempAsync(request.File!, cancellationToken);
        try
        {
            return Ok(await workflowService.ValidateAsync(
                tempPath,
                request.File!.FileName,
                request.AsOfDate,
                request.DefinitionVersion,
                cancellationToken));
        }
        catch (PortfolioSnapshotWorkflowException exception)
        {
            return WorkflowError(exception);
        }
        finally
        {
            DeleteTempFile(tempPath);
        }
    }

    [HttpPost("drafts")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateDraft(
        [FromForm] PortfolioSnapshotCreateDraftRequest request,
        CancellationToken cancellationToken)
    {
        var requestError = ValidateUpload(request.File, request.AsOfDate);
        if (requestError is not null)
            return BadRequest(requestError);

        var tempPath = await CopyToControlledTempAsync(request.File!, cancellationToken);
        try
        {
            var result = await workflowService.CreateDraftAsync(
                tempPath,
                request.File!.FileName,
                request.AsOfDate,
                request.ExpectedSourceFileHash,
                request.ExpectedSnapshotContentHash,
                request.DefinitionVersion,
                cancellationToken);
            return result.WasExisting ? Ok(result) : CreatedAtAction(nameof(Review), new { id = result.Id }, result);
        }
        catch (PortfolioSnapshotWorkflowException exception)
        {
            return WorkflowError(exception);
        }
        finally
        {
            DeleteTempFile(tempPath);
        }
    }

    [HttpPost("{id:int}/validate")]
    public async Task<IActionResult> ValidateDraft(int id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await workflowService.ValidateDraftAsync(id, cancellationToken));
        }
        catch (PortfolioSnapshotWorkflowException exception)
        {
            return WorkflowError(exception);
        }
    }

    [HttpGet("{id:int}/review")]
    public async Task<IActionResult> Review(int id, CancellationToken cancellationToken)
    {
        var result = await workflowService.GetReviewAsync(id, cancellationToken);
        return result is null
            ? NotFound(new PortfolioSnapshotErrorDto("SnapshotNotFound", $"Portfolio Snapshot {id} was not found."))
            : Ok(result);
    }

    [HttpGet("{id:int}/records")]
    public async Task<IActionResult> Records(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? termStatus = null,
        [FromQuery] string? balanceStatus = null,
        [FromQuery] string? inclusionStatus = null,
        [FromQuery] string? warningCode = null,
        [FromQuery] string? canonicalMatchStatus = null,
        [FromQuery] string? memberMatchStatus = null,
        [FromQuery] string? loanTypePrefix = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await workflowService.GetRecordsAsync(
                id,
                page,
                pageSize,
                termStatus,
                balanceStatus,
                inclusionStatus,
                warningCode,
                canonicalMatchStatus,
                memberMatchStatus,
                loanTypePrefix,
                cancellationToken);
            return result is null
                ? NotFound(new PortfolioSnapshotErrorDto("SnapshotNotFound", $"Portfolio Snapshot {id} was not found."))
                : Ok(result);
        }
        catch (PortfolioSnapshotWorkflowException exception)
        {
            return WorkflowError(exception);
        }
    }

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] PortfolioSnapshotRejectRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await workflowService.RejectAsync(id, request.Reason, cancellationToken));
        }
        catch (PortfolioSnapshotWorkflowException exception)
        {
            return WorkflowError(exception);
        }
    }

    [HttpPost("{id:int}/publish")]
    public IActionResult Publish(int id) =>
        Conflict(new PortfolioSnapshotErrorDto(
            "PublishingDisabled",
            $"Publishing Portfolio Snapshot {id} is disabled in Gate 2A and no data was changed."));

    private IActionResult WorkflowError(PortfolioSnapshotWorkflowException exception)
    {
        var error = new PortfolioSnapshotErrorDto(exception.Code, exception.Message);
        if (exception.Code == "SnapshotNotFound")
            return NotFound(error);
        if (exception.IsConflict)
            return Conflict(error);
        if (exception.Code == "SnapshotValidationFailed")
            return UnprocessableEntity(error);
        return BadRequest(error);
    }

    private static PortfolioSnapshotErrorDto? ValidateUpload(IFormFile? file, DateOnly asOfDate)
    {
        if (file is null || file.Length == 0)
            return new PortfolioSnapshotErrorDto("FileRequired", "A non-empty .xlsx file is required.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return new PortfolioSnapshotErrorDto("UnsupportedFileType", "Only .xlsx files are supported.");
        if (asOfDate == default)
            return new PortfolioSnapshotErrorDto("AsOfDateRequired", "AsOfDate is required.");
        return null;
    }

    private static async Task<string> CopyToControlledTempAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "COOPAI-PortfolioSnapshots");
        Directory.CreateDirectory(tempFolder);
        var tempPath = Path.Combine(tempFolder, $"snapshot_{Guid.NewGuid():N}.xlsx");
        try
        {
            await using var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous);
            await file.CopyToAsync(stream, cancellationToken);
            return tempPath;
        }
        catch
        {
            DeleteTempFile(tempPath);
            throw;
        }
    }

    private static void DeleteTempFile(string tempPath)
    {
        if (System.IO.File.Exists(tempPath))
            System.IO.File.Delete(tempPath);
    }
}
