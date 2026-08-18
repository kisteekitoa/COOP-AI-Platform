import { useEffect, useState } from "react";
import { AlertTriangle, FileSpreadsheet, Landmark, ShieldCheck, Users, Wallet } from "lucide-react";

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

function formatGeneratedAt(value: string | null | undefined) {
    if (!value)
        return "--";

    const generatedAt = new Date(value);
    if (Number.isNaN(generatedAt.getTime()))
        return "--";

    return new Intl.DateTimeFormat("th-TH", {
        dateStyle: "medium",
        timeStyle: "medium",
    }).format(generatedAt);
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
                const data = await getDashboardSummary();
                setSummary(data);
            } catch {
                setError("ไม่สามารถโหลดข้อมูลสรุปได้ กรุณาลองใหม่อีกครั้ง");
            } finally {
                setIsLoading(false);
            }
        }

        loadSummary();
    }, []);

    const loadingValue = "กำลังโหลด...";
    const contractTypes = summary?.contractTypes ?? [];

    return (
        <div className="space-y-6">
            <div className="grid grid-cols-1 gap-6 md:grid-cols-2 xl:grid-cols-3">
                <KPICard
                    title="จำนวนสัญญาทั้งหมด"
                    value={isLoading ? loadingValue : formatCount(summary?.totalContracts)}
                    subtitle={summary ? "ไม่รวมรายการที่ถูกลบ" : " "}
                    icon={FileSpreadsheet}
                />

                <KPICard
                    title="สัญญาที่ยังมีหนี้"
                    value={isLoading ? loadingValue : formatCount(summary?.outstandingContracts)}
                    subtitle={summary ? "ยอดคงเหลือรวมมากกว่าศูนย์" : " "}
                    icon={ShieldCheck}
                />

                <KPICard
                    title="จำนวนสมาชิกทั้งหมด"
                    value={isLoading ? loadingValue : formatCount(summary?.totalMembers)}
                    subtitle={summary ? "ไม่รวมสมาชิกที่ถูกลบ" : " "}
                    icon={Users}
                />

                <KPICard
                    title="เงินต้นคงเหลือ"
                    value={isLoading ? loadingValue : formatMoney(summary?.principalBalance)}
                    subtitle={summary ? "รวมจากสัญญาที่ไม่ถูกลบ" : " "}
                    icon={Landmark}
                />

                <KPICard
                    title="ผลตอบแทนคงเหลือ"
                    value={isLoading ? loadingValue : formatMoney(summary?.profitBalance)}
                    subtitle={summary ? "รวมจากสัญญาที่ไม่ถูกลบ" : " "}
                    icon={Wallet}
                />

                <KPICard
                    title="ยอดคงเหลือรวม"
                    value={isLoading ? loadingValue : formatMoney(summary?.totalBalance)}
                    subtitle={summary ? "รวมจากสัญญาที่ไม่ถูกลบ" : " "}
                    icon={AlertTriangle}
                />
            </div>

            {error ? (
                <div className="bg-red-50 border border-red-200 text-red-700 rounded-xl p-6">
                    <p className="font-semibold">เกิดข้อผิดพลาด</p>
                    <p className="mt-2">{error}</p>
                </div>
            ) : null}

            {!error && isLoading ? (
                <Card>
                    <p className="text-center text-slate-500">กำลังโหลดข้อมูลภาพรวมสินเชื่อ...</p>
                </Card>
            ) : null}

            {!error && summary ? (
                <>
                    <Card>
                        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                            <div>
                                <p className="text-sm text-slate-500">สัญญาที่มียอดคงเหลือเป็นศูนย์</p>
                                <p className="mt-2 text-3xl font-bold text-slate-900">
                                    {formatCount(summary.zeroBalanceContracts)}
                                </p>
                            </div>
                            <p className="text-sm text-slate-500">
                                ข้อมูล ณ เวลา {formatGeneratedAt(summary.generatedAt)}
                            </p>
                        </div>
                    </Card>

                    <Card>
                        <SectionTitle title="ภาพรวมสินเชื่อแยกตามประเภท" />

                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-slate-200 text-sm">
                                <thead className="bg-slate-50 text-left text-slate-600">
                                    <tr>
                                        <th className="px-4 py-3 font-semibold">ประเภทสินเชื่อ</th>
                                        <th className="px-4 py-3 font-semibold">รหัส</th>
                                        <th className="px-4 py-3 text-right font-semibold">จำนวนสัญญา</th>
                                        <th className="px-4 py-3 text-right font-semibold">สัญญาที่ยังมีหนี้</th>
                                        <th className="px-4 py-3 text-right font-semibold">เงินต้นคงเหลือ</th>
                                        <th className="px-4 py-3 text-right font-semibold">ผลตอบแทนคงเหลือ</th>
                                        <th className="px-4 py-3 text-right font-semibold">ยอดคงเหลือรวม</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-100">
                                    {contractTypes.length > 0 ? contractTypes.map((contractType) => (
                                        <tr key={contractType.prefix} className="text-slate-700">
                                            <td className="whitespace-nowrap px-4 py-3 font-medium text-slate-900">
                                                {contractType.name}
                                            </td>
                                            <td className="whitespace-nowrap px-4 py-3">{contractType.prefix}</td>
                                            <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">
                                                {formatCount(contractType.contractCount)}
                                            </td>
                                            <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">
                                                {formatCount(contractType.outstandingContractCount)}
                                            </td>
                                            <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">
                                                {formatMoney(contractType.principalBalance)}
                                            </td>
                                            <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">
                                                {formatMoney(contractType.profitBalance)}
                                            </td>
                                            <td className="whitespace-nowrap px-4 py-3 text-right font-medium tabular-nums">
                                                {formatMoney(contractType.totalBalance)}
                                            </td>
                                        </tr>
                                    )) : (
                                        <tr>
                                            <td colSpan={7} className="px-4 py-8 text-center text-slate-500">
                                                ไม่พบข้อมูลสัญญาสินเชื่อสำหรับแสดงผล
                                            </td>
                                        </tr>
                                    )}
                                </tbody>
                            </table>
                        </div>
                    </Card>

                    <DashboardCharts contractTypes={contractTypes} />
                </>
            ) : null}

        </div>
    );
}
