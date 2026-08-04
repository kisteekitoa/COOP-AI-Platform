import Card from "../ui/Card";
import SectionTitle from "../ui/SectionTitle";

const activities = [
    {
        title: "สมาชิกใหม่",
        detail: "สมัครสมาชิกใหม่ 18 ราย",
        time: "10 นาทีที่แล้ว",
    },
    {
        title: "นำเข้า Excel",
        detail: "Import ข้อมูลสินเชื่อสำเร็จ",
        time: "30 นาทีที่แล้ว",
    },
    {
        title: "เงินฝาก",
        detail: "ยอดเงินฝากเพิ่มขึ้น 1.8%",
        time: "วันนี้",
    },
    {
        title: "สินเชื่อ",
        detail: "อนุมัติสินเชื่อ 6 สัญญา",
        time: "วันนี้",
    },
];

export default function RecentActivity() {
    return (
        <Card>

            <SectionTitle
                title="Recent Activity"
            />

            <div className="space-y-4">

                {activities.map((item) => (

                    <div
                        key={`${item.title}-${item.time}`}
                        className="
                            border-b
                            border-slate-200
                            pb-4
                            last:border-none
                            last:pb-0
                        "
                    >

                        <h3 className="font-semibold text-slate-800">
                            {item.title}
                        </h3>

                        <p className="mt-1 text-sm text-slate-600">
                            {item.detail}
                        </p>

                        <span className="mt-2 block text-xs text-slate-400">
                            {item.time}
                        </span>

                    </div>

                ))}

            </div>

        </Card>
    );
}