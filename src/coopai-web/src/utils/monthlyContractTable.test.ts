import assert from "node:assert/strict";
import test from "node:test";

import {
    initialMonthlyContractTableQuery,
    monthlyContractPageSizes,
    remainingOrOverdueFilterOptions,
    resultRange,
    withContractSearch,
    withMonthlyContractPageSize,
    withRemainingOrOverdueFilter,
} from "./monthlyContractTable.ts";

test("all required remaining/overdue presets are available", () => {
    assert.deepEqual(remainingOrOverdueFilterOptions.map((option) => option.label), [
        "ทั้งหมด",
        "หมดอายุ / เกินกำหนดทั้งหมด",
        "เกินกำหนด 10 ปีขึ้นไป",
        "เกินกำหนด 5–10 ปี",
        "เกินกำหนด 3–5 ปี",
        "เกินกำหนด 1–3 ปี",
        "เกินกำหนดไม่เกิน 1 ปี",
        "ครบกำหนดเดือนนี้",
        "คงเหลือ 1–6 เดือน",
        "คงเหลือ 7–12 เดือน",
        "คงเหลือ 1–3 ปี",
        "คงเหลือมากกว่า 3 ปี",
        "ยังไม่ถึงงวดแรก",
        "ชำระหมดแล้ว",
    ]);
});

test("filter and search changes reset pagination", () => {
    const laterPage = { ...initialMonthlyContractTableQuery, page: 8 };

    assert.equal(withRemainingOrOverdueFilter(laterPage, "overdue-all").page, 1);
    assert.equal(withContractSearch(laterPage, " 123 ").page, 1);
});

test("page-size selector supports 50, 100, and 200 and resets pagination", () => {
    assert.deepEqual(monthlyContractPageSizes, [50, 100, 200]);
    for (const pageSize of monthlyContractPageSizes) {
        const changed = withMonthlyContractPageSize(
            { ...initialMonthlyContractTableQuery, page: 5 }, pageSize);
        assert.equal(changed.pageSize, pageSize);
        assert.equal(changed.page, 1);
    }
});

test("result ranges cover first, middle, and last pages", () => {
    assert.deepEqual(resultRange(1, 50, 176), { first: 1, last: 50 });
    assert.deepEqual(resultRange(2, 50, 176), { first: 51, last: 100 });
    assert.deepEqual(resultRange(4, 50, 176), { first: 151, last: 176 });
    assert.deepEqual(resultRange(1, 50, 0), { first: 0, last: 0 });
});
