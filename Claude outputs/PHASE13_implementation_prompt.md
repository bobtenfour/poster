# PHASE 13 — Microsoft Entra ID / OIDC Migration: Implementation

Implement PHASE 13 in the existing PosterPrintRequest application in `C:\projects\posters`.

## Authority

The project owner reviewed the contract and explicitly authorized implementation on 8 November 2026. This is recorded in the PHASE 13 status note of the roadmap and in the status of the Authentication Contract. Do not request renewed approval.

Binding documents:
- `docs/Poster_Print_Request_System_Roadmap_v4_Inventory.docx` — PHASE 13 notes and section 10.2 (identity fields).
- `docs/Authentication_Contract_v1.docx` — sole authority for authentication, identity, authorization, OIDC, session, sign-out, Development authentication, the Entra client secret, and Data Protection on IIS.
- `docs/Storage_Access_Contract_v1.docx` and `docs/Shared_Storage_Contract_v1.docx` — unchanged by this work.

Read the full Authentication Contract before writing code. Where this prompt summarizes it, the contract wins.

Classification: A — STRUCTURAL CONTRACT.

## Required implementation

1. **Authentication** (contract §5, §9). In `AddPosterAuthentication`, replace Negotiate with a cookie scheme plus an OpenIdConnect challenge scheme. Use the authorization code flow with PKCE, `prompt=login` on every authentication request, and callback path `/signin-oidc`. The implicit flow and `response_type=id_token` are not used anywhere.
2. **Configuration keys.** `Authentication:TenantId`, `Authentication:ClientId` and `Authentication:ClientSecretFile` (IIS: `Authentication__TenantId`, `Authentication__ClientId`, `Authentication__ClientSecretFile`). None of them is committed to source control, appsettings files, `launchSettings.json`, or the deployment package. In Development they are supplied as environment variables on the developer's machine.
3. **Production versus Development** (§5.12–§5.14).
   - Production is a confidential client that uses the client secret of §6.
   - Development is a public client that uses only the PKCE verifier and no secret, secret file, or User Secrets.
   - Production refuses to start without the secret or with the public-client configuration. There is no fallback between the two.
   - Environments other than Production and Development keep their current authentication registration unchanged (§1).
4. **Claims and authorization** (§4).
   - Set the role claim type to `roles` and disable inbound claim-type remapping.
   - The three policies keep their names: `UserFrontend` → `Requester`; `TechnicianOperations` → `Technician`; `FallbackPolicy` → `Requester` or `Technician`.
   - Keep the technician-first entry priority and the existing AccessDenied redirect under PathBase.
   - Do not register the Windows group verifier or group handlers in Production. No group names in code.
5. **Identity** (§3, §9).
   - `IInstitutionalActorSource` stays the single place where a principal becomes an actor, and `InstitutionalActor` changes to carry the `oid`, the `onprem_sid` used only for linking, and the `preferred_username` snapshot.
   - Add `SubmittedByObjectId` and `ProcessedByObjectId` as nullable columns with an EF Core migration, and update `ApprovedErdModelTests` for exactly these two fields.
   - Link historical requests by exact `onprem_sid` match only (§3.4); the SID and AccountName fields keep their historical values.
   - New rows write the `preferred_username` snapshot into the AccountName fields (§3.8).
   - Remove the account-name fallback in `AcceptanceLookup`/`UserActivity`; unlinked historical requests are denied with the exact message of §3.6.
6. **Session** (§5.7–§5.10).
   - Cookie `ExpireTimeSpan = 30 minutes`, `SlidingExpiration = false`, OIDC `UseTokenLifetime = false`, all set explicitly.
   - Record the absolute expiry instant as an application claim at sign-in.
   - Register a `RevalidatingServerAuthenticationStateProvider` with a one-minute interval that fails once that instant has passed. On failure, navigate with a full page load to the application entry. This covers the four interactive pages.
   - No autosave and no recovery mechanism.
7. **Sign-out** (§5.11). Add a POST `/Account/SignOut` endpoint with anti-forgery validation. It removes the local cookie and returns to the application entry. It does not call the Entra end-session endpoint, and there is no signed-out callback path.
8. **Client secret** (§6).
   - Read the DPAPI LocalMachine file once at startup, with entropy `PosterPrintRequest.Authentication.v1`, and apply every startup refusal of §6.7.
   - The plaintext lives only in the OIDC options.
   - Write `docs/Entra_Credential_Provisioning.md` (§6.6): ACL first, then interactive entry and immediate protection, with no plaintext on disk, in the command line, in history, or in a ticket. Include rotation per §6.8. The document contains no real path, identifier, or value.
9. **Data Protection** (§7). Call `AddDataProtection().SetApplicationName("PosterPrintRequest")` and nothing else: no key repository, no key directory, no explicit key protection. Registry provisioning and `LoadUserProfile = false` are administrator actions.
10. **Forwarded headers** (§8). In Production only, do not configure or apply forwarded-header processing. Leave every other environment unchanged.
11. **Packages.** Add `Microsoft.AspNetCore.Authentication.OpenIdConnect` (version aligned with the existing 10.0.x packages). Remove `Microsoft.AspNetCore.Authentication.Negotiate` only if no environment still registers Negotiate under item 3. No other new package.

## Mandatory constraints

- Do not modify the governing documents listed above.
- Do not change the Qumulo storage implementation, the storage boundary, the SQL Server connection, or Windows Integrated Authentication for SQL.
- Do not use group claims, Windows group membership, `preferred_username`, or any other mutable claim as identity, ownership, or linking evidence.
- Do not invent tenant, client, role-assignment, group, or path values.
- No placeholders, TODOs, FIXMEs, speculative abstractions, duplicate authorities, or temporary compatibility paths.
- Do not commit or deploy.

## Pre-implementation gate

Inspect the current authentication, identity, authorization, persistence, and test code against the contract. If a contract requirement cannot be implemented as written, STOP and report the exact evidence and contract section before writing code. Do not reinterpret the contract.

## Tests (contract §12)

Implement every repository acceptance item of §12, including:
- the three policies and the §4.3 constraints;
- the identity model, linking, and denial;
- the `preferred_username` snapshot and its non-use for access;
- OIDC configuration, `prompt=login`, and the Production refusals;
- the session: expiry, no sliding, no token lifetime, circuit revalidation;
- sign-out with anti-forgery and no end-session redirect;
- the client-secret refusals and absence from logs;
- the Production-shaped DI build with `ValidateOnBuild` and `ValidateScopes`.

Replace tests that assert Negotiate or Windows group behavior with equivalent role-claim scenarios; do not weaken any expected result. Tests use constructed identities only (§5.15).

If Entra, IIS, SQL Server, or Windows is unavailable for a step, report it as NOT RUN with the reason. Do not simulate Entra or IIS and do not claim production acceptance.

## Report

- Files changed, grouped by area, and the architectural ownership after the change.
- Each §12 repository criterion: PASS, FAIL, or NOT RUN with the reason.
- The administrator and IT actions that remain (contract §10 and §13, A1–A8).
- Residual Negotiate or Windows-group code, if any, and why it remains.

Build PASS and Tests PASS are not acceptance.
