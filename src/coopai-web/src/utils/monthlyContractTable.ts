export const monthlyContractPageSizes = [50, 100, 200] as const;

export type MonthlyContractPageSize = (typeof monthlyContractPageSizes)[number];

export const remainingOrOverdueFilterOptions = [
    { value: "", label: "ทั้งหมด" },
    { value: "overdue-all", label: "หมดอายุ / เกินกำหนดทั้งหมด" },
    { value: "overdue-10-years-or-more", label: "เกินกำหนด 10 ปีขึ้นไป" },
    { value: "overdue-5-to-10-years", label: "เกินกำหนด 5–10 ปี" },
    { value: "overdue-3-to-5-years", label: "เกินกำหนด 3–5 ปี" },
    { value: "overdue-1-to-3-years", label: "เกินกำหนด 1–3 ปี" },
    { value: "overdue-up-to-1-year", label: "เกินกำหนดไม่เกิน 1 ปี" },
    { value: "due-this-month", label: "ครบกำหนดเดือนนี้" },
    { value: "remaining-1-to-6-months", label: "คงเหลือ 1–6 เดือน" },
    { value: "remaining-7-to-12-months", label: "คงเหลือ 7–12 เดือน" },
    { value: "remaining-1-to-3-years", label: "คงเหลือ 1–3 ปี" },
    { value: "remaining-more-than-3-years", label: "คงเหลือมากกว่า 3 ปี" },
    { value: "not-due", label: "ยังไม่ถึงงวดแรก" },
    { value: "paid-off", label: "ชำระหมดแล้ว" },
] as const;

export type RemainingOrOverdueFilter =
    (typeof remainingOrOverdueFilterOptions)[number]["value"];

export interface MonthlyContractTableQuery {
    remainingOrOverdueFilter: RemainingOrOverdueFilter;
    search: string;
    page: number;
    pageSize: MonthlyContractPageSize;
    sortRemainingMonthsDescending: boolean;
}

export const initialMonthlyContractTableQuery: MonthlyContractTableQuery = {
    remainingOrOverdueFilter: "",
    search: "",
    page: 1,
    pageSize: 50,
    sortRemainingMonthsDescending: false,
};

export function withRemainingOrOverdueFilter(
    query: MonthlyContractTableQuery,
    remainingOrOverdueFilter: RemainingOrOverdueFilter,
): MonthlyContractTableQuery {
    return { ...query, remainingOrOverdueFilter, page: 1 };
}

export function withContractSearch(
    query: MonthlyContractTableQuery,
    search: string,
): MonthlyContractTableQuery {
    return { ...query, search, page: 1 };
}

export function withMonthlyContractPageSize(
    query: MonthlyContractTableQuery,
    pageSize: MonthlyContractPageSize,
): MonthlyContractTableQuery {
    return { ...query, pageSize, page: 1 };
}

export function resultRange(page: number, pageSize: number, totalCount: number) {
    if (totalCount === 0) return { first: 0, last: 0 };
    const first = ((page - 1) * pageSize) + 1;
    return { first, last: Math.min(page * pageSize, totalCount) };
}
