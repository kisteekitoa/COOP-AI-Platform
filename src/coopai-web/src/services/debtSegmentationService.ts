import axios from "axios";
import { apiClient, getAntiforgeryHeaders } from "./apiClient";

const PREVIEW_URL = "/api/debt-segmentation/preview";

export interface DebtAutoSyncStatus {
    enabled: boolean;
    status: "Disabled" | "Checking" | "Ready" | "Syncing" | "NetworkUnavailable" | "Error";
    pollSeconds: number;
    lastCheckedAtUtc: string | null;
    lastSyncAttemptAtUtc: string | null;
    lastSuccessfulSyncAtUtc: string | null;
    message: string;
    sourceType: "Local" | "Network";
    sourceFileName: string;
}

export interface InstallmentMasterAutoSyncStatus {
    enabled: boolean;
    status: DebtAutoSyncStatus["status"];
    pollSeconds: number;
    lastCheckedAtUtc: string | null;
    lastSyncAttemptAtUtc: string | null;
    lastSuccessfulSyncAtUtc: string | null;
    message: string;
    sourceType: "Local" | "Network";
    sourceFileName: string;
    sourceFileHash: string | null;
    snapshotId: string | null;
    contractCount: number;
    validContractCount: number;
    invalidContractCount: number;
    schemaMode: "HEADERED" | "HEADERLESS_POSITIONAL" | "UNKNOWN";
}

export interface InstallmentMasterSyncStatus {
    attemptedAtUtc: string;
    trigger: string;
    status: "Success" | "NoChange" | "Failed" | "Busy";
    published: boolean;
    previousSnapshotPreserved: boolean;
    message: string;
    sourceFileName: string;
    sourceFileHash: string | null;
    snapshotId: string | null;
    sourceRows: number;
    contractCount: number;
    validContractCount: number;
    invalidContractCount: number;
    duplicateContractNumberGroups: number;
    invalidTotalInstallmentRows: number;
    invalidMonthlyInstallmentRows: number;
    durationMilliseconds: number;
    errors: string[];
    warnings: string[];
    sourceType: "InstallmentMaster";
    schemaMode: "HEADERED" | "HEADERLESS_POSITIONAL" | "UNKNOWN";
}

export interface InstallmentMasterSnapshot {
    available: boolean;
    message: string;
    snapshotId: string | null;
    sourceFileName: string | null;
    sourceFileHash: string | null;
    sourceLastWriteTimeUtc: string | null;
    publishedAtUtc: string | null;
    sourceRows: number;
    contractCount: number;
    validContractCount: number;
    invalidContractCount: number;
    duplicateContractNumberGroups: number;
    invalidTotalInstallmentRows: number;
    invalidMonthlyInstallmentRows: number;
    warnings: string[];
    schemaMode: "HEADERED" | "HEADERLESS_POSITIONAL" | "UNKNOWN";
}

export interface MonthlyCollectionBreakdown {
    category: string;
    contractCount: number;
    openingOutstanding: number;
    amountDue: number;
    actualPayment: number;
    shortfall: number;
    excessPayment: number;
    advancePayment: number;
}

export interface MonthlyCollectionDataQuality {
    debtContracts: number;
    installmentMasterContracts: number;
    matchedContracts: number;
    unmatchedContracts: number;
    matchCoveragePercent: number;
    duplicateContractNumberGroups: number;
    invalidTotalInstallmentRows: number;
    invalidMonthlyInstallmentRows: number;
    usableInstallmentSchedules: number;
    currentDueContractsMissingInstallmentData: number;
    scheduleInconsistencies: number;
    installmentsElapsedContracts: number;
    activeContractsMissingInstallmentRecord: number;
}

