import sys, copy
import docx
from docx.shared import Pt

base, out = sys.argv[1], sys.argv[2]
d = docx.Document(base)
body = d.element.body
for el in list(body):
    if el.tag.endswith('sectPr'):
        continue
    body.remove(el)

def title(t):
    p = d.add_paragraph(); r = p.add_run(t); r.bold = True; r.font.size = Pt(16)

def line(t):
    d.add_paragraph(t)

def h(t):
    p = d.add_paragraph(); r = p.add_run(t); r.bold = True

def table(rows):
    t = d.add_table(rows=len(rows), cols=len(rows[0])); t.style = 'Table Grid'
    for i, row in enumerate(rows):
        for j, v in enumerate(row):
            t.cell(i, j).text = v
            if i == 0:
                for r in t.cell(i, j).paragraphs[0].runs: r.bold = True

title('TDX INTEGRATION CONTRACT')
line('Poster Print Request System')
line('Version: 1.0')
line('Date: 16 November 2026')
line('Classification: A — STRUCTURAL CONTRACT')
line('Status: Documents the PHASE 15 integration of TDX into PosterPrintRequest, authorized by the project owner on 16 November 2026. Implementation starts only after the owner reviews this contract and gives GO.')
line('Scope: Creation of one TDX ticket per accepted poster request in the TDX sandbox; storage of the ticket number; requestor identification; retries without duplicates; the new Poster title field; presentation of the ticket on the confirmation page, in Staff Help and in the technician area; configuration and credential for the sandbox.')

h('1. Authority')
line('1.1 This contract is the sole authority for the integration of TDX into PosterPrintRequest.')
line('1.2 Poster_Print_Request_System_Roadmap_v4_Inventory.docx remains the sole authority for phase order and status. The PHASE 15 integration note of 16 November 2026 references this contract.')
line('1.3 docs/TDX_Validation_Tool_Contract_v1.md remains the authority for the validation tool. The validator keeps its behavior; this contract only allows the shared library to be generalized as described in section 11.')
line('1.4 docs/Shared_Storage_Contract_v1.docx, docs/Storage_Access_Contract_v1.docx, docs/Authentication_Contract_v1.docx and docs/Temporary_Poster_Retention_Contract_v1.docx are not changed, except the single test exemption required by section 9.5, which needs the owner\'s amendment of the Storage Access Contract. If any of them contradicts this contract, work stops and the conflict is reported.')
line('1.5 Authorization basis. The owner authorized the integration on 16 November 2026 (validation contract section 14, explicit owner authorization). Evidence available: a manual test by the owner in the sandbox, in which the service account authenticated, created a ticket with the configured identifiers, and found people by email and by name. The validator\'s own acceptance run is pending; the general sandbox test of section 14 replaces it for this phase, and its results are recorded.')

h('2. Current behavior (evidence)')
line('Inspected on 16 November 2026. This section records what exists. It authorizes nothing.')
table([
    ['Area', 'Current behavior', 'Evidence'],
    ['Confirmation page', 'Tells the requester to create the TDX ticket manually with the Poster ID; states that the system does not create the ticket.', 'Components/Pages/RequestAccepted.razor'],
    ['Staff Help', 'Panel "Poster ID and the TDX ticket" says the application does not create a TDX ticket.', 'Components/Pages/Help.razor, panel confirmation'],
    ['Data model', 'PosterRequest has no poster title and no ticket number.', 'PosterPrintRequest.Domain/PosterRequest.cs'],
    ['Requester email', 'The form asks for Email; it is stored in PosterRequest.Email.', 'Request.razor, RequesterDraft.cs'],
    ['TDX client', 'The shared library src/PosterPrintRequest.Tdx exists, used only by the validator. It enforces the endpoint guard, refuses redirects, performs the bounded lookup and reports Created, AlreadyExists, Uncertain or Failed.', 'src/PosterPrintRequest.Tdx'],
    ['Credentials pattern', 'Secrets are DPAPI LocalMachine files with a fixed entropy per purpose, path from configuration.', 'QumuloCredential.cs, EntraClientSecret.cs'],
])

h('3. Environment')
line('3.1 Sandbox only. In this phase the application is configured for, and calls, only the TDX sandbox Web API. No production URL, application ID, identifiers or service account are configured, documented with real values, or called.')
line('3.2 The Web API path and the TDX web portal path are different. Only the Web API path is used.')
line('3.3 Production is a later step, after the owner accepts the general test, through an amendment of this contract.')

