import axios from "axios";

export interface DashboardSummaryDto {
    TotalLoanContracts: number;
    PrincipalBalance: number;
    ProfitBalance: number;
    TotalBalance: number;
    GeneratedAt: string;
}

export async function getDashboardSummary() {
    const response = await axios.get<DashboardSummaryDto>(
        "http://localhost:5171/api/dashboard/summary"
    );

    return response.data;
}
