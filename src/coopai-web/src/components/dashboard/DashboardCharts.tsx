import Chart from "react-apexcharts";
import type { ApexOptions } from "apexcharts";

import Card from "../ui/Card";
import SectionTitle from "../ui/SectionTitle";

const chartOptions: ApexOptions = {
    chart: {
        toolbar: {
            show: false,
        },
        zoom: {
            enabled: false,
        },
    },
    stroke: {
        curve: "smooth",
        width: 3,
    },
    dataLabels: {
        enabled: false,
    },
    xaxis: {
        categories: [
            "ม.ค.",
            "ก.พ.",
            "มี.ค.",
            "เม.ย.",
            "พ.ค.",
            "มิ.ย.",
        ],
    },
    grid: {
        borderColor: "#E2E8F0",
    },
};

const loanSeries = [
    {
        name: "สินเชื่อ",
        data: [302, 306, 309, 312, 319, 327],
    },
];

const depositSeries = [
    {
        name: "เงินฝาก",
        data: [430, 441, 452, 466, 474, 485],
    },
];

export default function DashboardCharts() {
    return (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">

            <Card>

                <SectionTitle
                    title="แนวโน้มสินเชื่อ"
                />

                <Chart
                    options={chartOptions}
                    series={loanSeries}
                    type="line"
                    height={320}
                />

            </Card>

            <Card>

                <SectionTitle
                    title="แนวโน้มเงินฝาก"
                />

                <Chart
                    options={chartOptions}
                    series={depositSeries}
                    type="area"
                    height={320}
                />

            </Card>

        </div>
    );
}