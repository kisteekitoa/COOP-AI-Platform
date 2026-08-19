# Portfolio Snapshot V1.1 — Gate 2B-1 authentication foundation

Gate 2B-1 adds first-party COOP-AI authentication and role/policy foundations only. Portfolio publishing remains hard-disabled, Dashboard V1 continues to use its existing data source, and Import Engine behavior is unchanged.

## Selected architecture

COOP-AI uses ASP.NET Core Identity with EF Core stores and integer user/role keys. `CoopUser.Id` is the stable identity and is independent of mutable username/display data. Identity owns username normalization, password hashes, security stamps, lockout state, user-role links, and password verification.

This fits the existing ASP.NET Core 8/EF Core 8 monolith, avoids a large external identity platform, supports browsers on desktop/tablet/mobile, and uses the framework's maintained password and cookie security instead of custom cryptography.

Authentication and authorization remain separate:

- Authentication establishes the `CoopUser` identity and cookie session.
- Authorization evaluates server-owned role claims and policies.
- Client-supplied role fields are ignored; the database-backed Identity role store is authoritative.

## User model and password security

`CoopUser : IdentityUser<int>` adds:

- stable integer `Id`;
- framework `UserName` and normalized username;
- framework-managed `PasswordHash`;
- bounded `DisplayName`;
- `IsEnabled` status;
- `CreatedAt` timestamp.

Passwords are processed only by ASP.NET Core Identity's `PasswordHasher<CoopUser>` and are never stored as plaintext, logged, or returned. The configured policy requires at least 12 characters plus uppercase, lowercase, digit, and non-alphanumeric characters. Five failed attempts lock an enabled account for 15 minutes. Unknown usernames run a dummy framework hash verification before returning the same generic 401 body used for a wrong password or disabled account, reducing account-enumeration differences.

No public registration, password-reset email flow, user-management UI, default user, known password, or real credential is included.

## Roles and Manager policy

Initial role constants are:

- `Manager`
- `LoanOfficer`
- `Admin`
- `Viewer`

`ManagerOnly` is a server authorization policy requiring an authenticated principal with the explicit `Manager` role. `LoanOfficer`, `Viewer`, unauthenticated principals, and `Admin` without `Manager` fail. Admin does not silently inherit Manager authority; any future privilege inheritance requires a separate business decision.

Gate 2B-1 deliberately does not attach this policy to Portfolio Publish because that would change the established anonymous 409 contract. Gate 2B-2 must add the policy while preserving disabled-by-default publish governance.

## Authentication API and flow

Endpoints in `AuthController`:

- `GET /api/auth/csrf`: issues an antiforgery cookie and returns its request token.
- `POST /api/auth/login`: requires the antiforgery header, verifies credentials/enabled state, establishes the Identity application cookie, and returns only safe user ID, username, display name, and roles.
- `POST /api/auth/logout`: requires authentication and antiforgery validation, then invalidates the application cookie.
- `GET /api/auth/me`: requires authentication and returns the current safe identity/roles.

Login, logout, and identity responses disable response caching. Cookie challenge/forbid behavior is API-native HTTP 401/403 and never redirects to an HTML server login page. Cookie validation rechecks that the user exists and remains enabled; a disabled/deleted account's existing session is rejected.

## Session and transport model

The application uses a server-validated encrypted Identity cookie named `COOPAI.Auth`:

- `HttpOnly=true` so browser JavaScript cannot read it;
- `SameSite=Lax`;
- `Secure=Always` outside Development;
- Development uses `SameAsRequest`, allowing explicit localhost HTTP without weakening non-Development behavior;
- eight-hour sliding ticket lifetime;
- non-persistent login, so no long-lived “remember me” cookie;
- no bearer token or sensitive token in `localStorage`/`sessionStorage`.

Production should serve frontend and API from the same HTTPS site where possible. Before production activation, configure a durable protected ASP.NET Core Data Protection key ring appropriate to the approved host topology; multi-instance deployments must share that protected key ring. This repository does not guess the deployment key store.

## CSRF and CORS security

Cookie-authenticated state changes use ASP.NET Core antiforgery validation. The frontend gets a request token from `/api/auth/csrf`, keeps it only in memory for the request, and sends `X-CSRF-TOKEN` for login/logout. The antiforgery cookie is HttpOnly, `SameSite=Strict`, and Secure outside Development.

CORS no longer permits arbitrary origins when credentials are involved:

- Development defaults to exactly `http://localhost:5173`.
- Approved origins can be supplied as `Cors:AllowedOrigins` (for example environment keys using the standard indexed configuration syntax).
- Production with no configured origins denies cross-origin requests; same-origin hosting continues to work.
- Allowed configured origins may send credentials, headers, and methods. Wildcard origin plus credentials is never used.

All future cookie-authenticated write endpoints must use antiforgery validation. CORS is not a replacement for CSRF protection or authorization.

