import Card from "../ui/Card";
import SectionTitle from "../ui/SectionTitle";

const insights = [
    {
        icon: "✅",
        message: "วันนี้มีสมาชิกใหม่ 18 ราย",
    },
    {
        icon: "📈",
        message: "เงินฝากเพิ่มขึ้น 1.8%",
    },
    {
        icon: "⚠️",
        message: "มีลูกหนี้ใกล้ครบกำหนด 6 ราย",
    },
    {
        icon: "💡",
        message: "ยังไม่พบความเสี่ยงผิดปกติ",
    },
];

export default function AIInsight() {
    return (
        <Card>

            <SectionTitle
                title="AI Insight"
            />

            <div className="space-y-3">

                {insights.map((item) => (

                    <div
                        key={item.message}
                        className="
                            flex
                            items-start
                            gap-3
                            rounded-lg
                            border
                            border-slate-200
                            bg-slate-50
                            p-3
                            transition-colors
                            hover:bg-green-50
                        "
                    >

                        <span className="text-xl">
                            {item.icon}
                        </span>

                        <p className="text-sm text-slate-700">
                            {item.message}
                        </p>

                    </div>

                ))}

            </div>

        </Card>
    );
}