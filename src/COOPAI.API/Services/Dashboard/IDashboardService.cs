using COOPAI.API.DTOs.Dashboard;

namespace COOPAI.API.Services.Dashboard;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
