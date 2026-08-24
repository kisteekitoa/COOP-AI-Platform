import { useCallback, useEffect, useState } from "react";
import { AlertTriangle, CheckCircle2, FileCheck2, LockKeyhole, XCircle } from "lucide-react";
import { Link, useNavigate, useParams } from "react-router-dom";

import Card from "../components/ui/Card";
import { useAuth } from "../auth/useAuth";
import {
    createSnapshotDraft,
    getSnapshotReview,
    listSnapshots,
    publishSnapshot,
    rejectSnapshot,
    snapshotApiError,
    validatePersistedDraft,
    validateSnapshot,
    type SnapshotCounts,
    type SnapshotListItem,
    type SnapshotReview,
    type SnapshotValidation,
} from "../services/portfolioSnapshotService";

const countFormatter = new Intl.NumberFormat("th-TH");
const moneyFormatter = new Intl.NumberFormat("th-TH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
});

function Metric({ label, value }: { label: string; value: number }) {
    return (
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
            <p className="text-sm text-slate-600">{label}</p>
            <p className="mt-2 text-2xl font-bold text-slate-900">{countFormatter.format(value)}</p>
        </div>
    );
}

function MoneyMetric({ label, value }: { label: string; value: number }) {
    return (
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
            <p className="text-sm text-slate-600">{label}</p>
            <p className="mt-2 text-xl font-bold text-slate-900">{moneyFormatter.format(value)} บาท</p>
        </div>
    );
}

function CountsPanel({ counts }: { counts: SnapshotCounts }) {
    return (
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-4">
            <Metric label="สัญญาทั้งหมด" value={counts.totalContracts} />
            <Metric label="อยู่ในระยะสัญญา" value={counts.inTermContracts} />
            <Metric label="หมดระยะสัญญา" value={counts.expiredContracts} />
            <Metric label="มีจำนวนคงเหลือ" value={counts.outstandingContracts} />
            <Metric label="ชำระหมดแล้ว" value={counts.paidOffContracts} />
            <Metric label="อยู่ในระยะ + มีจำนวนคงเหลือ" value={counts.inTermOutstandingContracts} />
            <Metric label="อยู่ในระยะ + ชำระหมดแล้ว" value={counts.inTermPaidOffContracts} />
            <Metric label="หมดระยะ + มีจำนวนคงเหลือ" value={counts.expiredOutstandingContracts} />
            <Metric label="หมดระยะ + ชำระหมดแล้ว" value={counts.expiredPaidOffContracts} />
            <Metric label="คำเตือน" value={counts.warningContracts} />
            <Metric label="ยังเชื่อมสมาชิกไม่ได้" value={counts.unresolvedMemberContracts} />
            <Metric label="แถวแม่แบบ" value={counts.placeholderRows} />
            <Metric label="จับคู่ Canonical แล้ว" value={counts.canonicalMatchedContracts} />
            <Metric label="ไม่พบ Canonical" value={counts.missingCanonicalContracts} />
            <Metric label="Shadow ที่แยกออก" value={counts.shadowExcludedContracts} />
        </div>
    );
}

