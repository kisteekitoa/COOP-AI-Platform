import axios from "axios";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5171";

export interface ImportErrorDetail {
    rowNumber: number;
    contractNo: string;
    memberNo: string;
    memberName: string;
    fieldName: string;
    rawValue: string;
    parsedValue: string;
    errorType: string;
    errorMessage: string;
    canSkip: boolean;
    suggestedAction: string;
    wasUserSkipped?: boolean;
}

export interface ImportSkipDetail {
    rowNumber: number;
    contractNo: string;
    memberName: string;
    status: string;
    message: string;
}

export interface UserSkippedErrorDetail {
    rowNumber: number;
    contractNo: string;
    memberNo: string;
    memberName: string;
    skipReason: string;
    originalErrors: ImportErrorDetail[];
}

export interface ImportValidationResult {
    success: boolean;
    fileHash: string;
    totalRows: number;
    validRows: number;
    updateCandidates: number;
    existingContracts: number;
    missingContracts: number;
    skippedRows: number;
    userSkippedErrorRows: number;
    failedRows: number;
    errors: string[];
    errorDetails: ImportErrorDetail[];
    skipDetails: ImportSkipDetail[];
}

export interface ImportResult {
    success: boolean;
    fileHash: string;
    totalRows: number;
    importedRows: number;
    updatedRows: number;
    skippedRows: number;
    userSkippedErrorRows: number;
    failedRows: number;
    batchId: number | null;
    errors: string[];
    errorDetails: ImportErrorDetail[];
    skipDetails: ImportSkipDetail[];
    userSkippedErrorDetails: UserSkippedErrorDetail[];
}

export interface ApprovedErrorSkipSelection {
    rowNumber: number;
    contractNo: string;
}

export async function validateImport(file: File): Promise<ImportValidationResult> {
    const formData = new FormData();
    formData.append("File", file);

    const response = await axios.post<ImportValidationResult>(
        `${API_BASE_URL}/api/import/validate`,
        formData
    );

    return response.data;
}

export async function runImport(
    file: File,
    validationFileHash: string,
    skipRows: ApprovedErrorSkipSelection[]
): Promise<ImportResult> {
    const formData = new FormData();
    formData.append("File", file);
    formData.append("ValidationFileHash", validationFileHash);
    skipRows.forEach((row, index) => {
        formData.append(`SkipRows[${index}].RowNumber`, String(row.rowNumber));
        formData.append(`SkipRows[${index}].ContractNo`, row.contractNo);
    });

    const response = await axios.post<ImportResult>(
        `${API_BASE_URL}/api/import/import`,
        formData
    );

    return response.data;
}
