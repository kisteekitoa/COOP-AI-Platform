import Card from "../ui/Card";
import SectionTitle from "../ui/SectionTitle";

const notifications = [
    "Import Excel วันนี้ยังไม่ดำเนินการ",
    "มีสมาชิกใหม่รออนุมัติ 5 ราย",
    "ลูกหนี้ครบกำหนดภายใน 7 วัน จำนวน 6 ราย",
];

export default function NotificationPanel() {
    return (
        <Card>

            <SectionTitle
                title="Notification"
            />

            <div className="space-y-4">

                {notifications.map((item) => (

                    <div
                        key={item}
                        className="
                            rounded-lg
                            border
                            border-amber-200
                            bg-amber-50
                            p-4
                            transition-colors
                            hover:bg-amber-100
                        "
                    >
                        <p className="text-sm text-slate-700">
                            {item}
                        </p>
                    </div>

                ))}

            </div>

        </Card>
    );
}