# Portfolio Snapshot V1.1 handoff after Gate 2A

Audit date: 2026-08-19 (Asia/Bangkok). This is a static repository handoff. It does not enable Gate 2B, publish data, apply migrations, access the future network source, or change production state.

## 1. Current Git checkpoint

- Current branch: `refactor/import-engine`.
- `HEAD`: `a75e38ac9acf6347a27ff7266d8fa40a4d521a37`, subject `feat: complete portfolio snapshot v1.1 gate 2a`.
- Annotated tag `portfolio-snapshot-v1.1-gate-2a` targets the same commit.
- Annotated tag `dashboard-v1` targets `3aa058958855112559726365c5140b0b8cd81537`, subject `feat(dashboard): add verified loan portfolio dashboard v1`.
- Audit-start tracked modification: `src/COOPAI.API/appsettings.json`. It is protected production configuration and was not inspected, printed, restored, staged, or changed.
- Audit-start untracked items: `Loan.xlsx`, `artifacts/`, and `docs/production-loan-import-baseline-2026-08-17.md`. They were not touched.
- Audit-start staged set: empty.
- Import Engine status: no modified or untracked Import Engine files.

## 2. Architecture map

### Portfolio Snapshot

- API: `Controllers/PortfolioSnapshotsController.cs` exposes `GET /api/portfolio-snapshots`, `POST /validate`, `POST /drafts`, `POST /{id}/validate`, `GET /{id}/review`, `GET /{id}/records`, `POST /{id}/reject`, and the disabled `POST /{id}/publish`.
- Contract: `IPortfolioSnapshotWorkflowService` and `PortfolioSnapshotWorkflowService` coordinate validate, draft persistence, review, record filtering, lifecycle transitions, and listing.
- Source abstraction: `IPortfolioSnapshotSource`; current implementation `ExcelPortfolioSnapshotSource` reads a controlled local `.xlsx` copy.
- Domain processing: `SnapshotContractNoNormalizer`, `PortfolioSnapshotDryRunService`, `PortfolioSnapshotValidator`, `PortfolioSnapshotContentHasher`, source models, and `PortfolioSnapshotAcceptanceBaseline`.
- DTOs: `DTOs/PortfolioSnapshots/PortfolioSnapshotDtos.cs` contains upload, validation, draft, review, list, records, lifecycle, quality, financial, warning, exclusion, and error contracts.
- Entities: `PortfolioSnapshot`, `PortfolioSnapshotRecord`, `PortfolioSnapshotExclusion`, plus status/code enums.
- Dependencies: read-only canonical `LoanContracts` and `Members`; EPPlus for Excel; EF Core for canonical lookup and Gate 2A persistence.
- Registration: `Program.cs` registers `IPortfolioSnapshotSource -> ExcelPortfolioSnapshotSource` and `IPortfolioSnapshotWorkflowService -> PortfolioSnapshotWorkflowService` as scoped services.
- Frontend: `services/portfolioSnapshotService.ts` and `pages/PortfolioSnapshotsPage.tsx`; routes `/portfolio-snapshots` and `/portfolio-snapshots/:id`; navigation entry in `components/layout/Sidebar.tsx`.
- Tests: the five Portfolio Snapshot test files listed in section 12.

### Dashboard

- API: `DashboardController.GetSummary` at `GET /api/dashboard/summary`.
- Service: `IDashboardService.GetSummaryAsync` and `DashboardService.GetSummaryAsync`; `DashboardLoanTypeClassifier` groups approved prefixes.
- DTO: `DashboardSummaryDto` and `DashboardContractTypeDto`.
- Current database dependencies: `LoanContracts` and `Members`, both filtered by `!IsDeleted`.
- Registration: `IDashboardService -> DashboardService` in `Program.cs`.
- Frontend: `services/dashboardService.ts`, `pages/DashboardPage.tsx`, `components/dashboard/DashboardCharts.tsx`, and KPI/UI components.
- Route/layout: `/` under `MainLayout`; `Sidebar` links to it.
- Tests: `DashboardServiceTests.cs`.

