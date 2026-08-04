import {
    FileSpreadsheet,
    UserPlus,
    Wallet,
    BarChart3,
    Bot,
} from "lucide-react";

import Card from "../ui/Card";
import SectionTitle from "../ui/SectionTitle";

const actions = [
    {
        title: "Import Excel",
        icon: FileSpreadsheet,
    },
    {
        title: "เพิ่มสมาชิก",
        icon: UserPlus,
    },
    {
        title: "อนุมัติสินเชื่อ",
        icon: Wallet,
    },
    {
        title: "รายงาน",
        icon: BarChart3,
    },
    {
        title: "AI Assistant",
        icon: Bot,
    },
];

export default function QuickActions() {
    return (
        <Card>

            <SectionTitle
                title="Quick Actions"
            />

            <div className="grid grid-cols-2 gap-4">

                {actions.map((item) => {

                    const Icon = item.icon;

                    return (

                        <button
                            key={item.title}
                            type="button"
                            className="
                                flex flex-col items-center gap-3
                                rounded-xl border p-5
                                transition-all
                                hover:border-green-600
                                hover:bg-green-50
                                hover:shadow-md
                            "
                        >

                            <Icon
                                size={28}
                                className="text-green-700"
                            />

                            <span className="font-medium text-slate-700">
                                {item.title}
                            </span>

                        </button>

                    );

                })}

            </div>

        </Card>
    );
}