import { useEffect, useState } from "react";
import {
    Activity,
    AlertTriangle,
    CalendarDays,
    CheckCircle2,
    CircleDollarSign,
    FileCheck2,
    FileSpreadsheet,
    Landmark,
    ShieldAlert,
    TimerOff,
    Wallet,
} from "lucide-react";
import { Link } from "react-router-dom";

import DashboardCharts from "../components/dashboard/DashboardCharts";
import KPICard from "../components/dashboard/KPICard";
import Card from "../components/ui/Card";
import SectionTitle from "../components/ui/SectionTitle";
import {
    DEFAULT_DASHBOARD_MODE,
    getDashboardSummary,
    type DashboardMode,
    type DashboardSummaryDto,
} from "../services/dashboardService";

const countFormatter = new Intl.NumberFormat("th-TH");
const moneyFormatter = new Intl.NumberFormat("th-TH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
});

function formatCount(value: number | null | undefined) {
    return value == null ? "ไม่พร้อมใช้งาน" : countFormatter.format(value);
}

function formatMoney(value: number | null | undefined) {
    return value == null ? "ไม่พร้อมใช้งาน" : `${moneyFormatter.format(value)} บาท`;
}

function formatBusinessDate(value: string | null | undefined) {
    if (!value)
        return "ไม่พร้อมใช้งาน";
    const date = new Date(`${value}T00:00:00Z`);
    return new Intl.DateTimeFormat("th-TH", {
        month: "long",
        year: "numeric",
        timeZone: "UTC",
    }).format(date);
}

function formatTimestamp(value: string | null | undefined) {
    if (!value)
        return "--";
    const date = new Date(value);
    return Number.isNaN(date.getTime())
        ? "--"
        : new Intl.DateTimeFormat("th-TH", { dateStyle: "medium", timeStyle: "short" }).format(date);
}

function ModeSelector({ mode, onChange }: { mode: DashboardMode; onChange: (mode: DashboardMode) => void }) {
    return (
        <div className="flex w-fit rounded-xl border border-slate-200 bg-white p-1 shadow-sm" aria-label="Dashboard data mode">
            {(["CURRENT", "PUBLISHED"] as DashboardMode[]).map((item) => (
                <button
                    key={item}
                    type="button"
                    onClick={() => onChange(item)}
                    className={`rounded-lg px-4 py-2 text-sm font-semibold ${mode === item ? "bg-green-700 text-white" : "text-slate-600 hover:bg-slate-50"}`}
                >
                    {item === "CURRENT" ? "ข้อมูลปัจจุบัน" : "ข้อมูลเผยแพร่"}
                </button>
            ))}
        </div>
    );
}

function Stat({ title, value, prominent = false }: { title: string; value: string; prominent?: boolean }) {
    return (
        <div className={`rounded-xl border p-4 ${prominent ? "border-amber-300 bg-amber-50" : "border-slate-200 bg-white"}`}>
            <p className={`text-sm ${prominent ? "font-semibold text-amber-900" : "text-slate-600"}`}>{title}</p>
            <p className={`mt-2 text-xl font-bold tabular-nums ${prominent ? "text-amber-950" : "text-slate-900"}`}>{value}</p>
        </div>
    );
}

