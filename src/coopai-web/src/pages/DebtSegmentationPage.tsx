import { useCallback, useEffect, useMemo, useState } from "react";
import { AlertTriangle, ArrowUpDown, CheckCircle2, ChevronLeft, ChevronRight, Database, RefreshCw } from "lucide-react";

import Card from "../components/ui/Card";
import PageHeader from "../components/ui/PageHeader";
import {
    debtPreviewError,
    debtSyncResultFromError,
    getDebtAutoSyncStatus,
    getDebtContracts,
    getDebtPreview,
    getInstallmentMasterAutoSyncStatus,
    getInstallmentMasterSnapshot,
    getMonthlyAmountDueContracts,
    getMonthlyCollection,
    getMonthlyPerformance,
    getWorkQueue,
    syncInstallmentMaster,
    syncDebtPreview,
    type DebtAutoSyncStatus,
    type DebtBucketPeriodComparison,
    type DebtContractFilters,
    type DebtContractPage,
    type DebtMovementSummary,
    type DebtPreviewDashboard,
    type DebtSyncStatus,
    type InstallmentMasterAutoSyncStatus,
    type InstallmentMasterSnapshot,
    type InstallmentMasterSyncStatus,
    type MonthlyAmountDueContractPage,
    type MonthlyCollectionDashboard,
    type MonthlyPerformancePeriod,
    type MonthlyPerformanceTrend,
    type WorkQueueOfficerSummary,
    type WorkQueuePage,
} from "../services/debtSegmentationService";
import { formatRemainingOrOverdueDuration } from "../utils/remainingDuration";
import {
    initialMonthlyContractTableQuery,
    monthlyContractPageSizes,
    remainingOrOverdueFilterOptions,
    resultRange,
    withContractSearch,
    withMonthlyContractPageSize,
    withRemainingOrOverdueFilter,
    type MonthlyContractPageSize,
    type RemainingOrOverdueFilter,
} from "../utils/monthlyContractTable";
import {
    initialWorkQueueQuery,
    updateWorkQueueFilter,
    updateWorkQueuePageSize,
    workQueuePageSizes,
    workQueueResultRange,
    type WorkQueuePageSize,
    type WorkQueueQuery,
} from "../utils/workQueueTable";
import {
    advanceCreditPresentation,
    contractMovementReconciles,
    formatThaiDataPeriod,
    missingDownPaymentEvidenceState,
    moneyMovementReconciles,
    workQueueMoneySemantics,
    workQueueTerms,
} from "../utils/managerPresentation";

const money = new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB", maximumFractionDigits: 2 });
const integer = new Intl.NumberFormat("th-TH");
const signedInteger = new Intl.NumberFormat("th-TH", { signDisplay: "always" });

type Filters = Pick<DebtContractFilters,
    "bucket" | "paymentStatus" | "loanType" | "groupCode" | "search" | "paymentMovement" |
    "previousBucket" | "currentBucket" | "movementCategory" | "branch" | "sortTotalDescending">;

const emptyContracts: DebtContractPage = {
    available: false, message: "", page: 1, pageSize: 50, totalCount: 0, items: [],
};
const emptyAmountDueContracts: MonthlyAmountDueContractPage = {
    available: false, message: "", page: 1, pageSize: 50, totalCount: 0, items: [],
};
const emptyWorkQueue: WorkQueuePage = {
    available: false,
    message: "",
    analysisId: null,
    debtSnapshotId: null,
    installmentSnapshotId: null,
    currentPeriod: "",
    summary: null,
    page: 1,
    pageSize: 50,
    totalCount: 0,
    items: [],
    generatedAtUtc: null,
};

