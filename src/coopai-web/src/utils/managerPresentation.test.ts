import assert from "node:assert/strict";
import test from "node:test";

import {
    advanceCreditPresentation,
    contractMovementReconciles,
    formatThaiDataPeriod,
    missingDownPaymentEvidenceState,
    moneyMovementReconciles,
    workQueueMoneySemantics,
} from "./managerPresentation.ts";

test("Work Queue amounts retain their distinct money semantics", () => {
    const presentation = workQueueMoneySemantics({
        collectionOutstanding: 129_985_125,
        reviewOnlyAmount: 3_662_915,
        currentShortfallAmount: 12_345,
    } as never);

    assert.deepEqual(presentation.collection, {
        amount: 129_985_125,
        amountLabel: "ยอดหนี้คงเหลือของกลุ่ม",
    });
    assert.deepEqual(presentation.review, {
        amount: 3_662_915,
        amountLabel: "ยอดชำระที่ยังจำแนกไม่ได้",
    });
    assert.equal(presentation.currentShortfall.amountLabel, "ยอดขาดชำระของภาระงวดปัจจุบัน");
});

test("New advance credit is the authoritative parent of existing components", () => {
    const presentation = advanceCreditPresentation({
        newAdvanceCreditAmount: 243_148,
        newAdvanceCreditContracts: 224,
        trueExcessPayment: 239_048,
        advancePaymentAmount: 4_100,
    } as never);

    assert.equal(presentation.total, 243_148);
    assert.equal(presentation.fromAllocatedExcess + presentation.fromBeforeDue, presentation.total);
    assert.equal(presentation.reconciles, true);
});

test("resolved Source B evidence uses a positive state", () => {
    assert.deepEqual(missingDownPaymentEvidenceState(0), {
        resolved: true,
        label: "หลักฐานเงินดาวน์ Source B ครบแล้ว",
    });
    assert.equal(missingDownPaymentEvidenceState(2).resolved, false);
});

test("trusted period formatting follows the supplied month", () => {
    assert.equal(formatThaiDataPeriod("2026-08-01"), "สิงหาคม 2569");
    assert.equal(formatThaiDataPeriod("2026-09-01"), "กันยายน 2569");
});

test("movement helpers verify the two manager identities without zero-filling unknown opening", () => {
    const known = {
        openingOutstanding: 1_100,
        increaseDuringPeriod: 500,
        actualPayment: 200,
        endingOutstanding: 1_400,
        openingOutstandingContracts: 2,
        newOutstandingContracts: 1,
        reducedOutstandingContracts: 1,
        endingOutstandingContracts: 2,
    } as never;
    assert.equal(moneyMovementReconciles(known), true);
    assert.equal(contractMovementReconciles(known), true);

    const unknown = {
        ...known,
        openingOutstanding: null,
        increaseDuringPeriod: null,
        openingOutstandingContracts: null,
        newOutstandingContracts: null,
        reducedOutstandingContracts: null,
    } as never;
    assert.equal(moneyMovementReconciles(unknown), null);
    assert.equal(contractMovementReconciles(unknown), null);
});