### Authentication and authorization

- No application authentication/authorization implementation exists. Details are in section 7.
- Controllers, including publish, are anonymous because there are no authorization attributes or fallback policy.

### API host

- ASP.NET Core 8 controllers, SQL Server EF provider, Swagger in Development, permissive named CORS policy, HTTPS redirection, and `/health`.
- `Program.cs` calls neither `AddAuthentication`/`AddAuthorization` nor `UseAuthentication`/`UseAuthorization`.

### EF Core/database

- `CoopDbContext` exposes master, loan, import, and snapshot sets and owns all mappings.
- Gate 2A reads canonical loan/member rows without tracking and writes only snapshot tables when a draft/lifecycle action is explicitly requested.
- A non-building EF inspection (`dotnet ef migrations has-pending-model-changes --no-build`) reported no model changes since the last migration. It did not apply a migration or contact/write the database. Because `--no-build` uses existing build output, rerun after the next model build before generating any Gate 2B migration.

### Frontend routing, layout, and navigation

- `App.tsx -> AppRouter.tsx -> MainLayout.tsx -> Outlet`.
- Routes are `/` (Dashboard), `/import`, `/portfolio-snapshots`, and `/portfolio-snapshots/:id`.
- `Sidebar.tsx` performs full-page navigation with `window.location.href`; there is no route guard or identity-aware menu.
- Axios services use `VITE_API_BASE_URL` or the local API default and do not attach credentials/tokens.

### Import Engine boundary

- Import endpoints and implementation remain in `ImportController`, `IExcelImportService`, `ExcelImportService`, `MemberImporter`, `LoanImporter`, and related `Services/Import` types.
- Import can write `Members`, `LoanContracts`, import records/batches/logs in its own transaction. Snapshot processing does not call Import Engine services and does not modify those tables.
- Snapshot uses `LoanContracts`/`Members` only as read-only canonical evidence. Dashboard V1 also reads them independently.
- Gate 2B and Dashboard V1.1 must not alter Import Engine classification, persistence, endpoints, or tests unless a separately approved task changes that boundary.

## 3. Gate 2A workflow

### End-to-end flow

1. The controller accepts an uploaded `.xlsx`, validates basic file/date presence, copies it to a controlled temporary path, invokes the workflow, and deletes the temporary copy in `finally`.
2. `ExcelPortfolioSnapshotSource.ReadAsync` hashes the exact file bytes with SHA-256, records length/time metadata, reads the required worksheet, detects source rows, and checks that length/last-write time did not change during the read.
3. It classifies each row as contract, template placeholder, or unknown. It selects exactly one opening side, parses dates explicitly (including Buddhist-to-Gregorian normalization), and reads repayment plus authoritative outstanding values from columns CZ:DB.
4. `SnapshotContractNoNormalizer` trims/NFC-normalizes, canonicalizes approved hyphen variants, and accepts only approved formal three-part contract numbers.
5. `PortfolioSnapshotDryRunService` validates rows, rejects duplicate/malformed source identities, reads non-deleted canonical contracts/members without tracking, establishes contract/member linkage, creates records/exclusions, calculates independent term/balance dimensions and aggregates, validates report totals and optional acceptance baseline, then computes the content hash.
6. `ValidateAsync` returns the in-memory result only. It never calls `SaveChanges`.
7. `CreateDraftAsync` rereads and rebuilds independently, requires both expected hashes from validation, checks identity/idempotency, and atomically saves the header, all records, and exclusions as `Draft`.
8. Review/list/record-filter calls are `AsNoTracking` reads. A persisted `Draft` can be revalidated to `Validated`; a `Draft` or `Validated` snapshot can be rejected with an audited reason.

### Statuses and enforced transitions