export default function DebtSegmentationPage() {
    const [period, setPeriod] = useState("");
    const [dashboard, setDashboard] = useState<DebtPreviewDashboard | null>(null);
    const [contracts, setContracts] = useState<DebtContractPage>(emptyContracts);
    const [filters, setFilters] = useState<Filters>({ sortTotalDescending: true });
    const [page, setPage] = useState(1);
    const [loading, setLoading] = useState(true);
    const [syncing, setSyncing] = useState(false);
    const [syncResult, setSyncResult] = useState<DebtSyncStatus | null>(null);
    const [autoSync, setAutoSync] = useState<DebtAutoSyncStatus | null>(null);
    const [installmentAutoSync, setInstallmentAutoSync] = useState<InstallmentMasterAutoSyncStatus | null>(null);
    const [installmentSnapshot, setInstallmentSnapshot] = useState<InstallmentMasterSnapshot | null>(null);
    const [installmentSyncing, setInstallmentSyncing] = useState(false);
    const [installmentSyncResult, setInstallmentSyncResult] = useState<InstallmentMasterSyncStatus | null>(null);
    const [monthlyCollection, setMonthlyCollection] = useState<MonthlyCollectionDashboard | null>(null);
    const [monthlyPerformance, setMonthlyPerformance] = useState<MonthlyPerformanceTrend | null>(null);
    const [amountDueContracts, setAmountDueContracts] = useState<MonthlyAmountDueContractPage>(emptyAmountDueContracts);
    const [amountDueQuery, setAmountDueQuery] = useState(initialMonthlyContractTableQuery);
    const [workQueue, setWorkQueue] = useState<WorkQueuePage>(emptyWorkQueue);
    const [workQueueQuery, setWorkQueueQuery] = useState<WorkQueueQuery>(initialWorkQueueQuery);
    const [workQueueLoading, setWorkQueueLoading] = useState(false);
    const [error, setError] = useState("");

    const loadDashboard = useCallback(async () => {
        setLoading(true);
        setError("");
        try {
            const result = await getDebtPreview(period || undefined);
            setDashboard(result);
            if (!period) setPeriod(result.requestedCurrentPeriod.slice(0, 7));
            if (!result.available)
                setContracts({ ...emptyContracts, message: result.message });
        } catch (requestError) {
            setError(debtPreviewError(requestError));
        } finally {
            setLoading(false);
        }
    }, [period]);

    const loadContracts = useCallback(async () => {
        if (!dashboard?.available) return;
        try {
            setContracts(await getDebtContracts({ currentPeriod: period, ...filters, page, pageSize: 50 }));
        } catch (requestError) {
            setError(debtPreviewError(requestError));
        }
    }, [dashboard?.available, filters, page, period]);

    const loadMonthlyCollection = useCallback(async () => {
        if (!dashboard?.available || !period) return;
        try {
            const [snapshot, collection, details] = await Promise.all([
                getInstallmentMasterSnapshot(),
                getMonthlyCollection(period),
                getMonthlyAmountDueContracts({
                    currentPeriod: period,
                    remainingOrOverdueFilter: amountDueQuery.remainingOrOverdueFilter || undefined,
                    search: amountDueQuery.search.trim() || undefined,
                    page: amountDueQuery.page,
                    pageSize: amountDueQuery.pageSize,
                    sortRemainingMonthsDescending: amountDueQuery.sortRemainingMonthsDescending,
                }),
            ]);
            setInstallmentSnapshot(snapshot);
            setMonthlyCollection(collection);
            setAmountDueContracts(details);
        } catch (requestError) {
            setError(debtPreviewError(requestError));
        }
    }, [dashboard?.available, period, amountDueQuery]);

    const loadMonthlyPerformance = useCallback(async () => {
        if (!dashboard?.available) return;
        try {
            setMonthlyPerformance(await getMonthlyPerformance());
        } catch (requestError) {
            setError(debtPreviewError(requestError));
        }
    }, [dashboard?.available]);

    const loadWorkQueue = useCallback(async () => {
        if (!dashboard?.available || !period) return;
        setWorkQueueLoading(true);
        try {
            setWorkQueue(await getWorkQueue({
                currentPeriod: period,
                queueType: workQueueQuery.queueType || undefined,
                priority: workQueueQuery.priority || undefined,
                debtBucket: workQueueQuery.debtBucket || undefined,
                payment: workQueueQuery.payment || undefined,
                contractStatus: workQueueQuery.contractStatus || undefined,
                reason: workQueueQuery.reason || undefined,
                search: workQueueQuery.search.trim() || undefined,
                page: workQueueQuery.page,
                pageSize: workQueueQuery.pageSize,
            }));
        } catch (requestError) {
            setError(debtPreviewError(requestError));
        } finally {
            setWorkQueueLoading(false);
        }
    }, [dashboard?.available, period, workQueueQuery]);

    useEffect(() => { void loadDashboard(); }, [loadDashboard]);
    useEffect(() => { void loadContracts(); }, [loadContracts]);
    useEffect(() => { void loadMonthlyCollection(); }, [loadMonthlyCollection]);
    useEffect(() => { void loadMonthlyPerformance(); }, [loadMonthlyPerformance]);
    useEffect(() => { void loadWorkQueue(); }, [loadWorkQueue]);
    useEffect(() => {
        async function refreshAutoSync() {
            try {
                const [debtStatus, installmentStatus] = await Promise.all([
                    getDebtAutoSyncStatus(), getInstallmentMasterAutoSyncStatus(),
                ]);
                setAutoSync(debtStatus);
                setInstallmentAutoSync(installmentStatus);
            } catch { /* Dashboard remains usable. */ }
        }
        void refreshAutoSync();
        const timer = window.setInterval(() => void refreshAutoSync(), 15_000);
        return () => window.clearInterval(timer);
    }, []);
    useEffect(() => {
        if (autoSync?.lastSuccessfulSyncAtUtc) {
            void loadDashboard();
            void loadMonthlyPerformance();
            void loadWorkQueue();
        }
    }, [autoSync?.lastSuccessfulSyncAtUtc, loadDashboard, loadMonthlyPerformance, loadWorkQueue]);
    useEffect(() => {
        if (installmentAutoSync?.lastSuccessfulSyncAtUtc) {
            void loadMonthlyCollection();
            void loadWorkQueue();
        }
    }, [installmentAutoSync?.lastSuccessfulSyncAtUtc, loadMonthlyCollection, loadWorkQueue]);

    const hasFilters = useMemo(() => Object.entries(filters)
        .some(([key, value]) => key !== "sortTotalDescending" && Boolean(value)), [filters]);

    function updateFilters(next: Filters) {
        setFilters(next);
        setPage(1);
        document.getElementById("debt-contracts")?.scrollIntoView({ behavior: "smooth" });
    }

    async function syncNow() {
        setSyncing(true);
        setError("");
        setSyncResult(null);
        try {
            const result = await syncDebtPreview(period || undefined);
            setSyncResult(result);
            await loadDashboard();
            await loadWorkQueue();
        } catch (requestError) {
            const result = debtSyncResultFromError(requestError);
            setSyncResult(result);
            if (!result) setError(debtPreviewError(requestError));
        } finally {
            setSyncing(false);
        }
    }

    async function syncInstallmentsNow() {
        setInstallmentSyncing(true);
        setInstallmentSyncResult(null);
        setError("");
        try {
            const result = await syncInstallmentMaster();
            setInstallmentSyncResult(result);
            await loadMonthlyCollection();
            await loadWorkQueue();
        } catch (requestError) {
            setError(debtPreviewError(requestError));
        } finally {
            setInstallmentSyncing(false);
        }
    }

    function drillPayment(movement: DebtMovementSummary) {
        updateFilters({ paymentMovement: movement.paymentMovement });
    }

    function drillBucket(movement: DebtMovementSummary) {
        updateFilters({
            previousBucket: movement.previousBucket,
            currentBucket: movement.currentBucket,
            movementCategory: movement.category,
        });
    }

    return (
        <div className="space-y-6">
            <PageHeader title="รายการแยกหนี้" subtitle="สถานะและความเคลื่อนไหวหนี้รายเดือน · สำหรับตรวจทานข้อมูลเท่านั้น" />

            <Card className="border-amber-200 bg-amber-50">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                    <div className="flex items-start gap-3">
                        <Database className="mt-0.5 shrink-0 text-amber-700" size={21} />
                        <div>
                            <p className="font-semibold text-amber-950">ข้อมูลหนี้ที่ผ่านการตรวจสอบล่าสุด</p>
                            <p className="text-sm text-amber-800">
                                {dashboard?.sourceFileName || "กำลังตรวจสอบไฟล์"}
                                {dashboard?.sync?.sourceType ? ` · ${dashboard.sync.sourceType === "Network" ? "Network / เครือข่าย" : "Local"}` : ""}
                                {dashboard?.worksheetName ? ` · ชีต ${dashboard.worksheetName}` : ""}
                                {dashboard?.dataThroughPeriod ? ` · ข้อมูลปัจจุบัน ณ ${formatThaiDataPeriod(dashboard.dataThroughPeriod)}` : ""}
                            </p>
                            {dashboard?.sourceFileHash && (
                                <p className="mt-1 font-mono text-xs text-amber-700">SHA-256 {dashboard.sourceFileHash.slice(0, 16)}…</p>
                            )}
                            {dashboard?.sync && <SyncEvidence dashboard={dashboard} />}
                            {autoSync && <AutoSyncEvidence status={autoSync} />}
                        </div>
                    </div>
                    <div className="flex flex-wrap items-end gap-2">
                        <label className="text-sm font-medium text-slate-700">
                            เดือนปัจจุบัน
                            <input type="month" value={period}
                                onChange={(event) => {
                                    setPeriod(event.target.value);
                                    setFilters({});
                                    setPage(1);
                                    setWorkQueueQuery(initialWorkQueueQuery);
                                    setSyncResult(null);
                                }}
                                className="mt-1 block rounded-lg border border-slate-300 bg-white px-3 py-2" />
                        </label>
                        <button type="button" onClick={() => void syncNow()} disabled={syncing}
                            className="inline-flex items-center gap-2 rounded-lg bg-green-700 px-4 py-2.5 font-medium text-white hover:bg-green-800 disabled:opacity-50">
                            <RefreshCw size={18} className={syncing ? "animate-spin" : ""} /> {syncing ? "กำลังตรวจสอบไฟล์และซิงก์..." : "ซิงก์ข้อมูลตอนนี้"}
                        </button>
                        <button type="button" onClick={() => void loadDashboard()}
                            className="rounded-lg border border-slate-300 bg-white p-2.5 text-slate-700 hover:bg-slate-50" aria-label="รีเฟรชหน้าจอ">
                            <RefreshCw size={18} className={loading ? "animate-spin" : ""} />
                        </button>
                    </div>
                </div>
            </Card>

            <Card className="border-blue-200 bg-blue-50">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                    <div className="flex items-start gap-3">
                        <Database className="mt-0.5 shrink-0 text-blue-700" size={21} />
                        <div>
                            <p className="font-semibold text-blue-950">Installment Master / ข้อมูลจำนวนงวดและยอดชำระต่องวด</p>
                            <p className="text-sm text-blue-800">
                                {installmentSnapshot?.sourceFileName || installmentAutoSync?.sourceFileName || "2534-2569.xlsx"}
                                {installmentAutoSync ? ` · ${installmentAutoSync.sourceType}` : ""}
                                {installmentSnapshot?.available ? ` · ${integer.format(installmentSnapshot.contractCount)} สัญญา` : ""}
                            </p>
                            {installmentSnapshot?.sourceFileHash && <p className="mt-1 font-mono text-xs text-blue-700">
                                SHA-256 {installmentSnapshot.sourceFileHash.slice(0, 16)}…
                            </p>}
                            {installmentAutoSync && <InstallmentAutoSyncEvidence status={installmentAutoSync} />}
                            {installmentSnapshot?.available && <p className="mt-1 text-xs text-blue-800">
                                รูปแบบ {installmentSnapshot.schemaMode} · ใช้งานได้ {integer.format(installmentSnapshot.validContractCount)} · ต้องตรวจทาน {integer.format(installmentSnapshot.invalidContractCount)} · ซ้ำ {integer.format(installmentSnapshot.duplicateContractNumberGroups)} กลุ่ม
                            </p>}
                            {installmentSyncResult && <p className="mt-2 text-sm text-blue-900">{installmentSyncResult.message}</p>}
                        </div>
                    </div>
                    <button type="button" onClick={() => void syncInstallmentsNow()} disabled={installmentSyncing}
                        className="inline-flex items-center gap-2 rounded-lg bg-blue-700 px-4 py-2.5 font-medium text-white hover:bg-blue-800 disabled:opacity-50">
                        <RefreshCw size={18} className={installmentSyncing ? "animate-spin" : ""} />
                        {installmentSyncing ? "กำลังซิงก์ Installment Master..." : "ซิงก์ Installment Master ตอนนี้"}
                    </button>
                </div>
            </Card>

            {error && <Notice message={error} />}
            {(syncing || syncResult) && <SyncResultNotice syncing={syncing} result={syncResult} />}
            {!loading && dashboard && !dashboard.available && <Unavailable dashboard={dashboard} />}

            {dashboard?.available && <WorkQueueSection
                queue={workQueue}
                query={workQueueQuery}
                loading={workQueueLoading}
                onQueryChange={setWorkQueueQuery}
            />}

            {monthlyCollection?.available && monthlyCollection.totals && (
                <section className="space-y-4">
                    <div>
                        <h2 className="text-xl font-semibold text-slate-900">ยอดที่ต้องชำระประจำเดือน / Monthly Collection</h2>
                        <p className="text-sm text-slate-500">คำนวณจาก Debt Snapshot และ Installment Master Snapshot ล่าสุด โดยไม่เปลี่ยนกลุ่มหนี้หรือสถานะการชำระเดิม</p>
                    </div>
                    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
                        <Metric label="ยอดที่ต้องชำระ" value={money.format(monthlyCollection.totals.amountDue)} />
                        <Metric label="รับชำระจริง" value={money.format(monthlyCollection.totals.actualPayment)} />
                        <Metric label="ขาดชำระ" value={money.format(monthlyCollection.totals.shortfall)} />
                        {monthlyCollection.paymentAllocation && <Metric label="เครดิตล่วงหน้าเกิดใหม่"
                            value={money.format(monthlyCollection.paymentAllocation.newAdvanceCreditAmount)} />}
                    </div>
                    {monthlyCollection.paymentAllocation && <Card className="border-amber-200 bg-amber-50">
                        <div className="flex gap-3"><AlertTriangle className="mt-0.5 shrink-0 text-amber-700" size={20} /><div className="w-full">
                            <h3 className="font-semibold text-amber-950">การจัดสรรยอดรับชำระ / Payment Allocation V1.1</h3>
                            <p className="mt-1 text-sm text-amber-900">ยอดชำระเกินเปลี่ยนเป็นเครดิตชำระล่วงหน้าและยกไปหักภาระเก่าที่สุดในงวดถัดไป โดยไม่ลดหรือเขียนทับยอดที่ต้องชำระตามสัญญา · รอตรวจสอบการจัดสรร {integer.format(monthlyCollection.paymentAllocation.unclassifiedPaymentContracts)} สัญญา ({money.format(monthlyCollection.paymentAllocation.unclassifiedPaymentAmount)})</p>
                            <AdvanceCreditBreakdown allocation={monthlyCollection.paymentAllocation} />
                            <div className="mt-3 grid gap-2 text-sm text-slate-700 sm:grid-cols-2 lg:grid-cols-4">
                                <span>เงินดาวน์ {money.format(monthlyCollection.paymentAllocation.downPaymentAmount)} ({integer.format(monthlyCollection.paymentAllocation.downPaymentContracts)})</span>
                                <span>เครดิตล่วงหน้ายกมา {money.format(monthlyCollection.paymentAllocation.priorAdvanceCreditAmount)} ({integer.format(monthlyCollection.paymentAllocation.priorAdvanceCreditContracts)})</span>
                                <span>ใช้เครดิตล่วงหน้า {money.format(monthlyCollection.paymentAllocation.advanceCreditAppliedAmount)} ({integer.format(monthlyCollection.paymentAllocation.advanceCreditAppliedContracts)})</span>
                                <span>เครดิตล่วงหน้าคงเหลือ {money.format(monthlyCollection.paymentAllocation.endingAdvanceCreditAmount)} ({integer.format(monthlyCollection.paymentAllocation.endingAdvanceCreditContracts)})</span>
                                <span>ชำระยอดค้าง {money.format(monthlyCollection.paymentAllocation.paymentToPriorArrears)}</span>
                                <span>ชำระงวดปัจจุบัน {money.format(monthlyCollection.paymentAllocation.paymentToCurrentDue)}</span>
                                <span>ปิดหนี้ก่อนกำหนด {money.format(monthlyCollection.paymentAllocation.earlyPayoffAmount)} ({integer.format(monthlyCollection.paymentAllocation.earlyPayoffContracts)})</span>
                                <span>ชำระปิดยอดสัญญาหมดอายุ {money.format(monthlyCollection.paymentAllocation.expiredPayoffAmount)} ({integer.format(monthlyCollection.paymentAllocation.expiredPayoffContracts)})</span>
                                <span>รอนโยบายเครดิตงวดก่อน {integer.format(monthlyCollection.paymentAllocation.priorCreditPolicyRequiredContracts)} สัญญา</span>
                            </div>
                        </div></div>
                    </Card>}
                    <Card>
                        <h3 className="mb-3 font-semibold text-slate-900">แยกตามภาระสัญญา</h3>
                        <div className="overflow-x-auto"><table className="min-w-full text-left text-sm">
                            <thead className="bg-slate-100 text-slate-600"><tr><Th>ประเภท</Th><Th>สัญญา</Th><Th>ยอดที่ต้องชำระ</Th><Th>รับจริง</Th><Th>ขาดชำระ</Th><Th>เกิน/ล่วงหน้า</Th></tr></thead>
                            <tbody className="divide-y">{monthlyCollection.breakdowns.map((item) => <tr key={item.category}>
                                <Td className="font-medium">{collectionCategoryLabel(item.category)}</Td><Td>{integer.format(item.contractCount)}</Td>
                                <Td>{money.format(item.amountDue)}</Td><Td>{money.format(item.actualPayment)}</Td><Td>{money.format(item.shortfall)}</Td>
                                <Td>{money.format(item.excessPayment + item.advancePayment)}</Td>
                            </tr>)}</tbody>
                        </table></div>
                        {monthlyCollection.dataQuality && <div className="mt-4 grid gap-2 text-sm text-slate-600 sm:grid-cols-2 lg:grid-cols-4">
                            <span>จับคู่ {integer.format(monthlyCollection.dataQuality.matchedContracts)} / {integer.format(monthlyCollection.dataQuality.debtContracts)} ({monthlyCollection.dataQuality.matchCoveragePercent.toFixed(2)}%)</span>
                            <span>ไม่พบใน Master {integer.format(monthlyCollection.dataQuality.unmatchedContracts)}</span>
                            <span>AmountDue ไม่ทราบ {integer.format(monthlyCollection.totals.unknownAmountDueContracts)}</span>
                            <span>คำเตือนตารางงวด {integer.format(monthlyCollection.dataQuality.scheduleInconsistencies)}</span>
                        </div>}
                    </Card>
                    <Card>
                        <h3 className="mb-1 font-semibold text-slate-900">รายละเอียดภาระชำระรายสัญญา</h3>
                        <p className="mb-3 text-sm text-slate-500">กรองและเรียงด้วยค่าจำนวนเดือนจริงก่อนแบ่งหน้า โดยไม่จำกัดเฉพาะ 50 รายการแรก</p>
                        <div className="mb-4 grid gap-2 md:grid-cols-[minmax(250px,1fr)_minmax(220px,1fr)_auto]">
                            <select value={amountDueQuery.remainingOrOverdueFilter}
                                onChange={(event) => setAmountDueQuery((query) => withRemainingOrOverdueFilter(
                                    query, event.target.value as RemainingOrOverdueFilter))}
                                aria-label="ตัวกรองงวดคงเหลือหรือเกินกำหนด"
                                className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm">
                                {remainingOrOverdueFilterOptions.map((option) => <option key={option.value || "all"} value={option.value}>{option.label}</option>)}
                            </select>
                            <input value={amountDueQuery.search}
                                onChange={(event) => setAmountDueQuery((query) => withContractSearch(query, event.target.value))}
                                placeholder="ค้นหาเลขที่สัญญา"
                                aria-label="ค้นหาเลขที่สัญญา"
                                className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm" />
                            <label className="flex items-center gap-2 text-sm text-slate-600">
                                แสดง
                                <select value={amountDueQuery.pageSize}
                                    onChange={(event) => setAmountDueQuery((query) => withMonthlyContractPageSize(
                                        query, Number(event.target.value) as MonthlyContractPageSize))}
                                    aria-label="จำนวนสัญญาต่อหน้า"
                                    className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm">
                                    {monthlyContractPageSizes.map((size) => <option key={size} value={size}>{size}</option>)}
                                </select>
                            </label>
                        </div>
                        <MonthlyAmountDueTable contracts={amountDueContracts}
                            sortDescending={amountDueQuery.sortRemainingMonthsDescending}
                            onToggleSort={() => setAmountDueQuery((query) => ({
                                ...query,
                                page: 1,
                                sortRemainingMonthsDescending: !query.sortRemainingMonthsDescending,
                            }))} />
                        <MonthlyAmountDuePagination contracts={amountDueContracts}
                            filtered={Boolean(amountDueQuery.remainingOrOverdueFilter || amountDueQuery.search.trim())}
                            onPrevious={() => setAmountDueQuery((query) => ({ ...query, page: Math.max(1, query.page - 1) }))}
                            onNext={() => setAmountDueQuery((query) => ({ ...query, page: query.page + 1 }))} />
                    </Card>
                </section>
            )}

            {monthlyPerformance?.available && monthlyPerformance.months.length > 0 && (
                <MonthlyPerformanceSection trend={monthlyPerformance} />
            )}

            {dashboard?.available && dashboard.dataQualityWarnings.length > 0 && (
                <Card className="border-amber-300 bg-amber-50">
                    <div className="flex gap-3">
                        <AlertTriangle className="mt-0.5 shrink-0 text-amber-700" size={20} />
                        <div>
                            <h2 className="font-semibold text-amber-950">ข้อสังเกตคุณภาพข้อมูล</h2>
                            {dashboard.dataQualityWarnings.map((warning) => (
                                <p key={warning.code} className="mt-1 text-sm text-amber-900">
                                    พบค่าที่ต้องตรวจทาน {integer.format(warning.factCount)} จุด ใน {integer.format(warning.affectedContractCount)} สัญญา โดยระบบเก็บค่าต้นทางไว้ครบถ้วน
                                </p>
                            ))}
                        </div>
                    </div>
                </Card>
            )}

            {dashboard?.available && dashboard.totals && (
                <>
                    <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-8">
                        <Metric label="จำนวนราย" value={integer.format(dashboard.totals.memberCount)} />
                        <Metric label="จำนวนสัญญา" value={integer.format(dashboard.totals.contractCount)} />
                        <Metric label="เงินต้นคงเหลือ" value={money.format(dashboard.totals.principalOutstanding ?? 0)} />
                        <Metric label="ผลตอบแทนคงเหลือ" value={money.format(dashboard.totals.profitOutstanding ?? 0)} />
                        <Metric label="ยอดรวมคงเหลือ" value={money.format(dashboard.totals.outstandingAmount)} />
                        <Metric label="มีชำระเดือนล่าสุด" value={integer.format(dashboard.totals.paidLatestMonthContracts)} />
                        <Metric label="ไม่ชำระเมื่อถึงกำหนด" value={integer.format(dashboard.totals.notPaidLatestMonthContracts)} />
                        <Metric label="ยังไม่ถึงกำหนดชำระ" value={integer.format(dashboard.totals.notDueLatestMonthContracts)} />
                    </section>

                    <section>
                        <h2 className="mb-3 text-xl font-semibold text-slate-800">สรุปกลุ่มหนี้</h2>
                        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                            {dashboard.buckets.map((bucket) => {
                                const comparison = dashboard.bucketComparisons?.find((item) => item.bucket === bucket.bucket);
                                return (
                                    <button key={bucket.bucket} type="button" onClick={() => updateFilters({ bucket: bucket.bucket })}
                                        className="rounded-xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:border-green-400 hover:shadow-md">
                                        <div className="flex items-start justify-between gap-3">
                                            <p className="font-semibold text-green-800">{bucket.bucketLabel ?? bucket.bucket}</p>
                                            <span className={`text-sm font-semibold ${(comparison?.contractDifference ?? 0) > 0 ? "text-red-700" : "text-green-700"}`}>
                                                {comparison ? signedInteger.format(comparison.contractDifference) : "—"} สัญญา
                                            </span>
                                        </div>
                                        <p className="mt-3 text-2xl font-bold text-slate-900">{integer.format(bucket.contractCount)} สัญญา</p>
                                        <p className="text-sm text-slate-600">{integer.format(bucket.memberCount)} ราย · {money.format(bucket.outstandingAmount)}</p>
                                        <p className={`mt-1 text-sm font-medium ${(comparison?.amountDifference ?? 0) > 0 ? "text-red-700" : (comparison?.amountDifference ?? 0) < 0 ? "text-green-700" : "text-slate-500"}`}>
                                            เปลี่ยนแปลงจากเดือนก่อน {comparison ? money.format(comparison.amountDifference) : "—"}
                                        </p>
                                        <div className="mt-3 grid grid-cols-3 gap-2 text-xs">
                                            <span className="rounded bg-green-50 px-2 py-1 text-green-800">มีชำระ {integer.format(bucket.paidContractCount)}</span>
                                            <span className="rounded bg-red-50 px-2 py-1 text-red-800">ไม่ชำระ {integer.format(bucket.notPaidContractCount)}</span>
                                            <span className="rounded bg-blue-50 px-2 py-1 text-blue-800">ยังไม่ถึงกำหนด {integer.format(bucket.notDueContractCount)}</span>
                                        </div>
                                    </button>
                                );
                            })}
                        </div>
                    </section>

                    <BucketComparisonTable rows={dashboard.bucketComparisons ?? []} onSelect={(row) => updateFilters({ bucket: row.bucket })} />

                    <section className="grid gap-6 xl:grid-cols-2">
                        <MovementTable title="ความเคลื่อนไหวการชำระ" rows={dashboard.paymentMovements} onSelect={drillPayment} payment />
                        <MovementTable title="ความเคลื่อนไหวกลุ่มหนี้" rows={dashboard.bucketMovements} onSelect={drillBucket} />
                    </section>

                    <Card className="scroll-mt-4">
                        <div id="debt-contracts" className="scroll-mt-4" />
                        <div className="mb-4 flex flex-col gap-3 xl:flex-row xl:items-end xl:justify-between">
                            <div>
                                <h2 className="text-xl font-semibold text-slate-800">รายละเอียดสมาชิกและสัญญา</h2>
                                <p className="text-sm text-slate-500">{integer.format(contracts.totalCount)} รายการ · คลิกยอดสรุปเพื่อดูรายการจริง</p>
                            </div>
                            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-7">
                                <select value={filters.bucket ?? ""} onChange={(e) => updateFilters({ ...filters, bucket: e.target.value || undefined })} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                    <option value="">ทุกกลุ่มหนี้</option>
                                    {dashboard.buckets.map((item) => <option key={item.bucket} value={item.bucket}>{item.bucketLabel ?? item.bucket}</option>)}
                                </select>
                                <select value={filters.paymentStatus ?? ""} onChange={(e) => updateFilters({ ...filters, paymentStatus: e.target.value || undefined })} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                    <option value="">การชำระทั้งหมด</option><option value="Not due">ยังไม่ถึงกำหนด</option><option value="Paid">มีชำระ</option><option value="Not paid">ไม่ชำระเมื่อถึงกำหนด</option><option value="Closed / paid off">ปิดสัญญา / ชำระหมด</option>
                                </select>
                                <select value={filters.loanType ?? ""} onChange={(e) => updateFilters({ ...filters, loanType: e.target.value || undefined })} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                    <option value="">ทุกประเภทสินเชื่อ</option>{dashboard.loanTypes.map((item) => <option key={item}>{item}</option>)}
                                </select>
                                {dashboard.branches.length > 0 && <select value={filters.branch ?? ""} onChange={(e) => updateFilters({ ...filters, branch: e.target.value || undefined })} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                    <option value="">ทุกสาขา</option>{dashboard.branches.map((item) => <option key={item}>{item}</option>)}
                                </select>}
                                <input value={filters.groupCode ?? ""} onChange={(e) => updateFilters({ ...filters, groupCode: e.target.value || undefined })} placeholder="รหัสกลุ่ม" className="rounded-lg border border-slate-300 px-3 py-2 text-sm" />
                                <input value={filters.search ?? ""} onChange={(e) => updateFilters({ ...filters, search: e.target.value || undefined })} placeholder="สมาชิก/ชื่อ/สัญญา" className="rounded-lg border border-slate-300 px-3 py-2 text-sm" />
                                <select value={filters.sortTotalDescending === false ? "asc" : "desc"} onChange={(e) => updateFilters({ ...filters, sortTotalDescending: e.target.value !== "asc" })} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                                    <option value="desc">ยอดรวม มาก → น้อย</option><option value="asc">ยอดรวม น้อย → มาก</option>
                                </select>
                            </div>
                        </div>
                        {hasFilters && <button type="button" onClick={() => updateFilters({ sortTotalDescending: filters.sortTotalDescending })} className="mb-3 text-sm font-medium text-green-700 hover:underline">ล้างตัวกรองรายการ</button>}
                        <ContractTable contracts={contracts} />
                        <div className="mt-4 flex items-center justify-end gap-3 text-sm">
                            <button type="button" disabled={page <= 1} onClick={() => setPage((value) => value - 1)} className="rounded-lg border p-2 disabled:opacity-40"><ChevronLeft size={18} /></button>
                            หน้า {page} / {Math.max(1, Math.ceil(contracts.totalCount / contracts.pageSize))}
                            <button type="button" disabled={page * contracts.pageSize >= contracts.totalCount} onClick={() => setPage((value) => value + 1)} className="rounded-lg border p-2 disabled:opacity-40"><ChevronRight size={18} /></button>
                        </div>
                    </Card>
                </>
            )}
        </div>
    );
}

