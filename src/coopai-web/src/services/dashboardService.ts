import axios from "axios";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5171";

export interface DashboardContractTypeDto {
    prefix: string;
    name: string;
    contractCount: number;
    outstandingContractCount: number;
    paidOffContractCount: number;
    inTermCount: number;
    expiredCount: number;
    principalOutstanding: number;
    profitOutstanding: number;
    totalOutstanding: number;
}

export interface DashboardSummaryDto {
    hasPublishedSnapshot: boolean;
    dataSource: string | null;
    snapshotId: number | null;
    asOfDate: string | null;
    publishedAt: string | null;
    generatedAt: string;
    isReconciled: boolean | null;
    totalSourceRows: number | null;
    totalContracts: number | null;
    templatePlaceholderRows: number | null;
    inTermContracts: number | null;
    expiredContracts: number | null;
    outstandingContracts: number | null;
    paidOffContracts: number | null;
    inTermOutstandingContracts: number | null;
    inTermPaidOffContracts: number | null;
    expiredOutstandingContracts: number | null;
    expiredPaidOffContracts: number | null;
    warningContracts: number | null;
    unresolvedMemberContracts: number | null;
    shadowExcludedContracts: number | null;
    canonicalMatchedContracts: number | null;
    canonicalMissingContracts: number | null;
    principalOutstanding: number | null;
    profitOutstanding: number | null;
    totalOutstanding: number | null;
    expiredOutstandingBalance: number | null;
    contractTypes: DashboardContractTypeDto[];
}

export async function getDashboardSummary() {
    const response = await axios.get<DashboardSummaryDto>(
        `${API_BASE_URL}/api/dashboard/summary`,
        { withCredentials: true },
    );
    return response.data;
}