h('4. Decisions of the owner')
line('4.1 On acceptance of a new request, exactly one TDX ticket is created.')
line('4.2 Data sent to TDX: Poster ID, requester name (PosterRequest.Name), poster title, and the requester\'s email (PosterRequest.Email), the email used only to identify the TDX Requestor. No attachments and no other data.')
line('4.3 The application never assigns a person, updates, comments on, or closes tickets.')
line('4.4 Acceptance of the poster never depends on TDX.')
line('4.5 Requests accepted before PHASE 15 get no ticket and no title.')
line('4.6 Nothing is ever created in the TDX people database.')

h('5. Ticket content')
line('5.1 Title: <Poster ID> — <Poster title>.')
line('5.2 Description: Requester: <name>.')
line('5.3 Form, type, status, priority and source: identifiers from configuration.')
line('5.4 Requestor: as defined in section 6.')
line('5.5 Account (Acct/Dept): the DefaultAccountID of the person found as Requestor. If no person is found, the fallback Tdx:AccountId from configuration (the value used in the sandbox test).')
line('5.6 Responsible group: Tdx:ResponsibleGroupId from configuration. No responsible person is sent; the person is assigned in TDX when a technician takes the ticket.')
line('5.7 Notifications: EnableNotifyReviewer=false, NotifyRequestor=false, NotifyResponsible=false. AllowRequestorCreation=false.')
line('5.8 "Created by" is always the service account. TDX does not allow it to be changed.')

h('6. Requestor identification')
line('6.1 Search with POST people/search and SearchText.')
line('6.2 By email: PosterRequest.Email, trimmed and lower-cased. Only people whose PrimaryEmail equals it are kept. A value without @ is not searched by email.')
line('6.3 By name, only if the email search did not give exactly one person: PosterRequest.Name as typed, at most 50 characters. Only people whose FullName equals it are kept.')
line('6.4 Comparisons are done by the application, case-insensitive, with repeated spaces collapsed. TDX\'s own case behavior is not relied on.')
line('6.5 Exactly one person: RequestorUid and that person\'s department. Zero or several: no requestor, the fallback account, and the name stays in the description. The application never chooses among several.')
line('6.6 The service account needs access to the TDX People application (confirmed in the sandbox).')

h('7. Data model')
line('7.1 PosterRequest.PosterTitle nvarchar(200) NULL. Required on new requests; null only for requests accepted before PHASE 15.')
line('7.2 PosterRequest.TdxTicketId int NULL, unique when not null (filtered unique index), written once and never changed.')
line('7.3 PosterRequest.TdxTicketState: one of None (before PHASE 15), Pending, Created, Uncertain. Stored as a short string column, not null, default None for existing rows.')
line('7.4 One EF Core migration adds the three columns and the index. artifacts/deployment/Poster.Database.Install.sql and Poster.Database.Upgrade.sql are regenerated from the migrations.')
line('7.5 The roadmap registers the new fields in section 10.3.')

h('8. Form, acceptance and retries')
line('8.1 New required form field Poster title: trimmed, 1 to 200 characters, no control characters.')
line('8.2 The request is accepted and stored first, with TdxTicketState = Pending. Ticket creation runs after the acceptance is committed and never rolls it back.')
line('8.3 Created or AlreadyExists: TdxTicketId stores the ticket number returned by TDX and the state becomes Created.')
line('8.4 Failed: the state stays Pending. A background retry calls the same create-if-absent operation, which always performs the lookup first.')
line('8.5 Uncertain: the state becomes Uncertain. The background retry performs lookups only, never a creation. If a lookup finds exactly one ticket, it is recorded as Created. Otherwise the technician sees "verify in TDX".')
line('8.6 The retry is bounded, uses back-off, and respects the documented rate limit of 60 calls per 60 seconds.')

