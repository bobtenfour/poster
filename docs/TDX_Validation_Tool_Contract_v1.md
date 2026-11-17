# TDX Validation Tool Contract

**Version:** 1.0
**Status:** Approved by the project owner — 14 November 2026. §4 amended by the owner on 14 November 2026: base path supplied by the owner before building.
**Repository:** `C:\projects\posters`

## 1. Purpose

The TDX Validation Tool validates real TDX sandbox operations from the owner's authorized institutional Windows machine before TDX integration is implemented in PosterPrintRequest.

It verifies:
- authentication with the sandbox account;
- actual API permissions;
- ticket creation with the required fields;
- duplicate prevention;
- request and response behavior;
- notification suppression;
- error handling.

Every execution produces evidence the owner can review locally.

## 2. Architectural Ownership

The TDX client has exactly one implementation.

- `src/PosterPrintRequest.Tdx` — the shared TDX client library. It owns authentication, token handling, ticket creation, duplicate prevention, retries and error mapping. It receives its base URL and ticket configuration as parameters and contains no environment-specific value.
- `src/PosterPrintRequest.TdxValidator` — the Windows validation executable. It consumes the library and does not implement any TDX protocol logic of its own.
- `src/PosterPrintRequest.Web` — may consume the same library only through a separately authorized integration phase (PHASE 15).
- Tests for the library's contractual behavior.

Duplicated TDX authentication, ticket creation, retry or duplicate-prevention code is prohibited.

## 3. Execution and Distribution

- The validator is published as `win-x64`, `Release`, self-contained, single file.
- The institutional machine needs no source repository, NuGet access or installed .NET runtime, and no access to the developer machine.
- If the institutional machine blocks unsigned executables (SmartScreen, AppLocker), IT must allow the executable or it must be signed. No other workaround is permitted.

## 4. Sandbox-Only Enforcement

| Element | Rule |
|---|---|
| Scheme | `https`, mandatory |
| Host | `help.uillinois.edu`, fixed in code |
| Port | 443 |
| Base path | The sandbox API base path officially provided by TDX, entered by the owner before building (placeholder below) |
| Subpaths | Allowed only inside that base path |
| Redirects | Prohibited |
| Production | Access must be impossible |

- **Placeholder:** in the repository source, the base path is the placeholder `<TDX_SANDBOX_API_PATH>`. Before building, the owner replaces it locally with the official path. Cursor never knows, writes or uses the real value. The replaced value is never committed. It is not read from configuration or user input at run time.
- **Validation of the base path** (before any HTTP call): the validator refuses to run if the placeholder was not replaced, or if the path is not absolute, or if it contains a query, a fragment, `.` or `..` segments, or percent-encoded separators.
- The restriction applies to every HTTP request actually sent, not only to the initial base URL:
  - automatic redirects are disabled;
  - any 3xx response is treated as an error and never followed;
  - before each request is sent, its URI is checked, and any other destination is refused:
    - the scheme must be `https`;
    - the host must be `help.uillinois.edu` (case-insensitive), with no user information in the URI;
    - the port must be the default HTTPS port (443); any other explicit port is refused;
    - the path must equal the base path or start with the base path followed by `/`. The comparison respects segment boundaries, so `<base path>-other` or `<base path>X` is refused. It is applied to the normalized path, after resolving `.` and `..` segments and percent-encoded separators.
- Automated tests use simulated HTTP only and never call TDX.

## 5. Authentication and Credentials

- The validator asks for the sandbox account's username and password at each execution.
- Credentials and the token live in memory only, for that execution only. They are never written to files, reports or logs, and never reused between executions.
- No DPAPI, ACL or credential file is used by the validator.
- The authentication endpoint and request format are verified against the sandbox Swagger.

## 6. Ticket Configuration

The ticket field values (Application ID, Form, Type, Status, Priority, Account and, if required, Source, Responsible Group and required custom attributes) are supplied in a local file, `tdx-validator.settings.json`, placed beside the executable and filled in by the owner on the institutional machine.

- The file contains configuration identifiers only. It never contains credentials.
- The validator checks that every required value is present and well formed before any API call. If one is missing, it stops without calling TDX.
- The values used are copied into the execution report.
- The file is not committed to the repository. The repository provides only a template with placeholder values.

