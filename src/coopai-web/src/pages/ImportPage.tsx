import { useMemo, useRef, useState } from "react";
import {
    AlertTriangle,
    CheckCircle2,
    ChevronRight,
    FileCheck2,
    FileSpreadsheet,
    LoaderCircle,
    RotateCcw,
    ShieldCheck,
    UploadCloud,
    X,
} from "lucide-react";
import Card from "../components/ui/Card";
import {
    runImport,
    validateImport,
    type ApprovedErrorSkipSelection,
    type ImportErrorDetail,
    type ImportResult,
    type ImportValidationResult,
} from "../services/importService";

interface ErrorReviewRow {
    identity: string;
    rowNumber: number;
    contractNo: string;
    memberNo: string;
    memberName: string;
    canSkip: boolean;
    details: ImportErrorDetail[];
}

const numberFormatter = new Intl.NumberFormat("th-TH");

function normalizeContractNo(contractNo: string) {
    return contractNo.trim().toLocaleLowerCase("th-TH");
}

function createIdentity(rowNumber: number, contractNo: string) {
    return `${rowNumber}:${normalizeContractNo(contractNo)}`;
}

function buildReviewRows(details: ImportErrorDetail[]): ErrorReviewRow[] {
    const rows = new Map<string, ErrorReviewRow>();

    details.forEach((detail) => {
        const identity = createIdentity(detail.rowNumber, detail.contractNo ?? "");
        const existing = rows.get(identity);
        if (existing) {
            existing.canSkip = existing.canSkip && detail.canSkip === true;
            existing.details.push(detail);
            return;
        }

        rows.set(identity, {
            identity,
            rowNumber: detail.rowNumber,
            contractNo: detail.contractNo ?? "",
            memberNo: detail.memberNo ?? "",
            memberName: detail.memberName ?? "",
            canSkip: detail.canSkip === true,
            details: [detail],
        });
    });

    return Array.from(rows.values()).sort((left, right) => left.rowNumber - right.rowNumber);
}

function errorTypeLabel(errorType: string) {
    switch (errorType) {
        case "Negative":
            return "ค่าติดลบ";
        case "TotalBalanceMismatch":
            return "ยอดรวมไม่ตรง";
        case "ContractNotFound":
            return "ไม่พบสัญญา";
        default:
            return errorType || "ข้อผิดพลาด";
    }
}

function uniqueValues(values: string[]) {
    return Array.from(new Set(values.filter((value) => value?.trim())));
}

function responseErrorMessage(error: unknown, fallback: string) {
    if (typeof error !== "object" || error === null || !("response" in error))
        return fallback;

    const response = (error as { response?: { data?: unknown } }).response;
    const data = response?.data;
    if (typeof data !== "object" || data === null)
        return fallback;

    const payload = data as { message?: unknown; errors?: unknown };
    if (typeof payload.message === "string" && payload.message.trim())
        return payload.message;
    if (Array.isArray(payload.errors)) {
        const messages = payload.errors.filter((item): item is string => typeof item === "string");
        if (messages.length > 0)
            return messages.join("; ");
    }

    return fallback;
}

function hasErrorType(result: ImportResult, errorType: string) {
    return (result.errorDetails ?? []).some((detail) => detail.errorType === errorType);
}

function SummaryCard({ label, value, tone }: { label: string; value: number; tone: "slate" | "green" | "amber" | "red" | "blue" }) {
    const tones = {
        slate: "border-slate-200 bg-slate-50 text-slate-800",
        green: "border-emerald-200 bg-emerald-50 text-emerald-800",
        amber: "border-amber-200 bg-amber-50 text-amber-800",
        red: "border-red-200 bg-red-50 text-red-800",
        blue: "border-blue-200 bg-blue-50 text-blue-800",
    };

    return (
        <div className={`rounded-xl border p-4 ${tones[tone]}`}>
            <p className="text-sm font-medium opacity-80">{label}</p>
            <p className="mt-2 text-2xl font-semibold tabular-nums">{numberFormatter.format(value)}</p>
        </div>
    );
}

