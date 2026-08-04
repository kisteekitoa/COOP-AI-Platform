import type { LucideProps } from "lucide-react";
import type { ComponentType } from "react";

type Props = {
    title: string;
    value: string;
    subtitle: string;
    icon: ComponentType<LucideProps>;
};

export default function KPICard({
    title,
    value,
    subtitle,
    icon: Icon,
}: Props) {
    return (
        <div className="bg-white rounded-xl shadow-sm border p-6">
            <div className="flex justify-between items-start">
                <div>
                    <p className="text-sm text-slate-500">
                        {title}
                    </p>

                    <h2 className="text-3xl font-bold mt-2">
                        {value}
                    </h2>

                    <p className="text-green-600 text-sm mt-3">
                        {subtitle}
                    </p>
                </div>

                <div className="w-12 h-12 rounded-xl bg-green-100 flex items-center justify-center">
                    <Icon
                        size={26}
                        className="text-green-700"
                    />
                </div>
            </div>
        </div>
    );
}