# TDX Validation Tool — Cursor prompt

Repository: `C:\projects\posters`. Sole authority: `docs/TDX_Validation_Tool_Contract_v1.md` (approved 14 Nov 2026). Read it completely before doing anything. If this prompt and the contract disagree, the contract governs: report the conflict and stop.

The roadmap is `docs/Poster_Print_Request_System_Roadmap_v4_Inventory.docx`. There is no `implementation-roadmap.md` and there are no "slices".

**Classification:** A — structural (new projects and a new external dependency).

## Hard rules

- **Execution boundary (absolute, with a public-documentation exception):**
  - **You (Cursor) MAY** read the publicly available TDX Web API documentation, including public Swagger/OpenAPI documentation pages. This is read-only and only to understand documented operations, request fields, parameters, response schemas and behavior.
  - **You MUST NOT** authenticate to, or open API connections with, any TDX sandbox or production instance. Do not execute API operations, submit requests, probe endpoints, test credentials, create tickets, or try to determine environment-specific paths through network calls.
  - Do not use documentation from a specific TDX customer instance as evidence of generic TDX behavior unless its applicability is explicitly established.
  - **Automated tests:** a simulated `HttpMessageHandler` only. No real API network connections.
  - **Owner:** supplies the official sandbox base path before building, and builds, tests and publishes.
  - **Validator:** the only component allowed to communicate with TDX APIs, only with the sandbox, and only when the owner runs it on the institutional machine.
- **You (Cursor):** never ask the owner for TDX credentials, and never put credentials or tokens in code, tests, files, chat or command lines. Tests use fake values only.
- **The validator** asks for the sandbox username and password at each execution, as contract §5 requires. The password is entered masked. Credentials and the token stay in memory for that execution only and are never written to files, reports, logs or the console. Neither are `Authorization` headers.
- No TDX configuration ID goes into code or into the repository. Use placeholders in the template only.
- Do not invent TDX behavior. Every endpoint, request field, query parameter and response shape must come from public TDX Web API documentation or from documentation files the owner provides. Cite the page or file used in your report. The documentation can confirm documented endpoints, request structures, parameters and responses; it is the basis for the implementation. It cannot by itself prove: the sandbox account's permissions, that the lookup finds a newly created ticket immediately, that duplicate prevention works in that instance, or that notifications are actually suppressed. Those remain subject to the sandbox verification in the contract (§8, §9, §11, §13): the validator establishes them at run time and records them in its reports. Do not report them as confirmed in Stage 1b. Distinguish two things: (a) the environment base path, which is always the placeholder `<TDX_SANDBOX_API_PATH>` and never enters the repository; (b) the relative paths of the API operations (auth, ticket creation, search), which come from the public documentation and are part of the implementation, appended to the base path. Apart from the host fixed by contract §4 and those relative operation paths, do not put any real TDX URL or path in code, tests, templates or docs. Tests inject a test base path and use simulated HTTP only. If a fact cannot be confirmed there, do not guess: mark it as a sandbox finding to be recorded at run time (contract §9 and §11), or report it as a blocker.
- Do not touch `PosterPrintRequest.Web`, `Domain`, `Infrastructure`, the existing tests or the database. TDX integration into the application is not authorized (contract §14).
- Do not compile, test, publish or run anything (`dotnet build`, `test`, `publish`, the exe). The owner replaces the placeholder locally and does all of that on his machine.
- Do not commit. The owner reviews and commits.

## Stage 1 — Status

The PHASE 15 note is already in the roadmap (English and Spanish). Do not modify the roadmap.

## Stage 1b — TDX facts, then STOP

1. Read the public TDX Web API documentation and any files the owner provides. If a required fact is not documented, STOP and report it; do not implement anything based on it.
2. Report:
   - the planned project layout, references and public library API;
   - the TDX facts confirmed from that documentation, with their source (page or file and section): auth request and response, ticket creation and its notification parameters, the creation response, and the search operation with the fields it examines. Earlier statements about TDX endpoints in this project are not confirmed facts;
   - the facts you could not confirm;
   - any conflict with the contract.
