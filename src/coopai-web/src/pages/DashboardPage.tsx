import { useEffect, useState } from "react";
import {
    AlertTriangle,
    CalendarDays,
    CheckCircle2,
    CircleDollarSign,
    FileCheck2,
    FileSpreadsheet,
    Landmark,
    Link2Off,
    ShieldAlert,
    ShieldCheck,
    TimerOff,
    Wallet,
} from "lucide-react";
import { Link } from "react-router-dom";

import KPICard from "../components/dashboard/KPICard";
import DashboardCharts from "../components/dashboard/DashboardCharts";
import Card from "../components/ui/Card";
import SectionTitle from "../components/ui/SectionTitle";
import { getDashboardSummary, type DashboardSummaryDto } from "../services/dashboardService";

const countFormatter = new Intl.NumberFormat("th-TH");
const moneyFormatter = new Intl.NumberFormat("th-TH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
});

function formatCount(value: number | null | undefined) {
    return value == null ? "--" : countFormatter.format(value);
}

function formatMoney(value: number | null | undefined) {
    return value == null ? "--" : `${moneyFormatter.format(value)} บาท`;
}

function formatBusinessDate(value: string | null | undefined) {
    if (!value)
        return "--";
    const date = new Date(`${value}T00:00:00Z`);
    return new Intl.DateTimeFormat("th-TH", {
        day: "numeric",
        month: "short",
        year: "numeric",
        timeZone: "UTC",
    }).format(date);
}

function formatTimestamp(value: string | null | undefined) {
    if (!value)
        return "--";
    const date = new Date(value);
    if (Number.isNaN(date.getTime()))
        return "--";
    return new Intl.DateTimeFormat("th-TH", {
        dateStyle: "medium",
        timeStyle: "short",
    }).format(date);
}

function CrossStatusCard({ title, value, prominent = false }: {
    title: string;
    value: number | null;
    prominent?: boolean;
}) {
    return (
        <div className={`rounded-xl border p-5 ${prominent ? "border-amber-300 bg-amber-50" : "border-slate-200 bg-white"}`}>
            <p className={prominent ? "font-semibold text-amber-900" : "text-sm text-slate-600"}>{title}</p>
            <p className={`mt-2 text-3xl font-bold ${prominent ? "text-amber-900" : "text-slate-900"}`}>{formatCount(value)}</p>
        </div>
    );
}