export interface MonthlyCollectionDashboard {
    available: boolean;
    message: string;
    analysisId: string | null;
    analysisVersion: string;
    debtSnapshotId: string | null;
    installmentSnapshotId: string | null;
    currentPeriod: string;
    totals: null | {
        contractCount: number;
        amountDue: number;
        actualPayment: number;
        shortfall: number;
        excessPayment: number;
        advancePayment: number;
        unknownAmountDueContracts: number;
        unknownAmountDueOutstanding: number;
        finalInstallmentContracts: number;
    };
    breakdowns: MonthlyCollectionBreakdown[];
    dataQuality: MonthlyCollectionDataQuality | null;
    paymentAllocation: null | {
        actualPayment: number;
        downPaymentContracts: number;
        downPaymentAmount: number;
        advancePaymentContracts: number;
        advancePaymentAmount: number;
        priorArrearsPaymentContracts: number;
        paymentToPriorArrears: number;
        currentDuePaymentContracts: number;
        paymentToCurrentDue: number;
        trueExcessContracts: number;
        trueExcessPayment: number;
        earlyPayoffContracts: number;
        earlyPayoffAmount: number;
        expiredPayoffContracts: number;
        expiredPayoffAmount: number;
        unclassifiedPaymentContracts: number;
        unclassifiedPaymentAmount: number;
        knownPriorArrearsContracts: number;
        knownPriorArrearsAmount: number;
        unknownPriorArrearsContracts: number;
        priorCreditPolicyRequiredContracts: number;
        priorCreditPolicyResidualAmount: number;
        sourceBDownPaymentMatchedContracts: number;
        debtContracts: number;
        sourceBDownPaymentCoveragePercent: number;
        allocatedPaymentAmount: number;
        reconciliationDifference: number;
        isTrueExcessProvisional: boolean;
        paymentAllocationVersion: string;
        priorAdvanceCreditContracts: number;
        priorAdvanceCreditAmount: number;
        advanceCreditAppliedContracts: number;
        advanceCreditAppliedAmount: number;
        newAdvanceCreditContracts: number;
        newAdvanceCreditAmount: number;
        endingAdvanceCreditContracts: number;
        endingAdvanceCreditAmount: number;
        ledgerReconciliationDifference: number;
        trueExcessTreatment: string;
    };
    generatedAtUtc: string | null;
}

export interface MonthlyAmountDueContract {
    contractNumber: string;
    debtBucket: string;
    contractStatus: string;
    remainingOrOverdueMonths: number | null;
    currentInstallmentNumber: number;
    totalInstallments: number | null;
    monthlyInstallment: number | null;
    openingOutstanding: number;
    amountDue: number | null;
    actualPayment: number;
    shortfall: number | null;
    excessPayment: number;
    advancePayment: number;
    downPaymentSourceAmount: number | null;
    downPaymentDataStatus: string;
    downPaymentAmount: number;
    priorArrears: number | null;
    priorArrearsStatus: string;
    paymentToPriorArrears: number;
    paymentToCurrentDue: number;
    priorAdvanceCredit: number;
    advanceCreditAppliedToPriorArrears: number;
    advanceCreditAppliedToCurrentDue: number;
    advanceCreditApplied: number;
    remainingCurrentDueForCash: number | null;
    newAdvanceCredit: number;
    endingAdvanceCredit: number;
    endingArrears: number | null;
    isEarlyPayoff: boolean;
    earlyPayoffAmount: number;
    isExpiredPayoff: boolean;
    expiredPayoffAmount: number;
    unclassifiedPaymentAmount: number;
    paymentAllocationStatus: string;
    paymentAllocationWarning: string | null;
    endingOutstanding: number;
    dueBasis: string;
    installmentDataStatus: string;
    warnings: string[];
}

export interface MonthlyAmountDueContractPage {
    available: boolean;
    message: string;
    page: number;
    pageSize: number;
    totalCount: number;
    items: MonthlyAmountDueContract[];
}

export interface MonthlyAmountDueContractFilters {
    currentPeriod?: string;
    remainingOrOverdueFilter?: string;
    search?: string;
    page?: number;
    pageSize?: number;
    sortRemainingMonthsDescending?: boolean;
}

export interface MonthlyDebtBucketTrend {
    bucket: string;
    bucketLabel: string;
    contractCount: number;
    outstandingBalance: number;
}

export interface MonthlyPerformancePeriod {
    periodYear: number;
    periodMonth: number;
    periodLabelThai: string;
    contractCount: number;
    memberCount: number;
    openingOutstanding: number | null;
    increaseDuringPeriod: number | null;
    endingOutstanding: number;
    outstandingMovement: number | null;
    openingOutstandingContracts: number | null;
    newOutstandingContracts: number | null;
    reducedOutstandingContracts: number | null;
    endingOutstandingContracts: number;
    paidOffContracts: number;
    amountDue: number | null;
    unknownAmountDueContractCount: number;
    actualPayment: number;
    priorArrears: number | null;
    priorArrearsKnownContractCount: number;
    priorArrearsUnknownContractCount: number;
    priorAdvanceCredit: number;
    advanceCreditApplied: number;
    newAdvanceCredit: number;
    endingAdvanceCredit: number;
    downPayment: number;
    advancePayment: number;
    paymentToPriorArrears: number;
    paymentToCurrentDue: number;
    earlyPayoffCount: number;
    earlyPayoffAmount: number;
    expiredPayoffCount: number;
    expiredPayoffAmount: number;
    unclassifiedCount: number;
    unclassifiedAmount: number;
    paidOffDuringMonthCount: number | null;
    paidOffDuringMonthAmount: number | null;
    newContractCount: number;
    endingArrears: number | null;
    endingArrearsUnknownContractCount: number;
    cashReconciliationDifference: number;
    ledgerReconciliationDifference: number;
    historicalClassificationComplete: boolean;
    incompleteReasons: string[];
    debtBuckets: MonthlyDebtBucketTrend[];
}