## 7. Test Ticket Identification

**Two identifiers, two authorities:**

| Identifier | Authority | Rule |
|---|---|---|
| TDX ticket number | TDX | TDX assigns it when it creates the ticket. The library takes it only from the creation response or from a lookup that returns the ticket. It is never generated, predicted, reserved or computed locally. If the sandbox Swagger documents a different way to obtain it, that documentation governs and the report records it. |
| Poster ID | PosterPrintRequest | In production it is the value the application already assigned (`POSTER-<yyyy>-<nnnnnn>`, `PosterIds`). The library treats it as opaque text. It neither generates nor validates it. |

The validator has no real poster, so it uses a test Poster ID:

| Item | Value |
|---|---|
| Poster ID (test) | `POSTER-TEST-<yyyyMMdd-HHmmss>-<GUID, 32 hex digits without hyphens>` (60 characters, within the 64-character limit of the production field). It deliberately does not match the production pattern, so it can never collide with or be mistaken for a real Poster ID. It is generated once per creation step. The GUID makes a collision between executions, simultaneous or not, practically impossible, but not mathematically guaranteed: the pre-creation lookup (§9) remains the effective protection against duplicates. The repeat step (§9) reuses the ID on purpose. |
| Ticket title | `[TEST] PosterPrintRequest integration — <Poster ID>` |
| Requester name (text in the ticket) | `Integration Test` |
| Attachments | none |

**Requestor rule:**
- If TDX accepts the requester as text, `Integration Test` is used.
- If TDX requires a requestor that is a TDX person, the requestor is the authenticated sandbox account itself, never a third person.
- The report records which case applied.

No real third-party personal data is used.

## 8. Notification Suppression

- Every creation request explicitly disables all supported notification parameters, as documented in the sandbox Swagger.
- The report records the exact parameter values sent.
- Suppression is accepted only with evidence: either written confirmation from TDX staff, or a check that no notification reached the requestor or the responsible party for the test tickets. A request containing disabled values is not evidence.

## 9. Duplicate Prevention

- Before creating a ticket, the library determines whether a ticket for the same Poster ID already exists, using a sandbox lookup by Poster ID. The lookup searches by Poster ID; it never searches for or derives a ticket number.
- **The lookup mechanism is a verification result, not an assumption.** Which search operation exists, which fields it examines (for example, title, description or custom attribute text), and whether it finds a newly created ticket are established only from the sandbox Swagger and from observed sandbox behavior. Documentation from other TDX instances is not evidence. Until the validator has recorded these findings, no lookup mechanism is considered available.
- **Creation response:** what the sandbox creation operation returns, and whether it includes the ticket number, is likewise recorded from the sandbox Swagger and from the observed response.
- The read permission this requires is listed for TDX staff.
- The validator supports three steps:
  1. Create a ticket with a new test Poster ID.
  2. Repeat with the same Poster ID.
  3. Verify that no second ticket was created.
- **Before a creation:** the lookup must succeed. If the lookup itself fails (error, timeout or denied permission), no ticket is created.
- **After an uncertain creation** (timeout, lost or unreadable response):
  - the library never creates again directly;
  - it repeats the lookup on a bounded schedule (attempts and interval from configuration, defined in the template) to allow for indexing delay;
  - if the ticket is found, its number is recorded;
  - if it is still not found when the schedule ends, the outcome is **Uncertain**: the operation stops, no new creation is attempted, and the report asks the owner to check the sandbox manually.
  A lookup with no result is never treated as proof that the ticket does not exist.
- **Outcomes** the library reports: `Created`, `AlreadyExists`, `Uncertain`, `Failed`.
- If the sandbox offers no reliable lookup, this is reported as a blocker, and the duplicate-prevention guarantee is not accepted.

## 10. Data Confinement

Reports, logs and evidence stay exclusively on the machine running the validator.

The only authorized external communication is the API traffic needed for the approved sandbox operations.

Prohibited: telemetry, analytics, remote logging, cloud reporting, automatic uploads, external crash reporting, synchronization, automatic sharing with PosterPrintRequest, and any other transmission of diagnostic information. No report-transfer feature may be implemented.

