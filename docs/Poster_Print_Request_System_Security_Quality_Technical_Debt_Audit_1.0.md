# Poster Print Request System — Security, Quality, and Technical Debt Audit 1.0

**Date:** 14 October 2026  
**Baseline commit:** `a779913823b8cfc8637865a804bc06ec62572526` (`Improve Printing Supplies dashboard`)  
**Activity:** Audit only. No application code, tests, contracts, dependencies, configuration, schema, roadmap status, or deployment artifacts were changed by this audit.  
**Governing order and status:** `docs/Poster_Print_Request_System_Roadmap_v4_Inventory_DirectPrinting.docx` remains the sole authority for implementation order and status. This report does not promote a phase, authorize a schema change, or amend a contract.

The governing contracts in `docs/` are Word documents. This audit is Markdown so the evidence and file references stay reviewable as text. It does not replace those contracts.

## 1. Executive summary and overall assessment

The solution is a .NET 8 Blazor Server application with a real SQL Server model, server-side role policies, content checks for poster PDF and PPTX files, path containment on shared storage, and a tested rollback path when a printed-folder move cannot be saved. The Release build succeeded, and the existing suite passed 156 tests. Those results are a baseline. They are not evidence that the application is secure.

The highest-priority exposure is the evaluation deployment itself. The Demo environment used for `poster.blueignix.com` enables two well-known accounts, including an Operator account, and the startup seeder resets those passwords from committed configuration whenever the process is not running as Production. An Operator can download poster files and approval sheets and can change inventory and request state. The live host was not probed.

The highest-priority application defect is that any signed-in User can open another requester’s acceptance page by Poster ID. Poster IDs are sequential. The page returns the requester’s name, event, date in, and lamination flag. Email, phone, room, and files are not on that page. The home activity list is scoped to the signed-in user; this confirmation page is not.

The demo SQL definition connects as `sa` with `Encrypt=False` on a Docker network shared with other applications. That is a confirmed insecure configuration. Capturing the traffic would require a position on that network or the host, which this audit did not attempt.

Stock changes are last-write-wins. Poster acceptance and the printed transition do use transactions and compensating file moves. .NET 8 support ends on 10 November 2026, about four weeks after this audit. Production projects reported no NuGet advisories. The test project reported three transitive advisories that are not in the production graph.

## 2. Scope, tools, commands, and execution environment

### In scope

- Solution `PosterPrintRequest.sln`: Domain, Infrastructure, Web, and Tests.
- Identity, authorization, uploads, preflight, storage, poster acceptance, printed-state transitions, inventory mutations, configuration, migrations, Docker demo deployment, and Caddy site snippet.
- Governing text: roadmap v4 (inventory / direct printing), Shared Storage Contract v1, and Printing Consumables Inventory Presentation Contract v1. The older bilingual file is Version 3.0 and is not authority for order or status.

### Out of scope

- No exploit attempts, no requests to `poster.blueignix.com`, no production or demo data queries, and no changes to the Droplet, Caddy, or SQL Server.
- No new packages, scanners, or coverage collection.

### Environment

| Item | Value |
| --- | --- |
| OS | Windows 10.0.26200 |
| SDK | 8.0.421 (`global.json` pins 8.0.421, roll forward latest feature) |
| ASP.NET Core runtime used by `net8.0` | 8.0.27 |
| Also installed | .NET runtime 8.0.14, .NET runtime 10.0.1, Windows Desktop 8.0.27 and 10.0.1 |
| NuGet source | `https://api.nuget.org/v3/index.json` |
| Audit date | 14 October 2026 |