export interface MonthlyPerformanceTrend {
    available: boolean;
    message: string;
    debtSnapshotId: string | null;
    installmentSnapshotId: string | null;
    calendarYear: number | null;
    firstPeriod: string | null;
    latestPeriod: string | null;
    months: MonthlyPerformancePeriod[];
}

export interface WorkQueuePrioritySummary {
    priority: "URGENT" | "HIGH" | "MEDIUM" | "REVIEW";
    contractCount: number;
    outstandingAmount: number;
}

export interface WorkQueueLongTermBucket {
    debtBucket: string;
    debtBucketLabel: string;
    contractCount: number;
    outstandingAmount: number;
}

export interface WorkQueueOfficerSummary {
    officerName: string;
    contractCount: number;
    outstandingAmount: number;
    urgentContracts: number;
    highContracts: number;
}

export interface WorkQueueSummary {
    analysisUniverseContracts: number;
    totalQueueContracts: number;
    collectionContracts: number;
    collectionOutstanding: number;
    reviewOnlyContracts: number;
    reviewOnlyAmount: number;
    priorities: WorkQueuePrioritySummary[];
    noPaymentContracts: number;
    currentShortfallContracts: number;
    currentShortfallAmount: number;
    priorArrearsContracts: number;
    priorArrearsAmount: number;
    expiredOutstandingContracts: number;
    expiredOutstandingAmount: number;
    longTermOverdue: WorkQueueLongTermBucket[];
    unclassifiedReviewContracts: number;
    unclassifiedReviewAmount: number;
    unknownPriorArrearsContracts: number;
    missingDownPaymentEvidenceContracts: number;
    missingDownPaymentEvidenceAmount: number;
    paidOffCollectionContracts: number;
    notDueFalseNoPaymentContracts: number;
    fullyCreditCoveredFalseNoPaymentContracts: number;
    assignedContracts: number;
    unassignedContracts: number;
    conflictContracts: number;
    officers: WorkQueueOfficerSummary[];
}

export interface WorkQueueItem {
    contractNo: string;
    memberNo: string | null;
    memberName: string | null;
    groupCode: string | null;
    officerName: string | null;
    assignmentStatus: "ASSIGNED" | "UNASSIGNED" | "CONFLICT";
    assignmentRule: string;
    officerCandidates: string[];
    contractStatus: string;
    debtBucket: string;
    debtBucketLabel: string;
    currentDelinquencyMonths: number | null;
    currentDelinquencyLabel: string;
    expiredAgeMonths: number | null;
    expiredAgeLabel: string | null;
    remainingOrOverdueMonths: number | null;
    remainingOrOverdueLabel: string;
    openingOutstanding: number;
    endingOutstanding: number;
    monthlyInstallment: number | null;
    amountDue: number | null;
    actualPayment: number;
    knownPriorArrears: number | null;
    priorArrearsStatus: string;
    priorAdvanceCredit: number;
    advanceCreditApplied: number;
    endingAdvanceCredit: number;
    paymentToPriorArrears: number;
    paymentToCurrentDue: number;
    currentShortfall: number | null;
    unclassifiedAmount: number;
    queueType: "COLLECTION" | "DATA_REVIEW";
    priority: "URGENT" | "HIGH" | "MEDIUM" | "REVIEW";
    prioritySortOrder: number;
    reasonCodes: string[];
    reasonLabelsThai: string[];
    primaryReason: string;
    dataQualityWarnings: string[];
}

export interface WorkQueuePage {
    available: boolean;
    message: string;
    analysisId: string | null;
    debtSnapshotId: string | null;
    installmentSnapshotId: string | null;
    currentPeriod: string;
    summary: WorkQueueSummary | null;
    page: number;
    pageSize: number;
    totalCount: number;
    items: WorkQueueItem[];
    generatedAtUtc: string | null;
}

