# PHASE 15 — TDX Ticket Integration

Repo: `C:\projects\posters`. Classification: A — STRUCTURAL CONTRACT.

## Authority
- Authorized by the owner on 14 Nov 2026. Do not reopen these decisions.
- Order: 1 → … → 10 → 12 → 13 → 14 → 15. PHASE 14 (Poster Deletion) is separate: do not touch it. PHASE 15 may proceed before it and must not depend on it.
- PHASE 15 replaces roadmap §12 ("no TDX integration") and the criterion "No technical TDX dependency exists", only within this scope.
- Other contracts (Shared Storage, Storage Access, Authentication) stay unchanged. If one contradicts this phase, STOP and report.

## Decisions
1. On acceptance, create exactly one TDX ticket.
2. Send only: **Poster ID**, **requester name** (`PosterRequest.Name`), **poster title**. No attachments.
3. The app never assigns, updates, comments on, or closes tickets. Assignment happens in TDX.
4. New required form field **Poster title**: trimmed, max 200 characters, no control characters. Stored in `PosterRequest.PosterTitle nvarchar(200)`, null only for requests accepted before PHASE 15.
5. Ticket title: `<Poster ID> — <Poster title>`.
6. Store the ticket number in `PosterRequest.TdxTicketId int` (nullable, unique when set, written once, never overwritten). Never create a second ticket for a Poster ID, even if a create response is lost (define how, e.g. search by Poster ID before retrying).
7. Acceptance does not depend on TDX. If creation fails, the request is still accepted, the technician sees "ticket pending", and the app retries without duplicates.
8. Requests accepted before PHASE 15 get no ticket and no title.
9. The confirmation page stops telling the requester to create a TDX ticket by hand and shows the ticket number. The technician area shows the ticket number.

## Ticket field facts (owner fills in; real IDs live only in configuration)
| Field | Required? | Source |
|---|---|---|
| FormID / TypeID / StatusID / PriorityID | `<yes/no each>` | `Tdx:FormId`, `Tdx:TypeId`, `Tdx:StatusId`, `Tdx:PriorityId` |
| AccountID / SourceID / ResponsibleGroupID | `<yes/no/unused>` | `Tdx:AccountId`, `Tdx:SourceId`, `Tdx:ResponsibleGroupId` |
| Required custom attributes | `<names or none>` | `<value source>` |
| Requestor | `<real person by email, or fixed UID>` | `<signed-in user's email, or Tdx:RequestorUid>` |

If the requestor must be the real person, the email may also be sent, only to identify the requestor.

## Technical rules
- `Tdx:BaseUrl`: the sandbox (`https://help.uillinois.edu/sbTDWebAPI/api`) in Development, the production URL in Production. Production refuses to start with the sandbox URL.
- Auth with `POST /api/auth` and a dedicated service account per environment. No `loginadmin` or BEID key. The token lives in memory only and is renewed before it expires.
- Credential as JSON `UserName`/`Password`, protected with DPAPI LocalMachine and entropy `PosterPrintRequest.Tdx.v1`, in its own folder under `Secrets` with its own ACL. Path from `Tdx__CredentialFile`. Same mechanism on the developer machine; no User Secrets. Annual rotation. Write `docs/Tdx_Credential_Provisioning.md`.
- Runtime calls: auth, create ticket, the read needed for duplicate prevention, and optionally get ticket. Nothing else.
- Create ticket with no TDX notifications the owner has not approved and `AllowRequestorCreation=false`.
- One TDX client service owns all calls, tokens, retries, and error mapping.
- Logs may contain: operation, Poster ID, ticket ID, HTTP status. Never the password, token, credential path or content, or requester name.
- Explicit timeout, back-off retries, and the rate limits documented in the sandbox Swagger.
- Automated tests use a fake HTTP handler. Only the sandbox receives real calls.

## Stage 1 — Documents (STOP afterwards)
- Create `docs/TDX_Integration_Contract_v1.docx` covering all of the above, plus acceptance criteria, external confirmations (production URL and AppID, service account and its permissions, notifications, requestor rule), and a boundary section.
- Add the PHASE 15 note to the roadmap in Spanish and English. Amend §12 and the general criterion. Register `PosterTitle` and `TdxTicketId` in §10.
- Report the documents and sections changed, any contradictions, and the assumptions the owner must confirm.
- No code, tests, migrations, or TDX calls. Wait for the owner's GO.

## Stage 2 — Sandbox implementation (after GO)
- Implement the contract: field, migration, client, creation on acceptance, duplicate prevention, retry, ticket number in the confirmation and technician area.
- Test against the sandbox only. Report the tickets created and the payload sent, without secrets.
- Update the database Install and Upgrade scripts following the existing pattern.
- Report each acceptance criterion as PASS, FAIL, or NOT RUN with the reason. No production calls, no commit, no deployment. Build and test passing is not acceptance.