function AdvanceCreditBreakdown({ allocation }: {
    allocation: NonNullable<MonthlyCollectionDashboard["paymentAllocation"]>;
}) {
    const presentation = advanceCreditPresentation(allocation);
    return <div className="mt-3 rounded-xl border border-amber-200 bg-white/70 p-4">
        <div className="flex flex-col gap-1 sm:flex-row sm:items-end sm:justify-between">
            <div>
                <p className="text-sm font-medium text-amber-950">เครดิตล่วงหน้าเกิดใหม่ทั้งหมดในงวด</p>
                <p className="text-xs text-amber-800">ยอดหลักตาม Payment Allocation V1.1</p>
            </div>
            <p className="text-2xl font-bold tabular-nums text-amber-950">{money.format(presentation.total)}</p>
        </div>
        <div className="mt-3 grid gap-2 text-sm text-slate-700 sm:grid-cols-2">
            <p>จากยอดชำระเกินหลังจัดสรร <span className="font-semibold tabular-nums">{money.format(presentation.fromAllocatedExcess)}</span></p>
            <p>จากการชำระก่อนถึงงวด <span className="font-semibold tabular-nums">{money.format(presentation.fromBeforeDue)}</span></p>
        </div>
        <p className={`mt-2 text-xs ${presentation.reconciles ? "text-green-700" : "text-red-700"}`}>
            {presentation.reconciles ? "ส่วนประกอบรวมตรงกับเครดิตล่วงหน้าเกิดใหม่" : "ส่วนประกอบไม่ตรงกับยอดเครดิตล่วงหน้าเกิดใหม่ — ต้องตรวจสอบ"}
            {` · ${integer.format(presentation.contractCount)} สัญญา`}
        </p>
    </div>;
}

const assignmentStatusSearchToken = (status: "UNASSIGNED" | "CONFLICT") =>
    `__ASSIGNMENT_STATUS__:${status}`;
const officerSearchToken = (officerName: string) => `__OFFICER__:${officerName}`;
const isAssignmentSearchToken = (search: string) =>
    search.startsWith("__ASSIGNMENT_STATUS__:") || search.startsWith("__OFFICER__:");
const visibleWorkQueueSearch = (search: string) => isAssignmentSearchToken(search) ? "" : search;
const activeOfficerName = (search: string) =>
    search.startsWith("__OFFICER__:") ? search.slice("__OFFICER__:".length) : "";
const activeAssignmentStatus = (search: string) =>
    search.startsWith("__ASSIGNMENT_STATUS__:")
        ? search.slice("__ASSIGNMENT_STATUS__:".length) as "UNASSIGNED" | "CONFLICT"
        : "";