export interface WorkQueueFilters {
    currentPeriod?: string;
    queueType?: string;
    priority?: string;
    debtBucket?: string;
    payment?: string;
    contractStatus?: string;
    reason?: string;
    search?: string;
    page?: number;
    pageSize?: 50 | 100 | 200;
}

export interface DebtPreviewTotals {
    memberCount: number;
    contractCount: number;
    principalOutstanding: number;
    profitOutstanding: number;
    outstandingAmount: number;
    paidLatestMonthContracts: number;
    notPaidLatestMonthContracts: number;
    notDueLatestMonthContracts: number;
}

export interface DebtBucketSummary {
    bucket: string;
    bucketLabel: string;
    memberCount: number;
    contractCount: number;
    principalOutstanding: number;
    profitOutstanding: number;
    outstandingAmount: number;
    paidMemberCount: number;
    paidContractCount: number;
    notPaidMemberCount: number;
    notPaidContractCount: number;
    notDueMemberCount: number;
    notDueContractCount: number;
}

export interface DebtBucketPeriodComparison {
    bucket: string;
    bucketLabel: string;
    previousMemberCount: number;
    currentMemberCount: number;
    memberDifference: number;
    previousContractCount: number;
    currentContractCount: number;
    contractDifference: number;
    previousOutstanding: number;
    currentOutstanding: number;
    amountDifference: number;
    enteredContracts: number;
    improvedContracts: number;
    worsenedContracts: number;
    paidOffContracts: number;
    sameBucketBalanceDecreasedContracts: number;
    sameBucketBalanceIncreasedContracts: number;
}

export interface DebtSyncStatus {
    status: "Success" | "NoChange" | "Failed" | "Busy";
    attemptedAtUtc: string;
    success: boolean;
    published: boolean;
    previousSnapshotPreserved: boolean;
    trigger: string;
    message: string;
    sourceFileName: string;
    sourceFileHash: string | null;
    publishedAtUtc: string | null;
    sourceRows: number;
    contractCount: number;
    addedContracts: number;
    removedContracts: number;
    changedContracts: number;
    validationErrors: string[];
    warnings: string[];
    previousPeriod: string | null;
    currentPeriod: string | null;
    candidateRows: number;
    excludedRows: number;
    memberCount: number;
    outstandingAmount: number;
    newContracts: number;
    paidOffContracts: number;
    snapshotId: string | null;
    durationMilliseconds: number;
    sourceType: "Local" | "Network";
}

export interface DebtMovementSummary {
    previousBucket: string;
    currentBucket: string;
    paymentMovement: string;
    category: string;
    memberCount: number;
    contractCount: number;
    previousOutstanding: number;
    currentOutstanding: number;
    amountChange: number;
}

export interface DebtDataQualityWarningSummary {
    code: string;
    affectedContractCount: number;
    factCount: number;
}

export interface DebtDataQualityWarning {
    code: string;
    period: string;
    field: string;
    componentValue: number;
    principalOutstanding: number;
    profitOutstanding: number;
    totalOutstanding: number;
    principalPaymentInput: number;
    profitPaymentInput: number;
    totalPaymentInput: number | null;
    cellAddress: string;
    formula: string;
    formulaR1C1: string | null;
}

export interface DebtPreviewDashboard {
    available: boolean;
    message: string;
    sourceFileName: string;
    sourceFileHash: string | null;
    debtSnapshotPublishedAtUtc: string | null;
    worksheetName: string;
    dataThroughPeriod: string | null;
    policyVersion: string;
    autoSyncOnQueryEnabled: boolean;
    requestedPreviousPeriod: string;
    requestedCurrentPeriod: string;
    warnings: string[];
    dataQualityWarnings: DebtDataQualityWarningSummary[];
    totals: DebtPreviewTotals | null;
    sync: DebtSyncStatus;
    loanTypes: string[];
    branches: string[];
    buckets: DebtBucketSummary[];
    bucketComparisons: DebtBucketPeriodComparison[];
    paymentMovements: DebtMovementSummary[];
    bucketMovements: DebtMovementSummary[];
}

