import type {
    MonthlyCollectionDashboard,
    MonthlyPerformancePeriod,
    WorkQueueSummary,
} from "../services/debtSegmentationService";

export const workQueueTerms = {
    collection: "งานติดตามเรียกเก็บ",
    review: "งานตรวจสอบข้อมูล",
    noPayment: "ไม่ชำระงวดปัจจุบัน",
    currentShortfall: "ชำระไม่ครบภาระงวดปัจจุบัน",
} as const;

export function workQueueMoneySemantics(summary: WorkQueueSummary) {
    return {
        collection: {
            amount: summary.collectionOutstanding,
            amountLabel: "ยอดหนี้คงเหลือของกลุ่ม",
        },
        review: {
            amount: summary.reviewOnlyAmount,
            amountLabel: "ยอดชำระที่ยังจำแนกไม่ได้",
        },
        currentShortfall: {
            amount: summary.currentShortfallAmount,
            amountLabel: "ยอดขาดชำระของภาระงวดปัจจุบัน",
        },
    } as const;
}

export function advanceCreditPresentation(
    allocation: NonNullable<MonthlyCollectionDashboard["paymentAllocation"]>,
) {
    return {
        total: allocation.newAdvanceCreditAmount,
        contractCount: allocation.newAdvanceCreditContracts,
        fromAllocatedExcess: allocation.trueExcessPayment,
        fromBeforeDue: allocation.advancePaymentAmount,
        reconciles: allocation.trueExcessPayment + allocation.advancePaymentAmount === allocation.newAdvanceCreditAmount,
    } as const;
}

export function missingDownPaymentEvidenceState(count: number) {
    return count === 0
        ? { resolved: true, label: "หลักฐานเงินดาวน์ Source B ครบแล้ว" }
        : { resolved: false, label: "ขาดหลักฐานเงินดาวน์จาก Source B" };
}

export function formatThaiDataPeriod(period: string | null) {
    if (!period) return "ไม่ทราบงวดข้อมูล";
    const [yearText, monthText] = period.slice(0, 7).split("-");
    const year = Number(yearText);
    const month = Number(monthText);
    const thaiMonths = [
        "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
        "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม",
    ];
    if (!Number.isInteger(year) || month < 1 || month > 12)
        return period.slice(0, 7);
    return `${thaiMonths[month - 1]} ${year + 543}`;
}

export function moneyMovementReconciles(month: MonthlyPerformancePeriod) {
    if (month.openingOutstanding == null ||
        month.increaseDuringPeriod == null)
        return null;
    return month.openingOutstanding + month.increaseDuringPeriod - month.actualPayment === month.endingOutstanding;
}

export function contractMovementReconciles(month: MonthlyPerformancePeriod) {
    if (month.openingOutstandingContracts == null ||
        month.newOutstandingContracts == null ||
        month.reducedOutstandingContracts == null)
        return null;
    return month.openingOutstandingContracts + month.newOutstandingContracts - month.reducedOutstandingContracts ===
        month.endingOutstandingContracts;
}
