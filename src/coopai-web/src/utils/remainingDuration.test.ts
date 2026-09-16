import assert from "node:assert/strict";
import test from "node:test";

import { formatRemainingOrOverdueDuration } from "./remainingDuration.ts";

const cases: Array<[number | null, string, string]> = [
    [-383, "Expired", "เกินกำหนด 31 ปี 11 เดือน"],
    [-367, "Expired", "เกินกำหนด 30 ปี 7 เดือน"],
    [-24, "Expired", "เกินกำหนด 2 ปี"],
    [-13, "Expired", "เกินกำหนด 1 ปี 1 เดือน"],
    [-12, "Expired", "เกินกำหนด 1 ปี"],
    [-1, "Expired", "เกินกำหนด 1 เดือน"],
    [0, "Active/InTerm", "ครบกำหนดเดือนนี้"],
    [1, "Active/InTerm", "คงเหลือ 1 เดือน"],
    [11, "Active/InTerm", "คงเหลือ 11 เดือน"],
    [12, "Active/InTerm", "คงเหลือ 1 ปี"],
    [13, "Active/InTerm", "คงเหลือ 1 ปี 1 เดือน"],
    [24, "Active/InTerm", "คงเหลือ 2 ปี"],
    [null, "PaidOff", "ชำระหมดแล้ว"],
    [24, "NotDue", "ยังไม่ถึงงวดแรก"],
];

test("formats remaining and overdue durations without changing the raw month value", () => {
    for (const [months, status, expected] of cases)
        assert.equal(formatRemainingOrOverdueDuration(months, status), expected);
});
