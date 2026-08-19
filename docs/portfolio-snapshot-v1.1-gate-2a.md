# Portfolio Snapshot V1.1 — Gate 2A Review Workflow

## Authorized workflow

Gate 2A supports `upload → validate in memory → create Draft atomically → review → validate or reject`. It does not publish, switch Dashboard data, read a network share, or modify Import Engine data.

Validation and draft creation always read the uploaded controlled copy independently. Draft creation requires the source-file and snapshot-content hashes returned by validation; both are recomputed before any entity is added to the context.

## Persistence and idempotency

A valid snapshot header, all 4,454 authoritative records, and all quarantined exclusions are saved in one explicit database transaction and one `SaveChangesAsync` call. Any exception rolls the transaction back and clears tracked snapshot entities. Canonical `LoanContracts`, `Members`, and Import tables are read-only inputs.

The persisted identity is `(SourceFileHash, AsOfDate, DefinitionVersion, SnapshotContentHash)`. An existing Draft or Validated snapshot is returned instead of duplicated. An existing Rejected or Published identity is a conflict; Gate 2A has no reprocess or publish implementation.

## Lifecycle

Allowed transitions are:

- `Draft → Validated`
- `Draft → Rejected`
- `Validated → Rejected`

Persisted validation recalculates record/header reconciliations, the content hash, financial totals, status totals, warning totals, unresolved-member totals, shadow exclusions, and the one-InTerm-contract-per-stable-member invariant without rereading the mutable upload.

`Published` and `Superseded` remain future schema values only. The publish endpoint is hard-disabled and performs no write regardless of frontend state.

## Review privacy

Review responses expose formal normalized ContractNo values, Excel row numbers, approved statuses, warning codes, and financial values. They never expose member names, addresses, phone numbers, citizen IDs, or malformed shadow ContractNo text. Shadow review is aggregate-only by reason code.

## Rehearsal boundary

The real `Loan.xlsx` rehearsal may persist only to an isolated relational test database seeded with read-only copies of canonical contract/member identities. No Gate 2A migration or snapshot is applied to production.
