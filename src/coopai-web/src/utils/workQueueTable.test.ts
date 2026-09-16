import assert from "node:assert/strict";
import test from "node:test";

import {
    initialWorkQueueQuery,
    updateWorkQueueFilter,
    updateWorkQueuePageSize,
    workQueuePageSizes,
    workQueueResultRange,
} from "./workQueueTable.ts";

test("every Work Queue filter and search change resets page one", () => {
    const paged = { ...initialWorkQueueQuery, page: 7 };
    for (const key of ["queueType", "priority", "debtBucket", "payment", "contractStatus", "reason", "search"] as const)
        assert.equal(updateWorkQueueFilter(paged, key, "value").page, 1);
});

test("Work Queue page sizes are exactly 50, 100, and 200", () => {
    assert.deepEqual(workQueuePageSizes, [50, 100, 200]);
    for (const size of workQueuePageSizes) {
        const changed = updateWorkQueuePageSize({ ...initialWorkQueueQuery, page: 4 }, size);
        assert.equal(changed.page, 1);
        assert.equal(changed.pageSize, size);
    }
});

test("Work Queue result ranges cover first, middle, last, and empty pages", () => {
    assert.deepEqual(workQueueResultRange(1, 50, 176), { first: 1, last: 50 });
    assert.deepEqual(workQueueResultRange(2, 50, 176), { first: 51, last: 100 });
    assert.deepEqual(workQueueResultRange(4, 50, 176), { first: 151, last: 176 });
    assert.deepEqual(workQueueResultRange(1, 50, 0), { first: 0, last: 0 });
});
