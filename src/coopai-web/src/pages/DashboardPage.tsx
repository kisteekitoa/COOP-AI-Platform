import { useEffect, useState } from "react";
import { Users, Wallet, Landmark, AlertTriangle } from "lucide-react";

import KPICard from "../components/dashboard/KPICard";
import QuickActions from "../components/dashboard/QuickActions";
import NotificationPanel from "../components/dashboard/NotificationPanel";
import DashboardCharts from "../components/dashboard/DashboardCharts";
import RecentActivity from "../components/dashboard/RecentActivity";
import AIInsight from "../components/dashboard/AIInsight";
import { getDashboardSummary, type DashboardSummaryDto } from "../services/dashboardService";

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

    const totalLoanContracts = summary ? summary.TotalLoanContracts.toLocaleString() : "--";
    const principalBalance = summary
        ? `${summary.PrincipalBalance.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} บาท`
        : "--";
    const profitBalance = summary
        ? `${summary.ProfitBalance.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} บาท`
        : "--";
    const totalBalance = summary
        ? `${summary.TotalBalance.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} บาท`
        : "--";

    return (
        <div className="space-y-6">

            {/* KPI Cards */}
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-6">

                <KPICard
                    title="สัญญาสินเชื่อทั้งหมด"
                    value={isLoading ? "กำลังโหลด..." : totalLoanContracts}
                    subtitle={summary ? "อัปเดตล่าสุด" : " "}
                    icon={Users}
                />

                <KPICard
                    title="ยอดคงเหลือทุน"
                    value={isLoading ? "กำลังโหลด..." : principalBalance}
                    subtitle={summary ? "รวมทุนคงเหลือ" : " "}
                    icon={Landmark}
                />

                <KPICard
                    title="ยอดคงเหลือกำไร"
                    value={isLoading ? "กำลังโหลด..." : profitBalance}
                    subtitle={summary ? "รวมกำไรคงเหลือ" : " "}
                    icon={Wallet}
                />

                <KPICard
                    title="ยอดคงเหลือรวม"
                    value={isLoading ? "กำลังโหลด..." : totalBalance}
                    subtitle={summary ? "รวมยอดทั้งหมด" : " "}
                    icon={AlertTriangle}
                />

            </div>

            {error ? (
                <div className="bg-red-50 border border-red-200 text-red-700 rounded-xl p-6">
                    <p className="font-semibold">เกิดข้อผิดพลาด</p>
                    <p className="mt-2">{error}</p>
                </div>
            ) : null}

            {/* Quick Actions + Notification */}
            <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">

                <QuickActions />

                <NotificationPanel />

            </div>

            {/* Charts */}
            <DashboardCharts />

            {/* Bottom */}
            <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">

                <RecentActivity />

                <AIInsight />

            </div>

        </div>
    );
}