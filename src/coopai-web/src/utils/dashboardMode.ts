export type DashboardMode = "CURRENT" | "PUBLISHED";

export const DEFAULT_DASHBOARD_MODE: DashboardMode = "CURRENT";

export function dashboardSummaryParams(mode: DashboardMode = DEFAULT_DASHBOARD_MODE) {
    return { mode };
}