export default function DashboardPage() {
    const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        async function loadSummary() {
            setIsLoading(true);
            setError(null);
            try {
                setSummary(await getDashboardSummary());
            } catch {
                setError("ไม่สามารถโหลดข้อมูลสรุปได้ กรุณาลองใหม่อีกครั้ง");
            } finally {
                setIsLoading(false);
            }
        }
        void loadSummary();
    }, []);

    if (error) {
        return (
            <div className="rounded-xl border border-red-200 bg-red-50 p-6 text-red-700">
                <p className="font-semibold">เกิดข้อผิดพลาด</p>
                <p className="mt-2">{error}</p>
            </div>
        );
    }

    if (isLoading || !summary) {
        return <Card><p className="text-center text-slate-500">กำลังโหลดข้อมูลภาพรวมสินเชื่อ...</p></Card>;
    }

    if (!summary.hasPublishedSnapshot) {
        return (
            <Card className="border-amber-300 bg-amber-50">
                <div className="flex flex-col items-center px-2 py-10 text-center">
                    <FileCheck2 size={48} className="text-amber-700" aria-hidden="true" />
                    <h1 className="mt-4 text-2xl font-bold text-amber-950">ยังไม่มีชุดข้อมูล Portfolio ที่เผยแพร่สำหรับ Dashboard</h1>
                    <p className="mt-3 max-w-2xl text-amber-900">Dashboard จะไม่ใช้ข้อมูล LoanContracts แทนโดยอัตโนมัติ โปรดให้ผู้จัดการตรวจทานและอนุมัติ Published Snapshot ก่อน</p>
                </div>
            </Card>
        );
    }

    const contractTypes = summary.contractTypes;
    return (
        <div className="space-y-6">
            <Card className="border-green-200 bg-green-50">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                    <div>
                        <p className="flex items-center gap-2 font-semibold text-green-900"><ShieldCheck size={20} /> ข้อมูลจาก Portfolio Snapshot ที่เผยแพร่แล้ว</p>
                        <p className="mt-2 text-sm text-green-800">ข้อมูล ณ วันที่ {formatBusinessDate(summary.asOfDate)} · Published Snapshot #{summary.snapshotId}</p>
                    </div>
                    <div className="grid gap-1 text-sm text-green-900 sm:text-right">
                        <span>เผยแพร่เมื่อ {formatTimestamp(summary.publishedAt)}</span>
                        <span>เรียกดูเมื่อ {formatTimestamp(summary.generatedAt)}</span>
                        <span>{summary.isReconciled ? "ตรวจสอบยอดและจำนวนครบถ้วน" : "พบสถานะการกระทบยอดที่ต้องตรวจสอบ"}</span>
                    </div>
                </div>
            </Card>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-5">
                <KPICard title="สัญญาทั้งหมด" value={formatCount(summary.totalContracts)} subtitle="ประชากรสัญญาใน Snapshot" icon={FileSpreadsheet} />
                <KPICard title="อยู่ในระยะสัญญา" value={formatCount(summary.inTermContracts)} subtitle="TermStatus: InTerm" icon={CalendarDays} />
                <KPICard title="หมดระยะสัญญา" value={formatCount(summary.expiredContracts)} subtitle="ไม่หมายถึงสัญญาไม่ใช้งาน" icon={TimerOff} />
                <KPICard title="มียอดคงเหลือ" value={formatCount(summary.outstandingContracts)} subtitle="BalanceStatus: Outstanding" icon={ShieldAlert} />
                <KPICard title="ชำระหมดแล้ว" value={formatCount(summary.paidOffContracts)} subtitle="BalanceStatus: PaidOff" icon={CheckCircle2} />
            </div>

            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                <KPICard title="เงินต้นคงเหลือ" value={formatMoney(summary.principalOutstanding)} subtitle="ยอด Outstanding จาก Snapshot" icon={Landmark} />
                <KPICard title="ผลตอบแทนคงเหลือ" value={formatMoney(summary.profitOutstanding)} subtitle="ยอด Outstanding จาก Snapshot" icon={Wallet} />
                <KPICard title="ยอดคงเหลือรวม" value={formatMoney(summary.totalOutstanding)} subtitle="เงินต้น + ผลตอบแทน" icon={CircleDollarSign} />
            </div>

            <Card className="border-amber-300 bg-amber-50">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                    <div>
                        <p className="flex items-center gap-2 text-lg font-bold text-amber-950"><AlertTriangle size={22} /> หมดระยะสัญญาแล้ว แต่ยังมีหนี้คงเหลือ</p>
                        <p className="mt-1 text-sm text-amber-900">รายการสำคัญสำหรับการติดตามของฝ่ายบริหาร</p>
                    </div>
                    <div className="grid gap-1 sm:grid-cols-2 sm:gap-8 lg:text-right">
                        <div><p className="text-sm text-amber-800">จำนวนสัญญา</p><p className="text-3xl font-bold text-amber-950">{formatCount(summary.expiredOutstandingContracts)}</p></div>
                        <div><p className="text-sm text-amber-800">ยอดคงเหลือรวม</p><p className="text-2xl font-bold text-amber-950">{formatMoney(summary.expiredOutstandingBalance)}</p></div>
                    </div>
                </div>
            </Card>

            <Card>
                <SectionTitle title="สถานะสัญญาแบบสองมิติ" />
                <p className="mb-4 text-sm text-slate-600">ระยะสัญญาและยอดคงเหลือเป็นคนละมิติ จึงต้องพิจารณาร่วมกันทั้งสี่กลุ่ม</p>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                    <CrossStatusCard title="อยู่ในระยะ + มียอดคงเหลือ" value={summary.inTermOutstandingContracts} />
                    <CrossStatusCard title="อยู่ในระยะ + ชำระหมดแล้ว" value={summary.inTermPaidOffContracts} />
                    <CrossStatusCard title="หมดระยะ + มียอดคงเหลือ" value={summary.expiredOutstandingContracts} prominent />
                    <CrossStatusCard title="หมดระยะ + ชำระหมดแล้ว" value={summary.expiredPaidOffContracts} />
                </div>
            </Card>

            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                <KPICard title="รายการมีคำเตือน" value={formatCount(summary.warningContracts)} subtitle="รวมอยู่ในยอดการเงินตามนโยบาย" icon={AlertTriangle} />
                <KPICard title="สัญญาที่ยังเชื่อมสมาชิกไม่ได้" value={formatCount(summary.unresolvedMemberContracts)} subtitle="ไม่ใช่จำนวนสมาชิกทั้งหมด" icon={Link2Off} />
                <KPICard title="Shadow ที่แยกออก" value={formatCount(summary.shadowExcludedContracts)} subtitle="ข้อมูลคุณภาพ ไม่ใช่สัญญาเพิ่ม" icon={ShieldCheck} />
            </div>

            <Card>
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                        <p className="font-semibold text-slate-900">การเชื่อมโยงข้อมูล Canonical</p>
                        <p className="mt-1 text-sm text-slate-600">เชื่อมโยงแล้ว {formatCount(summary.canonicalMatchedContracts)} · ไม่พบ {formatCount(summary.canonicalMissingContracts)}</p>
                        <p className="text-sm text-slate-600">แถวแม่แบบ {formatCount(summary.templatePlaceholderRows)} จากแถวต้นทาง {formatCount(summary.totalSourceRows)}</p>
                    </div>
                    <Link className="text-sm font-semibold text-green-700 underline" to={`/portfolio-snapshots/${summary.snapshotId}`}>ดูรายละเอียด Snapshot และคำเตือน</Link>
                </div>
            </Card>

            <Card>
                <SectionTitle title="ภาพรวมสินเชื่อแยกตามประเภท" />
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-slate-200 text-sm">
                        <thead className="bg-slate-50 text-left text-slate-600">
                            <tr>
                                <th className="px-4 py-3 font-semibold">ประเภทสินเชื่อ</th><th className="px-4 py-3 font-semibold">รหัส</th>
                                <th className="px-4 py-3 text-right font-semibold">สัญญา</th><th className="px-4 py-3 text-right font-semibold">มีหนี้</th><th className="px-4 py-3 text-right font-semibold">ชำระหมด</th>
                                <th className="px-4 py-3 text-right font-semibold">ในระยะ</th><th className="px-4 py-3 text-right font-semibold">หมดระยะ</th>
                                <th className="px-4 py-3 text-right font-semibold">เงินต้น</th><th className="px-4 py-3 text-right font-semibold">ผลตอบแทน</th><th className="px-4 py-3 text-right font-semibold">รวม</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100">
                            {contractTypes.map((type) => (
                                <tr key={type.prefix} className="text-slate-700">
                                    <td className="whitespace-nowrap px-4 py-3 font-medium text-slate-900">{type.name}</td><td className="px-4 py-3">{type.prefix}</td>
                                    <td className="px-4 py-3 text-right tabular-nums">{formatCount(type.contractCount)}</td><td className="px-4 py-3 text-right tabular-nums">{formatCount(type.outstandingContractCount)}</td><td className="px-4 py-3 text-right tabular-nums">{formatCount(type.paidOffContractCount)}</td>
                                    <td className="px-4 py-3 text-right tabular-nums">{formatCount(type.inTermCount)}</td><td className="px-4 py-3 text-right tabular-nums">{formatCount(type.expiredCount)}</td>
                                    <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">{formatMoney(type.principalOutstanding)}</td><td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">{formatMoney(type.profitOutstanding)}</td><td className="whitespace-nowrap px-4 py-3 text-right font-semibold tabular-nums">{formatMoney(type.totalOutstanding)}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </Card>

            <DashboardCharts contractTypes={contractTypes} />
        </div>
    );
}
