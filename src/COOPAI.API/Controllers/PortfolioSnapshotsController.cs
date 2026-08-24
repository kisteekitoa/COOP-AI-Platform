using System.Security.Claims;
using COOPAI.API.DTOs.PortfolioSnapshots;
using COOPAI.API.Security;
using COOPAI.API.Services.PortfolioSnapshots;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Controllers;

[ApiController]
[Route("api/portfolio-snapshots")]
public sealed class PortfolioSnapshotsController(
    IPortfolioSnapshotWorkflowService workflowService,
    IAntiforgery antiforgery,
    IOptions<PortfolioSnapshotOptions> snapshotOptions) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = CoopPolicies.PortfolioRead)]
    public async Task<ActionResult<IReadOnlyList<PortfolioSnapshotListItemDto>>> List(
        CancellationToken cancellationToken) =>
        Ok(await workflowService.ListAsync(cancellationToken));

    [HttpPost("validate")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = CoopPolicies.PortfolioReview)]
    [EnableRateLimiting("Upload")]
    public async Task<IActionResult> Validate(
        [FromForm] PortfolioSnapshotValidateRequest request,
        CancellationToken cancellationToken)
    {
        var antiforgeryError = await ValidateAntiforgeryAsync();
        if (antiforgeryError is not null)
            return antiforgeryError;
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
    [Authorize(Policy = CoopPolicies.PortfolioManage)]
    [EnableRateLimiting("Upload")]
    public async Task<IActionResult> CreateDraft(
        [FromForm] PortfolioSnapshotCreateDraftRequest request,
        CancellationToken cancellationToken)
    {
        var antiforgeryError = await ValidateAntiforgeryAsync();
        if (antiforgeryError is not null)
            return antiforgeryError;
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
    [Authorize(Policy = CoopPolicies.PortfolioManage)]
    public async Task<IActionResult> ValidateDraft(int id, CancellationToken cancellationToken)
    {
        var antiforgeryError = await ValidateAntiforgeryAsync();
        if (antiforgeryError is not null)
            return antiforgeryError;
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
    [Authorize(Policy = CoopPolicies.PortfolioReview)]
    public async Task<IActionResult> Review(int id, CancellationToken cancellationToken)
    {
        var result = await workflowService.GetReviewAsync(id, cancellationToken);
        return result is null
            ? NotFound(new PortfolioSnapshotErrorDto("SnapshotNotFound", $"Portfolio Snapshot {id} was not found."))
            : Ok(result);
    }

    [HttpGet("{id:int}/records")]
    [Authorize(Policy = CoopPolicies.PortfolioReview)]
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
    [Authorize(Policy = CoopPolicies.PortfolioManage)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] PortfolioSnapshotRejectRequest request,
        CancellationToken cancellationToken)
    {
        var antiforgeryError = await ValidateAntiforgeryAsync();
        if (antiforgeryError is not null)
            return antiforgeryError;
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
    [Authorize(Policy = CoopPolicies.ManagerOnly)]
    public async Task<IActionResult> Publish(
        int id,
        [FromBody] PortfolioSnapshotPublishRequest? request,
        CancellationToken cancellationToken)
    {
        // Preserve the verified Gate 2B-1 disabled response. No state can change while disabled.
        if (!snapshotOptions.Value.PublishingEnabled)
        {
            return Conflict(new PortfolioSnapshotErrorDto(
                "PublishingDisabled",
                $"Publishing Portfolio Snapshot {id} is disabled and no data was changed."));
        }

        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest(new PortfolioSnapshotErrorDto(
                "InvalidAntiforgeryToken",
                "The publish confirmation request is missing or has an invalid antiforgery token."));
        }

        if (request is null || !request.Confirmed)
        {
            return BadRequest(new PortfolioSnapshotErrorDto(
                "PublishConfirmationRequired",
                "Explicit confirmation is required before publishing a Portfolio Snapshot."));
        }

        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var publisherUserId) || publisherUserId <= 0)
        {
            return Unauthorized(new PortfolioSnapshotErrorDto(
                "PublisherIdentityUnavailable",
                "The authenticated Manager identity could not be resolved safely."));
        }

        try
        {
            return Ok(await workflowService.PublishAsync(
                id,
                request.ExpectedSnapshotContentHash,
                publisherUserId,
                cancellationToken));
        }
        catch (PortfolioSnapshotWorkflowException exception)
        {
            return WorkflowError(exception);
        }
    }

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

    private async Task<IActionResult?> ValidateAntiforgeryAsync() =>
        await AntiforgeryError.IsInvalidAsync(HttpContext, antiforgery)
            ? BadRequest(new PortfolioSnapshotErrorDto(
                "InvalidAntiforgeryToken",
                "The request is missing or has an invalid antiforgery token."))
            : null;

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
