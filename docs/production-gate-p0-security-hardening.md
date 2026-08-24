# Production Gate P0 Security Hardening

This document defines the non-production P0 implementation. It does not authorize production access, migration, account creation, publishing, certificate installation, proxy changes, or network exposure.

## Authorization matrix

| Capability | Anonymous | Viewer | LoanOfficer | Manager | Admin |
|---|---:|---:|---:|---:|---:|
| CSRF token and login | yes | yes | yes | yes | yes |
| Dashboard read | no | yes | yes | yes | yes |
| Snapshot list | no | yes | yes | yes | no |
| Snapshot upload validation/review/records | no | no | yes | yes | no |
| Snapshot Draft/Validated/Reject mutation | no | no | no | yes | no |
| Snapshot Publish | no | no | no | Manager only | no |
| Import status/validation | no | no | yes | no | no |
| Import execution | no | no | yes | no | no |
| Future web administration | no | no | no | no | Admin only |

Admin and Manager are intentionally independent. Operators requiring combined duties must receive both roles explicitly. The default/fallback server policy requires authentication, and health is the only deliberately anonymous non-auth endpoint.

## Endpoint policies and antiforgery

- `AuthenticatedUser`: Dashboard and ordinary signed-in identity access.
- `PortfolioRead`: Viewer, LoanOfficer, Manager.
- `PortfolioReview`: LoanOfficer, Manager.
- `PortfolioManage`: Manager.
- `ImportRead` and `ImportExecute`: LoanOfficer.
- `ManagerOnly`: Manager; unchanged for Publish.
- `AdminOnly`: Admin; reserved for future administration endpoints.

All implemented browser mutations validate the `X-CSRF-TOKEN` token obtained from `GET /api/auth/csrf`: login, logout, Import validate/execute, Snapshot file validation, Draft creation, Draft validation, Reject, and Publish. Authorization executes before controller logic, so a valid CSRF token never grants a role. Publish retains the verified disabled-feature response before enabled-mode confirmation/CSRF logic.

## Import upload lifecycle and limits

The old workstation-path preview was retired. Its authenticated response exposes only supported extensions and the byte limit. Upload names are reduced to their extension and replaced by collision-safe `coopai-import-{guid}` names under the configured temp root. Absolute paths are never returned.

`ImportUpload` configures `TempDirectory`, `MaxUploadBytes` (default 25 MiB), `RetentionHours` (default 24), and the `.xlsx`/`.xls` allow-list. A streaming byte count rejects oversized content. Every controller outcome owns an async-disposable lease, so success, validation failure, import failure, exceptions, and supported cancellation paths attempt cleanup. The deletion guard accepts only direct children with the subsystem prefix. Opportunistic retention removes only stale owned files; it does not touch `Loan.xlsx`, operator files, network files, or artifacts. Malware scanning remains a recommended infrastructure control before production activation.

## One-shot bootstrap

Bootstrap is a command, not an HTTP endpoint. It uses application DI, `RoleManager`, and `UserManager`, creates `Manager`, `LoanOfficer`, `Admin`, and `Viewer` idempotently, and permits one explicit Admin or Manager assignment per invocation.

```powershell
$env:COOPAI_BOOTSTRAP_PASSWORD = '<secret supplied out of band>'
dotnet run --project src/COOPAI.API -- bootstrap-user --role Admin --username first.admin --display-name "Initial Administrator"
dotnet run --project src/COOPAI.API -- bootstrap-user --role Manager --username first.manager
Remove-Item Env:COOPAI_BOOTSTRAP_PASSWORD
```

If the environment secret is absent and the console is interactive, the password is read with echo disabled. Raw passwords, hashes, and security stamps are never output. Identity enforces the configured password policy and duplicate user names fail. Do not run these commands against production until P1 authorizes the target and operator procedure.

## Identity session behavior

The custom cookie event now calls the framework `ISecurityStampValidator` before applying the per-request enabled-user check. Disabled accounts are rejected immediately. Password/security-stamp changes invalidate existing sessions after the configured validation interval (default five minutes), and successful revalidation rebuilds the principal so role additions/removals take effect. Manager removal therefore removes Publish permission after revalidation; production may choose a shorter reviewed interval.

## Data Protection production requirements

Production startup fails if `DataProtection:ApplicationName`, an absolute durable `KeyRingPath`, or an approved protection mode is missing. Supported initial modes are Windows user-scoped DPAPI and certificate protection with an explicit thumbprint. The key ring must live outside the deployment directory with an ACL limited to the application identity. No production key or certificate is in the repository. Development/Test may use isolated ephemeral keys.

## Same-origin LAN HTTPS and proxy trust

Recommended topology:

`browser -> https://internal-hostname/ (IIS/reverse proxy: SPA + /api) -> loopback Kestrel`

The frontend defaults to relative `/api` URLs and sends cookies consistently. Vite proxies local development requests. Production requires exact `AllowedHosts`; `*` fails startup. Forwarded `For` and `Proto` headers are processed before HTTPS redirection only when `ReverseProxy:Enabled=true`. Enabled mode requires explicit valid `KnownProxies` or CIDR `KnownNetworks`, retains framework defaults, requires header symmetry, and limits forwarding to one hop. Replace example trust values with the reviewed proxy address. Do not expose Kestrel directly.

## CORS rules

Same-origin production uses an empty `Cors:AllowedOrigins`. Optional cross-origin deployments must list exact absolute HTTP(S) origins; wildcard origins fail startup and credentials are allowed only for those exact values. Development retains its explicit localhost fallback.

## Frontend authentication pattern

All API modules use one Axios client with `withCredentials: true`. Mutations obtain a fresh token through the shared antiforgery helper and send `X-CSRF-TOKEN`. No bearer or session token is stored in local storage. Snapshot UI wording states that only the current Published Snapshot feeds Dashboard; Draft and Validated do not, and a previous Published Snapshot becomes Superseded atomically.

## Abuse controls

Standard fixed-window endpoint rate limits allow ten login attempts per source address per minute and ten upload operations per authenticated user/source address per minute. Multipart parsing and the owned temp store apply the configured byte limit before business processing. Distributed rate limiting, upstream request limits, malware scanning, centralized security monitoring, and alerting remain production infrastructure recommendations.

## Verification and regression

Focused P0 tests cover anonymous/default denial, role policies, valid/missing/invalid CSRF, safe preview output, cleanup on success/failure/exception/cancellation, file ownership, extension/size limits, bootstrap rules, disabled/stale sessions, Manager revocation, and production configuration validation. Existing authentication, Import Engine, Portfolio Snapshot, Gate 2B-2 atomic Publish, and Dashboard Published-only suites remain the regression authority. Final command results are recorded in the task handoff rather than hard-coded here.

No EF model was changed and P0 must not generate a migration. The pending-model check must remain clean.

## P1 prerequisites and rollback

Before P1: complete independent security review; provision and ACL the key ring/temp paths; choose DPAPI or certificate custody; inject the database secret; review exact host/proxy values; keep `PublishingEnabled=false`; define upstream size/rate limits and malware scanning; plan account bootstrap under dual control; and authorize a separate production DB verification/migration window.

Rollback is source-only: remove the P0 files/changes before deployment. Do not roll back production data because P0 performs no production work and adds no migration. If production startup validation fails, correct reviewed configuration rather than disabling the checks.