export default function DashboardPage() {
    const [mode, setMode] = useState<DashboardMode>(DEFAULT_DASHBOARD_MODE);
    const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let active = true;
        async function loadSummary() {
            setIsLoading(true);
            setError(null);
            try {
                const result = await getDashboardSummary(mode);
                if (active)
                    setSummary(result);
            } catch {
                if (active)
                    setError("ไม่สามารถโหลดข้อมูล Dashboard ได้ กรุณาลองใหม่อีกครั้ง");
            } finally {
                if (active)
                    setIsLoading(false);
            }
        }
        void loadSummary();
        return () => { active = false; };
    }, [mode]);

    return (
        <div className="space-y-4">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <ModeSelector mode={mode} onChange={setMode} />
                <span className="text-sm font-semibold text-slate-600">โหมดที่เลือก: {mode}</span>
            </div>
            {error && <div className="rounded-xl border border-red-200 bg-red-50 p-6 text-red-700">{error}</div>}
            {!error && (isLoading || !summary) && <Card><p className="text-center text-slate-500">กำลังโหลดข้อมูลภาพรวมสินเชื่อ...</p></Card>}
            {!error && !isLoading && summary && !summary.available && <Unavailable summary={summary} />}
            {!error && !isLoading && summary?.available && (
                summary.mode === "CURRENT" ? <CurrentDashboard summary={summary} /> : <PublishedDashboard summary={summary} />
            )}
        </div>
    );
}

function Unavailable({ summary }: { summary: DashboardSummaryDto }) {
    return (
        <Card className="border-amber-300 bg-amber-50">
            <div className="flex flex-col items-center px-2 py-10 text-center">
                <FileCheck2 size={48} className="text-amber-700" aria-hidden="true" />
                <h1 className="mt-4 text-2xl font-bold text-amber-950">ข้อมูลโหมด {summary.mode} ไม่พร้อมใช้งาน</h1>
                <p className="mt-3 max-w-2xl text-amber-900">{summary.message}</p>
            </div>
        </Card>
    );
}