The enum contains `Draft`, `Validated`, `Failed`, `Rejected`, `Published`, and `Superseded`.

- Dry run uses `Validated` when blocking errors are zero and `Failed` otherwise; these in-memory statuses are mapped to a validation response.
- Persisted creation always changes a valid dry-run result to `Draft`.
- Allowed persisted transitions enforced in `PortfolioSnapshotWorkflowService`: `Draft -> Validated`, `Draft -> Rejected`, `Validated -> Rejected`.
- Every other current transition returns `InvalidSnapshotTransition`. `Published` and `Superseded` have no implementation.

### Transaction and SaveChanges boundaries

- Validate/review/list/records: no transaction and no `SaveChanges`.
- Create draft: one explicit transaction and one `SaveChangesAsync`; any exception rolls back and clears the change tracker. Persistence hooks at header/records/exclusions/before-save stages exist for atomicity tests.
- Validate draft: persisted reconciliation first, then one explicit transaction, one `SaveChangesAsync`, and commit.
- Reject: validates a trimmed 3-1000 character reason, then one explicit transaction, one `SaveChangesAsync`, and commit. Records/exclusions remain intact.

### Idempotency and hashes

- Identity is the unique tuple `(SourceFileHash, AsOfDate, DefinitionVersion, SnapshotContentHash)`.
- Existing `Draft`/`Validated`: returned with `WasExisting=true`; no duplicate write.
- Existing `Rejected`: conflict requiring explicit future reprocess behavior. Existing `Published`: immutable conflict.
- Concurrent identical insert: database uniqueness failure is rolled back, the tracker is cleared, and the existing identity is returned if found. A different identity racing for the same `(AsOfDate, Revision)` is not silently merged; its database error is rethrown.
- Source hash: SHA-256 of exact controlled-copy bytes.
- Content hash: SHA-256 of definition, AsOfDate, currency, and canonically ordered business records/statuses/financials/warnings/linkage. It deliberately excludes database IDs, timestamps, source metadata, member IDs, and record order.

### Rejection, exclusions, warnings, and unresolved members

- Rejection records `RejectedAt` and `RejectionReason`; it does not delete snapshot children. There is no `RejectedBy` yet.
- Canonical malformed shadows are stored only as aggregateable exclusions with `LoanContractId` and reason; malformed raw text is not persisted/exposed. Unsupported contract types are excluded under their own reason. The locked shadow KPI counts only malformed shadow exclusions.
- `Negative` and `TotalBalanceMismatch` are audited source warnings. A warning record remains `IncludedWithWarning` and stays in all applicable counts and totals.
- Missing canonical/member links add linkage warning codes but are tracked separately from the audited warning KPI.
- Member linkage requires a unique canonical contract and exact normalized `MemberNo`; names are never matched. Unresolved records keep `MemberId=null`, stay included with warning, and are excluded from the one-InTerm-contract-per-stable-member proof.

### What prevents Publish now

- `POST /api/portfolio-snapshots/{id}/publish` immediately returns HTTP 409 `PublishingDisabled` and does not call a service.
- Review DTO hardcodes `PublishingEnabled=false`; the frontend renders a disabled button and has no publish service function.
- The tracked `appsettings.json` at HEAD has no Portfolio Snapshot publishing section. The protected working file was not inspected. In either case, current code does not read a publishing flag, so configuration cannot enable publish.
- No authenticated manager identity, authorization policy, publish workflow, publish audit fields, current-snapshot invariant, concurrency control, or publish tests exist.

## 4. Database model

### `PortfolioSnapshot`