Microsoft’s support policy lists .NET 8 end of support as 10 November 2026. The download page listed runtime 8.0.32, released 13 October 2026, as the current 8.0 patch. Sources: [https://dotnet.microsoft.com/en-us/platform/support/policy](https://dotnet.microsoft.com/en-us/platform/support/policy), [https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/), [https://dotnet.microsoft.com/en-us/download/dotnet](https://dotnet.microsoft.com/en-us/download/dotnet).

### Commands

| Command | Result |
| --- | --- |
| `dotnet --version` and `dotnet --list-runtimes` | SDK 8.0.421; ASP.NET Core 8.0.27 |
| `dotnet build PosterPrintRequest.sln -c Release -p:AnalysisLevel=latest -p:AnalysisMode=Recommended` | Succeeded. 0 errors, 196 warnings. The properties were command-line only and were not written into the projects. |
| `dotnet test PosterPrintRequest.sln -c Release --no-build` | Passed 156, failed 0, skipped 0, about 4 seconds |
| `dotnet list <project> package --vulnerable --include-transitive` | Web: no vulnerable packages. Tests: three transitive advisories, recorded in SEC-11. Domain and Infrastructure were checked in the same way earlier in this audit and also reported none. |
| `dotnet list <project> package --outdated` and `--include-transitive` | Top-level 8.0 packages are on 8.0.31. The command’s “latest” column is mostly the 10.0 line. See section 5. |

The repository projects do not set `AnalysisLevel`. A default build was not given a separate warning count. The 196 warnings exist only under the one-off Recommended analysis.

Recommended analysis, unique warnings: 34 in `src`, 162 in `tests`. Production codes: CA1848 (27), CA1859 (2), CA1305 (2), CA1822 (1), CA1725 (1), CA1861 (1). No CA2100, CA3000-series, or CA5350-series security warnings were emitted. CA1707 (underscores in test names) accounts for most test warnings. An empty security-rule result is not proof of security; these rules do not model authorization or deployment configuration.

### Tools not run

No third-party SAST, DAST, dependency fuzzer, or malware scanner is installed in the repository. None was installed. Coverage was not collected, although `coverlet.collector` is referenced.

## 3. Build and test baseline

Release build of the solution succeeded with the analyzer properties above. The test host then ran the already-built Release assembly: **156 passed, 0 failed, 0 skipped**.

The suite includes role separation, production seeder refusal, path traversal, preflight of malformed PDF and PPTX, approval-sheet type checks, poster-id uniqueness, printed-folder rollback, inventory thresholds, and demo configuration shape. It does not include a cross-user acceptance check, a lockout check, a concurrent stock update, or a measurement of coverage.

Existing fixtures delete and recreate three local databases on the SQL instance named by `appsettings.json`, changing only the catalog name:

- `PosterPrintRequest_FoundationTests`
- `PosterPrintRequest_RequestTests`
- `PosterPrintRequest_WorkflowTests`

Each fixture calls `EnsureDeleted` in the constructor and in `Dispose`. The application catalog `PosterPrintRequest` is not the test catalog. This audit did not drop it. The three test catalogs were deleted by those fixtures when the suite ran.

## 4. Security findings

Findings are ordered by severity. Secret values are not repeated.

### SEC-01 — Evaluation Operator credential is committed and reapplied on startup

| | |
| --- | --- |
| Severity | High |
| Status | Confirmed in the repository. The running Droplet was not probed. |
| Category | Security |

**Evidence**

- `src/PosterPrintRequest.Web/appsettings.Demo.json` lines 5–8 enable evaluation users and store the shared password and the names `usera` and `usero`.
- `src/PosterPrintRequest.Web/appsettings.Development.json` lines 2–6 do the same for local Development.
- `docker-compose.demo.yml` line 36 sets `ASPNETCORE_ENVIRONMENT` to `Demo`.
- `DemoEvaluationUsersSeeder.SeedAsync` returns only when the environment is Production (`DemoEvaluationUsersSeeder.cs` lines 20–24). When enabled, it creates the accounts and, if the current password does not match configuration, resets it (lines 78–85).
- `docker/README-demo.md` documents sign-in at `https://poster.blueignix.com` with those accounts.
- `DemoDeploymentTests.Demo_configuration_targets_poster_database_storage_and_host` asserts that the Demo JSON contains the published password.
- `RoleSeparationTests.Production_does_not_create_temporary_accounts` covers the Production refusal.

**Impact.** Demo is the environment the public evaluation host is defined to run. The Operator role can download any poster or approval sheet (`ProductionEndpoints.cs` lines 38–44) and can change inventory, configuration, and printed state. Restarting the site puts the configured password back, so a database-only password change does not stick. This is the documented evaluation design, and it is still an internet-facing privileged credential if the host is reachable.

**Remediation direction.** Keep real requester data off this host, or place the host behind institutional authentication and stop publishing an Operator password. Remove password reset-on-start before any real account shares the Demo environment. Production already refuses to seed; do not point the evaluation host at Production as a shortcut. This audit does not authorize that change.

**Contract.** Roadmap section 1 describes an internal system for students and staff. Section 14 requires control of access to request data and files, and protection of student and staff data.

### SEC-02 — Any User can read another requester’s acceptance details

| | |
| --- | --- |
| Severity | Medium |
| Status | Confirmed by inspection. Not executed against a running site. |
| Category | Security |

**Evidence**

- `RequestAccepted.razor` line 2 authorizes policy `UserFrontend`, which requires the User role and does not compare the caller to the submitter.
- `RequestAccepted.razor.cs` lines 16–18 load `PosterId` through `AcceptanceLookup`.
- `AcceptanceLookup.FindAsync` (`AcceptanceLookup.cs` lines 28–46) returns name, event, date in, and lamination for any public Poster ID.
- `PosterIds` (`PosterIds.cs` lines 10–12) accepts `POSTER-YYYY-NNNNNN` with a sequence from 1.
- `UserActivityQuery.ForUserAsync` (`UserActivity.cs` lines 13–15) does filter `SubmittedByUserName`. The tests cover that home-page filter. They do not cover the acceptance page.

**Impact.** A signed-in User can request `/request/accepted/POSTER-<year>-000001` and continue through the sequence. Combined with SEC-01, the published User account is enough to enumerate names and events. The response does not include email, phone, room, or file bytes. File routes require Operator.

**False-positive note.** The page is not anonymous. The gap is object-level authorization, not a missing login.

**Remediation direction.** Load the confirmation only when `SubmittedByUserName` is the signed-in user. Keep Operator file access on the technician routes. Add a regression test that a second User receives the not-found state.

**Remediation status (14 October 2026).** Implemented. `AcceptanceLookup.FindAsync` returns a confirmation only when the Poster ID belongs to the authenticated requester. A missing ID, an invalid ID, and another requester’s ID all render the existing not-found state and do not include the other requester’s name. Operators remain on the technician workflow; `/request/accepted/{PosterId}` still requires the User role. Release tests: 157 passed. Roadmap status was not changed.

**Contract.** Roadmap section 14: control access to request data and protect student and staff data.

### SEC-03 — No lockout or rate limit, and the deployed environment uses the relaxed password policy

| | |
| --- | --- |
| Severity | Medium |
| Status | Confirmed |
| Category | Security |

**Evidence**

- `Login.razor.cs` lines 78–82 call `PasswordSignInAsync` with `lockoutOnFailure: false`. The third argument is `AccountEndpoints.SessionOnlySignIn`, which is `false`, so the cookie is not a persistent Identity cookie.
- The solution has no `AddRateLimiter` or rate-limit middleware.
- `PosterAccess.cs` lines 29–36 set minimum length 4 and disable character-class rules when the environment is not Production. `Demo` is not Production, so the evaluation host uses that policy. Identity’s stricter defaults apply only when the environment name is Production. There is no production host definition in the repository.

**Impact.** Failed guesses do not lock the account. On the current evaluation host the password is already published, so brute force adds little. The same settings would apply to any future account created in Demo or Development.

**Remediation direction.** Enable lockout and a login throttle before any non-published account exists. Treat “not Production” as the wrong switch for password strength if Demo remains a deployed environment.

### SEC-04 — Demo SQL uses the administrator account without encryption

| | |
| --- | --- |
| Severity | High |
| Status | Confirmed configuration. Traffic was not captured. |
| Category | Security |

**Evidence.** `docker-compose.demo.yml` lines 14 and 38 set `User Id=sa`, `TrustServerCertificate=True`, and `Encrypt=False`. The password is injected from `MSSQL_SA_PASSWORD` and is not stored in the repository. The compose file attaches both services to the external network `dentalinventory-demo_default`.

**Impact.** SQL authentication and poster query data, including requester name, email, and phone, travel in clear text on a network shared with other demo applications. A compromised sibling container or a host user who can sniff that network can read the administrator password and the data. This audit did not verify the live server’s encryption setting.

**Remediation direction.** Use a database user limited to the `POSTER` database, and enable encryption that the shared SQL instance can actually terminate. That choice belongs to the owner of `dentalinventory-demo-sql`. Changing it is a deployment decision; the current compose file is intentional demo wiring, and a test builds a connection string with the same flags.

### SEC-05 — Forwarded headers trust every proxy

| | |
| --- | --- |
| Severity | Medium |
| Status | Probable |
| Category | Security |

**Evidence.** `Program.cs` lines 41–48, outside Development, enable `X-Forwarded-For` and `X-Forwarded-Proto` and clear known networks and proxies. The demo port publish is loopback-only (`docker-compose.demo.yml` line 34), and the poster Caddy snippet proxies `127.0.0.1:8083`.

**Impact.** Another container on `dentalinventory-demo_default` can call the web container with a forged scheme or client address. The application has no IP allow list, so the practical effect is scheme and address confusion for cookies, HTTPS redirection, and logs. This was not demonstrated.

**Remediation direction.** Trust only the Caddy hop. Loopback publish already limits direct external access to the container port.

### SEC-06 — Session cookie does not set an explicit Secure policy

| | |
| --- | --- |
| Severity | Low |
| Status | Confirmed in code. The cookie flag on the live response was not observed. |
| Category | Security |

**Evidence.** `ApplicationCookieConfigurator.cs` lines 37–42 set the name, `HttpOnly`, essential, and `SameSite=Lax`. `SecurePolicy` is not set. `Program.cs` lines 67–70 enable HSTS outside Development. The framework default for the cookie builder is `SameAsRequest`, so the flag depends on the app seeing HTTPS after forwarded headers.

**Impact.** If the proxy header is missing, the browser can receive a session cookie without `Secure` even though the user connected to Caddy with HTTPS. `SameSite=Lax` still blocks cross-site cookie posts. Logout is a POST and is covered by that SameSite behavior.

**Remediation direction.** Set `CookieSecurePolicy.Always` for the Demo and Production hosts after confirming the forwarded protocol is present.

### SEC-07 — No Content-Security-Policy in the application or the poster Caddy snippet

| | |
| --- | --- |
| Severity | Low |
| Status | Confirmed for the files inspected. A shared Caddyfile outside this repository was not inspected. |
| Category | Security |

**Evidence.** `Program.cs` lines 58–65 set `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and `Referrer-Policy: no-referrer`. `docker/caddy/poster.blueignix.caddy` lines 3–6 encode gzip and reverse-proxy only. No `MarkupString` usage was found. Razor encodes the requester fields rendered on the acceptance page.

**Impact.** A future raw-HTML sink would have no policy to limit script. Blazor Server needs a carefully tested policy, so copying a strict template would break the circuit.

**Remediation direction.** Add a Blazor-compatible policy in the app or at the institutional proxy after a trial, and confirm whether the shared Caddy site already sends one.

### SEC-08 — Upload size is enforced at the Blazor call site, not in every reader

| | |
| --- | --- |
| Severity | Low |
| Status | Probable as defense in depth. No bypass through the current UI was shown. |
| Category | Security |

**Evidence**

- `Request.razor.cs` line 339 opens the browser stream with `UploadLimits.MaxBytes` (25 MB).
- `DraftFileStore.SaveAsync` (`DraftFileStore.cs` lines 48–57) writes until the stream ends. The size argument is checked first, and that argument is the browser-reported size.
- `PosterPreflight.ReadLimited` (`PosterPreflight.cs` lines 451–464) stops at 25 MB. Package inspection caps entries at 512 and each opened entry at 2 MB, and XML parsing prohibits DTDs (`PosterPreflight.cs` lines 19–21 and 437–444).
- `ApprovalSheetRules.Validate` (`ApprovalSheetRules.cs` lines 55–67) copies the whole stored stream before checking the `%PDF` header.

**Impact.** The current request page cannot push more than 25 MB through `OpenReadStream`. A later caller of `SaveAsync`, or a file already placed in the draft directory, can make the approval-sheet reader allocate the whole file. Poster preflight itself is bounded.

**Remediation direction.** Stop `SaveAsync` and `ApprovalSheetRules.Validate` at `UploadLimits.MaxBytes` regardless of the reported size.

**Contract.** Roadmap section 14: treat uploads as untrusted, validate actual content, and set size and processing limits. The poster path implements those limits. The approval-sheet reader implements content validation and misses an independent size cap.

### SEC-09 — Health endpoints are anonymous

| | |
| --- | --- |
| Severity | Informational |
| Status | Confirmed |
| Category | Security |

**Evidence.** `ProductionEndpoints.cs` lines 35–53 map `/health` and `/health/ready` without authorization. The JSON body is `Healthy` or `Unhealthy`. The demo healthcheck depends on `/health/ready`.

**Impact.** A caller learns whether the database and storage root are reachable. Connection strings and paths are not returned.

**Remediation direction.** Leave the check available to the local health probe. No change is required for the current payload.

### SEC-10 — No malware or quarantine step

| | |
| --- | --- |
| Severity | Medium if the institution requires it; otherwise informational |
| Status | Confirmed absence in the application. The institutional requirement is Unverified. |
| Category | Security |

**Evidence.** Upload handling validates extension, size at the UI, PDF structure, and PPTX package structure. No scanner, quarantine directory, or reputation check exists. Roadmap section 14 says to apply institutional malware and quarantine controls where required.

**Impact.** A structurally valid PDF or PPTX can still carry active content. Operators open those files, including inline PDF responses (`ProductionEndpoints.cs` lines 87–94).

**Remediation direction.** Ask institutional IT whether the shared storage host or the mail/file gateway already scans `C:\posters` or the demo volume. Add a scanner only if that control is required and available. This audit does not choose a product.

### SEC-11 — Test-project dependency advisories

| | |
| --- | --- |
| Severity | Low for this application |
| Status | Confirmed as NuGet reports. Exploitation in the test host is Unverified. |
| Category | Security |

**Evidence.** `dotnet list` on 14 October 2026, source nuget.org:

| Package | Project | Severity | Advisory |
| --- | --- | --- | --- |
| AngleSharp 1.2.0 | Tests, transitive | Moderate | GHSA-pgww-w46g-26qg |
| System.Net.Http 4.3.0 | Tests, transitive | High | GHSA-7jgj-8wvc-jh57 |
| System.Text.RegularExpressions 4.3.0 | Tests, transitive | High | GHSA-cmhx-cq75-c4mj |

Web reported no vulnerable packages. Domain and Infrastructure likewise reported none when listed with transitive packages.

**False-positive note.** `System.Net.Http` 4.3.0 and `System.Text.RegularExpressions` 4.3.0 are a common NuGet graph artifact. SDK-style test projects usually run on the shared framework, which supplies newer assemblies. This audit did not trace the test host’s actual loaded versions. AngleSharp is a bunit dependency and is not referenced by the Web project. A clean production report does not prove the Web application is free of vulnerabilities outside the advisory feed.

**Remediation direction.** Upgrade the test stack when convenient so the advisory graph is empty. Do not treat that upgrade as a fix for SEC-01 or SEC-02.

## 5. Technical debt

Effort figures are estimates, not measurements.

### TD-01 — Analyzer baseline is off, and Recommended analysis is noisy

**Impact.** Low. Security-relevant compiler rules are not part of the normal build, so a future unsafe API can land without a warning. The one-off run’s production warnings are logging and culture rules, not defects in authorization.

**Evidence.** No project sets `AnalysisLevel`. Unique production warnings are listed in section 2. Twenty-seven are CA1848 (`LoggerExtensions` instead of `LoggerMessage`), concentrated in `OperatorConfiguration.cs`.

**Estimate.** A few hours to switch production logs to `LoggerMessage` if that rule is adopted. Clearing CA1707 across the test suite is a large mechanical rename and is optional. Enabling Recommended analysis as errors would fail the current build.

### TD-02 — Operator configuration is one large type and one large page

**Impact.** Medium for change risk, low for current behavior. `OperatorConfiguration.cs` is 854 lines. `TechnicianConfiguration.razor` is 434 lines and its code-behind is 534. Catalog rules for departments, events, printers, and consumables share one save path. A mistake in the shared save is harder to see than a mistake in a smaller type.

**Estimate.** Several days to split by catalog, plus the existing configuration tests. Not a prerequisite for the security fixes.

### TD-03 — Package “latest” is a framework upgrade, not a patch

**Impact.** Informational for the 10.0 column. Operational when combined with OPS-01.

**Evidence.** Top-level packages resolve to the 8.0.31 line (`Microsoft.EntityFrameworkCore.*` 8.0.31, `Microsoft.Extensions.Options.ConfigurationExtensions` 8.0.0, `PdfPig` 0.1.16 was not listed as outdated). `dotnet list package --outdated` reports 10.0.13 for the EF and ASP.NET packages, bunit 2.11.3, coverlet 10.1.0, xunit 2.9.3, and xunit.runner 4.0.1. Moving those top-level libraries to 10.x means leaving `net8.0`. The Docker images are `mcr.microsoft.com/dotnet/sdk:8.0` and `aspnet:8.0`.

**Estimate.** A supported-runtime upgrade is a planned project, on the order of days to a few weeks including the demo image, not a package bump. This audit does not authorize it and does not change the roadmap.

### TD-04 — `SessionOnlySignIn` reads like a mode and is passed as `isPersistent`

**Impact.** Informational. `AccountEndpoints.SessionOnlySignIn` is `false`. `Login.razor.cs` line 81 passes it as the `isPersistent` argument, so the sign-in is a session cookie. The login copy matches that behavior. The constant name is easy to invert in a later edit.

**Estimate.** Under an hour, with the existing login test.

### TD-05 — Coverage is unmeasured

**Impact.** Medium for confidence, not a defect by itself. `coverlet.collector` 6.0.0 is referenced. This audit did not produce a coverage number and does not invent one. Section 7 lists the missing scenarios.

**Estimate.** A few hours to publish a report; the value is in the missing cases, not in a percentage goal.

## 6. Architecture, contracts, data integrity, and operational risks

### Controls that match the contracts

These are observations, not a certification.

- Poster acceptance runs inside a serializable transaction, allocates the next `POSTER-YYYY-NNNNNN`, and relies on a unique index (`PosterRequestConfiguration.cs` lines 19–20; `RequesterSubmissionService.cs` lines 89–178). A unique violation retries up to three times and deletes the placed directory. The sequence stops at 999999 (`PosterIds.cs` line 8).
- `AcceptedStorage.Resolve` rejects absolute paths, drive prefixes, `.` and `..`, checks the full path stays under the root, and refuses reparse points on move (`AcceptedStorage.cs` lines 26–61 and 160–253). Draft keys must be 32-character GUIDs (`DraftIds.cs`).
- Printed transition moves the directory first, then saves `Printed` and the new paths, and moves the directory back if the save fails or the destination is not settled (`TechnicianWorkflow.cs` lines 172–214). `PrintFolderTests` covers the save-failure restore. The shared-storage contract’s resting pairs are implemented on that path.
- The library listing allows only the print-folder shape and skips reparse points (`TechnicianLibrary.cs`). Tests keep drafts and paths outside the root out of the listing.
- Departments and reasons that are referenced by a request cannot be deleted. Submit copies `DepartmentName` and `ReasonName` onto the request. Rename does not rewrite that snapshot.
- Direct-print submission was not found. Phase 11 remains experimental and disabled by default. This audit did not treat absence of a symbol named `PrintJob` as a complete proof; no print-queue client appeared in the web or domain projects that were read.
- The printing-supplies dashboard reads `ConsumableStock` and `ExpirationAlert`. The presentation contract forbids the dashboard from inventing threshold rules. The current projection uses the existing low-stock and expiration results.
- The web container entrypoint checks the database name and storage root, then starts the process as `app` (`docker/web-entrypoint.sh` lines 18–39). The published port is loopback. The migrate container runs as root for one shot; that is normal for the SDK image and still holds the SQL password in its environment.
- Application code does not call `Database.Migrate`. The migrate service is the migration owner. One migration runs a bounded SQL update to backfill department and reason names (`20261008032642_ProtectConfigurationHistory.cs` lines 32–43). No other `FromSql` or `ExecuteSql` call was found.
- `Nullable` is enabled. No `TODO` or `FIXME` markers were found under `src`.

### DI-01 — Stock updates can lose a concurrent change

| | |
| --- | --- |
| Severity | Medium |
| Status | Confirmed by inspection. A concurrent run was not executed. |
| Category | Data integrity |

**Evidence.** `PrintingInventory.AddStockAsync` (lines 202–250) and `RemoveStockAsync` (lines 260–314) load entries, change quantities in memory, recompute status, and call `SaveChangesAsync` once. There is no transaction and no concurrency token on stock rows. Identity’s `ConcurrencyStamp` is unrelated.

**Impact.** Two operators adding or removing the same consumable can persist a total that omits one of the updates. Status and the dashboard then follow the stored total. The last save wins.

**Remediation direction.** Make the adjustment conditional in one transaction, and reject the second save when the row changed. A new rowversion column would be a schema change. This audit does not authorize an ERD change; the inventory presentation contract also leaves persistence ownership where it already is.

**Remediation status (14 October 2026).** Implemented without a new column or a second quantity. `AddStockAsync` and `RemoveStockAsync` read and save inside one serializable transaction and retry a deadlock. Two concurrent additions of 4 persist as 8. Two concurrent removals of 3 from 5 leave quantity 2, and the second removal returns the existing below-zero message. Release tests: 159 passed. Roadmap status was not changed.

### DI-02 — File move and database save are compensated, not atomic

| | |
| --- | --- |
| Severity | Low |
| Status | Confirmed |
| Category | Data integrity |

**Evidence.** Acceptance places files before commit and deletes them on failure (`RequesterSubmissionService.cs` lines 100–185). Printed transition and startup folder migration move the directory back when the save fails (`TechnicianWorkflow.cs` lines 204–214; `PrintFolderMigration.cs` lines 109–144). If the compensating move fails, or the restore save fails, the code logs a warning and returns. A process kill between the move and the commit can leave an orphan directory or a path that no longer matches `Printed`.

**Impact.** The resting pair required by the shared-storage contract can be broken in that window. Normal failures are covered by tests. The double failure is only logged.

**Remediation direction.** Keep the compensating move. Add an operator-visible reconciliation for the warning path rather than a second print state. The contract forbids a new print state.

### ARCH-01 — Authorization is per page, with no fallback policy

| | |
| --- | --- |
| Severity | Low |
| Status | Confirmed |
| Category | Architecture |

**Evidence.** `Routes.razor` uses `AuthorizeRouteView` and does not set a fallback policy. Pages with `[Authorize]` were: `/`, `/help`, `/request`, `/request/accepted/{PosterId}` (User); `/technician`, `/technician/work/{PosterId}`, `/technician/{PosterId}`, `/technician/library`, `/technician/inventory`, `/technician/configuration` (Operator). Anonymous by design: `/Account/Login`, `/Account/AccessDenied`, `/Error`, `/not-found`. File GETs require Operator. Logout requires an authenticated user.

**Impact.** A new `@page` without an attribute is public. Current functional pages are attributed. The error page shows a request id and does not show the exception outside Development.

**Remediation direction.** Add a fallback policy that requires an authenticated user, and keep an explicit allow list for login, health, and the error pages. Add a test that fails when a new page is missing `[Authorize]`.

### ARCH-02 — Data-protection keys are not persisted

| | |
| --- | --- |
| Severity | Low |
| Status | Confirmed absence |
| Category | Operational |

**Evidence.** No `AddDataProtection` or `PersistKeysTo*` registration was found. The demo container’s writable app directory is not a volume. The storage volume is `/var/poster-print-request` only.

**Impact.** Recreating the container invalidates session cookies and antiforgery keys. One web replica is defined, so a multi-instance key mismatch is not current.

**Remediation direction.** Persist keys on a volume if the evaluation host should keep sessions across redeploy. Protect that volume with the same care as the poster files.

### OPS-01 — .NET 8 leaves support on 10 November 2026

| | |
| --- | --- |
| Severity | High |
| Status | Confirmed against Microsoft’s published lifecycle and the repository target. The deployed image digest was not inspected. |
| Category | Operational |

**Evidence.** Every project targets `net8.0`. `global.json` pins SDK 8.0.421. This machine’s ASP.NET Core runtime is 8.0.27. The public download page listed 8.0.32 as the current 8.0 runtime on 13 October 2026. Support ends 10 November 2026. After that date the runtime and EF Core 8 receive no security fixes. The Dockerfiles use the floating `8.0` tags, so a future pull may be newer than this machine and will still be unsupported after that date.

**Impact.** This is a support cliff, not a vulnerability demonstrated today. Staying on 8 after 10 November 2026 means new runtime vulnerabilities remain unpatched.

**Remediation direction.** Plan a move to a supported LTS (.NET 10, supported through November 2028 on the policy page) as its own change, including the demo image. Patching the local runtime to 8.0.32 is useful before that date and does not extend support. This audit does not authorize the upgrade and does not change the roadmap.

**Remediation status (14 October 2026).** Implemented. Projects target `net10.0`. `global.json` pins SDK 10.0.402. Framework packages are 10.0.13. Demo Dockerfiles use `sdk:10.0` and `aspnet:10.0`, and the migrate image installs `dotnet-ef` 10.0.13. A direct reference to `System.Security.Cryptography.Pkcs` 10.0.13 replaces the vulnerable 9.0.11 transitive package pulled by that graph. Release build and 159 tests passed with the user-local SDK 10.0.402. The system-wide `dotnet` host on this machine still has only SDK 8.0.421. Compose, SQL, and Caddy were not changed. Roadmap status was not changed. The image was not deployed.

### OPS-02 — Startup rewrites print folders for every request

| | |
| --- | --- |
| Severity | Low |
| Status | Confirmed |
| Category | Operational |

**Evidence.** `Program.cs` lines 99–106 run `PrintFolderMigration.MigrateAsync` on every start except the `ef` entry assembly. The migration loads every poster request and moves directories that still use an old layout.

**Impact.** One demo replica is safe. Two web processes against one storage root can race the same move. Startup time grows with the request table.

**Remediation direction.** Keep a single web replica. Retire the startup migration after the stored paths are all on the contracted layout, under a later authorized change.

### OPS-03 — Phase 9 production checks were not evidenced

| | |
| --- | --- |
| Severity | Medium |
| Status | Unverified |
| Category | Operational |

**Evidence.** The roadmap’s security and production phase calls for validation of malformed uploads, access, permissions, accessibility, performance, backup and recovery, logging, and deployment. Malformed uploads, role separation, and parts of logging are tested. Accessibility, performance, backup, and recovery were not measured. Institutional IT owns backup of the SQL instance and the storage volume.

**Remediation direction.** Record the backup owner, restore drill, and retention for `POSTER` and `posterprintrequest-demo-storage` before treating the evaluation host as durable. This audit did not inspect those jobs.

## 7. Test effectiveness and missing verification

The 156 passing tests are effective for the rules they name: role separation, Production seeder refusal, storage containment, preflight, unique Poster ID, referenced-catalog delete, and printed rollback. They are silent on the following. A passing suite does not close these gaps.

| ID | Missing scenario | Related finding | What a useful test would assert |
| --- | --- | --- | --- |
| TG-01 | Second User opens `/request/accepted/{id}` for a poster submitted by the first user | SEC-02 | Not-found or an empty confirmation, and no requester name in the response |
| TG-02 | Repeated bad passwords | SEC-03 | Lockout or throttle once that control exists. Today a test would only document that lockout is off |
| TG-03 | Two stock updates of the same consumable | DI-01 | The stored quantity includes both updates, or the second is rejected |
| TG-04 | New Razor page without `[Authorize]` | ARCH-01 | Build or test failure outside an allow list |
| TG-05 | `SaveAsync` stream longer than the reported size | SEC-08 | Bytes written stop at 25 MB |
| TG-06 | Restore move fails after a printed save failure | DI-02 | The warning path is visible to an operator. The current test covers the successful restore |

Personal activity, file denial for a User, and path traversal already have tests. Duplicating them would not improve the baseline.

## 8. Findings that need manual validation or institutional IT

| Topic | Why it cannot be closed from the repository |
| --- | --- |
| Live evaluation host | Confirm whether `poster.blueignix.com` is still on this Demo configuration, who can reach it, and whether real student or staff posters are stored there. This audit did not connect to it. |
| Shared Caddy | `docker/caddy/poster.blueignix.caddy` is a site snippet. TLS and any extra headers live on the existing Caddy ingress. |
| SQL administrator and encryption | SEC-04 depends on the shared `dentalinventory-demo-sql` instance. IT must say whether TLS and a limited user are possible without breaking the other demo applications. |
| Malware control | SEC-10. IT must say whether a scanner already covers the storage volume. |
| Backup and restore | OPS-03. Owner, frequency, and last restore of database `POSTER` and volume `posterprintrequest-demo-storage` are outside the repo. |
| Storage ACLs | The entrypoint runs `chown app:app` on the container path. Host permissions on `C:\posters` and on the Docker volume were not inspected. |
| Runtime patch level on the Droplet | Local ASP.NET Core is 8.0.27. The image tag is floating `8.0`. The running image digest was not read. |
| Data-protection and cookie flags | ARCH-02 and SEC-06 need a response captured from the deployed host. |

## 9. Prioritized remediation and dependencies

Do these as separate, authorized changes. Nothing here promotes a roadmap phase or changes a contract. Items 1, 3, 8, and 9 depend on institutional IT.

| Order | Action | Depends on | Finding |
| --- | --- | --- | --- |
| 1 | Decide whether the evaluation host may stay on the public internet with a published Operator account. Prefer network restriction or institutional sign-in, and stop resetting that password on startup. | IT, and a decision that real posters are not stored there | SEC-01 |
| 2 | Restrict acceptance details to the submitting user and add TG-01. | None | SEC-02 |
| 3 | Replace `sa` and `Encrypt=False` when the shared SQL instance can support a limited user and TLS. | IT | SEC-04 |
| 4 | Turn on lockout and a login limit before any unpublished account is created. | None for the code; useless while SEC-01 stands | SEC-03 |
| 5 | Make stock adjustments conditional so two operators cannot drop an update. | Schema change only if a concurrency token is added; that needs its own authorization | DI-01 |
| 6 | Add a fallback authorization policy and TG-04. | None | ARCH-01 |
| 7 | Set an explicit secure cookie and, after a Blazor trial, a content-security policy. Confirm the shared proxy first. | IT for the proxy | SEC-06, SEC-07 |
| 8 | Record backup ownership and whether malware scanning is required. | IT | OPS-03, SEC-10 |
| 9 | Patch to the current 8.0 runtime now, and plan the supported-runtime upgrade before 10 November 2026. | IT for the image and SQL client | OPS-01 |
| 10 | Close TG-03, TG-05, and TG-06. Adopt analyzers only if the team wants CA1848 fixed; it is not a security fix. | None | Section 7, TD-01 |

## 10. Limitations and areas not verified

- The live site, the Droplet, Caddy’s full configuration, SQL Server settings, volume ACLs, and backups were not inspected.
- No vulnerability was exploited. “Confirmed” means the code or deployment definition shows the behavior. “Probable” means the impact depends on a condition that was not executed.
- Coverage percentage, accessibility, and performance were not measured.
- PDF and PPTX parsers were not fuzzed. Their bounds were read. A parser bug in PdfPig or `ZipArchive` remains a residual risk.
- Migration designer files and the model snapshot were searched for indexes and concurrency tokens. They were not read line by line.
- Word extraction of the roadmap can reorder paragraphs inside a section. Section 14 was coherent and is the security text cited here. Phase 1 through 9 have no separate status label in the 8 October 2026 amendment; this report does not assign one.
- The local Development site that was already running was left running and was not used as a target.
- A clean build, a green suite, and an empty production advisory list are recorded as facts. They are not a security attestation.

### Files this audit created

These were left in place:

- This report: `docs/Poster_Print_Request_System_Security_Quality_Technical_Debt_Audit_1.0.md`
- Extracted contract text, outside the repository: `C:\Users\btfre\AppData\Local\Temp\poster-audit-docs\` (`Poster_Print_Request_System_Roadmap_v4_Inventory_DirectPrinting.docx.txt`, `Shared_Storage_Contract_v1.docx.txt`, `Printing_Consumables_Inventory_Presentation_Contract_v1.docx.txt`)
- Release build outputs under the projects’ `bin` and `obj` directories, from the analyzer build and the test run
- NuGet HTTP cache used by `dotnet list package`
- The three local test databases named in section 3 were created and then deleted by the existing fixtures

No application source, test, contract, workflow file, or deployment file was intentionally edited.
