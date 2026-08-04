import { Users, Wallet, Landmark, AlertTriangle } from "lucide-react";

import KPICard from "../components/dashboard/KPICard";
import QuickActions from "../components/dashboard/QuickActions";
import NotificationPanel from "../components/dashboard/NotificationPanel";
import DashboardCharts from "../components/dashboard/DashboardCharts";
import RecentActivity from "../components/dashboard/RecentActivity";
import AIInsight from "../components/dashboard/AIInsight";

export default function DashboardPage() {
    return (
        <div className="space-y-6">

            {/* KPI Cards */}
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-6">

                <KPICard
                    title="สมาชิกทั้งหมด"
                    value="12,540"
                    subtitle="+18 วันนี้"
                    icon={Users}
                />

                <KPICard
                    title="เงินฝาก"
                    value="485.6 ล้านบาท"
                    subtitle="+1.8%"
                    icon={Landmark}
                />

                <KPICard
                    title="สินเชื่อ"
                    value="327.0 ล้านบาท"
                    subtitle="+4.2%"
                    icon={Wallet}
                />

                <KPICard
                    title="NPF"
                    value="1.82%"
                    subtitle="อยู่ในเกณฑ์ดี"
                    icon={AlertTriangle}
                />

            </div>

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