export interface DebtContractDetail {
    sourceRowNumber: number;
    memberCode: string;
    memberName: string;
    contractNumber: string;
    loanType: string;
    contractDate: string | null;
    firstDuePeriod: string | null;
    expireDate: string | null;
    previousBucket: string;
    currentBucket: string;
    currentBucketLabel: string;
    latestPaymentStatus: string;
    latestPaymentAmount: number;
    latestScheduledAmount: number | null;
    latestPaymentDifference: number | null;
    firstMissedPaymentPeriod: string | null;
    consecutiveMissedMonths: number;
    principalBalance: number;
    profitBalance: number;
    totalBalance: number;
    groupCode: string | null;
    branch: string | null;
    paymentMovement: string;
    movementCategory: string;
    previousBalance: number;
    amountChange: number;
    evaluatedPeriod: string;
    lastPaymentPeriod: string | null;
    calculationBasis: string;
    dataQualityWarnings: DebtDataQualityWarning[];
}

export interface DebtContractPage {
    available: boolean;
    message: string;
    page: number;
    pageSize: number;
    totalCount: number;
    items: DebtContractDetail[];
}

export interface DebtContractFilters {
    currentPeriod: string;
    bucket?: string;
    paymentStatus?: string;
    loanType?: string;
    groupCode?: string;
    search?: string;
    paymentMovement?: string;
    previousBucket?: string;
    currentBucket?: string;
    movementCategory?: string;
    branch?: string;
    sortTotalDescending?: boolean;
    page?: number;
    pageSize?: number;
}

export async function getDebtPreview(currentPeriod?: string) {
    const response = await apiClient.get<DebtPreviewDashboard>(PREVIEW_URL, {
        params: currentPeriod ? { currentPeriod } : {},
    });
    return response.data;
}

export async function getDebtAutoSyncStatus() {
    const response = await apiClient.get<DebtAutoSyncStatus>(`${PREVIEW_URL}/auto-sync-status`);
    return response.data;
}

export async function getInstallmentMasterAutoSyncStatus() {
    const response = await apiClient.get<InstallmentMasterAutoSyncStatus>(
        `${PREVIEW_URL}/installment-master/auto-sync-status`);
    return response.data;
}

export async function getInstallmentMasterSnapshot() {
    const response = await apiClient.get<InstallmentMasterSnapshot>(`${PREVIEW_URL}/installment-master`);
    return response.data;
}

export async function syncInstallmentMaster() {
    const headers = await getAntiforgeryHeaders();
    const response = await apiClient.post<InstallmentMasterSyncStatus>(
        `${PREVIEW_URL}/installment-master/sync`, null, { headers });
    return response.data;
}

export async function getMonthlyCollection(currentPeriod?: string) {
    const response = await apiClient.get<MonthlyCollectionDashboard>(`${PREVIEW_URL}/monthly-collection`, {
        params: currentPeriod ? { currentPeriod } : {},
    });
    return response.data;
}

export async function getMonthlyPerformance() {
    const response = await apiClient.get<MonthlyPerformanceTrend>(`${PREVIEW_URL}/monthly-performance`);
    return response.data;
}

export async function getWorkQueue(filters: WorkQueueFilters = {}) {
    const response = await apiClient.get<WorkQueuePage>(`${PREVIEW_URL}/work-queue`, { params: filters });
    return response.data;
}

export async function getMonthlyAmountDueContracts(filters: MonthlyAmountDueContractFilters) {
    const response = await apiClient.get<MonthlyAmountDueContractPage>(
        `${PREVIEW_URL}/monthly-collection/contracts`,
        { params: filters });
    return response.data;
}

export async function getDebtContracts(filters: DebtContractFilters) {
    const response = await apiClient.get<DebtContractPage>(`${PREVIEW_URL}/contracts`, { params: filters });
    return response.data;
}

export async function syncDebtPreview(currentPeriod?: string) {
    const headers = await getAntiforgeryHeaders();
    const response = await apiClient.post<DebtSyncStatus>(`${PREVIEW_URL}/sync`, null, {
        params: currentPeriod ? { currentPeriod } : {},
        headers,
    });
    return response.data;
}

export function debtSyncResultFromError(error: unknown) {
    if (axios.isAxiosError<DebtSyncStatus>(error) && error.response?.data?.status)
        return error.response.data;
    return null;
}

export function debtPreviewError(error: unknown) {
    if (axios.isAxiosError<{ message?: string }>(error))
        return error.response?.data?.message ?? "ไม่สามารถอ่านข้อมูลแยกหนี้ Preview ได้";
    return "ไม่สามารถอ่านข้อมูลแยกหนี้ Preview ได้";
}
