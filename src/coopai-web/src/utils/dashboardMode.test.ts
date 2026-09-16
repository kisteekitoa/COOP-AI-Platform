import assert from "node:assert/strict";
import test from "node:test";

import { DEFAULT_DASHBOARD_MODE, dashboardSummaryParams } from "./dashboardMode.ts";

test("dashboard defaults explicitly to CURRENT", () => {
    assert.equal(DEFAULT_DASHBOARD_MODE, "CURRENT");
    assert.deepEqual(dashboardSummaryParams(), { mode: "CURRENT" });
});

test("dashboard supports an explicit PUBLISHED query without mutation", () => {
    assert.deepEqual(dashboardSummaryParams("PUBLISHED"), { mode: "PUBLISHED" });
    assert.deepEqual(dashboardSummaryParams(), { mode: "CURRENT" });
});