- PK: `Id` (`int`, identity).
- Unique indexes: `(AsOfDate, Revision)` and `(SourceFileHash, AsOfDate, DefinitionVersion, SnapshotContentHash)`.
- Other indexes: `(Status, AsOfDate)` and `(SourceType, SourceFileHash)`.
- Status: enum stored as `nvarchar(24)`; definition version defaults to 2 in code.
- Source metadata: AsOfDate, revision, currency, source type/name, byte hash, size, retrieval time, source-through date, and content hash.
- Aggregate fields: population, linkage, warning/error/exclusion, independent term/balance/cross-status counts, opening/repayment/outstanding values, reconciliation differences, and expired-outstanding total.
- Money precision: every decimal is `decimal(19,2)`.
- Audit fields present: `CreatedAt`, nullable `ValidatedAt`, nullable `RejectedAt`, and nullable rejection reason (`nvarchar(1000)`).
- Audit fields absent: `PublishedAt`, `PublishedBy`, `RejectedBy`, supersession/current-snapshot metadata, and a concurrency token.
- Relationships: one-to-many records and exclusions; configured `DeleteBehavior.NoAction` from children.

### `PortfolioSnapshotRecord`

- PK: `Id` (`bigint`, identity); required FK `PortfolioSnapshotId`.
- Optional FKs: `LoanContractId` and `MemberId`; all three relationships use `DeleteBehavior.NoAction`.
- Unique index: `(PortfolioSnapshotId, NormalizedContractNo)`.
- Other indexes: `(PortfolioSnapshotId, LoanTypePrefix)`, `(PortfolioSnapshotId, MemberId)`, `(PortfolioSnapshotId, TermStatus, BalanceStatus)`, `(LoanContractId, PortfolioSnapshotId)`, plus the provider-created member FK index.
- Contains source row number/key, normalized formal contract number, dates, row/opening/term/balance/linkage/inclusion statuses, loan prefix, warning JSON, and opening/repayment/outstanding financials.
- All financials are `decimal(19,2)`; status enums are bounded strings.

### `PortfolioSnapshotExclusion`

- PK: `Id` (`bigint`, identity).
- Required FKs: `PortfolioSnapshotId` and `LoanContractId`, both `DeleteBehavior.NoAction`.
- Unique index: `(PortfolioSnapshotId, LoanContractId, ReasonCode)`; provider-created index on `LoanContractId`.
- Stores only a bounded reason code, not malformed source text.

### Migrations

- Gate 1: `20260819023205_AddPortfolioSnapshotV11` creates the three tables, relationships, decimal precision, and initial indexes.
- Gate 2A: `20260819024739_AddPortfolioSnapshotGate2AReview` adds `RejectedAt`, `RejectionReason`, record `SourceRowNumber`, and the unique snapshot identity index.
- Neither migration was applied during this audit.

## 5. Locked business baseline

Repository evidence agrees with every supplied acceptance value; no difference was found.

| Measure | Locked value | Enforcement/evidence |
|---|---:|---|
| Source rows | 5,084 | `PortfolioSnapshotAcceptanceBaseline.June2026`; baseline validator; synthetic and real-file tests |
| Contracts | 4,454 | Same |
| Template placeholders | 630 | Same |
| InTerm / Expired | 2,559 / 1,895 | Same; persisted reconciliation |
| Outstanding / PaidOff | 3,897 / 557 | Same; persisted reconciliation |
| InTerm+Outstanding | 2,394 | Same |
| InTerm+PaidOff | 165 | Same |
| Expired+Outstanding | 1,503 | Same |
| Expired+PaidOff | 392 | Same |
| Negative / TotalBalanceMismatch / total audited warnings | 7 / 1 / 8 | Real-file Gate 2A rehearsal asserts the split; baseline stores total 8 |
| Unresolved members | 7 | Baseline, dry-run tests, real-file validation/rehearsal; `MemberId` remains null and name matching is prohibited |
| Malformed shadows excluded | 327 | Baseline, dry-run tests, real-file validation/rehearsal |
| Outstanding principal | 219,547,383.55 | Baseline and reconciliation tests |
| Outstanding profit | 85,068,837.45 | Same |
| Outstanding total | 304,616,221.00 | Same |
| Expired+Outstanding total | 46,520,515.00 | Same |

