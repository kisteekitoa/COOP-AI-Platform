# Portfolio Snapshot V1.1 — Gate 1 Contract

## Scope and safety

Gate 1 builds and validates an in-memory snapshot from a controlled, read-only copy of `Loan.xlsx` and read-only canonical `LoanContracts`/`Members` queries. It does not persist, publish, switch Dashboard data, or change Import Engine behavior.

The approved snapshot date is 30 June B.E. 2569, represented internally and hashed in explicit Gregorian form as `2026-06-30`. Contract status is never evaluated against the current system date.

## Authoritative population

- `PortfolioSourceRowKind.Contract`: a real Excel row with exactly one populated opening side (G:I or J:L).
- `PortfolioSourceRowKind.TemplatePlaceholder`: a template row with neither opening side populated.
- `PortfolioSourceRowKind.Unknown`: a row that cannot safely be classified; this blocks validation.
- The G/J opening-side choice is retained independently as `PortfolioOpeningSide`; it is not a business active/inactive status.
- The 327 malformed/person-name production rows are quarantined as shadow exclusions and never enter the Excel-authoritative contract population.

Approved June 2026 population: 5,084 source rows = 4,454 contracts + 630 placeholders. Canonical linkage is 4,447 matched + 7 missing. Missing canonical rows remain included with null `MemberId`, `MemberMatchStatus.Missing`, and `IncludedWithWarning`.

## Independent business dimensions

`PortfolioTermStatus` is based only on Excel column F and `Snapshot.AsOfDate`:

- `InTerm`: expiry date is on or after `AsOfDate`.
- `Expired`: expiry date is before `AsOfDate`.
- `Unknown`: expiry is missing, invalid, or unprovable.

`PortfolioBalanceStatus` is based only on authoritative Excel outstanding columns CZ:DB:

- `Outstanding`: outstanding total is greater than zero.
- `PaidOff`: outstanding total equals zero.
- `Unknown`: the value is unavailable, invalid, or negative and therefore does not satisfy either approved definition.

Warnings are independent of both statuses and never remove a contract from counts or financial totals. The audited warning KPI counts the seven `Negative` rows plus one `TotalBalanceMismatch` row; linkage warnings are exposed separately through canonical/member match metrics.

## Member invariant

Member identity is proven only by a canonical contract's `MemberId` plus exact normalized `MemberNo`. Names are never used. A stable `MemberId` may have multiple expired historical contracts but at most one `InTerm` contract. Multiple `InTerm` contracts for one stable identity block validation and report only the stable member ID and contract numbers. Unresolved rows do not participate in this proof and are reported separately.

## Locked reconciliation

- Term: 2,559 InTerm + 1,895 Expired = 4,454.
- Balance: 3,897 Outstanding + 557 PaidOff = 4,454.
- Cross-status: 2,394 InTerm/Outstanding + 165 InTerm/PaidOff + 1,503 Expired/Outstanding + 392 Expired/PaidOff = 4,454.
- Outstanding: principal 219,547,383.55; profit 85,068,837.45; total 304,616,221.00.
- Expired/Outstanding: 1,503 contracts; outstanding total 46,520,515.00.
- Data quality: 8 audited warnings, 7 unresolved member links, and 327 excluded production shadows.
