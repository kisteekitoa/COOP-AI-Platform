# COOP-AI Dashboard V1.1 - Published Portfolio Snapshot

Dashboard V1.1 moves the management portfolio reporting boundary from mutable `LoanContracts` and `Members` to the sole current `PortfolioSnapshot` whose lifecycle status is `Published`. It does not publish data, change lifecycle rules, modify Import Engine behavior, apply a migration, or activate production.

## Source of truth and operational boundary

Portfolio population, independent term and balance dimensions, cross-status populations, outstanding financials, warning/linkage quality, exclusions, and contract-type breakdowns come only from the current Published snapshot and its records. Selection uses `Status == Published`, not creation time. Draft, Validated, Rejected, Failed, and Superseded snapshots are never Dashboard sources.

Dashboard V1 had no live follow-up queue, officer assignment, or operational action; all existing cards and charts were portfolio reporting. Therefore, no existing Dashboard element retains a `LoanContracts` or `Members` query. Future operational widgets may use live mutable tables only when explicitly identified as live workflow data and visually separated from snapshot reporting.

## No-Published behavior

When no current Published snapshot exists, the API returns `hasPublishedSnapshot=false`, nullable portfolio and financial fields, an empty contract-type collection, and request generation time. It never falls back to `LoanContracts` and never represents missing data as a real zero portfolio. The frontend displays a clear Thai no-published-dataset state and omits portfolio KPI cards, tables, and charts.

## API and KPI definitions

`GET /api/dashboard/summary` is a read-only authenticated endpoint. The V1.1 response exposes `dataSource = "Published Portfolio Snapshot"`, snapshot ID, reporting date (`AsOfDate`), `PublishedAt`, response generation time, persisted populations and financial totals, reconciliation status, data-quality counts, and a grouped record projection.

Term and balance remain independent dimensions:

- `InTerm` and `Expired` describe the contractual term.
- `Outstanding` and `PaidOff` describe the authoritative balance state.
- The four cross-status populations are exposed separately; Expired + Outstanding is emphasized for management attention and is not described as inactive.

Financial cards use only `PrincipalOutstanding`, `ProfitOutstanding`, `TotalOutstanding`, and `ExpiredOutstandingTotal`. Opening, repayment, legacy `LoanContract` balances, and loan amount are not Dashboard inputs. All calculations retain the project's exact decimal money semantics.

The organization-wide Members count was removed because the mutable Members table is not the reporting snapshot's authoritative population. The Dashboard instead exposes the specifically qualified unresolved-member-link count. Warnings and unresolved rows remain included financially under publication governance; they are data-quality information, not an invalid-Dashboard state.

Contract-type breakdowns use one read-only projection of records belonging to the selected Published snapshot, followed by deterministic exact-decimal grouping. Each type exposes total, outstanding, paid-off, InTerm, and Expired counts plus outstanding principal, profit, and total. Unknown prefixes are grouped defensively without exposing contract numbers or other PII.

## Frontend and responsive behavior

The page retains the COOP-AI visual language while adding a Published Snapshot banner, reporting date distinct from publication and response times, quality indicators, independent-dimension KPIs, prominent Expired + Outstanding treatment, four-way status cards, and responsive contract-type reporting. Cards collapse on mobile, and wide breakdown content remains horizontally scrollable.

## Locked real-file result

The isolated `Loan.xlsx` publish rehearsal returned these values through `DashboardService` from the newly Published persisted snapshot; none are hard-coded in application code:

| Metric | Result |
|---|---:|
| Contracts | 4,454 |
| Within-term | 2,559 |
| Expired | 1,895 |
| Outstanding | 3,897 |
| Paid-off | 557 |
| Expired + Outstanding | 1,503 |
| Principal outstanding | 219,547,383.55 |
| Profit/return outstanding | 85,068,837.45 |
| Total receivable | 304,616,221.00 |
| Warnings | 8 |
| Unresolved member links | 7 |
| Shadow excluded | 327 |

Financial reconciliation passed exactly: `219,547,383.55 + 85,068,837.45 = 304,616,221.00`.

## Verification and production safety

Verification completed on 2026-08-24:

- Focused Dashboard tests: 22/22 passed, including retained classifier coverage.
- Isolated real-file Dashboard and Gate 2B-2 publish/supersede rehearsal: 1/1 passed.
- Portfolio Snapshot regressions excluding separately run real-file tests: 66/66 passed.
- Authentication regressions: 23/23 passed.
- Import regressions: 109/109 passed.
- Full backend suite: 247/247 passed.
- Frontend lint and production build: passed; the build retains the advisory for a bundle over 500 kB.
- EF pending-model check: no pending model changes.

The rehearsal used isolated SQLite and did not open or write a production database. No migration was added or applied, publishing was not enabled, protected local files were not modified, and no file was staged, committed, tagged, or pushed.

Production activation remains a separate change-controlled task: apply already-approved migrations where required; configure durable Data Protection and exact HTTPS/CORS topology; bootstrap and assign approved accounts and roles; complete the controlled production smoke procedure; explicitly approve publishing enablement; publish the reviewed production snapshot; and only then deploy or enable Dashboard V1.1 against that current Published dataset.
