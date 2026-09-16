using COOPAI.API.DTOs.DebtSegmentation;
using COOPAI.API.Security;
using COOPAI.API.Services.DebtSegmentation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace COOPAI.API.Controllers;

[ApiController]
[Route("api/debt-segmentation/preview")]
[Authorize(Policy = CoopPolicies.ManagerOnly)]
public sealed class DebtSegmentationController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("installment-master/auto-sync-status")]
    [ProducesResponseType<InstallmentMasterAutoSyncStatusDto>(StatusCodes.Status200OK)]
    public ActionResult<InstallmentMasterAutoSyncStatusDto> GetInstallmentMasterAutoSyncStatus(
        [FromServices] IInstallmentMasterAutoSyncStatus status) => Ok(status.GetStatus());

    [HttpGet("installment-master")]
    [ProducesResponseType<InstallmentMasterSnapshotDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<InstallmentMasterSnapshotDto>> GetInstallmentMaster(
        [FromServices] IInstallmentMasterSyncService service,
        CancellationToken cancellationToken) => Ok(await service.GetSnapshotAsync(cancellationToken));

    [HttpGet("installment-master/sync-history")]
    [ProducesResponseType<IReadOnlyList<InstallmentMasterSyncStatusDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InstallmentMasterSyncStatusDto>>> GetInstallmentMasterHistory(
        [FromServices] IInstallmentMasterSyncService service,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetHistoryAsync(Math.Clamp(take, 1, 200), cancellationToken));

    [HttpPost("installment-master/sync")]
    [ProducesResponseType<InstallmentMasterSyncStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<InstallmentMasterSyncStatusDto>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<InstallmentMasterSyncStatusDto>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InstallmentMasterSyncStatusDto>> SyncInstallmentMaster(
        [FromServices] IInstallmentMasterSyncService service,
        CancellationToken cancellationToken)
    {
        if (await AntiforgeryError.IsInvalidAsync(HttpContext, antiforgery))
            return BadRequest(new { code = "InvalidAntiforgeryToken", message = "The request has an invalid antiforgery token." });
        var result = await service.SyncNowAsync(cancellationToken);
        if (string.Equals(result.Status, DebtSyncStatuses.Busy, StringComparison.Ordinal))
            return Conflict(result);
        return result.Status == DebtSyncStatuses.Failed ? UnprocessableEntity(result) : Ok(result);
    }

    [HttpGet("monthly-collection")]
    [ProducesResponseType<MonthlyCollectionDashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MonthlyCollectionDashboardDto>> GetMonthlyCollection(
        [FromServices] IMonthlyAmountDueService service,
        [FromQuery] string? currentPeriod,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetDashboardAsync(currentPeriod, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { code = "InvalidPeriod", message = exception.Message });
        }
    }

    [HttpGet("monthly-performance")]
    [ProducesResponseType<MonthlyPerformanceTrendDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MonthlyPerformanceTrendDto>> GetMonthlyPerformance(
        [FromServices] IMonthlyPerformanceTrendService service,
        CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(cancellationToken));

    [HttpGet("work-queue")]
    [ProducesResponseType<WorkQueuePageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkQueuePageDto>> GetWorkQueue(
        [FromServices] IWorkQueueService service,
        [FromQuery] string? currentPeriod,
        [FromQuery] string? queueType,
        [FromQuery] string? priority,
        [FromQuery] string? debtBucket,
        [FromQuery] string? payment,
        [FromQuery] string? contractStatus,
        [FromQuery] string? reason,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await service.GetAsync(
                currentPeriod, queueType, priority, debtBucket, payment,
                contractStatus, reason, search, page, pageSize, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { code = "InvalidWorkQueueFilter", message = exception.Message });
        }
    }

    [HttpGet("monthly-collection/contracts")]
    [ProducesResponseType<MonthlyAmountDueContractPageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MonthlyAmountDueContractPageDto>> GetMonthlyCollectionContracts(
        [FromServices] IMonthlyAmountDueService service,
        [FromQuery] string? currentPeriod,
        [FromQuery] string? dueBasis,
        [FromQuery] string? remainingOrOverdueFilter,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool? sortRemainingMonthsDescending = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await service.GetContractsAsync(
                currentPeriod, dueBasis, remainingOrOverdueFilter, search, page, pageSize,
                sortRemainingMonthsDescending, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { code = "InvalidPeriod", message = exception.Message });
        }
    }

    [HttpGet("auto-sync-status")]
    [ProducesResponseType<DebtAutoSyncStatusDto>(StatusCodes.Status200OK)]
    public ActionResult<DebtAutoSyncStatusDto> GetAutoSyncStatus(
        [FromServices] IDebtAutoSyncStatus autoSyncStatus) =>
        Ok(autoSyncStatus.GetStatus());

    [HttpGet("sync-history")]
    [ProducesResponseType<IReadOnlyList<DebtSyncHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DebtSyncHistoryDto>>> GetSyncHistory(
        [FromServices] IDebtSegmentationPreviewService service,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetSyncHistoryAsync(Math.Clamp(take, 1, 200), cancellationToken));

    [HttpPost("sync")]
    [ProducesResponseType<DebtSyncStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<DebtSyncStatusDto>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<DebtSyncStatusDto>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DebtSyncStatusDto>> SyncNow(
        [FromServices] IDebtSegmentationPreviewService service,
        [FromQuery] string? currentPeriod,
        CancellationToken cancellationToken)
    {
        if (await AntiforgeryError.IsInvalidAsync(HttpContext, antiforgery))
            return BadRequest(new { code = "InvalidAntiforgeryToken", message = "The Sync Now request is missing or has an invalid antiforgery token." });
        try
        {
            var result = await service.SyncNowAsync(currentPeriod, cancellationToken);
            if (string.Equals(result.Status, DebtSyncStatuses.Busy, StringComparison.Ordinal))
                return Conflict(result);
            return result.Success ? Ok(result) : UnprocessableEntity(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { code = "InvalidPeriod", message = exception.Message });
        }
    }

    [HttpGet]
    [ProducesResponseType<DebtPreviewDashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DebtPreviewDashboardDto>> GetDashboard(
        [FromServices] IDebtSegmentationPreviewService service,
        [FromQuery] string? currentPeriod,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetDashboardAsync(currentPeriod, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { code = "InvalidPeriod", message = exception.Message });
        }
    }

    [HttpGet("contracts")]
    [ProducesResponseType<DebtContractPageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DebtContractPageDto>> GetContracts(
        [FromServices] IDebtSegmentationPreviewService service,
        [FromQuery] string? currentPeriod,
        [FromQuery] string? bucket,
        [FromQuery] string? paymentStatus,
        [FromQuery] string? loanType,
        [FromQuery] string? groupCode,
        [FromQuery] string? search,
        [FromQuery] string? paymentMovement,
        [FromQuery] string? previousBucket,
        [FromQuery] string? currentBucket,
        [FromQuery] string? movementCategory,
        [FromQuery] string? branch,
        [FromQuery] bool sortTotalDescending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await service.GetContractsAsync(
                currentPeriod, bucket, paymentStatus, loanType, groupCode, search, paymentMovement,
                previousBucket, currentBucket, movementCategory, page, pageSize, branch,
                sortTotalDescending, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { code = "InvalidPeriod", message = exception.Message });
        }
    }
}