function CurrentDashboard({ summary }: { summary: DashboardSummaryDto }) {
    const queue = summary.workQueue;
    return (
        <div className="space-y-6">
            <Card className="border-blue-200 bg-blue-50">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                    <div>
                        <p className="flex items-center gap-2 font-semibold text-blue-950"><Activity size={20} /> ข้อมูลปัจจุบัน ณ {formatBusinessDate(summary.asOfDate)}</p>
                        <p className="mt-2 text-sm text-blue-900">CURRENT · Source A + Source B · กระทบยอดแล้ว</p>
                    </div>
                    <details className="max-w-xl text-xs text-blue-900 lg:text-right">
                        <summary className="cursor-pointer font-semibold">ที่มาข้อมูลและสถานะซิงก์</summary>
                        <p className="mt-2 break-all">Source A: {summary.debtSnapshotId} · {summary.sourceAFingerprint}</p>
                        <p className="mt-1 break-all">Source B: {summary.installmentSnapshotId}</p>
                        <p className="mt-1">ซิงก์สำเร็จล่าสุด A: {formatTimestamp(summary.debtLastSuccessfulSyncAt)} · B: {formatTimestamp(summary.installmentLastSuccessfulSyncAt)}</p>
                    </details>
                </div>
            </Card>

            {summary.isStale && <div className="rounded-xl border border-amber-300 bg-amber-50 p-4 text-amber-900"><AlertTriangle className="mr-2 inline" size={18} />{summary.freshnessWarning}</div>}

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
                <KPICard title="สัญญาทั้งหมด" value={formatCount(summary.totalContracts)} subtitle="ประชากรสัญญาปัจจุบัน" icon={FileSpreadsheet} />
                <KPICard title="สัญญาที่มียอดคงเหลือ" value={formatCount(summary.outstandingContracts)} subtitle="EndingOutstanding > 0" icon={ShieldAlert} />
                <KPICard title="ชำระหมดแล้ว" value={formatCount(summary.paidOffContracts)} subtitle="PaidOff ปัจจุบัน" icon={CheckCircle2} />
                <KPICard title="เงินต้นคงเหลือ" value={formatMoney(summary.principalOutstanding)} subtitle="ยอดหนี้คงเหลือ" icon={Landmark} />
                <KPICard title="ผลตอบแทนคงเหลือ" value={formatMoney(summary.profitOutstanding)} subtitle="ยอดหนี้คงเหลือ" icon={Wallet} />
                <KPICard title="ยอดหนี้คงเหลือรวม" value={formatMoney(summary.totalOutstanding)} subtitle="เงินต้น + ผลตอบแทน" icon={CircleDollarSign} />
            </div>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                <KPICard title="อยู่ในระยะสัญญา" value={formatCount(summary.inTermContracts)} subtitle="ContractExpireDate ยังไม่ผ่าน" icon={CalendarDays} />
                <KPICard title="หมดระยะสัญญา" value={formatCount(summary.expiredContracts)} subtitle="ContractExpireDate ผ่านแล้ว" icon={TimerOff} />
                <KPICard title="หมดสัญญาและยังมียอดหนี้" value={formatCount(summary.expiredOutstandingContracts)} subtitle={formatMoney(summary.expiredOutstandingBalance)} icon={AlertTriangle} />
                <KPICard title="เงินดาวน์" value={formatCount(summary.downPaymentContracts)} subtitle={formatMoney(summary.downPaymentAmount)} icon={Wallet} />
            </div>

            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <KPICard title="ภาระที่ต้องชำระงวดนี้" value={formatMoney(summary.amountDue)} subtitle="AmountDue จาก Monthly Performance" icon={CalendarDays} />
                <KPICard title="รับชำระจริง" value={formatMoney(summary.actualPayment)} subtitle="ActualPayment จาก Payment Allocation V1.1" icon={CircleDollarSign} />
            </div>

            <Card>
                <SectionTitle title="การเคลื่อนไหวของยอดหนี้ประจำเดือน" />
                <div className="grid grid-cols-1 gap-3 text-center sm:grid-cols-4">
                    <Stat title="ยอดยกมา" value={formatMoney(summary.openingOutstanding)} />
                    <Stat title="+ เพิ่มขึ้นระหว่างงวด" value={formatMoney(summary.increaseDuringPeriod)} />
                    <Stat title="− รับชำระจริง" value={formatMoney(summary.actualPayment)} />
                    <Stat title="= ยอดคงเหลือปลายงวด" value={formatMoney(summary.endingOutstanding)} prominent />
                </div>
            </Card>

            <Card>
                <SectionTitle title="สถานะหนี้ปัจจุบัน" />
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-slate-200 text-sm">
                        <thead className="bg-slate-50 text-left text-slate-600"><tr><th className="px-4 py-3">กลุ่มสถานะหนี้</th><th className="px-4 py-3 text-right">สัญญา</th><th className="px-4 py-3 text-right">ยอดหนี้คงเหลือ</th></tr></thead>
                        <tbody className="divide-y divide-slate-100">{summary.debtBuckets.map((bucket) => <tr key={bucket.bucket}><td className="px-4 py-3 font-medium">{bucket.label}</td><td className="px-4 py-3 text-right tabular-nums">{formatCount(bucket.contractCount)}</td><td className="px-4 py-3 text-right tabular-nums">{formatMoney(bucket.outstandingAmount)}</td></tr>)}</tbody>
                    </table>
                </div>
            </Card>

            {queue && <Card>
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"><SectionTitle title="สรุปงานวันนี้" /><Link className="text-sm font-semibold text-green-700 underline" to="/work-queue">ไปยัง Work Queue</Link></div>
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-7">
                    <Stat title="งานทั้งหมด" value={formatCount(queue.totalQueueContracts)} />
                    <Stat title="งานติดตามเรียกเก็บ" value={formatCount(queue.collectionContracts)} />
                    <Stat title="งานตรวจสอบข้อมูล" value={formatCount(queue.reviewOnlyContracts)} />
                    <Stat title="เร่งด่วน" value={formatCount(queue.urgentContracts)} prominent />
                    <Stat title="สูง" value={formatCount(queue.highContracts)} />
                    <Stat title="ปานกลาง" value={formatCount(queue.mediumContracts)} />
                    <Stat title="ตรวจสอบ" value={formatCount(queue.reviewContracts)} />
                </div>
            </Card>}
        </div>
    );
}

