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
    ClipboardCheck
} from "lucide-react";

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

        </aside>
    );
}