The seven unresolved records are included with warnings. They are not silently removed and do not acquire a member through name similarity. Term and balance statuses are independent dimensions.

## 6. Dashboard V1 current data flow

`DashboardPage` calls `getDashboardSummary()` -> Axios `GET /api/dashboard/summary` -> `DashboardController.GetSummary()` -> `DashboardService.GetSummaryAsync()` -> EF Core.

- Contract population and balances come directly from non-deleted `LoanContracts` (`PrincipalBalance`, `ProfitBalance`, `TotalBalance`).
- Organization-member KPI comes directly from the count of non-deleted `Members`.
- Contract-type groups are calculated in memory from `LoanContract.ContractNo` by `DashboardLoanTypeClassifier`.
- `DashboardCharts` charts group contract count and total balance.
- Dashboard V1 does **not** read `PortfolioSnapshot` or `PortfolioSnapshotRecords`.

Files/methods eventually requiring Dashboard V1.1 changes:

- `Services/Dashboard/DashboardService.cs`: `GetSummaryAsync` and breakdown construction must query the current published snapshot and its records.
- `DTOs/Dashboard/DashboardSummaryDto.cs`: add independent term/balance dimensions, snapshot identity/date/source, warnings/linkage/data-quality fields, and correctly separated member KPIs.
- `Controllers/DashboardController.cs`: likely retain the route; adjust response/error semantics for no published snapshot if required.
- `tests/COOPAI.API.Tests/DashboardServiceTests.cs`: replace/add published-snapshot selection, aggregation, stale-draft isolation, and quality tests.
- `src/coopai-web/src/services/dashboardService.ts`: mirror the V1.1 DTO.
- `src/coopai-web/src/pages/DashboardPage.tsx`: render the independent dimensions, AsOfDate/source, quality and incomplete-member indicators.
- `src/coopai-web/src/components/dashboard/DashboardCharts.tsx`: chart snapshot-derived counts/balances and preserve term/balance independence.

## 7. Authentication/authorization audit

- Authentication implemented: **no**.
- Schemes: none; no JWT, cookie, Negotiate/Windows, OpenID Connect, or other handler/package configuration.
- ASP.NET setup: no authentication/authorization service registration or middleware; no roles, policies, claims transforms, authorization attributes, or fallback policy.
- Windows Authentication: repository launch settings explicitly set `windowsAuthentication=false` and `anonymousAuthentication=true`. No repository evidence supports choosing Windows Authentication now.
- IIS/IIS Express: only development launch settings are present; no repository `web.config` provides a production authentication mode.
- Frontend: no login/logout/session/context/token storage, credential attachment, user display, or route guard.
- Current API identity assumption: anonymous caller. The API cannot identify a stable user and cannot prove manager membership.

Manager-only Publish therefore requires an approved identity provider/deployment decision, a stable immutable user identifier, server-side manager membership/claim mapping, authentication and middleware, a manager policy, endpoint authorization, and denial/audit tests. A UI-only manager flag is insufficient.

## 8. Gate 2B gaps

Current endpoint: `POST /api/portfolio-snapshots/{id}/publish`; current behavior is unconditional 409 without database/service access.

- Publishing configuration: no tracked section at HEAD and no consuming code. The protected local configuration remains uninspected. Review is hardcoded false.
- Publish persistence code: none.
- `PublishedAt` / `PublishedBy`: absent.
- Single-current invariant: absent. The schema permits multiple rows with `Status=Published`.
- Concurrent publish protection: absent; no concurrency token, filtered unique current marker, serializable/advisory lock, or equivalent invariant.
- Idempotency: absent for publish.
- Rollback semantics: absent for publish.
- Audit history: snapshot records are structurally retained, but no publish/supersede governance proves who published or preserves a controlled transition history.

Minimum Gate 2B gap list:

1. Select and document the production-supported authentication scheme and stable subject identifier.
2. Implement authentication middleware and a server-side Manager policy; default-deny publish.
3. Add publish/supersede audit fields and decide whether a separate immutable transition/event table is required.
4. Define one-current-published invariant and database-enforced concurrency strategy.
5. Implement `Validated -> Published` and previous `Published -> Superseded` atomically; reject all other source statuses.
6. Define retry/idempotency semantics for already-current, superseded, rejected, and competing snapshots.
7. Ensure failure rolls back both new publish and previous-current supersession.
8. Add `PublishedAt`, stable `PublishedBy`, and correlation/audit evidence without personal display data.
9. Wire a non-secret feature flag with disabled-by-default startup/deployment behavior; never make configuration bypass authorization.
10. Add API, service, relational concurrency, authorization, rollback, and production-safe rehearsal tests before enabling.

## 9. Dashboard V1.1 target

The minimum safe read model is the one current `Published` snapshot (with prior publications `Superseded`) plus its records. Draft, Validated, Rejected, Failed, and a newly invalid/awaiting source must never displace the last successful Published snapshot.

Required contract dimensions, kept independent:

- จำนวนสัญญาทั้งหมด: snapshot total contracts.
- อยู่ในระยะสัญญา: `InTermContractCount`.
- หมดระยะสัญญา: `ExpiredContractCount`.
- มียอดคงเหลือ: `OutstandingContractCount`.
- ยอดคงเหลือศูนย์: `PaidOffContractCount`.
- Preserve all four cross-status counts rather than deriving one dimension from the other.

Financial KPIs must use snapshot `PrincipalOutstanding`, `ProfitOutstanding`, and `TotalOutstanding` (and record outstanding values for grouped breakdowns), never opening/addition balances. The response should carry snapshot ID, AsOfDate, revision, definition version, published timestamp, non-sensitive source identity/hash, warning count, warning/linkage indicators, unresolved-member count, shadow exclusions, blocking/reconciled state, and currency.

Member KPIs must remain separate:

1. สมาชิกทั้งหมด is the organization-wide member population. The current Dashboard counts `Members`, but the repository does not prove that this table is an authoritative complete organization roster rather than loan-derived data. Do not publish this KPI as authoritative until ownership/source is confirmed.
2. สมาชิกที่มีสินเชื่ออยู่ในระยะ/คงค้าง is portfolio-derived: distinct proven `MemberId` values under the explicitly chosen term/balance predicate. Report the predicate, resolved distinct count, unresolved contract count, and an `incomplete=true` indicator while unresolved linkage exists. Do not turn seven unresolved contracts into seven invented members.

Contract-type breakdown should group `PortfolioSnapshotRecords.LoanTypePrefix` and sum authoritative outstanding fields. If no published snapshot exists, return a deliberate no-published-data response/empty state rather than falling back silently to mutable `LoanContracts`.

## 10. Recommended Gate 2B implementation sequence