function WorkQueueSection({ queue, query, loading, onQueryChange }: {
    queue: WorkQueuePage;
    query: WorkQueueQuery;
    loading: boolean;
    onQueryChange: (query: WorkQueueQuery) => void;
}) {
    const summary = queue.summary;
    const range = workQueueResultRange(queue.page, queue.pageSize, queue.totalCount);
    const pageCount = Math.max(1, Math.ceil(queue.totalCount / queue.pageSize));
    const updateFilter = <K extends Exclude<keyof WorkQueueQuery, "page" | "pageSize">>(
        key: K,
        value: WorkQueueQuery[K],
    ) => onQueryChange(updateWorkQueueFilter(query, key, value));
    const showAssignmentWorkList = (search: string) => {
        onQueryChange(updateWorkQueueFilter(query, "search", search));
        window.setTimeout(() => {
            document.getElementById("work-queue-today-banner")
                ?.scrollIntoView({ behavior: "smooth", block: "start" });
        }, 50);
    };
    const selectedOfficer = activeOfficerName(query.search);
    const selectedAssignmentStatus = activeAssignmentStatus(query.search);
    const selectedOfficerSummary = summary?.officers.find((item) => item.officerName === selectedOfficer);
    const hasFilters = Object.entries(query).some(([key, value]) =>
        key !== "page" && key !== "pageSize" && Boolean(value));
    const moneySemantics = summary ? workQueueMoneySemantics(summary) : null;
    const collectionPriorities = summary?.priorities.filter((item) => item.priority !== "REVIEW") ?? [];
    const reviewPriority = summary?.priorities.find((item) => item.priority === "REVIEW");
    const downPaymentEvidence = summary
        ? missingDownPaymentEvidenceState(summary.missingDownPaymentEvidenceContracts)
        : null;

    return <section id="today-work-queue" className="scroll-mt-4 space-y-4">
        <div className="flex flex-col gap-2 lg:flex-row lg:items-end lg:justify-between">
            <div>
                <h2 className="text-xl font-semibold text-slate-900">งานที่ต้องดำเนินการวันนี้ / Today's Work Queue</h2>
                <p className="text-sm text-slate-500">
                    รายการอ่านอย่างเดียวจาก Preview และ Payment Allocation ปัจจุบัน พร้อมผู้รับผิดชอบตามตารางมอบหมาย V1; ยังไม่มีการบันทึกผลติดตาม
                </p>
            </div>
            {queue.generatedAtUtc && <p className="text-xs text-slate-500">
                วิเคราะห์ {new Date(queue.generatedAtUtc).toLocaleString("th-TH")} · ข้อมูลปัจจุบัน ณ {formatThaiDataPeriod(queue.currentPeriod)}
            </p>}
        </div>

        {!queue.available && queue.message && <Card className="border-amber-200 bg-amber-50">
            <div className="flex gap-3 text-amber-900"><AlertTriangle className="shrink-0" size={20} /><p>{queue.message}</p></div>
        </Card>}

        {summary && <>
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                <WorkQueueMetric label={workQueueTerms.collection} count={summary.collectionContracts}
                    amount={moneySemantics!.collection.amount} amountLabel={moneySemantics!.collection.amountLabel}
                    tone="red" active={query.queueType === "COLLECTION"}
                    onClick={() => updateFilter("queueType", "COLLECTION")} />
                <WorkQueueMetric label={workQueueTerms.review} count={summary.reviewOnlyContracts}
                    amount={moneySemantics!.review.amount} amountLabel={moneySemantics!.review.amountLabel}
                    tone="amber" active={query.queueType === "DATA_REVIEW"}
                    onClick={() => updateFilter("queueType", "DATA_REVIEW")} />
                <WorkQueueMetric label={workQueueTerms.noPayment} count={summary.noPaymentContracts}
                    helper="ไม่มีเงินชำระสำหรับภาระงวดปัจจุบันตามกฎการจัดสรร"
                    tone="slate" active={query.reason === "NO_PAYMENT_CURRENT_PERIOD"}
                    onClick={() => updateFilter("reason", "NO_PAYMENT_CURRENT_PERIOD")} />
                <WorkQueueMetric label={workQueueTerms.currentShortfall} count={summary.currentShortfallContracts}
                    amount={moneySemantics!.currentShortfall.amount} amountLabel={moneySemantics!.currentShortfall.amountLabel}
                    helper="ภาระ AmountDue ยังชำระไม่ครบ และอาจรวมสัญญาที่ชำระบางส่วนแล้ว"
                    tone="slate" active={query.reason === "CURRENT_DUE_SHORTFALL"}
                    onClick={() => updateFilter("reason", "CURRENT_DUE_SHORTFALL")} />
            </div>

            <Card>
                <div className="flex flex-col gap-2 lg:flex-row lg:items-start lg:justify-between">
                    <div>
                        <h3 className="font-semibold text-slate-900">ผู้รับผิดชอบงานติดตามหนี้ / Officer Assignment V1</h3>
                        <p className="mt-1 text-xs text-slate-500">
                            อ้างอิงตารางมอบหมายตามรหัสกลุ่มจาก Source A · L03 และ L31 มีผู้รับผิดชอบซ้ำ จึงแสดง CONFLICT โดยไม่เดาแทนผู้จัดการ
                        </p>
                    </div>
                    <div className="flex flex-wrap gap-2 text-xs">
                        <span className="rounded-full bg-green-50 px-3 py-1 font-medium text-green-800">มอบหมายแล้ว {integer.format(summary.assignedContracts)}</span>
                        <button type="button" onClick={() => showAssignmentWorkList(assignmentStatusSearchToken("UNASSIGNED"))}
                            className={`rounded-full px-3 py-1 font-medium ${query.search === assignmentStatusSearchToken("UNASSIGNED") ? "bg-green-700 text-white" : "bg-slate-100 text-slate-700"}`}>
                            ยังไม่มอบหมาย {integer.format(summary.unassignedContracts)}
                        </button>
                        <button type="button" onClick={() => showAssignmentWorkList(assignmentStatusSearchToken("CONFLICT"))}
                            className={`rounded-full px-3 py-1 font-medium ${query.search === assignmentStatusSearchToken("CONFLICT") ? "bg-green-700 text-white" : "bg-amber-100 text-amber-800"}`}>
                            ข้อมูลซ้ำ {integer.format(summary.conflictContracts)}
                        </button>
                    </div>
                </div>
                <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                    {summary.officers.map((officer) => <WorkQueueOfficerCard key={officer.officerName} officer={officer}
                        active={query.search === officerSearchToken(officer.officerName)}
                        onClick={() => showAssignmentWorkList(officerSearchToken(officer.officerName))} />)}
                </div>
            </Card>

            <Card>
                <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-[1.15fr_.7fr_1fr_1fr]">
                    <div>
                        <h3 className="font-semibold text-slate-900">ลำดับความสำคัญงานติดตาม</h3>
                        <p className="mt-1 text-xs text-slate-500">ใช้จัดลำดับเฉพาะงานติดตามเรียกเก็บ</p>
                        <div className="mt-3 grid gap-2 sm:grid-cols-3 xl:grid-cols-1">
                            {collectionPriorities.map((item) => <button key={item.priority} type="button"
                                onClick={() => updateFilter("priority", item.priority)}
                                className={`rounded-lg border px-3 py-2 text-left text-sm transition ${query.priority === item.priority ? "border-green-600 bg-green-50 ring-1 ring-green-600" : "border-slate-200 hover:border-green-400"}`}>
                                <span className={`font-semibold ${workQueuePriorityText(item.priority)}`}>{workQueuePriorityLabel(item.priority)}</span>
                                <span className="mt-1 block text-slate-700">{integer.format(item.contractCount)} สัญญา</span>
                                <span className="block text-xs text-slate-500">ยอดหนี้คงเหลือ {money.format(item.outstandingAmount)}</span>
                            </button>)}
                        </div>
                    </div>
                    <div>
                        <h3 className="font-semibold text-amber-900">งานตรวจสอบข้อมูล</h3>
                        <p className="mt-1 text-xs text-slate-500">ไม่ใช่ระดับความรุนแรงของหนี้</p>
                        {reviewPriority && <button type="button" onClick={() => updateFilter("priority", "REVIEW")}
                            className={`mt-3 w-full rounded-lg border border-amber-200 bg-amber-50 px-3 py-3 text-left text-sm transition hover:border-amber-400 ${query.priority === "REVIEW" ? "ring-2 ring-green-600" : ""}`}>
                            <span className="font-semibold text-amber-800">ตรวจสอบข้อมูล</span>
                            <span className="mt-1 block text-slate-700">{integer.format(reviewPriority.contractCount)} สัญญา</span>
                            <span className="block text-xs text-slate-500">ยอดหนี้คงเหลือของสัญญาที่รอตรวจ {money.format(reviewPriority.outstandingAmount)}</span>
                        </button>}
                    </div>
                    <div className="text-sm text-slate-700">
                        <h3 className="font-semibold text-slate-900">เหตุผลที่สัญญาเข้าคิวติดตาม</h3>
                        <dl className="mt-3 space-y-2">
                            <WorkQueueEvidence label="มียอดค้างเดิมที่ทราบ" count={summary.priorArrearsContracts}
                                amount={summary.priorArrearsAmount} amountLabel="ยอดค้างเดิมรวม" />
                            <WorkQueueEvidence label="สัญญาหมดอายุและยังมียอดหนี้" count={summary.expiredOutstandingContracts}
                                amount={summary.expiredOutstandingAmount} amountLabel="ยอดหนี้คงเหลือรวม" />
                            {summary.longTermOverdue.filter((item) => item.contractCount > 0).map((item) =>
                                <WorkQueueEvidence key={item.debtBucket} label={`อายุหนี้ ${item.debtBucketLabel}`} count={item.contractCount}
                                    amount={item.outstandingAmount} amountLabel="ยอดหนี้คงเหลือรวม" />)}
                        </dl>
                        <p className="mt-3 text-xs text-slate-500">หนึ่งสัญญาอาจมีหลายเหตุผล จึงไม่ควรรวมจำนวนในส่วนนี้เข้าด้วยกัน</p>
                    </div>
                    <div className="text-sm text-slate-700">
                        <h3 className="font-semibold text-slate-900">เหตุผลที่ต้องตรวจสอบข้อมูล</h3>
                        <dl className="mt-3 space-y-2">
                            <WorkQueueEvidence label="ยอดชำระที่ยังจำแนกไม่ได้" count={summary.unclassifiedReviewContracts}
                                amount={summary.unclassifiedReviewAmount} amountLabel="ยอดชำระรอตรวจรวม" />
                            <WorkQueueEvidence label="ประวัติยอดค้างเดิมยังไม่ครบ (ไม่ทราบ ≠ ศูนย์)" count={summary.unknownPriorArrearsContracts} />
                            {downPaymentEvidence && !downPaymentEvidence.resolved && <WorkQueueEvidence label={downPaymentEvidence.label}
                                count={summary.missingDownPaymentEvidenceContracts} amount={summary.missingDownPaymentEvidenceAmount}
                                amountLabel="ยอดชำระที่รอหลักฐาน" />}
                        </dl>
                        {downPaymentEvidence?.resolved && <p className="mt-3 flex items-center gap-2 rounded-lg bg-green-50 p-2 text-xs font-medium text-green-800">
                            <CheckCircle2 size={16} /> {downPaymentEvidence.label}
                        </p>}
                        <p className="mt-3 rounded-lg bg-green-50 p-2 text-xs text-green-800">
                            ตัวตรวจสอบ false-positive: ชำระหมด {summary.paidOffCollectionContracts} · ยังไม่ถึงงวด {summary.notDueFalseNoPaymentContracts} · เครดิตครอบคลุมเต็ม {summary.fullyCreditCoveredFalseNoPaymentContracts}
                        </p>
                    </div>
                </div>
            </Card>
        </>}

        {(selectedOfficer || selectedAssignmentStatus) && <Card className="border-green-200 bg-green-50">
            <div id="work-queue-today-banner" className="scroll-mt-4 flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-wide text-green-700">Today's Officer Work List</p>
                    {selectedOfficer && <>
                        <h3 className="mt-1 text-lg font-semibold text-slate-900">งานวันนี้ของ {selectedOfficer}</h3>
                        <p className="mt-1 text-sm text-slate-600">
                            แสดงเฉพาะงานที่มอบหมายให้เจ้าหน้าที่คนนี้ เรียงตาม Urgent - High - Medium แล้วตามความรุนแรงของกลุ่มหนี้
                        </p>
                    </>}
                    {selectedAssignmentStatus === "UNASSIGNED" && <>
                        <h3 className="mt-1 text-lg font-semibold text-slate-900">งานที่ยังไม่มอบหมาย</h3>
                        <p className="mt-1 text-sm text-slate-600">รอเจ้าหน้าที่ตรวจและอัปเดตรหัสกลุ่มใน Excel ต้นทาง</p>
                    </>}
                    {selectedAssignmentStatus === "CONFLICT" && <>
                        <h3 className="mt-1 text-lg font-semibold text-slate-900">งานที่มีผู้รับผิดชอบซ้ำ</h3>
                        <p className="mt-1 text-sm text-slate-600">ยังไม่เลือกเจ้าหน้าที่แทนผู้จัดการ จนกว่าจะยืนยันกฎ L03 / L31</p>
                    </>}
                </div>
                <div className="flex flex-wrap gap-2 text-sm">
                    {selectedOfficerSummary && <>
                        <span className="rounded-full bg-white px-3 py-1 font-medium text-slate-700">
                            ทั้งหมด {integer.format(selectedOfficerSummary.contractCount)}
                        </span>
                        <span className="rounded-full bg-red-100 px-3 py-1 font-medium text-red-700">
                            เร่งด่วน {integer.format(selectedOfficerSummary.urgentContracts)}
                        </span>
                        <span className="rounded-full bg-orange-100 px-3 py-1 font-medium text-orange-700">
                            สูง {integer.format(selectedOfficerSummary.highContracts)}
                        </span>
                    </>}
                    <span className="rounded-full bg-white px-3 py-1 font-medium text-slate-700">
                        รายการที่ตรงตัวกรอง {integer.format(queue.totalCount)}
                    </span>
                    <button type="button"
                        onClick={() => onQueryChange({ ...initialWorkQueueQuery, pageSize: query.pageSize })}
                        className="rounded-full border border-green-300 bg-white px-3 py-1 font-medium text-green-800 hover:bg-green-100">
                        กลับงานทั้งหมด
                    </button>
                </div>
            </div>
        </Card>}

        {queue.available && <Card>
            <div id="work-queue-contract-list" className="scroll-mt-4 mb-4 flex flex-col gap-3">
                <div className="flex flex-col gap-1 lg:flex-row lg:items-end lg:justify-between">
                    <div>
                        <h3 className="text-lg font-semibold text-slate-900">รายการงานรายสัญญา</h3>
                        <p className="text-sm text-slate-500">
                            แสดง {integer.format(range.first)}–{integer.format(range.last)} จาก {integer.format(queue.totalCount)} รายการที่ตรงตัวกรอง
                        </p>
                    </div>
                    {queue.analysisId && <p className="font-mono text-xs text-slate-400">Analysis {queue.analysisId}</p>}
                </div>
                <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-8">
                    <select aria-label="ประเภทคิว" value={query.queueType} onChange={(event) => updateFilter("queueType", event.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        <option value="">ทุกประเภทงาน</option><option value="COLLECTION">งานติดตามเรียกเก็บ</option><option value="DATA_REVIEW">งานตรวจสอบข้อมูล</option>
                    </select>
                    <select aria-label="ลำดับความสำคัญ" value={query.priority} onChange={(event) => updateFilter("priority", event.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        <option value="">ทุกลำดับ</option><option value="URGENT">เร่งด่วน</option><option value="HIGH">สูง</option><option value="MEDIUM">ปานกลาง</option><option value="REVIEW">ตรวจสอบข้อมูล (ไม่ใช่ระดับหนี้)</option>
                    </select>
                    <select aria-label="กลุ่มหนี้" value={query.debtBucket} onChange={(event) => updateFilter("debtBucket", event.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        <option value="">ทุกกลุ่มหนี้</option>
                        {["Normal", "1 month delinquent", "2 months delinquent", "3–6 months delinquent", "7–12 months delinquent", ">1 to 3 years", ">3 to 5 years", ">5 to 10 years", "10 years and above"].map((bucket) =>
                            <option key={bucket} value={bucket}>{thaiBucket(bucket)}</option>)}
                    </select>
                    <select aria-label="สถานะการชำระ" value={query.payment} onChange={(event) => updateFilter("payment", event.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        <option value="">ทุกการชำระ</option><option value="NO_PAYMENT">ไม่ชำระ</option><option value="PARTIAL_PAYMENT">ชำระบางส่วน</option><option value="HAS_PAYMENT">มีการชำระ</option>
                    </select>
                    <select aria-label="สถานะสัญญา" value={query.contractStatus} onChange={(event) => updateFilter("contractStatus", event.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        <option value="">ทุกสถานะสัญญา</option><option value="InTerm">อยู่ในสัญญา</option><option value="Expired">หมดอายุ</option><option value="NotDue">ยังไม่ถึงงวด</option>
                    </select>
                    <select aria-label="เหตุผล" value={query.reason} onChange={(event) => updateFilter("reason", event.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        <option value="">ทุกเหตุผล</option>
                        {workQueueReasonOptions.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                    </select>
                    <input aria-label="ค้นหาสมาชิก สัญญา กลุ่ม หรือเจ้าหน้าที่" value={visibleWorkQueueSearch(query.search)}
                        onChange={(event) => updateFilter("search", event.target.value)} placeholder="สมาชิก/ชื่อ/สัญญา/กลุ่ม/เจ้าหน้าที่"
                        className="rounded-lg border border-slate-300 px-3 py-2 text-sm" />
                    <select aria-label="จำนวนรายการต่อหน้า" value={query.pageSize}
                        onChange={(event) => onQueryChange(updateWorkQueuePageSize(query, Number(event.target.value) as WorkQueuePageSize))}
                        className="rounded-lg border border-slate-300 px-3 py-2 text-sm">
                        {workQueuePageSizes.map((size) => <option key={size} value={size}>{size} รายการ/หน้า</option>)}
                    </select>
                </div>
                {hasFilters && <button type="button" onClick={() => onQueryChange(initialWorkQueueQuery)}
                    className="self-start text-sm font-medium text-green-700 hover:underline">ล้างตัวกรอง Work Queue</button>}
            </div>

            {loading ? <div className="flex items-center justify-center gap-2 py-12 text-slate-500"><RefreshCw className="animate-spin" size={18} /> กำลังโหลด Work Queue…</div>
                : <WorkQueueTable queue={queue} />}

            <div className="mt-4 flex flex-wrap items-center justify-between gap-3 text-sm text-slate-600">
                <span>หน้า {queue.page} / {pageCount}</span>
                <div className="flex items-center gap-2">
                    <button type="button" aria-label="หน้าก่อนหน้าของ Work Queue" disabled={query.page <= 1 || loading}
                        onClick={() => onQueryChange({ ...query, page: query.page - 1 })}
                        className="rounded-lg border border-slate-300 p-2 disabled:opacity-40"><ChevronLeft size={18} /></button>
                    <button type="button" aria-label="หน้าถัดไปของ Work Queue" disabled={query.page >= pageCount || loading}
                        onClick={() => onQueryChange({ ...query, page: query.page + 1 })}
                        className="rounded-lg border border-slate-300 p-2 disabled:opacity-40"><ChevronRight size={18} /></button>
                </div>
            </div>
        </Card>}
    </section>;
}

function WorkQueueMetric({ label, count, amount, amountLabel, helper, tone, active, onClick }: {
    label: string;
    count: number;
    amount?: number;
    amountLabel?: string;
    helper?: string;
    tone: "red" | "amber" | "slate";
    active: boolean;
    onClick: () => void;
}) {
    const tones = {
        red: "border-red-200 bg-red-50 text-red-900",
        amber: "border-amber-200 bg-amber-50 text-amber-900",
        slate: "border-slate-200 bg-white text-slate-900",
    };
    return <button type="button" onClick={onClick}
        className={`rounded-xl border p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:shadow ${tones[tone]} ${active ? "ring-2 ring-green-600" : ""}`}>
        <span className="text-sm">{label}</span>
        <span className="mt-1 block text-2xl font-bold">{integer.format(count)} <span className="text-sm font-medium">สัญญา</span></span>
        {amount != null && <span className="mt-1 block text-sm"><span className="font-medium">{amountLabel}:</span> {money.format(amount)}</span>}
        {helper && <span className="mt-2 block text-xs leading-relaxed opacity-75">{helper}</span>}
    </button>;
}

function WorkQueueEvidence({ label, count, amount, amountLabel }: { label: string; count: number; amount?: number; amountLabel?: string }) {
    return <div className="flex items-start justify-between gap-3 border-b border-slate-100 pb-2 last:border-0">
        <dt>{label}</dt><dd className="text-right font-medium">{integer.format(count)} สัญญา{amount != null && <span className="block text-xs font-normal text-slate-500">{amountLabel}: {money.format(amount)}</span>}</dd>
    </div>;
}

function WorkQueueOfficerCard({ officer, active, onClick }: {
    officer: WorkQueueOfficerSummary;
    active: boolean;
    onClick: () => void;
}) {
    return <button type="button" onClick={onClick}
        className={`rounded-xl border p-3 text-left transition hover:border-green-400 hover:shadow-sm ${active ? "border-green-600 bg-green-50 ring-1 ring-green-600" : "border-slate-200 bg-white"}`}>
        <span className="font-semibold text-slate-900">{officer.officerName}</span>
        <span className="mt-2 block text-xl font-bold text-slate-900">{integer.format(officer.contractCount)} <span className="text-xs font-medium text-slate-500">สัญญา</span></span>
        <span className="block text-xs text-slate-500">ยอดหนี้คงเหลือ {money.format(officer.outstandingAmount)}</span>
        <span className="mt-2 block text-xs text-slate-600">เร่งด่วน {integer.format(officer.urgentContracts)} · สูง {integer.format(officer.highContracts)}</span>
    </button>;
}

function WorkQueueTable({ queue }: { queue: WorkQueuePage }) {
    if (queue.items.length === 0)
        return <p className="py-10 text-center text-sm text-slate-500">ไม่พบรายการที่ตรงตัวกรอง</p>;
    return <div className="overflow-x-auto"><table className="min-w-[2850px] text-left text-sm">
        <thead className="bg-slate-100 text-slate-600"><tr>
            <Th>ลำดับ</Th><Th>สมาชิก / สัญญา</Th><Th>กลุ่ม / เจ้าหน้าที่</Th><Th>เหตุผลดำเนินการ</Th><Th>ขาดชำระ</Th><Th>อายุสัญญา / เลยสัญญา</Th>
            <Th>ยอดหนี้ยกมา</Th><Th>ยอดหนี้คงเหลือปลายงวด</Th><Th>ค่างวด / ภาระ AmountDue</Th><Th>ยอดรับชำระจริง</Th><Th>ยอดค้างเดิมที่ทราบ</Th>
            <Th>เครดิตล่วงหน้า</Th><Th>ยอดรับชำระที่จัดสรร</Th><Th>ยอดขาดชำระ / ยอดรอตรวจ</Th><Th>คุณภาพข้อมูล</Th>
        </tr></thead>
        <tbody className="divide-y">{queue.items.map((item) => <tr key={item.contractNo} className={item.queueType === "DATA_REVIEW" ? "bg-amber-50/40" : "hover:bg-slate-50"}>
            <Td><span className={`font-semibold ${workQueuePriorityText(item.priority)}`}>{workQueuePriorityLabel(item.priority)}</span>
                <span className="block text-xs text-slate-500">{item.queueType === "COLLECTION" ? "ติดตามเรียกเก็บ" : "ตรวจสอบข้อมูล"}</span></Td>
            <Td><span className="font-semibold text-slate-900">{item.contractNo}</span><span className="block text-slate-600">{item.memberNo || "—"}</span><span className="block max-w-64 whitespace-normal text-xs text-slate-500">{item.memberName || "—"}</span></Td>
            <Td><span className="font-semibold text-slate-900">{item.groupCode || "ไม่พบกลุ่ม"}</span>
                {item.assignmentStatus === "ASSIGNED"
                    ? <><span className="block max-w-64 whitespace-normal text-xs font-medium text-green-700">{item.officerName}</span><span className="block text-xs text-slate-500">{item.assignmentRule}</span></>
                    : item.assignmentStatus === "CONFLICT"
                        ? <><span className="block text-xs font-semibold text-amber-700">CONFLICT</span><span className="block max-w-72 whitespace-normal text-xs text-amber-700">{item.officerCandidates.join(" / ")}</span><span className="block text-xs text-slate-500">{item.assignmentRule}</span></>
                        : <><span className="block text-xs font-semibold text-slate-500">UNASSIGNED</span><span className="block max-w-64 whitespace-normal text-xs text-slate-500">{item.assignmentRule}</span></>}
            </Td>
            <Td><span className="font-medium text-slate-900">{item.primaryReason}</span><div className="mt-1 flex max-w-96 flex-wrap gap-1">{item.reasonLabelsThai.map((label, index) => <span key={`${item.reasonCodes[index]}-${label}`} className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-700">{label}</span>)}</div></Td>
            <Td>
                <span className="text-xs font-medium text-slate-500">สถานะสัญญา: {collectionCategoryLabel(item.contractStatus)}</span>
                <span className="block text-[11px] font-medium text-slate-500">อายุของงวดค้างที่เก่าที่สุดซึ่งยังชำระไม่ครบ</span><span className="block font-semibold text-green-800">{item.currentDelinquencyLabel}</span>
                {item.currentDelinquencyMonths != null && <span className="block text-xs text-slate-500">
                    ขาดชำระ {formatYearsMonths(item.currentDelinquencyMonths)}
                </span>}
                
            </Td>
            <Td>
                {item.expiredAgeLabel
                    ? <><span className="block text-[11px] font-medium text-slate-500">เลยสัญญา = อายุสัญญาหลังวันสิ้นสุด</span><span className="font-semibold text-red-700">{item.expiredAgeLabel}</span>
                        <span className="block text-xs text-slate-500">อายุเลยสัญญา {formatYearsMonths(item.expiredAgeMonths)}</span></>
                    : <><span className="font-medium text-slate-700">{item.remainingOrOverdueLabel}</span>
                        <span className="block text-xs text-slate-500">{item.remainingOrOverdueMonths ?? "—"} เดือน</span></>}
            </Td>
            <Td>{money.format(item.openingOutstanding)}</Td><Td className="font-medium">{money.format(item.endingOutstanding)}</Td>
            <Td>{formatNullableMoney(item.monthlyInstallment)}<span className="block text-xs text-slate-500">Due {formatNullableMoney(item.amountDue)}</span></Td>
            <Td>{money.format(item.actualPayment)}</Td><Td>{formatNullableMoney(item.knownPriorArrears)}<span className="block text-xs text-slate-500">{item.priorArrearsStatus}</span></Td>
            <Td>ยกมา {money.format(item.priorAdvanceCredit)}<span className="block text-xs text-slate-500">ใช้ {money.format(item.advanceCreditApplied)} · เหลือ {money.format(item.endingAdvanceCredit)}</span></Td>
            <Td>ค้างเดิม {money.format(item.paymentToPriorArrears)}<span className="block text-xs text-slate-500">งวดนี้ {money.format(item.paymentToCurrentDue)}</span></Td>
            <Td>{formatNullableMoney(item.currentShortfall)}<span className="block text-xs text-amber-700">ยังจำแนกไม่ได้ {money.format(item.unclassifiedAmount)}</span></Td>
            <Td>{item.dataQualityWarnings.length === 0 ? <span className="text-green-700">ไม่มีคำเตือน</span>
                : <ul className="max-w-96 whitespace-normal text-xs text-amber-800">{item.dataQualityWarnings.map((warning) => <li key={warning}>• {warning}</li>)}</ul>}</Td>
        </tr>)}</tbody>
    </table></div>;
}

const workQueueReasonOptions: Array<[string, string]> = [
    ["NO_PAYMENT_CURRENT_PERIOD", "ไม่ชำระงวดปัจจุบัน"],
    ["CURRENT_DUE_SHORTFALL", "ชำระไม่ครบภาระงวดปัจจุบัน"],
    ["PRIOR_ARREARS", "มียอดค้างยกมา"],
    ["CONTRACT_EXPIRED_OUTSTANDING", "หมดอายุและยังมียอดหนี้"],
    ["LONG_TERM_OVERDUE", "ค้างชำระระยะยาว"],
    ["DATA_REVIEW_UNCLASSIFIED", "ยอดชำระที่ยังจำแนกไม่ได้"],
    ["UNKNOWN_PRIOR_ARREARS", "ประวัติยอดค้างเดิมยังไม่ครบ"],
    ["MISSING_DOWNPAYMENT_EVIDENCE", "ขาดหลักฐานเงินดาวน์จาก Source B"],
];

function workQueuePriorityLabel(priority: string) {
    return ({ URGENT: "เร่งด่วน", HIGH: "สูง", MEDIUM: "ปานกลาง", REVIEW: "ตรวจสอบข้อมูล" } as Record<string, string>)[priority] ?? priority;
}

function formatYearsMonths(totalMonths: number | null | undefined) {
    if (totalMonths == null) return "—";
    const safeMonths = Math.max(0, Math.trunc(totalMonths));
    if (safeMonths < 12) return `${integer.format(safeMonths)} เดือน`;

    const years = Math.floor(safeMonths / 12);
    const months = safeMonths % 12;
    return months === 0
        ? `${integer.format(years)} ปี`
        : `${integer.format(years)} ปี ${integer.format(months)} เดือน`;
}

function workQueuePriorityText(priority: string) {
    return priority === "URGENT" ? "text-red-700" : priority === "HIGH" ? "text-orange-700" : priority === "REVIEW" ? "text-amber-700" : "text-blue-700";
}

function SyncResultNotice({ syncing, result }: { syncing: boolean; result: DebtSyncStatus | null }) {
    if (syncing) {
        return <Card className="border-blue-200 bg-blue-50 text-blue-900">
            <div className="flex items-center gap-3"><RefreshCw className="animate-spin" size={20} /><span>กำลังตรวจสอบไฟล์และซิงก์ข้อมูล กรุณารอสักครู่...</span></div>
        </Card>;
    }
    if (!result) return null;
    const failed = result.status === "Failed";
    const busy = result.status === "Busy";
    const tone = failed ? "border-red-200 bg-red-50 text-red-900" : busy ? "border-amber-200 bg-amber-50 text-amber-900" : "border-green-200 bg-green-50 text-green-900";
    const title = result.status === "Success" ? "ซิงก์ข้อมูลสำเร็จ"
        : result.status === "NoChange" ? "ข้อมูลไม่เปลี่ยนแปลง"
            : result.status === "Busy" ? "กำลังซิงก์ข้อมูลอยู่"
                : "ซิงก์ไม่สำเร็จ — ใช้ข้อมูลล่าสุดที่ผ่านการตรวจสอบต่อ";
    return <Card className={tone}>
        <div className="flex gap-3">
            {failed || busy ? <AlertTriangle className="mt-0.5 shrink-0" size={20} /> : <CheckCircle2 className="mt-0.5 shrink-0" size={20} />}
            <div className="min-w-0">
                <h2 className="font-semibold">{title}</h2>
                <p className="mt-1 text-sm">{result.message}</p>
                <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs">
                    {result.attemptedAtUtc && <span>ตรวจสอบ {new Date(result.attemptedAtUtc).toLocaleString("th-TH")}</span>}
                    {result.sourceFileName && <span>ไฟล์ {result.sourceFileName}</span>}
                    {result.previousPeriod && result.currentPeriod && <span>{result.previousPeriod.slice(0, 7)} → {result.currentPeriod.slice(0, 7)}</span>}
                    <span>แถวผู้สมัคร {integer.format(result.candidateRows)}</span>
                    <span>รวม {integer.format(result.contractCount)} สัญญา / {integer.format(result.memberCount)} ราย</span>
                    <span>ตัดออก {integer.format(result.excludedRows)}</span>
                    <span>ยอดคงเหลือ {money.format(result.outstandingAmount)}</span>
                    <span>สัญญาใหม่ {integer.format(result.newContracts)}</span>
                    <span>ชำระหมด {integer.format(result.paidOffContracts)}</span>
                    <span>ใช้เวลา {integer.format(result.durationMilliseconds)} มิลลิวินาที</span>
                    {result.sourceFileHash && <span className="font-mono">SHA-256 {result.sourceFileHash.slice(0, 16)}…</span>}
                    {result.snapshotId && <span className="font-mono">Snapshot {result.snapshotId.slice(0, 28)}…</span>}
                </div>
                {result.validationErrors.map((issue) => <p key={issue} className="mt-2 text-sm">• {issue}</p>)}
                {result.warnings.map((warning) => <p key={warning} className="mt-2 text-sm">• คำเตือน: {warning}</p>)}
            </div>
        </div>
    </Card>;
}

function SyncEvidence({ dashboard }: { dashboard: DebtPreviewDashboard }) {
    const sync = dashboard.sync;
    return <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-amber-800">
        <span className="inline-flex items-center gap-1"><CheckCircle2 size={13} /> {sync.success ? (sync.published ? "ตรวจสอบและเผยแพร่ข้อมูลล่าสุดแล้ว" : "ข้อมูลตรงกับครั้งก่อน ไม่มีรายการซ้ำ") : "ข้อมูลครั้งล่าสุดไม่ผ่านการตรวจสอบ จึงคงข้อมูลเดิมไว้"}</span>
        <span>เพิ่ม {integer.format(sync.addedContracts)}</span><span>ออก {integer.format(sync.removedContracts)}</span><span>เปลี่ยน {integer.format(sync.changedContracts)}</span>
        <span>Auto Sync เมื่อเปิดดู: {dashboard.autoSyncOnQueryEnabled ? "เปิด" : "ปิด"}</span>
        <span>กฎ {dashboard.policyVersion}</span>
        {dashboard.debtSnapshotPublishedAtUtc && <span>เผยแพร่ {new Date(dashboard.debtSnapshotPublishedAtUtc).toLocaleString("th-TH")}</span>}
    </div>;
}

function AutoSyncEvidence({ status }: { status: DebtAutoSyncStatus }) {
    const labels: Record<DebtAutoSyncStatus["status"], string> = {
        Disabled: "ปิด",
        Checking: "กำลังตรวจสอบ",
        Ready: "พร้อม / กำลังเฝ้าดู",
        Syncing: "กำลังซิงก์",
        NetworkUnavailable: "เครือข่ายไม่พร้อม",
        Error: "ผิดพลาด — คงข้อมูลล่าสุดไว้",
    };
    return <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-amber-800">
        <span>Auto Sync: {status.enabled ? "เปิด" : "ปิด"}</span>
        <span>สถานะ: {labels[status.status]}</span>
        <span>ตรวจทุก {integer.format(status.pollSeconds)} วินาที</span>
        {status.lastCheckedAtUtc && <span>ตรวจล่าสุด {new Date(status.lastCheckedAtUtc).toLocaleString("th-TH")}</span>}
        {status.lastSuccessfulSyncAtUtc && <span>ซิงก์ล่าสุด {new Date(status.lastSuccessfulSyncAtUtc).toLocaleString("th-TH")}</span>}
    </div>;
}

function InstallmentAutoSyncEvidence({ status }: { status: InstallmentMasterAutoSyncStatus }) {
    const labels: Record<InstallmentMasterAutoSyncStatus["status"], string> = {
        Disabled: "ปิด", Checking: "กำลังตรวจสอบ", Ready: "พร้อม / กำลังเฝ้าดู",
        Syncing: "กำลังซิงก์", NetworkUnavailable: "เครือข่ายไม่พร้อม", Error: "ผิดพลาด — ใช้ Snapshot ล่าสุด",
    };
    return <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-blue-800">
        <span>Auto Sync: {status.enabled ? "เปิด" : "ปิด"}</span>
        <span>สถานะ: {labels[status.status]}</span>
        <span>ตรวจทุก {integer.format(status.pollSeconds)} วินาที</span>
        {status.lastCheckedAtUtc && <span>ตรวจล่าสุด {new Date(status.lastCheckedAtUtc).toLocaleString("th-TH")}</span>}
        {status.lastSuccessfulSyncAtUtc && <span>ซิงก์ล่าสุด {new Date(status.lastSuccessfulSyncAtUtc).toLocaleString("th-TH")}</span>}
    </div>;
}

function MonthlyAmountDuePagination({ contracts, filtered, onPrevious, onNext }: {
    contracts: MonthlyAmountDueContractPage;
    filtered: boolean;
    onPrevious: () => void;
    onNext: () => void;
}) {
    const pageCount = Math.max(1, Math.ceil(contracts.totalCount / contracts.pageSize));
    const range = resultRange(contracts.page, contracts.pageSize, contracts.totalCount);
    const rangeText = contracts.totalCount === 0
        ? "ไม่พบสัญญา"
        : filtered
            ? `พบ ${integer.format(contracts.totalCount)} สัญญา • แสดง ${integer.format(range.first)}–${integer.format(range.last)}`
            : `แสดง ${integer.format(range.first)}–${integer.format(range.last)} จาก ${integer.format(contracts.totalCount)} สัญญา`;

    return <div className="mt-4 flex flex-col gap-3 text-sm text-slate-600 sm:flex-row sm:items-center sm:justify-between">
        <span>{rangeText}</span>
        <div className="flex items-center gap-3">
            <button type="button" disabled={contracts.page <= 1} onClick={onPrevious}
                className="inline-flex items-center gap-1 rounded-lg border border-slate-300 px-3 py-2 disabled:opacity-40">
                <ChevronLeft size={16} /> ก่อนหน้า
            </button>
            <span>หน้า {integer.format(contracts.page)} / {integer.format(pageCount)}</span>
            <button type="button" disabled={contracts.page >= pageCount} onClick={onNext}
                className="inline-flex items-center gap-1 rounded-lg border border-slate-300 px-3 py-2 disabled:opacity-40">
                ถัดไป <ChevronRight size={16} />
            </button>
        </div>
    </div>;
}

function MonthlyAmountDueTable({ contracts, sortDescending, onToggleSort }: {
    contracts: MonthlyAmountDueContractPage;
    sortDescending: boolean;
    onToggleSort: () => void;
}) {
    return <div className="overflow-x-auto"><table className="min-w-[3500px] text-left text-sm">
        <thead className="bg-slate-100 text-slate-600"><tr>
            <Th>เลขที่สัญญา</Th><Th>กลุ่มหนี้</Th><Th>สถานะสัญญา</Th><Th><button type="button" onClick={onToggleSort}
                className="inline-flex items-center gap-1 font-medium text-slate-700 hover:text-green-700"
                title={sortDescending ? "เรียงจากเหลือมากไปเกินกำหนดมาก" : "เรียงจากเกินกำหนดมากไปเหลือมาก"}>
                งวดคงเหลือ / เกินกำหนด <ArrowUpDown size={14} />
            </button></Th>
            <Th>ชำระต่องวด</Th><Th>ยอดยกมา</Th><Th>ยอดที่ต้องชำระ</Th><Th>รับจริง</Th><Th>ขาดชำระ</Th>
            <Th>เงินดาวน์</Th><Th>เครดิตล่วงหน้ายกมา</Th><Th>ใช้เครดิตกับยอดค้าง</Th><Th>ใช้เครดิตกับงวดนี้</Th><Th>ยอดค้างก่อนงวด</Th><Th>ชำระยอดค้าง (เงินรับงวดนี้)</Th><Th>ชำระงวดปัจจุบัน (เงินรับงวดนี้)</Th>
            <Th>ภาระงวดนี้ที่เหลือรับเงิน</Th><Th>เครดิตล่วงหน้าเกิดใหม่</Th><Th>เครดิตล่วงหน้าคงเหลือ</Th><Th>ยอดค้างปลายงวด</Th><Th>ปิดก่อนกำหนด</Th><Th>ปิดยอดหมดอายุ</Th><Th>รอตรวจสอบการจัดสรร</Th>
            <Th>ยอดคงเหลือ</Th><Th>หลักเกณฑ์</Th><Th>ข้อมูลตารางงวด</Th><Th>สถานะการจัดสรร</Th>
        </tr></thead>
        <tbody className="divide-y">{contracts.items.map((item) => <tr key={item.contractNumber} className="hover:bg-slate-50">
            <Td className="font-medium">{item.contractNumber}</Td><Td>{thaiBucket(item.debtBucket)}</Td><Td>{collectionCategoryLabel(item.contractStatus)}</Td>
            <Td><InstallmentTimeline item={item} /></Td>
            <Td>{item.monthlyInstallment == null ? "—" : money.format(item.monthlyInstallment)}</Td><Td>{money.format(item.openingOutstanding)}</Td>
            <Td>{item.amountDue == null ? "ไม่ทราบ" : money.format(item.amountDue)}</Td><Td>{money.format(item.actualPayment)}</Td>
            <Td>{item.shortfall == null ? "ไม่ทราบ" : money.format(item.shortfall)}</Td>
            <Td>{money.format(item.downPaymentAmount)}</Td><Td>{money.format(item.priorAdvanceCredit)}</Td>
            <Td>{money.format(item.advanceCreditAppliedToPriorArrears)}</Td><Td>{money.format(item.advanceCreditAppliedToCurrentDue)}</Td>
            <Td>{item.priorArrears == null ? "ไม่ทราบ" : money.format(item.priorArrears)}</Td>
            <Td>{money.format(item.paymentToPriorArrears)}</Td><Td>{money.format(item.paymentToCurrentDue)}</Td>
            <Td>{item.remainingCurrentDueForCash == null ? "ไม่ทราบ" : money.format(item.remainingCurrentDueForCash)}</Td>
            <Td>{money.format(item.newAdvanceCredit)}</Td><Td>{money.format(item.endingAdvanceCredit)}</Td>
            <Td>{item.endingArrears == null ? "ไม่ทราบ" : money.format(item.endingArrears)}</Td>
            <Td>{money.format(item.earlyPayoffAmount)}</Td><Td>{money.format(item.expiredPayoffAmount)}</Td><Td>{money.format(item.unclassifiedPaymentAmount)}</Td>
            <Td>{money.format(item.endingOutstanding)}</Td><Td>{dueBasisLabel(item.dueBasis)}</Td><Td>{item.installmentDataStatus}</Td>
            <Td><span className={item.unclassifiedPaymentAmount > 0 ? "font-medium text-amber-700" : "text-green-700"}>{item.unclassifiedPaymentAmount > 0 ? "รอตรวจสอบการจัดสรร" : allocationStatusLabel(item.paymentAllocationStatus)}</span>
                {item.paymentAllocationWarning && <span className="block max-w-72 text-xs text-amber-700">{item.paymentAllocationWarning}</span>}</Td>
        </tr>)}</tbody>
    </table></div>;
}

function MonthlyPerformanceSection({ trend }: { trend: MonthlyPerformanceTrend }) {
    const latestKey = monthKey(trend.months[trend.months.length - 1]);
    const [selectedKey, setSelectedKey] = useState(latestKey);
    const [bucketMode, setBucketMode] = useState<"count" | "balance" | "movement">("count");
    const selected = trend.months.find((month) => monthKey(month) === selectedKey) ?? trend.months[trend.months.length - 1];
    const bucketNames = selected.debtBuckets.map((bucket) => ({ bucket: bucket.bucket, label: bucket.bucketLabel }));
    const unknownOpening = "ไม่ทราบ / ไม่มีข้อมูลก่อนหน้า";

    return <section className="space-y-4">
        <div>
            <h2 className="text-xl font-semibold text-slate-900">ผลการดำเนินงานรายเดือน</h2>
            <p className="text-sm text-slate-500">แนวโน้มจาก Source A ตั้งแต่เดือนแรกถึงเดือนล่าสุด โดยใช้บัญชีจัดสรร Payment Allocation V1.1 รายสัญญา</p>
            <p className="mt-1 text-sm font-medium text-green-800">ข้อมูลปัจจุบัน ณ {formatThaiDataPeriod(trend.latestPeriod)}</p>
        </div>
        <div className="flex flex-wrap gap-2" role="tablist" aria-label="เลือกเดือนผลการดำเนินงาน">
            {trend.months.map((month) => <button key={monthKey(month)} type="button"
                onClick={() => setSelectedKey(monthKey(month))}
                className={`rounded-full px-3 py-1.5 text-sm font-medium ${monthKey(month) === monthKey(selected) ? "bg-green-700 text-white" : "border border-slate-300 bg-white text-slate-700 hover:border-green-500"}`}>
                {month.periodLabelThai}
            </button>)}
        </div>
        <Card className="border-green-200 bg-green-50">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
                <div>
                    <p className="text-sm font-medium text-green-900">สัญญาที่ยังมียอดหนี้คงเหลือปลายเดือน</p>
                    <p className="mt-1 text-4xl font-bold tabular-nums text-green-950">{integer.format(selected.endingOutstandingContracts)} <span className="text-lg font-medium">สัญญา</span></p>
                </div>
                <div className="text-sm text-green-900 sm:text-right">
                    <p>สัญญาในฐานทั้งหมด {integer.format(selected.contractCount)}</p>
                    <p>ชำระหมดสะสม {integer.format(selected.paidOffContracts)}</p>
                </div>
            </div>
        </Card>
        <div className="grid gap-4 xl:grid-cols-2">
            <ManagerMovementFlow title="การเคลื่อนไหวทางการเงิน"
                note="ยอดเพิ่มระหว่างเดือนหักด้วยยอดรับชำระจริง เพื่ออธิบายยอดหนี้คงเหลือ"
                labels={["ยอดยกมา", "เพิ่มระหว่างเดือน", "รับชำระ/ลด", "ยอดคงเหลือ"]}
                values={[
                    selected.openingOutstanding == null ? unknownOpening : money.format(selected.openingOutstanding),
                    selected.increaseDuringPeriod == null ? unknownOpening : money.format(selected.increaseDuringPeriod),
                    money.format(selected.actualPayment),
                    money.format(selected.endingOutstanding),
                ]}
                reconciliation="ยอดยกมา + เพิ่มระหว่างเดือน − รับชำระจริง = ยอดคงเหลือ"
                reconciled={moneyMovementReconciles(selected)} />
            <ManagerMovementFlow title="การเคลื่อนไหวของสัญญาคงเหลือ"
                note="“ลดลง” นับเฉพาะสัญญาที่ออกจากกลุ่มยอดคงเหลือ ไม่ใช่การชำระบางส่วน"
                labels={["ยกมา", "เข้าใหม่", "ลดลง", "คงเหลือ"]}
                values={[
                    selected.openingOutstandingContracts == null ? unknownOpening : `${integer.format(selected.openingOutstandingContracts)} สัญญา`,
                    selected.newOutstandingContracts == null ? unknownOpening : `${integer.format(selected.newOutstandingContracts)} สัญญา`,
                    selected.reducedOutstandingContracts == null ? unknownOpening : `${integer.format(selected.reducedOutstandingContracts)} สัญญา`,
                    `${integer.format(selected.endingOutstandingContracts)} สัญญา`,
                ]}
                reconciliation="ยกมา + เข้าใหม่ − ลดลง = คงเหลือ"
                reconciled={contractMovementReconciles(selected)} />
        </div>
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Metric label="ยอดที่ต้องชำระ" value={formatNullableMoney(selected.amountDue)} />
            <Metric label="รับชำระจริง" value={money.format(selected.actualPayment)} />
            <Metric label="เครดิตล่วงหน้าเกิดใหม่" value={money.format(selected.newAdvanceCredit)} />
            <Metric label="รอตรวจสอบการจัดสรร" value={`${money.format(selected.unclassifiedAmount)} · ${integer.format(selected.unclassifiedCount)} สัญญา`} />
        </div>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <PerformanceDetailGroup title="ยอดค้างและการรับชำระ" rows={[
                ["ยอดค้างยกมา", formatNullableMoney(selected.priorArrears)],
                ["ใช้ชำระยอดค้าง", money.format(selected.paymentToPriorArrears)],
                ["ชำระงวดปัจจุบัน", money.format(selected.paymentToCurrentDue)],
            ]} />
            <PerformanceDetailGroup title="เครดิตล่วงหน้า" rows={[
                ["เครดิตยกมา", money.format(selected.priorAdvanceCredit)],
                ["ใช้เครดิต", money.format(selected.advanceCreditApplied)],
                ["เกิดเครดิตใหม่", money.format(selected.newAdvanceCredit)],
                ["เครดิตคงเหลือ", money.format(selected.endingAdvanceCredit)],
            ]} />
            <PerformanceDetailGroup title="ก่อนถึงงวด" rows={[
                ["เงินดาวน์", money.format(selected.downPayment)],
                ["ชำระล่วงหน้า", money.format(selected.advancePayment)],
            ]} />
            <PerformanceDetailGroup title="ปิดสัญญา" rows={[
                ["ปิดก่อนกำหนด", `${money.format(selected.earlyPayoffAmount)} · ${integer.format(selected.earlyPayoffCount)} สัญญา`],
                ["ปิดยอดหมดสัญญา", `${money.format(selected.expiredPayoffAmount)} · ${integer.format(selected.expiredPayoffCount)} สัญญา`],
                ["ชำระหมดในเดือน", selected.paidOffDuringMonthCount == null ? "ไม่ทราบ" : `${money.format(selected.paidOffDuringMonthAmount ?? 0)} · ${integer.format(selected.paidOffDuringMonthCount)} สัญญา`],
            ]} />
        </div>
        {!selected.historicalClassificationComplete && <Card className="border-amber-200 bg-amber-50">
            <p className="font-medium text-amber-950">ข้อมูลย้อนหลังบางส่วนแสดงเป็น “ไม่ทราบ”</p>
            {selected.incompleteReasons.map((reason) => <p key={reason} className="mt-1 text-sm text-amber-800">• {reason}</p>)}
        </Card>}
        <Card>
            <h3 className="mb-3 font-semibold text-slate-900">สรุปรายเดือน</h3>
            <div className="overflow-x-auto"><table className="min-w-[1900px] text-left text-sm">
                <thead className="bg-slate-100 text-slate-600"><tr><Th>เดือน</Th><Th>สัญญาคงเหลือ</Th><Th>สัญญาในฐาน</Th><Th>ชำระหมดสะสม</Th><Th>สมาชิก</Th><Th>ยอดยกมา</Th><Th>ยอดที่ต้องชำระ</Th><Th>รับจริง</Th><Th>ชำระยอดค้าง</Th><Th>ชำระงวดนี้</Th><Th>เงินดาวน์</Th><Th>เครดิตยกมา</Th><Th>ใช้เครดิต</Th><Th>เครดิตใหม่</Th><Th>เครดิตคงเหลือ</Th><Th>ปิดก่อนกำหนด</Th><Th>ปิดยอดหมดอายุ</Th><Th>รอตรวจสอบ</Th><Th>ยอดปลายเดือน</Th></tr></thead>
                <tbody className="divide-y">{trend.months.map((month) => <tr key={monthKey(month)} className={monthKey(month) === monthKey(selected) ? "bg-green-50" : ""}>
                    <Td className="font-medium">{month.periodLabelThai}</Td><Td className="font-semibold text-green-800">{integer.format(month.endingOutstandingContracts)}</Td><Td>{integer.format(month.contractCount)}</Td><Td>{integer.format(month.paidOffContracts)}</Td><Td>{integer.format(month.memberCount)}</Td>
                    <Td>{month.openingOutstanding == null ? unknownOpening : money.format(month.openingOutstanding)}</Td><Td>{formatNullableMoney(month.amountDue)}</Td><Td>{money.format(month.actualPayment)}</Td>
                    <Td>{money.format(month.paymentToPriorArrears)}</Td><Td>{money.format(month.paymentToCurrentDue)}</Td><Td>{money.format(month.downPayment)}</Td>
                    <Td>{money.format(month.priorAdvanceCredit)}</Td><Td>{money.format(month.advanceCreditApplied)}</Td><Td>{money.format(month.newAdvanceCredit)}</Td><Td>{money.format(month.endingAdvanceCredit)}</Td>
                    <Td>{money.format(month.earlyPayoffAmount)} ({integer.format(month.earlyPayoffCount)})</Td><Td>{money.format(month.expiredPayoffAmount)} ({integer.format(month.expiredPayoffCount)})</Td>
                    <Td>{money.format(month.unclassifiedAmount)} ({integer.format(month.unclassifiedCount)})</Td><Td>{money.format(month.endingOutstanding)}</Td>
                </tr>)}</tbody>
            </table></div>
        </Card>
        <Card>
            <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
                <div>
                    <h3 className="font-semibold text-slate-900">มุมมองแนวโน้ม</h3>
                    <p className="text-xs text-slate-500">เลือกดูจำนวนสัญญา ยอดคงเหลือ หรือการเคลื่อนไหวทางการเงิน</p>
                </div>
                <div className="rounded-lg border border-slate-300 bg-white p-1">
                    <button type="button" onClick={() => setBucketMode("count")} className={`rounded px-3 py-1 text-sm ${bucketMode === "count" ? "bg-green-700 text-white" : "text-slate-600"}`}>จำนวนสัญญา</button>
                    <button type="button" onClick={() => setBucketMode("balance")} className={`rounded px-3 py-1 text-sm ${bucketMode === "balance" ? "bg-green-700 text-white" : "text-slate-600"}`}>ยอดคงเหลือ</button>
                    <button type="button" onClick={() => setBucketMode("movement")} className={`rounded px-3 py-1 text-sm ${bucketMode === "movement" ? "bg-green-700 text-white" : "text-slate-600"}`}>การเคลื่อนไหวทางการเงิน</button>
                </div>
            </div>
            {bucketMode === "movement" ? <div className="overflow-x-auto"><table className="min-w-[900px] text-left text-sm">
                <thead className="bg-slate-100 text-slate-600"><tr><Th>เดือน</Th><Th>ยอดยกมา</Th><Th>เพิ่มระหว่างเดือน</Th><Th>รับชำระ/ลด</Th><Th>ยอดคงเหลือ</Th><Th>ตรวจสอบสมการ</Th></tr></thead>
                <tbody className="divide-y">{trend.months.map((month) => <tr key={monthKey(month)}>
                    <Td className="font-medium">{month.periodLabelThai}</Td>
                    <Td>{month.openingOutstanding == null ? unknownOpening : money.format(month.openingOutstanding)}</Td>
                    <Td>{month.increaseDuringPeriod == null ? unknownOpening : money.format(month.increaseDuringPeriod)}</Td>
                    <Td>{money.format(month.actualPayment)}</Td>
                    <Td className="font-semibold">{money.format(month.endingOutstanding)}</Td>
                    <Td>{month.openingOutstanding == null ? "รอข้อมูลก่อนหน้า" : moneyMovementReconciles(month) ? "ตรงกัน" : "ไม่ตรงกัน"}</Td>
                </tr>)}</tbody>
            </table></div> : <div className="overflow-x-auto"><table className="min-w-max text-left text-sm">
                <thead className="bg-slate-100 text-slate-600"><tr><Th>กลุ่มหนี้</Th>{trend.months.map((month) => <Th key={monthKey(month)}>{month.periodLabelThai}</Th>)}</tr></thead>
                <tbody className="divide-y">{bucketNames.map(({ bucket, label }) => <tr key={bucket}>
                    <Td className="font-medium text-green-800">{label}</Td>
                    {trend.months.map((month) => {
                        const value = month.debtBuckets.find((item) => item.bucket === bucket);
                        return <Td key={monthKey(month)}>{bucketMode === "count" ? integer.format(value?.contractCount ?? 0) : money.format(value?.outstandingBalance ?? 0)}</Td>;
                    })}
                </tr>)}</tbody>
            </table></div>}
        </Card>
    </section>;
}

function ManagerMovementFlow({ title, note, labels, values, reconciliation, reconciled }: {
    title: string;
    note: string;
    labels: [string, string, string, string];
    values: [string, string, string, string];
    reconciliation: string;
    reconciled: boolean | null;
}) {
    return <Card>
        <h3 className="font-semibold text-slate-900">{title}</h3>
        <p className="mt-1 text-xs text-slate-500">{note}</p>
        <div className="mt-4 grid gap-2 sm:grid-cols-4">
            {labels.map((label, index) => <div key={label} className="relative rounded-lg bg-slate-50 p-3">
                <p className="text-xs text-slate-500">{index > 0 ? "→ " : ""}{label}</p>
                <p className="mt-1 break-words font-semibold tabular-nums text-slate-900">{values[index]}</p>
            </div>)}
        </div>
        <p className={`mt-3 text-xs ${reconciled == null ? "text-amber-700" : reconciled ? "text-green-700" : "text-red-700"}`}>
            {reconciliation} · {reconciled == null ? "รอข้อมูลก่อนหน้า" : reconciled ? "ตรวจสอบแล้ว" : "ไม่ตรงกัน — ต้องตรวจสอบ"}
        </p>
    </Card>;
}

function PerformanceDetailGroup({ title, rows }: { title: string; rows: Array<[string, string]> }) {
    return <Card className="p-4"><h3 className="font-semibold text-slate-900">{title}</h3>
        <dl className="mt-3 space-y-2 text-sm">{rows.map(([label, value]) => <div key={label} className="flex items-start justify-between gap-3">
            <dt className="text-slate-500">{label}</dt><dd className="text-right font-medium tabular-nums text-slate-800">{value}</dd>
        </div>)}</dl>
    </Card>;
}

function monthKey(month: MonthlyPerformancePeriod) {
    return `${month.periodYear}-${String(month.periodMonth).padStart(2, "0")}`;
}

function formatNullableMoney(value: number | null) {
    return value == null ? "ไม่ทราบ" : money.format(value);
}

function allocationStatusLabel(value: string) {
    const labels: Record<string, string> = {
        Allocated: "จัดสรรครบถ้วน", EarlyPayoff: "ปิดหนี้ก่อนกำหนด", ExpiredPayoff: "ชำระปิดยอดสัญญาหมดอายุ",
        PriorCreditPolicyRequired: "รอนโยบายเครดิตงวดก่อน", UnresolvedDownPaymentEvidence: "รอหลักฐานเงินดาวน์",
    };
    return labels[value] ?? value;
}

function InstallmentTimeline({ item }: { item: MonthlyAmountDueContractPage["items"][number] }) {
    const label = formatRemainingOrOverdueDuration(item.remainingOrOverdueMonths, item.contractStatus);
    const color = item.contractStatus === "PaidOff" ? "text-green-700"
        : item.contractStatus === "NotDue" ? "text-blue-700"
            : item.remainingOrOverdueMonths == null ? "text-slate-500"
                : item.remainingOrOverdueMonths < 0 ? "text-red-700"
                    : item.remainingOrOverdueMonths === 0 ? "text-amber-700" : "text-slate-800";
    return <span className={`font-medium tabular-nums ${color}`}>{label}</span>;
}

function collectionCategoryLabel(value: string) {
    const labels: Record<string, string> = {
        "Active/InTerm": "อยู่ในสัญญา", Expired: "หมดอายุสัญญา", NotDue: "ยังไม่ถึงกำหนด",
        PaidOff: "ชำระหมด", Unknown: "ข้อมูลตารางงวดไม่ครบ",
    };
    return labels[value] ?? value;
}

function dueBasisLabel(value: string) {
    const labels: Record<string, string> = {
        PaidOff: "ชำระหมด", NotDue: "ยังไม่ถึงกำหนด", ActiveInstallment: "งวดปกติ",
        FinalInstallment: "งวดสุดท้าย — ยอดยกมาทั้งหมด", ExpiredFullBalance: "หมดอายุ — ยอดยกมาทั้งหมด",
        InstallmentsElapsedFullBalance: "เกินจำนวนงวด — ยอดยกมาทั้งหมด", MissingInstallmentData: "ข้อมูลตารางงวดไม่ครบ",
    };
    return labels[value] ?? value;
}

function Metric({ label, value }: { label: string; value: string }) {
    return <Card className="p-4"><p className="text-sm text-slate-500">{label}</p><p className="mt-1 text-xl font-bold text-slate-800">{value}</p></Card>;
}

function Notice({ message }: { message: string }) {
    return <div className="flex gap-3 rounded-xl border border-red-200 bg-red-50 p-4 text-red-800"><AlertTriangle className="shrink-0" size={20} /><p>{message}</p></div>;
}

function Unavailable({ dashboard }: { dashboard: DebtPreviewDashboard }) {
    return <Card className="border-amber-300"><div className="flex gap-3"><AlertTriangle className="shrink-0 text-amber-600" /><div>
        <h2 className="font-semibold text-slate-900">ยังไม่สามารถจัดทำข้อมูลแยกหนี้ของเดือนที่เลือกได้</h2>
        <p className="mt-1 text-slate-700">{dashboard.message}</p>
        {(dashboard.sync?.validationErrors ?? []).map((issue) => <p key={issue} className="mt-2 text-sm text-red-700">• {issue}</p>)}
        {dashboard.warnings.map((warning) => <p key={warning} className="mt-2 text-sm text-amber-800">• {warning}</p>)}
    </div></div></Card>;
}

function BucketComparisonTable({ rows, onSelect }: { rows: DebtBucketPeriodComparison[]; onSelect: (row: DebtBucketPeriodComparison) => void }) {
    return <Card><h2 className="mb-4 text-xl font-semibold text-slate-800">เปรียบเทียบเดือนก่อน → เดือนปัจจุบัน</h2><div className="overflow-x-auto"><table className="min-w-[1500px] text-left text-sm">
        <thead className="bg-slate-100 text-slate-600"><tr><Th>กลุ่มหนี้</Th><Th>รายก่อน</Th><Th>รายปัจจุบัน</Th><Th>ต่างราย</Th><Th>สัญญาก่อน</Th><Th>สัญญาปัจจุบัน</Th><Th>ต่างสัญญา</Th><Th>ยอดก่อน</Th><Th>ยอดปัจจุบัน</Th><Th>ต่างยอด</Th><Th>เข้า</Th><Th>ดีขึ้น</Th><Th>แย่ลง</Th><Th>ชำระหมด</Th><Th>ยอดลด</Th><Th>ยอดเพิ่ม</Th></tr></thead>
        <tbody className="divide-y">{rows.map((row) => <tr key={row.bucket} onClick={() => onSelect(row)} className="cursor-pointer hover:bg-green-50">
            <Td><span className="font-medium text-green-800">{row.bucketLabel}</span></Td><Td>{integer.format(row.previousMemberCount)}</Td><Td>{integer.format(row.currentMemberCount)}</Td><Td>{signedInteger.format(row.memberDifference)}</Td><Td>{integer.format(row.previousContractCount)}</Td><Td>{integer.format(row.currentContractCount)}</Td><Td>{signedInteger.format(row.contractDifference)}</Td><Td>{money.format(row.previousOutstanding)}</Td><Td>{money.format(row.currentOutstanding)}</Td><Td>{money.format(row.amountDifference)}</Td><Td>{integer.format(row.enteredContracts)}</Td><Td>{integer.format(row.improvedContracts)}</Td><Td>{integer.format(row.worsenedContracts)}</Td><Td>{integer.format(row.paidOffContracts)}</Td><Td>{integer.format(row.sameBucketBalanceDecreasedContracts)}</Td><Td>{integer.format(row.sameBucketBalanceIncreasedContracts)}</Td>
        </tr>)}</tbody>
    </table></div></Card>;
}

function MovementTable({ title, rows, onSelect, payment = false }: { title: string; rows: DebtMovementSummary[]; onSelect: (row: DebtMovementSummary) => void; payment?: boolean }) {
    return <Card><h2 className="mb-4 text-xl font-semibold text-slate-800">{title}</h2><div className="max-h-[34rem] overflow-auto"><table className="min-w-full text-left text-sm">
        <thead className="sticky top-0 bg-slate-100 text-slate-600"><tr><Th>ความเคลื่อนไหว</Th><Th>ราย</Th><Th>สัญญา</Th><Th>ยอดก่อน</Th><Th>ยอดปัจจุบัน</Th><Th>เปลี่ยนแปลง</Th></tr></thead>
        <tbody className="divide-y">{rows.map((row, index) => <tr key={`${row.paymentMovement}-${row.previousBucket}-${row.currentBucket}-${row.category}-${index}`} onClick={() => onSelect(row)} className="cursor-pointer hover:bg-green-50">
            <Td><span className="font-medium">{payment ? thaiPaymentMovement(row.paymentMovement) : `${thaiBucket(row.previousBucket)} → ${thaiBucket(row.currentBucket)}`}</span>{!payment && <span className="block text-xs text-slate-500">{thaiMovementCategory(row.category)}</span>}</Td><Td>{integer.format(row.memberCount)}</Td><Td>{integer.format(row.contractCount)}</Td><Td>{money.format(row.previousOutstanding)}</Td><Td>{money.format(row.currentOutstanding)}</Td><Td><span className={row.amountChange < 0 ? "text-green-700" : row.amountChange > 0 ? "text-red-700" : ""}>{money.format(row.amountChange)}</span></Td>
        </tr>)}</tbody>
    </table></div></Card>;
}

function ContractTable({ contracts }: { contracts: DebtContractPage }) {
    return <div className="overflow-x-auto"><table className="min-w-[2850px] text-left text-sm"><thead className="bg-slate-100 text-slate-600"><tr>
        <Th>รหัสสมาชิก</Th><Th>ชื่อสมาชิก</Th><Th>เลขที่สัญญา</Th><Th>ประเภทสินเชื่อ</Th><Th>วันที่ทำสัญญา</Th><Th>เริ่มครบกำหนดชำระ</Th><Th>วันสิ้นสุดสัญญา</Th><Th>งวดประเมิน</Th><Th>งวดชำระล่าสุด</Th><Th>กลุ่มเดือนก่อน</Th><Th>กลุ่มหนี้ปัจจุบัน</Th><Th>เกณฑ์จัดกลุ่ม</Th><Th>การชำระเดือนล่าสุด</Th><Th>ยอดชำระล่าสุด</Th><Th>เดือนแรกที่ขาดชำระ</Th><Th>จำนวนเดือนขาดชำระ</Th><Th>เงินต้นคงเหลือ</Th><Th>ผลตอบแทนคงเหลือ</Th><Th>ยอดรวม</Th><Th>รหัสกลุ่ม</Th><Th>สาขา</Th><Th>ความเคลื่อนไหว</Th>
    </tr></thead><tbody className="divide-y">{contracts.items.map((item) => <tr key={`${item.sourceRowNumber}-${item.contractNumber}`} className="hover:bg-slate-50">
        <Td>{item.memberCode || "—"}</Td><Td>{item.memberName || "—"}</Td><Td className="font-medium">{item.contractNumber}</Td><Td>{item.loanType}</Td><Td>{item.contractDate ?? "—"}</Td><Td>{item.firstDuePeriod?.slice(0, 7) ?? "—"}</Td><Td>{item.expireDate ?? "—"}</Td><Td>{item.evaluatedPeriod?.slice(0, 7) ?? "—"}</Td><Td>{item.lastPaymentPeriod?.slice(0, 7) ?? "—"}</Td><Td>{thaiBucket(item.previousBucket)}</Td><Td className="font-medium text-green-800">{item.currentBucketLabel ?? item.currentBucket}</Td><Td>{thaiCalculationBasis(item.calculationBasis)}</Td><Td><span className={paymentStatusClass(item.latestPaymentStatus)}>{thaiPaymentStatus(item.latestPaymentStatus)}</span></Td><Td>{money.format(item.latestPaymentAmount ?? 0)}</Td><Td>{item.firstMissedPaymentPeriod?.slice(0, 7) ?? "—"}</Td><Td>{integer.format(item.consecutiveMissedMonths)}</Td><Td>{money.format(item.principalBalance)}</Td><Td>{money.format(item.profitBalance)}</Td><Td>{money.format(item.totalBalance)}</Td><Td>{item.groupCode ?? "—"}</Td><Td>{item.branch ?? "—"}</Td><Td>{thaiMovementCategory(item.movementCategory)}</Td>
    </tr>)}</tbody></table></div>;
}

function thaiBucket(value: string) {
    const labels: Record<string, string> = {
        "Normal": "ปกติ", "1 month delinquent": "ขาด 1 เดือน", "2 months delinquent": "ขาด 2 เดือน",
        "3–6 months delinquent": "ขาด 3–6 เดือน", "7–12 months delinquent": "ขาด 7–12 เดือน",
        ">1 to 3 years": "เกิน 1 แต่ไม่เกิน 3 ปี", ">3 to 5 years": "เกิน 3 แต่ไม่เกิน 5 ปี",
        ">5 to 10 years": "เกิน 5 แต่ไม่เกิน 10 ปี", "10 years and above": "10 ปีขึ้นไป",
        "Paid off / no outstanding balance": "ชำระหมด / ไม่มียอดคงเหลือ",
    };
    return labels[value] ?? value;
}

function thaiPaymentMovement(value: string) {
    if (value === "New Contract") return "สัญญาใหม่";
    const transition = value.split(" -> ");
    return transition.length === 2
        ? `${thaiPaymentStatus(transition[0])} → ${thaiPaymentStatus(transition[1])}`
        : value;
}

function thaiPaymentStatus(value: string) {
    const labels: Record<string, string> = {
        "Not due": "ยังไม่ถึงกำหนดชำระ",
        "Paid": "มีชำระ",
        "Not paid": "ไม่ชำระเมื่อถึงกำหนด",
        "Closed / paid off": "ปิดสัญญา / ชำระหมด",
    };
    return labels[value] ?? value;
}

function paymentStatusClass(value: string) {
    if (value === "Paid") return "text-green-700";
    if (value === "Not due") return "text-blue-700";
    if (value === "Closed / paid off") return "text-slate-500";
    return "text-red-700";
}

function thaiMovementCategory(value: string) {
    const labels: Record<string, string> = {
        "New contract": "สัญญาใหม่", "New delinquency": "เริ่มค้างชำระใหม่", "Entered risk / reopened": "กลับมามียอด / เปิดความเสี่ยงใหม่", "Improved": "ดีขึ้น", "Worsened": "แย่ลง",
        "Same bucket but balance decreased": "อยู่กลุ่มเดิมแต่ยอดลด", "Same bucket but balance increased": "อยู่กลุ่มเดิมแต่ยอดเพิ่ม",
        "Same bucket unchanged": "อยู่กลุ่มเดิมและยอดเท่าเดิม", "Closed / paid off": "ปิดหนี้แล้ว",
    };
    return labels[value] ?? value;
}

function thaiCalculationBasis(value: string) {
    const labels: Record<string, string> = {
        "No outstanding balance": "ไม่มียอดคงเหลือ",
        "Elapsed time after contract expiry": "ระยะเวลาหลังวันสิ้นสุดสัญญา",
        "Before first payment due period": "ยังไม่ถึงงวดชำระแรก",
        "Positive payment in evaluated month": "มีการชำระในงวดประเมิน",
        "Current consecutive non-payment run": "ช่วงขาดชำระต่อเนื่องปัจจุบัน",
    };
    return labels[value] ?? value;
}

function Th({ children }: { children: React.ReactNode }) { return <th className="whitespace-nowrap px-3 py-3 font-semibold">{children}</th>; }
function Td({ children, className = "" }: { children: React.ReactNode; className?: string }) { return <td className={`whitespace-nowrap px-3 py-3 align-top ${className}`}>{children}</td>; }