## 11. Local Reports

- **Location:** a `reports\` folder beside the executable. Before any API call, the validator checks that it can create, write and remove a file there. If it cannot (for example, under `Program Files`), it stops without calling TDX and does not fall back to any other location.
- **Format:** two files per execution, with the same base name `tdx-validation-<yyyyMMdd-HHmmss>-<8 hex>` (the hex suffix avoids name clashes between simultaneous executions; an existing file is never overwritten):
  - `.json`: the authoritative record, UTF-8, containing all the content listed below;
  - `.txt`: a human-readable summary in UTF-8, generated only from the same data as the JSON. It adds nothing that is not in the JSON.
- The JSON is written first. If the summary cannot be written, the JSON remains valid and the error is reported on the console.
- Both files are subject to the same rules: no credentials, tokens or authentication headers, and no location other than `reports\`.
- **Content for each operation:**
  - timestamp;
  - test Poster ID;
  - ticket title;
  - requester value and the requestor case applied (§7);
  - ticket configuration values used (§6);
  - notification parameter values;
  - the relevant request payload;
  - HTTP status and relevant response data;
  - the TDX ticket number when returned or found by lookup;
  - the outcome;
  - errors and incomplete operations.
- **Sandbox verification findings** (once per execution), each with its source (sandbox Swagger section or observed response):
  - the lookup operation used and the fields it examines;
  - whether a ticket created in this execution was found by the lookup, and after how many attempts;
  - what the creation response contains, including whether it returns the ticket number.
- Unsuccessful and partial operations are always recorded.
- Passwords, tokens and authentication headers never appear in any report or log.
- No result is approved for integration without the owner's explicit review and authorization.

## 12. Operational Boundaries

The validator only authenticates, looks up tickets for duplicate prevention, creates test tickets and optionally reads a created ticket.

It must not assign, modify, comment on, close or delete tickets, connect to production, or perform any other operation.

## 13. Acceptance Criteria

1. The validator uses the single shared library (verified by project references; it contains no TDX protocol code).
2. The executable runs on the institutional machine without the repository, NuGet or an installed .NET runtime.
3. The validator cannot call any URL other than the sandbox. Automated tests prove that:
    - an unreplaced placeholder or an invalid base path stops the validator before any HTTP call;
    - a redirect response is not followed;
    - requests to another scheme, host or port, to a path that only shares a textual prefix with the base path, or to a path that escapes it through `..` or encoded separators are refused.
4. Authentication succeeds against the sandbox.
5. The actual permissions of the sandbox account are recorded.
6. A correctly identified test ticket is created with the configured fields, and the recorded ticket number is the one TDX returned.
7. The requestor case applied is recorded.
8. Notification parameters are sent disabled, and suppression is evidenced as defined in §8.
9. Repeating the same Poster ID in the sandbox creates no second ticket. The lookup mechanism and the content of the creation response are recorded as findings from the sandbox Swagger and from observed behavior (§9, §11), not taken from documentation of other TDX instances.
10. Automated tests with simulated HTTP prove that:
    - a lost creation response leads to the bounded lookup, not a second creation;
    - a lookup that finds nothing within the schedule ends as `Uncertain` without creating;
    - a failed pre-creation lookup prevents creation;
    - two test Poster IDs generated in the same second are different.
11. When the reports folder is not writable, the validator stops before any API call.
12. Reports contain the evidence listed in §11.
13. No credential, token or authentication header appears in any report or log.
14. Reports exist only on the execution host.
15. The owner has reviewed the complete reports.

Build success and test success are engineering checks, not acceptance.

## 14. Future Integration Boundary

Accepting the validator does not authorize integration into PosterPrintRequest. Integration requires:
- the owner's review of the reports;
- the owner's explicit authorization;
- the approved PHASE 15 integration contract;
- authorization through the PosterPrintRequest roadmap.

The validated library remains the single implementation for both the validator and the authorized integration.

## 15. Authority and Roadmap

This contract governs the validation tool, its use of the shared library, sandbox operations, local evidence and data confinement. It does not change existing PosterPrintRequest behavior or contracts.

The PHASE 15 note in the PosterPrintRequest roadmap records that this contract governs the validation that precedes the integration.