| Step | Atomic scope and likely files | Required tests | Migration | Failure/rollback and production risk |
|---|---|---|---|---|
| 1. Authentication/identity foundation | Decide supported provider; update `Program.cs`, project packages if required, non-secret config template, and add a small identity abstraction | authenticated/anonymous integration tests; stable subject extraction | No | Keep all business endpoints behavior unchanged; risk high if deployment identity assumptions are wrong |
| 2. Manager policy | Add policy/requirement/handler and protect publish controller action only | 401 anonymous, 403 authenticated non-manager, success path still disabled for manager | No | Default deny on missing/malformed claims; medium risk |
| 3. Publish governance fields | Update snapshot model, `CoopDbContext`, DTOs; consider immutable publish event entity | model/index/precision/audit mapping tests | Yes | Migration must be reversible and additive; high production schema risk |
| 4. Atomic publish transaction | Add publish method to workflow interface/service and replace controller stub; atomically publish selected Validated snapshot and supersede prior current | success, invalid transition, rollback at each persistence stage | Possibly same migration as step 3 | One transaction; any failure leaves previous current unchanged; high risk |
| 5. Concurrency/idempotency | Add database-enforced current invariant/concurrency token and deterministic retry result | parallel competing publish, duplicate retry, stale request | Likely | Never permit two current Published snapshots; high risk |
| 6. Publish test completion | Extend workflow tests and add controller/auth relational integration tests | audit identity/time, previous history, no mutation of records, disabled flag, denial cases | No | Publishing remains disabled; low production risk |
| 7. Production-safe rehearsal | Use isolated relational DB and sanitized canonical copies; exercise migration and publish rollback | full Gate 2B rehearsal and reconciliation | Apply only to isolated DB | Delete isolated DB only; never connect to production; medium operational risk |
| 8. Enable only after acceptance | Add/wire disabled-by-default feature option and deployment checklist | false=409/no writes; true still requires Manager; startup config validation | No | Roll back by disabling flag; authorization remains mandatory; high change-control risk |
| 9. Dashboard V1.1 integration | Change Dashboard backend/DTO/tests and frontend service/page/charts to current Published snapshot | selection, dimensions, outstanding totals, quality, no-published, stale drafts | No unless read-model index needed | Feature switch/fallback must not read unapproved drafts; high business-reporting risk |
| 10. Production reconciliation | Read-only compare published aggregates to locked acceptance and approved source evidence | scripted SELECT/read-only reconciliation evidence | No | Stop on any mismatch; never auto-correct; high data-trust risk |
| 11. Checkpoint | Run status/diff checks, allowlist Gate 2B/V1.1 files, commit/tag locally per approval | final targeted/full tests and build | No | Exclude source workbook, artifacts, secrets, baseline notes; do not push without separate instruction |

## 11. Future network automation integration point

Future only; do not access now:

- Network host: `192.168.0.4`.
- Folder: `pn04 ลูกหนี้เงินกู้สามัญ ปี2569` -> `สินเชื่อ ปัตตานี`.
- Workbook: `ลูกหนี้เงินกู้ทั้งหมด 2569.xlsx`.

Add a future adapter implementing `IPortfolioSnapshotSource` (or a separate acquisition service that produces the controlled local copy consumed by the current source). It should authenticate to the share outside business validation, detect a stable file (size/time/lock checks), copy to controlled storage, hash exact bytes, and then delegate to the unchanged normalization/validation/content-hash pipeline.

Intended flow: staff workbook -> network acquisition/source adapter -> stable controlled copy and SHA-256 -> Validate -> Draft/Review -> Manager Publish -> Dashboard. Acquisition failure, invalid validation, or awaiting approval must leave Dashboard on the last successful Published snapshot. No watcher, polling, network credential handling, or ingestion change belongs in Gate 2B.

## 12. Test inventory

### Dashboard V1: `DashboardServiceTests.cs` (12 methods)

- Approved-prefix display names; shared-only/unsupported/malformed unknown classification; whitespace normalization.
- Deleted-contract exclusion; positive/zero/negative count rules; current balances rather than loan amount.
- Non-deleted member count; multi-prefix/unknown grouping; empty database; group-to-total reconciliation.

### Snapshot source/normalization: `ExcelPortfolioSnapshotSourceTests.cs` (7 methods)

- Authoritative opening/repayment/outstanding extraction and report summary.
- Both opening sides, formula cache absence, and sub-cent precision block validation.
- Buddhist-calendar conversion; canonical hyphen normalization; malformed/unsupported rejection.

### Gate 1/dry run: `PortfolioSnapshotDryRunServiceTests.cs` (15 methods)

- Read-only canonical/member match; seven missing canonical rows retained with warnings/null member.
- 327 malformed shadows excluded without raw text; eight audited warning records remain included.
- Duplicate/malformed source blockers; one InTerm contract per proven member; multiple expired allowed.
- Four independent term/balance combinations; warnings do not alter status; no member-name matching.
- AsOfDate classifier; deterministic content hash; EF indexes/NoAction/precision; locked June 2026 baseline.

