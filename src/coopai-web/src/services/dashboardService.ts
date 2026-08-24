import { apiClient } from "./apiClient";

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
    const response = await apiClient.get<DashboardSummaryDto>("/api/dashboard/summary");
    return response.data;
}
