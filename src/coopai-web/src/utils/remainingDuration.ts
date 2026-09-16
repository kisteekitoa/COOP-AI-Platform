export function formatRemainingOrOverdueDuration(
    months: number | null,
    contractStatus: string,
): string {
    if (contractStatus === "PaidOff") return "ชำระหมดแล้ว";
    if (contractStatus === "NotDue") return "ยังไม่ถึงงวดแรก";
    if (months == null) return "ไม่พบวันสิ้นสุดสัญญา";
    if (months === 0) return "ครบกำหนดเดือนนี้";

    const absoluteMonths = Math.abs(months);
    const years = Math.floor(absoluteMonths / 12);
    const remainingMonths = absoluteMonths % 12;
    const parts = [
        years > 0 ? `${years} ปี` : "",
        remainingMonths > 0 ? `${remainingMonths} เดือน` : "",
    ].filter(Boolean).join(" ");
    return months < 0 ? `เกินกำหนด ${parts}` : `คงเหลือ ${parts}`;
}
