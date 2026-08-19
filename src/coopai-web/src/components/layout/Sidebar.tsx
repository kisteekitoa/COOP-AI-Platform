import {
    LayoutDashboard,
    Users,
    Wallet,
    Landmark,
    ShieldCheck,
    FileSpreadsheet,
    BarChart3,
    Bot,
    Settings,
    ClipboardCheck,
    LogOut,
    UserRound
} from "lucide-react";
import { useState } from "react";
import { useNavigate } from "react-router-dom";

import { useAuth } from "../../auth/useAuth";

const menus = [
    { icon: LayoutDashboard, title: "Dashboard", path: "/" },
    { icon: Users, title: "สมาชิก", path: "/members" },
    { icon: Wallet, title: "สินเชื่อ", path: "/loans" },
    { icon: Landmark, title: "เงินฝาก", path: "/savings" },
    { icon: ShieldCheck, title: "ผู้ค้ำประกัน", path: "/guarantors" },
    { icon: FileSpreadsheet, title: "Import Excel", path: "/import" },
    { icon: ClipboardCheck, title: "ตรวจทาน Snapshot", path: "/portfolio-snapshots" },
    { icon: BarChart3, title: "รายงาน", path: "/reports" },
    { icon: Bot, title: "AI Assistant", path: "/assistant" },
    { icon: Settings, title: "ตั้งค่า", path: "/settings" },
];

export default function Sidebar() {
    const { user, logout } = useAuth();
    const navigate = useNavigate();
    const [isLoggingOut, setIsLoggingOut] = useState(false);

    async function handleLogout() {
        setIsLoggingOut(true);
        try {
            await logout();
            navigate("/login", { replace: true });
        } finally {
            setIsLoggingOut(false);
        }
    }

    return (
        <aside className="w-64 bg-green-800 text-white h-screen flex flex-col shadow-xl">

            <div className="p-6 border-b border-green-700">

                <h1 className="text-2xl font-bold">
                    COOP-AI
                </h1>

                <p className="text-sm text-green-200 mt-1">
                    Smart Cooperative Platform
                </p>

            </div>

            <nav className="flex-1 mt-5">

                {menus.map((item) => {

                    const Icon = item.icon;

                    return (

                        <button
                            key={item.title}
                            type="button"
                            onClick={() => {
                                if (item.path) {
                                    window.location.href = item.path;
                                }
                            }}
                            className="flex items-center w-full gap-4 px-6 py-3 hover:bg-green-700 transition"
                        >

                            <Icon size={20} />

                            <span>{item.title}</span>

                        </button>

                    );

                })}

            </nav>

            <div className="border-t border-green-700 p-4">
                <div className="flex items-center gap-3 rounded-xl bg-green-900/50 px-3 py-3">
                    <UserRound aria-hidden="true" size={20} className="shrink-0 text-green-200" />
                    <div className="min-w-0 flex-1">
                        <p className="truncate text-sm font-semibold">{user?.displayName || user?.userName}</p>
                        <p className="truncate text-xs text-green-200">{user?.roles.join(", ") || "ไม่มีบทบาท"}</p>
                    </div>
                </div>
                <button
                    type="button"
                    disabled={isLoggingOut}
                    onClick={() => void handleLogout()}
                    className="mt-3 flex w-full items-center justify-center gap-2 rounded-xl border border-green-600 px-4 py-2 text-sm font-medium transition hover:bg-green-700 disabled:opacity-50"
                >
                    <LogOut aria-hidden="true" size={18} />
                    {isLoggingOut ? "กำลังออกจากระบบ..." : "ออกจากระบบ"}
                </button>
            </div>

        </aside>
    );
}
