# Portfolio Snapshot V1.1 — Gate 2B-2 Manager Publish

Gate 2B-2 adds only the production-safe governance transition from a persisted `Validated` Portfolio Snapshot to `Published`. Dashboard V1 continues to read its existing source, Import Engine is unchanged, network ingestion remains out of scope, repository publishing defaults remain disabled, and no migration or snapshot is written to production in this gate.

## Authorization and confirmation

`POST /api/portfolio-snapshots/{id}/publish` requires the existing `ManagerOnly` policy. `Manager` is explicit; `Admin`, `LoanOfficer`, and `Viewer` do not inherit it. The stable integer publisher ID is read from the authenticated server principal and never accepted from request JSON. When publishing is enabled, the cookie-authenticated request must pass antiforgery validation and contain explicit confirmation plus the content hash reviewed by the Manager.

## Lifecycle and server gates

The final lifecycle is `Draft -> Validated -> Published`, with an existing current `Published` snapshot transitioning to terminal history state `Superseded`. `Rejected` remains terminal. Direct Draft publish, republishing Published/Superseded/Rejected snapshots, stale hash review, blocking errors, and failed persisted reconciliation are conflicts.

Before changing state, the server reloads the persisted header, records, and exclusions. It verifies lifecycle, zero blocking errors, source/header population and status counts, canonical/member counts, warning and shadow-exclusion counts, financial aggregates/remainders, the one-InTerm-contract-per-stable-member rule, and a newly computed canonical content hash. The approved warning and unresolved-member inclusion model is non-blocking; Publish does not reread Excel.

Review exposes `CanPublish` and `PublishBlockedReasons` derived from the same server rules. Authorization remains independently enforced even when review reports eligibility.

## Atomicity, one-current invariant, and concurrency

Publish runs in one serializable relational transaction. The workflow loads and verifies the target, finds the existing current Published snapshot, then—if present—marks it Superseded with time and replacement snapshot ID. It saves that mutation inside the still-uncommitted transaction, marks the target Published with immutable time and stable publisher ID, saves, and commits. Any validation, concurrency, database, or injected failure rolls the entire transaction back, keeping the previous Published snapshot live.

Application logic selects at most one current row and the database adds a unique filtered index over `Status = 'Published'`. A numeric concurrency token changes on publish/supersede transitions. Unique-index and optimistic-concurrency failures become structured publish conflicts and are not retried. Publishing the same target again fails before audit fields can be overwritten.

Each request also observes the current Published snapshot ID and concurrency version before entering the serializable transaction. The transaction rechecks that observation after it has obtained serialization. Two overlapping requests that reviewed the same publication generation therefore cannot both succeed sequentially: the loser returns `ConcurrentPublishDetected` or `PublishConflict`. A later request that starts after the prior publish committed observes the new generation and may intentionally replace it.

## Audit and migration scope

The additive migration is limited to Portfolio Snapshot governance columns, an Identity publisher foreign key with no-action deletion, a self-reference to the replacing snapshot with no-action deletion, the concurrency token, and current-Published index. It must not alter member, loan, import, or Dashboard business tables and must not be applied to production in this gate.

## Frontend workflow

Only an authenticated Manager sees an enabled publish action when server review reports `CanPublish`. The Thai confirmation summarizes AsOfDate, contract/warning/unresolved counts, and outstanding principal/profit/total. It states that the dataset is approved as a Published Snapshot and that Dashboard adoption is a later integration step. A submission lock prevents double clicks; backend transaction and concurrency controls remain authoritative.

## Verification and activation boundary

Focused relational tests cover role denial, disabled configuration, lifecycle, confirmation/hash/integrity, warnings/unresolved inclusion, stable publisher audit, first publish, supersession, idempotency, one-current enforcement, concurrency, rollback after supersede, double submission, and protected-table boundaries.

Verification completed on 2026-08-24 (Asia/Bangkok):

- 21 focused Publish tests passed, including separate-connection concurrency with one winner, explicit API outcome/confirmation tests, and rollback after the prior current snapshot had been saved inside the uncommitted transaction.
- The complete backend suite passed: 244 passed, 0 failed, 0 skipped.
- `npm.cmd run lint` and `npm.cmd run build` passed; Vite retained only its existing bundle-size advisory.
- EF reported no pending model changes after migration generation.
- The additive migration `20260823091434_AddPortfolioSnapshotGate2B2Publish` was inspected and was not applied to production.
- The read-only local `Loan.xlsx` rehearsal ran against isolated SQLite with a synthetic canonical identity set: 4,454 contracts, 4,447 canonical matches, 7 missing/unresolved, 8 warnings, and 327 shadow exclusions reconciled. First publish succeeded; a synthetic newer Validated snapshot superseded it; exactly one current Published row remained.
- No production connection was opened and no production read or write occurred. The protected local configuration was not used to obtain a connection.

Production activation remains separate and requires controlled migration deployment, durable Data Protection keys, exact HTTPS/reverse-proxy and CORS topology, the approved bootstrap/account lifecycle process, a real Manager assignment, operational monitoring/recovery, and an explicit approval to set `PortfolioSnapshots:PublishingEnabled=true`. Dashboard V1.1 remains separate work: select only the current Published snapshot and expose its quality/date/source semantics without fallback to mutable drafts.