export default function ImportPage() {
    const [selectedFile, setSelectedFile] = useState<File | null>(null);
    const [validationResult, setValidationResult] = useState<ImportValidationResult | null>(null);
    const [selectedSkipIdentities, setSelectedSkipIdentities] = useState<Set<string>>(new Set());
    const [importResult, setImportResult] = useState<ImportResult | null>(null);
    const [isValidating, setIsValidating] = useState(false);
    const [isImporting, setIsImporting] = useState(false);
    const [showConfirmation, setShowConfirmation] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);
    const fileInputRef = useRef<HTMLInputElement>(null);
    const validationSubmissionLock = useRef(false);
    const importSubmissionLock = useRef(false);

    const reviewRows = useMemo(
        () => buildReviewRows(validationResult?.errorDetails ?? []),
        [validationResult]
    );
    const skippableRows = useMemo(
        () => reviewRows.filter((row) => row.canSkip),
        [reviewRows]
    );
    const selectedSkipRows = useMemo<ApprovedErrorSkipSelection[]>(
        () => skippableRows
            .filter((row) => selectedSkipIdentities.has(row.identity))
            .map((row) => ({ rowNumber: row.rowNumber, contractNo: row.contractNo })),
        [selectedSkipIdentities, skippableRows]
    );

    const unresolvedErrors = Math.max(
        0,
        (validationResult?.failedRows ?? 0) - selectedSkipRows.length
    );
    const missingContracts = validationResult?.missingContracts ?? 0;
    const canImport = Boolean(
        selectedFile &&
        validationResult?.fileHash &&
        unresolvedErrors === 0 &&
        missingContracts === 0 &&
        !isValidating &&
        !isImporting &&
        !importResult?.success
    );
    const allSkippableSelected = skippableRows.length > 0 &&
        skippableRows.every((row) => selectedSkipIdentities.has(row.identity));

    function resetReviewState() {
        setValidationResult(null);
        setSelectedSkipIdentities(new Set());
        setImportResult(null);
        setShowConfirmation(false);
        setErrorMessage(null);
        setSuccessMessage(null);
    }

    function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
        setSelectedFile(event.target.files?.[0] ?? null);
        resetReviewState();
    }

    function handleStartOver() {
        setSelectedFile(null);
        if (fileInputRef.current)
            fileInputRef.current.value = "";
        resetReviewState();
    }

    async function handleValidate() {
        if (!selectedFile || validationSubmissionLock.current)
            return;

        validationSubmissionLock.current = true;
        setIsValidating(true);
        setValidationResult(null);
        setSelectedSkipIdentities(new Set());
        setImportResult(null);
        setShowConfirmation(false);
        setErrorMessage(null);
        setSuccessMessage(null);

        try {
            const result = await validateImport(selectedFile);
            setValidationResult(result);

            if (!result.fileHash) {
                setErrorMessage("ไม่สามารถยืนยันไฟล์ที่ตรวจสอบได้ กรุณาตรวจสอบไฟล์ใหม่อีกครั้ง");
            } else if (result.failedRows === 0 && result.missingContracts === 0) {
                setSuccessMessage("ตรวจสอบไฟล์เรียบร้อย พร้อมยืนยันการนำเข้า");
            }
        } catch (error) {
            setErrorMessage(responseErrorMessage(error, "ตรวจสอบไฟล์ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง"));
        } finally {
            validationSubmissionLock.current = false;
            setIsValidating(false);
        }
    }

    function toggleSkip(identity: string) {
        const row = skippableRows.find((item) => item.identity === identity);
        if (!row)
            return;

        setSelectedSkipIdentities((current) => {
            const next = new Set(current);
            if (next.has(identity))
                next.delete(identity);
            else
                next.add(identity);
            return next;
        });
        setImportResult(null);
        setSuccessMessage(null);
    }

    function toggleAllSkippable() {
        setSelectedSkipIdentities(() => {
            if (allSkippableSelected)
                return new Set();
            return new Set(skippableRows.map((row) => row.identity));
        });
        setImportResult(null);
        setSuccessMessage(null);
    }

    function openConfirmation() {
        if (!canImport)
            return;
        setShowConfirmation(true);
    }

    async function handleConfirmImport() {
        if (!selectedFile || !validationResult || !canImport || importSubmissionLock.current)
            return;

        importSubmissionLock.current = true;
        setIsImporting(true);
        setErrorMessage(null);
        setSuccessMessage(null);
        setImportResult(null);

        try {
            const result = await runImport(selectedFile, validationResult.fileHash, selectedSkipRows);
            setImportResult(result);

            if (hasErrorType(result, "ValidationFileHashMismatch")) {
                setValidationResult(null);
                setSelectedSkipIdentities(new Set());
                setErrorMessage("ไฟล์ที่นำเข้าไม่ตรงกับไฟล์ที่ตรวจสอบ กรุณาตรวจสอบไฟล์ใหม่อีกครั้ง");
            } else if (hasErrorType(result, "ImportTransactionRolledBack")) {
                setErrorMessage("การนำเข้าไม่สำเร็จ ระบบยกเลิกการเปลี่ยนแปลงทั้งหมดแล้ว");
            } else if (hasErrorType(result, "ImportRollbackFailed")) {
                setErrorMessage("การนำเข้าไม่สำเร็จและไม่สามารถยืนยันผลการย้อนกลับได้ กรุณาให้ผู้ดูแลระบบตรวจสอบฐานข้อมูลก่อนลองใหม่");
            } else if (result.success) {
                setSuccessMessage("นำเข้าข้อมูลสำเร็จครบทั้งชุด");
            } else {
                setErrorMessage((result.errors ?? []).join("; ") || "นำเข้าข้อมูลไม่สำเร็จ");
            }
        } catch (error) {
            setErrorMessage(responseErrorMessage(error, "นำเข้าข้อมูลไม่สำเร็จ กรุณาลองใหม่อีกครั้ง"));
        } finally {
            importSubmissionLock.current = false;
            setIsImporting(false);
            setShowConfirmation(false);
        }
    }

    return (
        <div className="space-y-6">
            <Card>
                <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
                    <div className="flex items-start gap-4">
                        <div className="rounded-xl bg-emerald-100 p-3 text-emerald-700">
                            <FileSpreadsheet size={28} />
                        </div>
                        <div>
                            <h1 className="text-2xl font-semibold text-slate-900">ตรวจสอบและนำเข้า Excel</h1>
                            <p className="mt-1 text-sm text-slate-500">
                                ตรวจสอบข้อมูล เลือกรายการที่อนุญาตให้ข้าม และยืนยันก่อนนำเข้าจริง
                            </p>
                        </div>
                    </div>

                    <div className="flex flex-wrap items-center gap-2 text-xs font-medium text-slate-500">
                        <span className={validationResult ? "text-emerald-700" : "text-slate-700"}>1. เลือกไฟล์</span>
                        <ChevronRight size={14} />
                        <span className={validationResult ? "text-emerald-700" : "text-slate-400"}>2. ตรวจสอบ</span>
                        <ChevronRight size={14} />
                        <span className={importResult ? "text-emerald-700" : "text-slate-400"}>3. ยืนยันนำเข้า</span>
                    </div>
                </div>

                <div className="mt-6 grid gap-4 lg:grid-cols-[1fr_auto] lg:items-end">
                    <div>
                        <label htmlFor="loan-import-file" className="mb-2 block text-sm font-medium text-slate-700">
                            เลือกไฟล์ .xlsx หรือ .xls
                        </label>
                        <input
                            ref={fileInputRef}
                            id="loan-import-file"
                            type="file"
                            accept=".xlsx,.xls"
                            onChange={handleFileChange}
                            disabled={isValidating || isImporting}
                            className="block w-full rounded-xl border border-slate-200 bg-white p-3 text-sm text-slate-700 file:mr-4 file:rounded-lg file:border-0 file:bg-emerald-50 file:px-4 file:py-2 file:font-medium file:text-emerald-700 disabled:cursor-not-allowed disabled:opacity-60"
                        />
                        {selectedFile ? (
                            <p className="mt-2 text-xs text-slate-500">
                                ไฟล์ที่เลือก: <span className="font-medium text-slate-700">{selectedFile.name}</span>
                            </p>
                        ) : null}
                    </div>

                    <button
                        type="button"
                        onClick={handleValidate}
                        disabled={!selectedFile || isValidating || isImporting}
                        className="inline-flex min-w-40 items-center justify-center gap-2 rounded-xl bg-emerald-600 px-5 py-3 font-medium text-white transition hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-slate-300"
                    >
                        {isValidating ? <LoaderCircle className="animate-spin" size={18} /> : <FileCheck2 size={18} />}
                        {isValidating ? "กำลังตรวจสอบ..." : validationResult ? "ตรวจสอบอีกครั้ง" : "ตรวจสอบไฟล์"}
                    </button>
                </div>
            </Card>

            {errorMessage ? (
                <div role="alert" className="flex items-start gap-3 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800">
                    <AlertTriangle className="mt-0.5 shrink-0" size={18} />
                    <span>{errorMessage}</span>
                </div>
            ) : null}

            {successMessage ? (
                <div role="status" className="flex items-start gap-3 rounded-xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800">
                    <CheckCircle2 className="mt-0.5 shrink-0" size={18} />
                    <span>{successMessage}</span>
                </div>
            ) : null}

            {validationResult ? (
                <>
                    <Card>
                        <div className="mb-4 flex items-center gap-3">
                            <ShieldCheck className="text-emerald-700" size={22} />
                            <div>
                                <h2 className="text-lg font-semibold text-slate-900">ผลการตรวจสอบ</h2>
                                <p className="text-sm text-slate-500">ตัวเลขทั้งหมดมาจากผล validate-only ของ backend</p>
                            </div>
                        </div>

                        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                            <SummaryCard label="รายการทั้งหมด" value={validationResult.totalRows ?? 0} tone="slate" />
                            <SummaryCard label="พร้อมอัปเดต" value={validationResult.updateCandidates ?? 0} tone="green" />
                            <SummaryCard label="ข้ามอัตโนมัติ" value={validationResult.skippedRows ?? 0} tone="blue" />
                            <SummaryCard label="ต้องตรวจสอบ" value={validationResult.failedRows ?? 0} tone="amber" />
                            <SummaryCard label="ไม่พบสัญญา" value={validationResult.missingContracts ?? 0} tone="red" />
                        </div>
                    </Card>

                    {reviewRows.length > 0 ? (
                        <Card>
                            <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                                <div>
                                    <h2 className="text-lg font-semibold text-slate-900">รายการที่ต้องตรวจสอบ</h2>
                                    <p className="mt-1 text-sm text-slate-500">
                                        การเลือกข้ามคือการยืนยันอย่างชัดเจน ไม่ได้แก้ไขข้อมูลใน Excel
                                    </p>
                                </div>
                                {skippableRows.length > 0 ? (
                                    <button
                                        type="button"
                                        onClick={toggleAllSkippable}
                                        disabled={isImporting}
                                        className="inline-flex items-center justify-center gap-2 rounded-lg border border-amber-300 bg-amber-50 px-4 py-2 text-sm font-medium text-amber-800 hover:bg-amber-100 disabled:opacity-50"
                                    >
                                        {allSkippableSelected ? "ยกเลิกเลือกทั้งหมด" : "เลือกข้ามทั้งหมดที่อนุญาต"}
                                    </button>
                                ) : null}
                            </div>

                            <div className="max-h-[34rem] overflow-auto rounded-xl border border-slate-200">
                                <table className="min-w-[1280px] w-full text-left text-sm">
                                    <thead className="sticky top-0 z-10 bg-slate-100 text-xs uppercase tracking-wide text-slate-600 shadow-sm">
                                        <tr>
                                            <th className="px-3 py-3 text-center">ข้าม</th>
                                            <th className="px-3 py-3">Excel Row</th>
                                            <th className="px-3 py-3">ContractNo</th>
                                            <th className="px-3 py-3">MemberNo</th>
                                            <th className="px-3 py-3">MemberName</th>
                                            <th className="px-3 py-3">ประเภท</th>
                                            <th className="px-3 py-3">Field</th>
                                            <th className="px-3 py-3">Raw Value</th>
                                            <th className="px-3 py-3">ข้อความ</th>
                                            <th className="px-3 py-3">คำแนะนำ</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-slate-200 bg-white align-top">
                                        {reviewRows.map((row) => {
                                            const selected = selectedSkipIdentities.has(row.identity);
                                            const errorTypes = uniqueValues(row.details.map((detail) => errorTypeLabel(detail.errorType)));
                                            const fields = uniqueValues(row.details.map((detail) => detail.fieldName));
                                            const rawValues = uniqueValues(row.details.map((detail) => detail.rawValue));
                                            const suggestions = uniqueValues(row.details.map((detail) => detail.suggestedAction));

                                            return (
                                                <tr key={row.identity} className={selected ? "bg-amber-50" : "hover:bg-slate-50"}>
                                                    <td className="px-3 py-3 text-center">
                                                        {row.canSkip ? (
                                                            <input
                                                                type="checkbox"
                                                                checked={selected}
                                                                onChange={() => toggleSkip(row.identity)}
                                                                disabled={isImporting}
                                                                aria-label={`เลือกข้ามแถว ${row.rowNumber} สัญญา ${row.contractNo}`}
                                                                className="h-4 w-4 rounded border-slate-300 text-amber-600 focus:ring-amber-500"
                                                            />
                                                        ) : (
                                                            <span className="text-slate-300">—</span>
                                                        )}
                                                    </td>
                                                    <td className="whitespace-nowrap px-3 py-3 font-medium text-slate-800">{row.rowNumber}</td>
                                                    <td className="whitespace-nowrap px-3 py-3 text-slate-700">{row.contractNo || "-"}</td>
                                                    <td className="whitespace-nowrap px-3 py-3 text-slate-700">{row.memberNo || "-"}</td>
                                                    <td className="min-w-52 px-3 py-3 text-slate-700">{row.memberName || "-"}</td>
                                                    <td className="px-3 py-3">
                                                        <div className="flex flex-wrap gap-1">
                                                            {errorTypes.map((type) => (
                                                                <span key={type} className="rounded-full bg-red-100 px-2 py-1 text-xs font-medium text-red-700">{type}</span>
                                                            ))}
                                                        </div>
                                                    </td>
                                                    <td className="px-3 py-3 text-slate-700">{fields.join(", ") || "-"}</td>
                                                    <td className="px-3 py-3 font-mono text-xs text-slate-700">{rawValues.join(", ") || "-"}</td>
                                                    <td className="min-w-72 px-3 py-3 text-slate-700">
                                                        <ul className="space-y-1">
                                                            {row.details.map((detail, index) => (
                                                                <li key={`${detail.fieldName}-${index}`}>{detail.errorMessage}</li>
                                                            ))}
                                                        </ul>
                                                        {!row.canSkip ? (
                                                            <p className="mt-2 font-medium text-red-700">ต้องแก้ไขก่อนนำเข้า</p>
                                                        ) : null}
                                                    </td>
                                                    <td className="min-w-52 px-3 py-3 text-slate-600">{suggestions.join("; ") || (row.canSkip ? "ตรวจสอบหรือเลือกข้าม" : "แก้ไขไฟล์และตรวจสอบใหม่")}</td>
                                                </tr>
                                            );
                                        })}
                                    </tbody>
                                </table>
                            </div>
                        </Card>
                    ) : null}

                    <Card className="border-emerald-200">
                        <div className="flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
                            <div className="flex-1">
                                <h2 className="text-lg font-semibold text-slate-900">สรุปก่อนยืนยัน</h2>
                                <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                                    <SummaryCard label="พร้อมอัปเดต" value={validationResult.updateCandidates ?? 0} tone="green" />
                                    <SummaryCard label="ข้ามอัตโนมัติ" value={validationResult.skippedRows ?? 0} tone="blue" />
                                    <SummaryCard label="เลือกข้ามข้อผิดพลาด" value={selectedSkipRows.length} tone="amber" />
                                    <SummaryCard label="ยังไม่ได้แก้/ข้าม" value={unresolvedErrors + missingContracts} tone="red" />
                                </div>
                                {missingContracts > 0 ? (
                                    <p className="mt-3 text-sm font-medium text-red-700">
                                        มี {numberFormatter.format(missingContracts)} สัญญาที่ไม่พบในระบบ ต้องแก้ไขก่อนนำเข้า
                                    </p>
                                ) : null}
                            </div>

                            <button
                                type="button"
                                onClick={openConfirmation}
                                disabled={!canImport}
                                className="inline-flex min-w-52 items-center justify-center gap-2 rounded-xl bg-blue-600 px-5 py-3 font-medium text-white transition hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-slate-300"
                            >
                                <UploadCloud size={18} />
                                {importResult?.success ? "นำเข้าแล้ว" : "ยืนยันการนำเข้า"}
                            </button>
                        </div>
                    </Card>
                </>
            ) : null}

            {importResult ? (
                <Card className={importResult.success ? "border-emerald-200" : "border-red-200"}>
                    <div className="flex items-start gap-3">
                        {importResult.success
                            ? <CheckCircle2 className="shrink-0 text-emerald-700" size={24} />
                            : <AlertTriangle className="shrink-0 text-red-700" size={24} />}
                        <div className="min-w-0 flex-1">
                            <h2 className="text-lg font-semibold text-slate-900">
                                {importResult.success ? "ผลการนำเข้า" : "การนำเข้าไม่สำเร็จ"}
                            </h2>
                            <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                                <SummaryCard label="ทั้งหมด" value={importResult.totalRows ?? 0} tone="slate" />
                                <SummaryCard label="อัปเดต" value={importResult.updatedRows ?? 0} tone="green" />
                                <SummaryCard label="นำเข้าใหม่" value={importResult.importedRows ?? 0} tone="blue" />
                                <SummaryCard label="ข้ามอัตโนมัติ" value={importResult.skippedRows ?? 0} tone="blue" />
                                <SummaryCard label="ผู้ใช้เลือกข้าม" value={importResult.userSkippedErrorRows ?? 0} tone="amber" />
                            </div>
                            <div className="mt-3 flex flex-wrap gap-4 text-sm text-slate-600">
                                <span>ล้มเหลว: <strong className="text-slate-900">{numberFormatter.format(importResult.failedRows ?? 0)}</strong></span>
                                <span>Batch ID: <strong className="text-slate-900">{importResult.batchId ?? "-"}</strong></span>
                            </div>
                            {(importResult.errors ?? []).length > 0 ? (
                                <ul className="mt-4 list-disc space-y-1 pl-5 text-sm text-red-700">
                                    {importResult.errors.map((error, index) => <li key={index}>{error}</li>)}
                                </ul>
                            ) : null}
                        </div>
                    </div>
                </Card>
            ) : null}

            {!selectedFile && !validationResult ? (
                <Card className="border-dashed text-center">
                    <FileCheck2 className="mx-auto text-slate-300" size={40} />
                    <h2 className="mt-3 font-medium text-slate-700">เริ่มจากเลือกไฟล์ Excel</h2>
                    <p className="mt-1 text-sm text-slate-500">ระบบจะตรวจสอบข้อมูลเท่านั้น และจะไม่เริ่มนำเข้าโดยอัตโนมัติ</p>
                </Card>
            ) : null}

            {showConfirmation && validationResult ? (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/50 p-4" role="presentation">
                    <div role="dialog" aria-modal="true" aria-labelledby="confirm-import-title" className="w-full max-w-lg rounded-2xl bg-white p-6 shadow-2xl">
                        <div className="flex items-start justify-between gap-4">
                            <div className="flex items-start gap-3">
                                <div className="rounded-xl bg-blue-100 p-2 text-blue-700">
                                    <ShieldCheck size={22} />
                                </div>
                                <div>
                                    <h2 id="confirm-import-title" className="text-xl font-semibold text-slate-900">ยืนยันการนำเข้าข้อมูล</h2>
                                    <p className="mt-1 text-sm text-slate-500">กรุณาตรวจสอบจำนวนรายการครั้งสุดท้าย</p>
                                </div>
                            </div>
                            <button
                                type="button"
                                onClick={() => setShowConfirmation(false)}
                                disabled={isImporting}
                                aria-label="ปิดหน้าต่างยืนยัน"
                                className="rounded-lg p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700 disabled:opacity-50"
                            >
                                <X size={20} />
                            </button>
                        </div>

                        <ul className="mt-6 space-y-3 rounded-xl bg-slate-50 p-4 text-sm text-slate-700">
                            <li>จะอัปเดต <strong>{numberFormatter.format(validationResult.updateCandidates ?? 0)}</strong> สัญญา</li>
                            <li>ข้ามอัตโนมัติ <strong>{numberFormatter.format(validationResult.skippedRows ?? 0)}</strong> รายการ</li>
                            <li>ข้ามข้อผิดพลาดตามที่เลือก <strong>{numberFormatter.format(selectedSkipRows.length)}</strong> รายการ</li>
                            <li className="font-medium text-emerald-700">ไม่มีรายการผิดพลาดที่ยังไม่ได้จัดการ</li>
                        </ul>

                        <div className="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
                            <button
                                type="button"
                                onClick={() => setShowConfirmation(false)}
                                disabled={isImporting}
                                className="rounded-xl border border-slate-300 px-5 py-2.5 font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                            >
                                ยกเลิก
                            </button>
                            <button
                                type="button"
                                onClick={handleConfirmImport}
                                disabled={isImporting || !canImport}
                                className="inline-flex items-center justify-center gap-2 rounded-xl bg-blue-600 px-5 py-2.5 font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-slate-300"
                            >
                                {isImporting ? <LoaderCircle className="animate-spin" size={18} /> : <UploadCloud size={18} />}
                                {isImporting ? "กำลังนำเข้า..." : "ยืนยันนำเข้า"}
                            </button>
                        </div>
                    </div>
                </div>
            ) : null}

            {validationResult && !importResult?.success ? (
                <button
                    type="button"
                    onClick={handleStartOver}
                    disabled={isValidating || isImporting}
                    className="inline-flex items-center gap-2 text-sm font-medium text-slate-500 hover:text-slate-800 disabled:opacity-50"
                >
                    <RotateCcw size={16} />
                    เริ่มใหม่ด้วยไฟล์อื่น
                </button>
            ) : null}
        </div>
    );
}