h('9. Configuration and credential')
line('9.1 Configuration keys (never in code or in the repository): Tdx:BaseUrl (host and Web API path, without /api), Tdx:ApplicationId, Tdx:FormId, Tdx:TypeId, Tdx:StatusId, Tdx:PriorityId, Tdx:AccountId (fallback), Tdx:ResponsibleGroupId, optional Tdx:SourceId, Tdx:LookupAttempts, Tdx:LookupIntervalSeconds. In IIS they are environment variables at the ApplicationHost level. Development points to the sandbox.')
line('9.2 Credential: JSON with UserName and Password, protected with DPAPI LocalMachine and entropy PosterPrintRequest.Tdx.v1, in its own folder under the Secrets structure with its own ACL. Its path comes from Tdx__CredentialFile.')
line('9.3 docs/Tdx_Credential_Provisioning.md describes provisioning with placeholders only.')
line('9.4 If the configuration or the credential is missing or invalid, the application starts, accepts posters, and keeps tickets Pending; the problem is logged without secrets.')
line('9.5 The credential reader is the only new file access outside the storage boundary. The storage test (Only_the_storage_boundary_touches_the_file_system_under_the_root) gets one exemption naming that reader by exact path, like the Entra secret reader. This requires the owner\'s amendment of the Storage Access Contract section 14 before Stage 2.')

h('10. Runtime operations and security')
line('10.1 Allowed calls: authentication, ticket lookup, people search, ticket creation, optional ticket read. Nothing else.')
line('10.2 The token is kept in memory and renewed before expiry.')
line('10.3 Every request carries User-Agent: PosterPrintRequest.Tdx/1.0.')
line('10.4 The endpoint guard, redirect refusal and 3xx handling of the shared library apply to every request.')
line('10.5 Logs: operation, Poster ID, ticket number, HTTP status. Never password, token, credential path or content, requester name or email.')

h('11. Shared library')
line('11.1 The application uses src/PosterPrintRequest.Tdx. It is generalized without changing the validator\'s behavior:')
line('a) creation takes a request object (Poster ID, title, description, optional RequestorUid, account) instead of hard-coded test text; the validator passes its current test values;')
line('b) a lookup match is a ticket whose Title starts with "<Poster ID> " for application tickets, or contains the exact test Poster ID for validator tickets;')
line('c) people search is added for section 6;')
line('d) the User-Agent of section 10.3 is added;')
line('e) the endpoint guard, redirect refusal, bounded lookup, Uncertain outcome and redaction stay exactly as they are.')

h('12. Presentation')
line('12.1 Confirmation page: shows the ticket number when it is Created. While Pending or Uncertain it says that the TDX ticket is being created and that technicians already have the request. It no longer tells the requester to create the ticket manually. The 48-hour statement stays.')
line('12.2 Staff Help, panel "Poster ID and the TDX ticket": updated to say that the application creates the TDX ticket. No other Help content changes.')
line('12.3 Technician area: the request detail shows the ticket number, or "Ticket pending", or "Verify in TDX" for Uncertain.')

h('13. Tests')
line('13.1 Automated tests use a simulated HttpMessageHandler only; no real network connection.')
line('13.2 Existing tests stay green; the only change to an existing test is the exemption of section 9.5.')

h('14. Acceptance (general sandbox test)')
line('14.1 Every new accepted poster gets exactly one ticket in the sandbox, and its number is stored.')
line('14.2 A retry after a failure does not create a duplicate.')
line('14.3 Requestor found by email; found by name when the email gives no single match; no requestor when there is no single match.')
line('14.4 Account is the requestor\'s department, or the fallback.')
line('14.5 The ticket has the configured responsible group and no responsible person.')
line('14.6 No person is created in TDX and no notification email is received.')
line('14.7 The confirmation page, Staff Help and technician area show the states of section 12.')
line('14.8 A poster is accepted when TDX is unreachable, and its ticket is created later.')
line('14.9 Build PASS and Tests PASS are not acceptance.')

h('15. Pending external confirmations')
line('15.1 Production URL, application ID, identifiers and service account (later phase).')
line('15.2 The responsible group identifier for the sandbox application.')
line('15.3 Whether AllowRequestorCreation=false with a RequestorUid behaves as observed in the manual test.')

h('16. Boundary')
line('16.1 Not in scope: production, attachments, ticket updates or closure, notifications, creation of people, changes to the poster workflow, storage, authentication or retention.')
line('16.2 The validator and its contract are not changed beyond section 11.')
line('16.3 Stage 2 implementation starts only after the owner\'s GO.')

d.save(out)
print('ok')
