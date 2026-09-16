using COOPAI.API.DTOs.Dashboard;
using COOPAI.API.Services.Dashboard;
using COOPAI.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace COOPAI.API.Controllers;

[ApiController]
[Authorize(Policy = CoopPolicies.AuthenticatedUser)]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(
        [FromQuery] string? mode,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(mode) &&
            !string.Equals(mode, DashboardDataModes.Current, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(mode, DashboardDataModes.Published, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Dashboard mode must be CURRENT or PUBLISHED." });
        }

        var summary = await _dashboardService.GetSummaryAsync(mode, cancellationToken);
        return Ok(summary);
    }
}