3. **STOP.** Wait for the owner's GO.

## Stage 2 — Implementation (only after GO)

Projects, added to `PosterPrintRequest.sln`, targeting `net10.0`:

| Project | Content |
|---|---|
| `src/PosterPrintRequest.Tdx` | The single TDX client (contract §2). It owns every outgoing request and enforces all of contract §4 on each one (scheme, host, port, base path with segment boundaries, normalization, redirects disabled, 3xx as error); the validator is not the security barrier. On construction it validates the values it receives: `https`, port 443, no user information, and an absolute base path without query, fragment, `.`/`..` segments or encoded separators. It rejects them before any request. It does not hard-code the host (contract §2: no environment value); the validator supplies it, and the validator checks that the host it passes is exactly the fixed host of contract §4 before calling the library. A test proves that check. The library reads no environment variable, configuration file or IIS setting itself: the caller always passes the address. Production configuration (an IIS environment variable read by PosterPrintRequest.Web) belongs to the future PHASE 15 integration contract and is out of scope; add nothing for it now. It receives the base URL, the ticket configuration and the lookup schedule as parameters, and contains no environment value. It handles auth, token in memory, endpoint guard (§4), pre-creation lookup, creation with notifications disabled, bounded lookup after an uncertain creation, and outcomes `Created`/`AlreadyExists`/`Uncertain`/`Failed` (§9). It treats the Poster ID as opaque text and takes the ticket number only from TDX (§7). |
| `src/PosterPrintRequest.TdxValidator` | Console exe. Supplies the fixed host `help.uillinois.edu`, port 443, `https` and the base path, which in source is the placeholder `<TDX_SANDBOX_API_PATH>` (contract §4), to the library. It refuses to run if the placeholder was not replaced or the path is invalid. It has no other route to TDX: it creates no `HttpClient`, `HttpMessageHandler` or socket, and every network call goes through the library. Before any API call, it verifies the reports folder as contract §11 requires. Loads and validates `tdx-validator.settings.json` (§6). Prompts for username and masked password (§5). Generates the test Poster ID (§7), runs the three steps (§9), writes the `.json` + `.txt` reports (§11). It contains no TDX protocol code. |
| `tests/PosterPrintRequest.Tdx.Tests` | xUnit, simulated HTTP only. Covers at least acceptance criteria 1, 3, 10, 11 and 13: references and absence of any HTTP or socket type in the validator, base-path validation, endpoint guard (redirect, scheme, host, port, segment boundary, `..` and encoded separators), lost response → bounded lookup without re-creation, `Uncertain`, failed pre-lookup blocks creation, distinct test IDs within the same second, unwritable reports folder stops before HTTP, no secret in reports. |

- Placeholder location: in the validator's source code, as contract §4 requires. The README warns the owner not to commit the replaced value.
- Template: `src/PosterPrintRequest.TdxValidator/tdx-validator.settings.template.json` with placeholders and comments. It includes the lookup attempts and interval. Add the real `tdx-validator.settings.json` to `.gitignore`.
- Write `README.txt` in `src/PosterPrintRequest.TdxValidator/` for the owner: where the placeholder is and how to replace it locally without committing it (for example, discard the change after publishing), the commands to build, test and publish (`dotnet publish src/PosterPrintRequest.TdxValidator -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/tdx-validator`), how to fill the settings, how to run it, and where the reports go.

## Final report

- the files created or changed;
- the exact location of the `<TDX_SANDBOX_API_PATH>` placeholder;
- the commands the owner must run (build, test, publish);
- a mapping of acceptance criteria 1–15: covered by automated tests (to be run by the owner) or engineering evidence / pending owner validation on the institutional machine or sandbox (2, 4–9, 12, 14, 15);
- open sandbox findings;
- deviations from the contract (should be none).

Build and test success, when the owner runs them, are engineering checks, not acceptance.