## Frontend foundation

The React application now includes:

- responsive Thai `LoginPage` for mobile, tablet, and desktop widths;
- `authService` using Axios `withCredentials`, CSRF acquisition, login, logout, and `/me`;
- `AuthProvider` current-user/session state;
- `ProtectedRoute`, which checks `/me` and redirects anonymous users to `/login` while preserving their intended internal path;
- safe display of current display name/roles and a logout action in the existing Sidebar.

Role data may improve UX later but is not a security boundary. No Publish button behavior or Dashboard calculation was added.

## Database migration and schema

Generated migration: `20260819083906_AddCoopAuthenticationFoundation`.

It creates only standard Identity authentication/authorization tables:

- `AspNetUsers`
- `AspNetRoles`
- `AspNetUserRoles`
- `AspNetUserClaims`
- `AspNetRoleClaims`
- `AspNetUserLogins`
- `AspNetUserTokens`

Important indexes:

- unique filtered `UserNameIndex` on normalized username;
- `EmailIndex` on normalized email;
- unique filtered `RoleNameIndex` on normalized role name;
- FK lookup indexes on role claims, user claims, user logins, and user roles.

Identity child relationships use its standard cascading FKs to user/role parents. The migration contains no add/alter/drop operation against Members, LoanContracts, Import Engine tables, Dashboard data, or Portfolio Snapshot tables. It was generated and inspected locally but not applied to any database.

## Bootstrap administrator strategy

No account or role is automatically seeded. Before production activation, implement and separately review a one-shot administration/bootstrap command with this procedure:

1. Run only on an approved administration host after the Identity migration has been applied through change control.
2. Read the initial username/display name from explicit operator input and read the password from a masked interactive prompt or approved secret provider, never a command-line argument, repository file, or log.
3. Refuse empty/default/predictable input and pass the password to `UserManager.CreateAsync` so the standard policy/hasher is used.
4. Create the four named roles idempotently, then assign only `Admin` to the initial administrator unless an independent approval explicitly also assigns `Manager`.
5. Fail if an account already exists unless an explicit audited recovery workflow is selected; never overwrite a password silently.
6. Emit only non-sensitive success/audit metadata, remove or disable the bootstrap capability after use, and rotate any temporary secret-provider material.

Until that controlled utility and procedure are approved, production authentication must not be activated. Direct SQL insertion of users/password hashes is prohibited.

## Verification

Focused authentication integration suite: 15 passed, 0 failed. Coverage includes:

- successful login and HttpOnly/Secure/SameSite cookie;
- framework password hash persisted and never returned;
- wrong password and unknown user return identical generic 401 responses;
- disabled user rejected;
- authenticated and unauthenticated `/me`;
- logout invalidates the session;
- missing CSRF token rejected;
- Manager allowed, LoanOfficer/Viewer/Admin/anonymous rejected by `ManagerOnly`;
- client role injection ignored;
- normalized duplicate username rejected;
- authenticated Manager still receives existing Publish 409;
- failed authentication leaves Member, LoanContract, Import, and Portfolio tables unchanged.

Full backend suite: 222 passed, 0 failed, 0 skipped. This includes Dashboard V1 and all Snapshot Gate 1/2A regressions.

Frontend: `oxlint` passed with no findings; TypeScript/Vite production build passed. Vite reports only the existing bundle-size advisory.

## Production activation prerequisites

Before any production login test or user creation:

1. Review/approve and apply the authentication-only migration through production change control.
2. Approve the production origin/topology and set exact CORS origins only if cross-origin hosting is required.
3. Enforce HTTPS and secure proxy forwarding behavior.
4. Configure and protect a durable Data Protection key ring.
5. Implement/review the one-shot bootstrap command and operational audit procedure.
6. Create the initial administrator through that controlled process; do not seed credentials through deployment files.
7. Define account lifecycle, password reset/recovery, administrator role assignment, monitoring, and incident response.
8. Run authentication smoke tests without creating or changing Portfolio, Import, loan, member, or Dashboard data.

## Exact remaining Gate 2B-2 work

Gate 2B-2 must remain a separate change. It needs to:

- require `ManagerOnly` on Publish while retaining the disabled feature gate;
- add approved immutable publish/supersede audit fields, including stable publisher UserId;
- define and database-enforce exactly one current Published snapshot;
- implement atomic `Validated -> Published` plus previous `Published -> Superseded` with rollback;
- add concurrency/idempotency protections and relational tests;
- keep all previous snapshots auditable;
- enable publishing only after isolated rehearsal and acceptance;
- leave Dashboard V1 unchanged until the separately approved Dashboard V1.1 integration.

Gate 2B-1 creates no Published snapshot, does not call Portfolio `SaveChanges`, does not switch Dashboard data, and does not alter Import Engine behavior.
