import axios from "axios";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5171";

export interface DashboardContractTypeDto {
    prefix: string;
    name: string;
    contractCount: number;
    outstandingContractCount: number;
    principalBalance: number;
    profitBalance: number;
    totalBalance: number;
}

export interface DashboardSummaryDto {
    totalContracts: number;
    outstandingContracts: number;
    totalMembers: number;
    principalBalance: number;
    profitBalance: number;
    totalBalance: number;
    zeroBalanceContracts: number;
    generatedAt: string;
    contractTypes: DashboardContractTypeDto[];
}

export async function getDashboardSummary() {
    const response = await axios.get<DashboardSummaryDto>(
        `${API_BASE_URL}/api/dashboard/summary`
    );

    return response.data;
}