function PublishedDashboard({ summary }: { summary: DashboardSummaryDto }) {
    return (
        <div className="space-y-6">
            <Card className="border-green-200 bg-green-50">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                    <div>
                        <p className="font-semibold text-green-900">ข้อมูลเผยแพร่ — {formatBusinessDate(summary.asOfDate)}</p>
                        <p className="mt-2 text-sm text-green-800">PUBLISHED · Published Snapshot #{summary.snapshotId} · {formatCount(summary.publishedRecordCount)} records</p>
                    </div>
                    <p className="text-sm text-green-900">เผยแพร่เมื่อ {formatTimestamp(summary.publishedAt)}</p>
                </div>
            </Card>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-5">
                <KPICard title="สัญญาทั้งหมด" value={formatCount(summary.totalContracts)} subtitle="Published Snapshot" icon={FileSpreadsheet} />
                <KPICard title="อยู่ในระยะสัญญา" value={formatCount(summary.inTermContracts)} subtitle="Published Snapshot" icon={CalendarDays} />
                <KPICard title="หมดระยะสัญญา" value={formatCount(summary.expiredContracts)} subtitle="Published Snapshot" icon={TimerOff} />
                <KPICard title="มียอดคงเหลือ" value={formatCount(summary.outstandingContracts)} subtitle="Published Snapshot" icon={ShieldAlert} />
                <KPICard title="ชำระหมดแล้ว" value={formatCount(summary.paidOffContracts)} subtitle="Published Snapshot" icon={CheckCircle2} />
            </div>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                <KPICard title="เงินต้นคงเหลือ" value={formatMoney(summary.principalOutstanding)} subtitle="ค่าที่จัดเก็บใน Published Snapshot" icon={Landmark} />
                <KPICard title="ผลตอบแทนคงเหลือ" value={formatMoney(summary.profitOutstanding)} subtitle="ค่าที่จัดเก็บใน Published Snapshot" icon={Wallet} />
                <KPICard title="ยอดคงเหลือรวม" value={formatMoney(summary.totalOutstanding)} subtitle="ค่าที่จัดเก็บใน Published Snapshot" icon={CircleDollarSign} />
            </div>
            <Card className="border-amber-300 bg-amber-50">
                <p className="font-semibold text-amber-950">หมดระยะสัญญาและยังมียอดหนี้: {formatCount(summary.expiredOutstandingContracts)} สัญญา</p>
                <p className="mt-1 text-amber-900">ยอดหนี้หมดสัญญาที่ยังคงเหลือ {formatMoney(summary.expiredOutstandingBalance)}</p>
            </Card>
            <Card>
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <SectionTitle title="ภาพรวมสินเชื่อแยกตามประเภท" />
                    <Link className="text-sm font-semibold text-green-700 underline" to={`/portfolio-snapshots/${summary.snapshotId}`}>ดู Published Snapshot</Link>
                </div>
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-slate-200 text-sm">
                        <thead className="bg-slate-50 text-left text-slate-600"><tr><th className="px-4 py-3">ประเภท</th><th className="px-4 py-3 text-right">สัญญา</th><th className="px-4 py-3 text-right">มียอดหนี้</th><th className="px-4 py-3 text-right">ยอดคงเหลือ</th></tr></thead>
                        <tbody className="divide-y divide-slate-100">{summary.contractTypes.map((type) => <tr key={type.prefix}><td className="px-4 py-3 font-medium">{type.name} ({type.prefix})</td><td className="px-4 py-3 text-right">{formatCount(type.contractCount)}</td><td className="px-4 py-3 text-right">{formatCount(type.outstandingContractCount)}</td><td className="px-4 py-3 text-right">{formatMoney(type.totalOutstanding)}</td></tr>)}</tbody>
                    </table>
                </div>
            </Card>
            <DashboardCharts contractTypes={summary.contractTypes} />
        </div>
    );
}
