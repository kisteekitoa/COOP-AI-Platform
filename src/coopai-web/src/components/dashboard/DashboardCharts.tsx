import Chart from "react-apexcharts";
import type { ApexOptions } from "apexcharts";

import Card from "../ui/Card";
import SectionTitle from "../ui/SectionTitle";
import type { DashboardContractTypeDto } from "../../services/dashboardService";

type Props = {
    contractTypes: DashboardContractTypeDto[];
};

const numberFormatter = new Intl.NumberFormat("th-TH");
const moneyFormatter = new Intl.NumberFormat("th-TH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
});

function createChartOptions(categories: string[], formatValue: (value: number) => string): ApexOptions {
    return {
        chart: {
            toolbar: {
                show: false,
            },
            zoom: {
                enabled: false,
            },
        },
        colors: ["#047857"],
        dataLabels: {
            enabled: false,
        },
        grid: {
            borderColor: "#E2E8F0",
        },
        plotOptions: {
            bar: {
                borderRadius: 4,
                barHeight: "58%",
                horizontal: true,
            },
        },
        tooltip: {
            y: {
                formatter: formatValue,
            },
        },
        xaxis: {
            categories,
            labels: {
                formatter: formatValue,
            },
        },
        yaxis: {
            labels: {
                maxWidth: 360,
            },
        },
    };
}

export default function DashboardCharts({ contractTypes }: Props) {
    const categories = contractTypes.map(
        (contractType) => `${contractType.prefix} — ${contractType.name}`
    );
    const contractCountOptions = createChartOptions(
        categories,
        (value) => numberFormatter.format(value)
    );
    const balanceOptions = createChartOptions(
        categories,
        (value) => `${moneyFormatter.format(value)} บาท`
    );
    const contractCountSeries = [{
        name: "จำนวนสัญญา",
        data: contractTypes.map((contractType) => contractType.contractCount),
    }];
    const balanceSeries = [{
        name: "ยอดคงเหลือรวม",
        data: contractTypes.map((contractType) => contractType.totalOutstanding),
    }];
    const chartHeight = Math.max(360, contractTypes.length * 72);

    if (contractTypes.length === 0) {
        return (
            <Card>
                <p className="text-center text-slate-500">ไม่พบข้อมูลประเภทสินเชื่อสำหรับสร้างกราฟ</p>
            </Card>
        );
    }

    return (
        <div className="grid grid-cols-1 gap-6">
            <Card>
                <SectionTitle title="จำนวนสัญญาตามประเภทสินเชื่อ" />

                <Chart
                    options={contractCountOptions}
                    series={contractCountSeries}
                    type="bar"
                    height={chartHeight}
                />
            </Card>

            <Card>
                <SectionTitle title="ยอดคงเหลือตามประเภทสินเชื่อ" />

                <Chart
                    options={balanceOptions}
                    series={balanceSeries}
                    type="bar"
                    height={chartHeight}
                />
            </Card>
        </div>
    );
}