### Real source baseline

- `PortfolioSnapshotRealFileValidationTests.ValidateOnly_ControlledLoanWorkbook_ReconcilesLockedBaselineWithoutWrites` validates the real controlled workbook against source/count/status/financial/warning/linkage/shadow expectations and protected-table counts without snapshot persistence.
- `PortfolioSnapshotGate2ARealFileRehearsalTests.RealLoanWorkbook_ValidateCreateReviewAndValidateDraft_UsesIsolatedDatabaseOnly` exercises validate -> create -> review -> validate draft in an isolated relational database, including 7 Negative, 1 TotalBalanceMismatch, 7 unresolved, 327 exclusions, hashes, and publish disabled.

### Gate 2A workflow: `PortfolioSnapshotWorkflowServiceTests.cs` (12 methods)

- Hook failure at every draft stage and database insert failures roll back header/records/exclusions.
- Validate is read-only; draft identity is idempotent for Draft/Validated; Rejected identity conflicts.
- Changed file/wrong source hash/wrong content hash/wrong AsOfDate persist nothing.
- Approved and invalid lifecycle transitions; direct Draft rejection retains children.
- Persisted tampering blocks validation; review/filter privacy and warnings; list excludes Published.
- Publish endpoint returns 409 and makes no workflow call.

### New Gate 2B tests required

- Authentication scheme and stable subject extraction; anonymous 401; non-manager 403; manager policy success.
- Feature disabled returns 409/no writes even for manager; configuration cannot bypass authorization.
- Only Validated can publish; Rejected/Draft/Failed/Superseded invalid transitions.
- First publish; replacement publish atomically supersedes previous; retry idempotency.
- Competing concurrent publishes cannot create two current snapshots.
- Failure injected before/after superseding/publishing/audit save rolls back the entire change.
- `PublishedAt`/stable `PublishedBy` recorded; prior snapshots and children remain auditable/immutable.
- Dashboard selects only current Published, ignores newer Draft/Validated/Rejected, preserves last Published when ingestion fails, and handles no Published snapshot explicitly.
- Dashboard exact independent dimensions, four cross-status cells, authoritative outstanding totals, source/AsOfDate/quality fields, and incomplete portfolio-member KPI.
- Migration up/down on isolated DB, model snapshot consistency, production-like read-only reconciliation, and Import Engine regression boundary.

## 13. Safety boundaries

- Do not implement/enable Gate 2B before authentication, authorization, governance, concurrency, rollback, and acceptance tests pass.
- Do not apply Gate 1/2A/2B migrations or write snapshots to production as part of development/rehearsal.
- Never inspect, print, restore, stage, or commit the protected working `appsettings.json`; use secret providers/environment deployment controls.
- Never modify or commit `Loan.xlsx`, `artifacts/`, or the production baseline note.
- Never persist raw malformed shadow values or personal data in diagnostics/review/audit fields.
- Keep Import Engine unchanged and separately checkpointed.
- Network automation is a later acquisition concern behind `IPortfolioSnapshotSource`, not a reason to change validation rules.
- Dashboard must show only successfully Published data and must not silently fall back to drafts or mutable canonical loan balances.

## 14. Exact next recommended coding task

**Gate 2B Step 1 only: establish the authenticated stable-identity foundation and a server-side Manager authorization policy while leaving the publish endpoint hard-disabled and making no snapshot schema/data changes.**

Before coding, the deployment owner must select the supported identity provider and define the stable subject claim plus manager-membership source. Then add the smallest authentication configuration/middleware and policy, protect only the publish endpoint, and add integration tests proving anonymous denial, authenticated non-manager denial, manager arrival at the still-disabled 409 behavior, and no database writes. Do not assume Windows Authentication without new deployment evidence.