export default function PortfolioSnapshotsPage() {
    const { id } = useParams();
    const navigate = useNavigate();
    const { user } = useAuth();
    const snapshotId = id ? Number(id) : null;
    const [file, setFile] = useState<File | null>(null);
    const [asOfDate, setAsOfDate] = useState("2026-06-30");
    const [validation, setValidation] = useState<SnapshotValidation | null>(null);
    const [review, setReview] = useState<SnapshotReview | null>(null);
    const [snapshots, setSnapshots] = useState<SnapshotListItem[]>([]);
    const [rejectionReason, setRejectionReason] = useState("");
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [showPublishConfirmation, setShowPublishConfirmation] = useState(false);

    const refreshList = useCallback(async () => {
        setSnapshots(await listSnapshots());
    }, []);

    const refreshReview = useCallback(async (targetId: number) => {
        setReview(await getSnapshotReview(targetId));
    }, []);

    useEffect(() => {
        async function load() {
            try {
                await refreshList();
                if (snapshotId)
                    await refreshReview(snapshotId);
            } catch (loadError) {
                setError(snapshotApiError(loadError));
            }
        }
        void load();
    }, [refreshList, refreshReview, snapshotId]);

    async function run(action: () => Promise<void>) {
        setBusy(true);
        setError(null);
        try {
            await action();
        } catch (actionError) {
            setError(snapshotApiError(actionError));
        } finally {
            setBusy(false);
        }
    }

    function handleValidate() {
        if (!file) {
            setError("กรุณาเลือกไฟล์ Loan.xlsx");
            return;
        }
        void run(async () => {
            const result = await validateSnapshot(file, asOfDate);
            setValidation(result);
        });
    }

    function handleCreateDraft() {
        if (!file || !validation)
            return;
        void run(async () => {
            const draft = await createSnapshotDraft(
                file,
                asOfDate,
                validation.sourceFileHash,
                validation.snapshotContentHash,
            );
            await refreshList();
            navigate(`/portfolio-snapshots/${draft.id}`);
        });
    }

    function handleValidateDraft() {
        if (!review)
            return;
        void run(async () => {
            await validatePersistedDraft(review.snapshot.id);
            await refreshReview(review.snapshot.id);
            await refreshList();
        });
    }

    function handleReject() {
        if (!review)
            return;
        void run(async () => {
            await rejectSnapshot(review.snapshot.id, rejectionReason);
            setRejectionReason("");
            await refreshReview(review.snapshot.id);
            await refreshList();
        });
    }

    function handlePublish() {
        if (!review || busy)
            return;
        void run(async () => {
            await publishSnapshot(
                review.snapshot.id,
                review.snapshot.snapshotContentHash,
            );
            setShowPublishConfirmation(false);
            await refreshReview(review.snapshot.id);
            await refreshList();
        });
    }

    const counts = review?.counts ?? validation?.counts;
    const financial = review?.financial ?? validation?.financial;
    const isManager = user?.roles.includes("Manager") ?? false;

    return (
        <div className="space-y-6">
            <div>
                <h1 className="text-3xl font-bold text-slate-900">ตรวจทาน Portfolio Snapshot</h1>
                <p className="mt-2 text-slate-600">ตรวจสอบ สร้างฉบับร่าง และทบทวนข้อมูลโดยไม่เผยแพร่ไปยัง Dashboard</p>
            </div>

            {error ? <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-red-700">{error}</div> : null}

            <Card>
                <h2 className="text-xl font-semibold">1. ตรวจสอบไฟล์ต้นทาง</h2>
                <div className="mt-4 grid gap-4 md:grid-cols-2">
                    <label className="text-sm font-medium text-slate-700">
                        ไฟล์ Loan.xlsx
                        <input
                            className="mt-2 block w-full rounded-lg border border-slate-300 p-2"
                            type="file"
                            accept=".xlsx"
                            onChange={(event) => {
                                setFile(event.target.files?.[0] ?? null);
                                setValidation(null);
                            }}
                        />
                    </label>
                    <label className="text-sm font-medium text-slate-700">
                        วันที่ข้อมูล
                        <input
                            className="mt-2 block w-full rounded-lg border border-slate-300 p-2"
                            type="date"
                            value={asOfDate}
                            onChange={(event) => {
                                setAsOfDate(event.target.value);
                                setValidation(null);
                            }}
                        />
                    </label>
                </div>
                <div className="mt-4 flex gap-3">
                    <button className="rounded-lg bg-green-700 px-5 py-2 text-white disabled:opacity-50" type="button" disabled={busy} onClick={handleValidate}>
                        ตรวจสอบข้อมูล
                    </button>
                    <button
                        className="rounded-lg bg-blue-700 px-5 py-2 text-white disabled:opacity-40"
                        type="button"
                        disabled={busy || !validation?.isValid || !file}
                        onClick={handleCreateDraft}
                    >
                        สร้างฉบับร่าง
                    </button>
                </div>
                {validation ? (
                    <div className="mt-4 rounded-lg border border-green-200 bg-green-50 p-4 text-sm">
                        <p className="font-semibold">ผลการตรวจสอบ: {validation.isValid ? "ผ่าน" : "ไม่ผ่าน"}</p>
                        <p className="mt-2 break-all">Source hash: {validation.sourceFileHash}</p>
                        <p className="break-all">Content hash: {validation.snapshotContentHash}</p>
                        <p>Reconciliation: {validation.quality.reconciled ? "ครบถ้วน" : "ไม่ครบถ้วน"}</p>
                    </div>
                ) : null}
            </Card>

            {counts && financial ? (
                <>
                    <Card>
                        <h2 className="mb-4 text-xl font-semibold">จำนวนสัญญาและสถานะ</h2>
                        <CountsPanel counts={counts} />
                    </Card>
                    <Card>
                        <h2 className="text-xl font-semibold">ข้อมูลทางการเงิน</h2>
                        <div className="mt-4 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                            <MoneyMetric label="เงินต้นคงเหลือ" value={financial.principalOutstanding} />
                            <MoneyMetric label="ผลตอบแทนคงเหลือ" value={financial.profitOutstanding} />
                            <MoneyMetric label="ยอดคงเหลือรวม" value={financial.totalOutstanding} />
                            <MoneyMetric label="หมดระยะแต่ยังมียอดคงเหลือ" value={financial.expiredOutstandingTotal} />
                        </div>
                        <p className="mt-3 text-xs text-slate-500">จำนวนเงินแสดงด้วยทศนิยมสองตำแหน่ง: {moneyFormatter.format(financial.totalOutstanding)} บาท</p>
                    </Card>
                </>
            ) : null}

            {review ? (
                <>
                    <Card>
                        <div className="flex flex-wrap items-center justify-between gap-3">
                            <div>
                                <h2 className="text-xl font-semibold">ข้อมูล Snapshot #{review.snapshot.id}</h2>
                                <p className="text-sm text-slate-600">สถานะ: {review.snapshot.status} · วันที่ข้อมูล: {review.snapshot.asOfDate}</p>
                                <p className="mt-1 break-all text-xs text-slate-500">{review.snapshot.sourceFileName} · {review.snapshot.sourceFileHash}</p>
                                <p className="break-all text-xs text-slate-500">Content hash: {review.snapshot.snapshotContentHash}</p>
                                {review.snapshot.rejectionReason ? <p className="mt-2 text-sm text-red-700">เหตุผลที่ปฏิเสธ: {review.snapshot.rejectionReason}</p> : null}
                            </div>
                            {review.snapshot.status === "Draft" ? (
                                <button className="flex items-center gap-2 rounded-lg bg-green-700 px-4 py-2 text-white" type="button" disabled={busy} onClick={handleValidateDraft}>
                                    <CheckCircle2 size={18} /> ยืนยันผลตรวจสอบ
                                </button>
                            ) : null}
                        </div>
                    </Card>

                    <Card>
                        <h2 className="flex items-center gap-2 text-xl font-semibold"><AlertTriangle size={20} /> รายการคำเตือน</h2>
                        <div className="mt-4 space-y-5">
                            {review.warningSummary.map((warning) => (
                                <div key={warning.code}>
                                    <p className="font-semibold">{warning.code}: {countFormatter.format(warning.count)} รายการ</p>
                                    <div className="mt-2 overflow-x-auto">
                                        <table className="min-w-full text-sm">
                                            <thead className="bg-slate-50"><tr><th className="p-2 text-left">แถว Excel</th><th className="p-2 text-left">เลขที่สัญญา</th><th className="p-2 text-left">ระยะสัญญา</th><th className="p-2 text-left">ยอดคงเหลือ</th><th className="p-2 text-right">เงินต้นตั้งต้น</th><th className="p-2 text-right">ผลตอบแทนตั้งต้น</th><th className="p-2 text-right">ยอดตั้งต้นรวม</th><th className="p-2 text-right">ยอดคงเหลือรวม</th></tr></thead>
                                            <tbody>{warning.records.map((record) => <tr key={`${warning.code}-${record.sourceRowNumber}`} className="border-t"><td className="p-2">{record.sourceRowNumber}</td><td className="p-2">{record.contractNo}</td><td className="p-2">{record.termStatus}</td><td className="p-2">{record.balanceStatus}</td><td className="p-2 text-right">{moneyFormatter.format(record.principalOpening)}</td><td className="p-2 text-right">{moneyFormatter.format(record.profitOpening)}</td><td className="p-2 text-right">{moneyFormatter.format(record.totalOpening)}</td><td className="p-2 text-right">{moneyFormatter.format(record.totalOutstanding)}</td></tr>)}</tbody>
                                        </table>
                                    </div>
                                </div>
                            ))}
                        </div>
                    </Card>

                    <Card>
                        <h2 className="text-xl font-semibold">สัญญาที่ยังเชื่อมข้อมูลสมาชิกไม่ได้</h2>
                        <p className="mt-1 text-sm text-slate-600">ไม่อนุญาตให้เดาหรือแก้ไขสมาชิกใน Gate 2A</p>
                        <div className="mt-4 overflow-x-auto">
                            <table className="min-w-full text-sm">
                                <thead className="bg-slate-50"><tr><th className="p-2 text-left">แถว Excel</th><th className="p-2 text-left">เลขที่สัญญา</th><th className="p-2 text-left">ระยะสัญญา</th><th className="p-2 text-left">ยอดคงเหลือ</th></tr></thead>
                                <tbody>{review.unresolvedMembers.map((record) => <tr key={record.sourceRowNumber} className="border-t"><td className="p-2">{record.sourceRowNumber}</td><td className="p-2">{record.contractNo}</td><td className="p-2">{record.termStatus}</td><td className="p-2">{record.balanceStatus}</td></tr>)}</tbody>
                            </table>
                        </div>
                    </Card>

                    {review.snapshot.status === "Draft" || review.snapshot.status === "Validated" ? (
                        <Card>
                            <h2 className="flex items-center gap-2 text-xl font-semibold"><XCircle size={20} /> ปฏิเสธ Snapshot</h2>
                            <textarea className="mt-3 w-full rounded-lg border border-slate-300 p-3" value={rejectionReason} onChange={(event) => setRejectionReason(event.target.value)} placeholder="ระบุเหตุผลโดยไม่ใส่ข้อมูลส่วนบุคคล" />
                            <button className="mt-3 rounded-lg bg-red-700 px-4 py-2 text-white disabled:opacity-40" type="button" disabled={busy || rejectionReason.trim().length < 3} onClick={handleReject}>ปฏิเสธ</button>
                        </Card>
                    ) : null}

                    {isManager ? (
                        <Card className={review.canPublish ? "border-green-300 bg-green-50" : "border-amber-300 bg-amber-50"}>
                            <h2 className="flex items-center gap-2 text-xl font-semibold"><LockKeyhole size={20} /> การอนุมัติ Published Snapshot</h2>
                            <p className="mt-2 text-slate-700">Dashboard ใช้เฉพาะ Published Snapshot ปัจจุบันเท่านั้น สถานะ Draft หรือ Validated จะยังไม่เปลี่ยนข้อมูลบน Dashboard และ Snapshot เดิมจะเป็น Superseded เมื่อเผยแพร่ชุดใหม่สำเร็จ</p>
                            {!review.canPublish ? <p className="mt-2 text-sm text-amber-800">ยังเผยแพร่ไม่ได้: {review.publishBlockedReasons.join(", ")}</p> : null}
                            <button
                                className="mt-3 rounded-lg bg-green-700 px-4 py-2 text-white disabled:cursor-not-allowed disabled:bg-slate-300 disabled:text-slate-600"
                                type="button"
                                disabled={busy || !review.canPublish}
                                onClick={() => setShowPublishConfirmation(true)}
                            >
                                อนุมัติชุดข้อมูลนี้เป็น Published Snapshot
                            </button>
                        </Card>
                    ) : null}
                </>
            ) : null}

            {showPublishConfirmation && review ? (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4" role="dialog" aria-modal="true" aria-labelledby="publish-confirmation-title">
                    <div className="max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-xl bg-white p-6 shadow-2xl">
                        <h2 id="publish-confirmation-title" className="text-2xl font-bold text-slate-900">ยืนยันการอนุมัติ Published Snapshot</h2>
                        <p className="mt-2 text-slate-600">โปรดตรวจสอบข้อมูลสรุปก่อนยืนยัน ระบบจะตรวจสอบข้อมูลที่บันทึกไว้อีกครั้งบนเซิร์ฟเวอร์</p>
                        <dl className="mt-5 grid gap-3 text-sm sm:grid-cols-2">
                            <div><dt className="text-slate-500">วันที่ข้อมูล</dt><dd className="font-semibold">{review.snapshot.asOfDate}</dd></div>
                            <div><dt className="text-slate-500">จำนวนสัญญา</dt><dd className="font-semibold">{countFormatter.format(review.counts.totalContracts)}</dd></div>
                            <div><dt className="text-slate-500">Warning</dt><dd className="font-semibold">{countFormatter.format(review.counts.warningContracts)}</dd></div>
                            <div><dt className="text-slate-500">สมาชิกที่ยังเชื่อมโยงไม่ได้</dt><dd className="font-semibold">{countFormatter.format(review.counts.unresolvedMemberContracts)}</dd></div>
                            <div><dt className="text-slate-500">เงินต้นคงเหลือ</dt><dd className="font-semibold">{moneyFormatter.format(review.financial.principalOutstanding)} บาท</dd></div>
                            <div><dt className="text-slate-500">ผลตอบแทนคงเหลือ</dt><dd className="font-semibold">{moneyFormatter.format(review.financial.profitOutstanding)} บาท</dd></div>
                            <div className="sm:col-span-2"><dt className="text-slate-500">ยอดคงเหลือรวม</dt><dd className="text-lg font-bold">{moneyFormatter.format(review.financial.totalOutstanding)} บาท</dd></div>
                        </dl>
                        <div className="mt-5 rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">
                            การยืนยันนี้จะทำให้ Snapshot ชุดนี้เป็น Published และเป็นแหล่งข้อมูลรายงานของ Dashboard โดย Snapshot ที่ Published อยู่ก่อนหน้าจะเปลี่ยนเป็น Superseded
                        </div>
                        <div className="mt-6 flex flex-wrap justify-end gap-3">
                            <button className="rounded-lg border border-slate-300 px-4 py-2" type="button" disabled={busy} onClick={() => setShowPublishConfirmation(false)}>ยกเลิก</button>
                            <button className="rounded-lg bg-green-700 px-4 py-2 text-white disabled:opacity-50" type="button" disabled={busy} onClick={handlePublish}>
                                {busy ? "กำลังเผยแพร่..." : "ยืนยัน Published Snapshot"}
                            </button>
                        </div>
                    </div>
                </div>
            ) : null}

            <Card>
                <h2 className="flex items-center gap-2 text-xl font-semibold"><FileCheck2 size={20} /> รายการฉบับร่างและผลตรวจทาน</h2>
                <div className="mt-4 overflow-x-auto">
                    <table className="min-w-full text-sm">
                        <thead className="bg-slate-50"><tr><th className="p-2 text-left">ID</th><th className="p-2 text-left">วันที่ข้อมูล</th><th className="p-2 text-left">สถานะ</th><th className="p-2 text-left">ไฟล์</th><th className="p-2 text-right">คำเตือน</th><th className="p-2 text-right">ยอดคงเหลือ</th></tr></thead>
                        <tbody>{snapshots.map((snapshot) => <tr key={snapshot.id} className="border-t"><td className="p-2"><Link className="text-blue-700 underline" to={`/portfolio-snapshots/${snapshot.id}`}>#{snapshot.id}</Link></td><td className="p-2">{snapshot.asOfDate}</td><td className="p-2">{snapshot.status}</td><td className="p-2">{snapshot.sourceFileName}</td><td className="p-2 text-right">{countFormatter.format(snapshot.warningContracts)}</td><td className="p-2 text-right">{moneyFormatter.format(snapshot.totalOutstanding)}</td></tr>)}</tbody>
                    </table>
                </div>
            </Card>
        </div>
    );
}
