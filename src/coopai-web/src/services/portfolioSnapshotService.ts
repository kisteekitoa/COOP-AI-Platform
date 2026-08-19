import axios from "axios";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5171";
const SNAPSHOT_URL = `${API_BASE_URL}/api/portfolio-snapshots`;

export interface SnapshotCounts {
    sourceRows: number;
    totalContracts: number;
    placeholderRows: number;
    inTermContracts: number;
    expiredContracts: number;
    outstandingContracts: number;
    paidOffContracts: number;
    inTermOutstandingContracts: number;
    inTermPaidOffContracts: number;
    expiredOutstandingContracts: number;
    expiredPaidOffContracts: number;
    canonicalMatchedContracts: number;
    missingCanonicalContracts: number;
    shadowExcludedContracts: number;
    warningContracts: number;
    unresolvedMemberContracts: number;
}

export interface SnapshotFinancial {
    principalOutstanding: number;
    profitOutstanding: number;
    totalOutstanding: number;
    expiredOutstandingTotal: number;
}

export interface SnapshotValidation {
    isValid: boolean;
    sourceFileHash: string;
    snapshotContentHash: string;
    asOfDate: string;
    definitionVersion: number;
    counts: SnapshotCounts;
    financial: SnapshotFinancial;
    quality: {
        blockingErrorCount: number;
        warnings: Array<{
            code: string;
            message: string;
            sourceRowNumber: number | null;
            severity: string;
        }>;
        unresolvedMembers: number;
        reconciled: boolean;
    };
}

export interface SnapshotListItem {
    id: number;
    asOfDate: string;
    revision: number;
    status: string;
    createdAt: string;
    sourceFileName: string;
    warningContracts: number;
    totalOutstanding: number;
}

export interface SnapshotReview {
    snapshot: {
        id: number;
        asOfDate: string;
        revision: number;
        status: string;
        definitionVersion: number;
        sourceFileName: string;
        sourceFileHash: string;
        snapshotContentHash: string;
        createdAt: string;
        validatedAt: string | null;
        rejectedAt: string | null;
        rejectionReason: string | null;
    };
    counts: SnapshotCounts;
    financial: SnapshotFinancial;
    warningSummary: Array<{
        code: string;
        count: number;
        records: Array<{
            sourceRowNumber: number;
            contractNo: string;
            termStatus: string;
            balanceStatus: string;
            principalOpening: number;
            profitOpening: number;
            totalOpening: number;
            principalOutstanding: number;
            profitOutstanding: number;
            totalOutstanding: number;
        }>;
    }>;
    unresolvedMembers: Array<{
        sourceRowNumber: number;
        contractNo: string;
        termStatus: string;
        balanceStatus: string;
        memberIdAbsent: boolean;
    }>;
    exclusions: Array<{ reasonCode: string; count: number }>;
    publishingEnabled: boolean;
}

export async function validateSnapshot(file: File, asOfDate: string) {
    const form = new FormData();
    form.append("file", file);
    form.append("asOfDate", asOfDate);
    const response = await axios.post<SnapshotValidation>(`${SNAPSHOT_URL}/validate`, form);
    return response.data;
}

export async function createSnapshotDraft(
    file: File,
    asOfDate: string,
    expectedSourceFileHash: string,
    expectedSnapshotContentHash: string,
) {
    const form = new FormData();
    form.append("file", file);
    form.append("asOfDate", asOfDate);
    form.append("expectedSourceFileHash", expectedSourceFileHash);
    form.append("expectedSnapshotContentHash", expectedSnapshotContentHash);
    const response = await axios.post<{ id: number; status: string; wasExisting: boolean }>(
        `${SNAPSHOT_URL}/drafts`,
        form,
    );
    return response.data;
}

export async function getSnapshotReview(id: number) {
    const response = await axios.get<SnapshotReview>(`${SNAPSHOT_URL}/${id}/review`);
    return response.data;
}

export async function listSnapshots() {
    const response = await axios.get<SnapshotListItem[]>(SNAPSHOT_URL);
    return response.data;
}

export async function validatePersistedDraft(id: number) {
    await axios.post(`${SNAPSHOT_URL}/${id}/validate`);
}

export async function rejectSnapshot(id: number, reason: string) {
    await axios.post(`${SNAPSHOT_URL}/${id}/reject`, { reason });
}

export function snapshotApiError(error: unknown) {
    if (axios.isAxiosError<{ message?: string }>(error))
        return error.response?.data?.message ?? "ไม่สามารถดำเนินการกับ Snapshot ได้";
    return "ไม่สามารถดำเนินการกับ Snapshot ได้";
}
