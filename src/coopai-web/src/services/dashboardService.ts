import { apiClient } from "./apiClient";
import {
    DEFAULT_DASHBOARD_MODE,
    dashboardSummaryParams,
    type DashboardMode,
} from "../utils/dashboardMode";

export { DEFAULT_DASHBOARD_MODE, dashboardSummaryParams } from "../utils/dashboardMode";
export type { DashboardMode } from "../utils/dashboardMode";

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

export interface DashboardWorkQueueSummaryDto {
    analysisUniverseContracts: number;
    totalQueueContracts: number;
    collectionContracts: number;
    reviewOnlyContracts: number;
    urgentContracts: number;
    highContracts: number;
    mediumContracts: number;
    reviewContracts: number;
}

export interface DashboardDebtBucketDto {
    bucket: string;
    label: string;
    contractCount: number;
    outstandingAmount: number;
}

export interface DashboardSummaryDto {
    mode: DashboardMode;
    available: boolean;
    message: string;
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
    publishedRecordCount: number | null;
    dataThroughPeriod: string | null;
    debtSnapshotId: string | null;
    sourceAFingerprint: string | null;
    sourceASnapshotPublishedAt: string | null;
    installmentSnapshotId: string | null;
    sourceBFingerprint: string | null;
    sourceBSnapshotPublishedAt: string | null;
    debtLastSuccessfulSyncAt: string | null;
    installmentLastSuccessfulSyncAt: string | null;
    isStale: boolean;
    freshnessWarning: string | null;
    amountDue: number | null;
    actualPayment: number | null;
    downPaymentContracts: number | null;
    downPaymentAmount: number | null;
    openingOutstanding: number | null;
    increaseDuringPeriod: number | null;
    endingOutstanding: number | null;
    workQueue: DashboardWorkQueueSummaryDto | null;
    debtBuckets: DashboardDebtBucketDto[];
    contractTypes: DashboardContractTypeDto[];
}

export async function getDashboardSummary(mode: DashboardMode = DEFAULT_DASHBOARD_MODE) {
    const response = await apiClient.get<DashboardSummaryDto>("/api/dashboard/summary", {
        params: dashboardSummaryParams(mode),
    });
    return response.data;
}
