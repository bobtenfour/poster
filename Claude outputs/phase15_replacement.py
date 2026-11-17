import copy, docx

def setp(p, t):
    for r in p.runs[1:]:
        r._r.getparent().remove(r._r)
    p.runs[0].text = t

def after(a, t, bold=False):
    n = copy.deepcopy(a._p)
    for c in list(n):
        if not c.tag.endswith('}pPr'):
            n.remove(c)
    a._p.addnext(n)
    q = docx.text.paragraph.Paragraph(n, a._parent)
    r = q.add_run(t)
    r.bold = bold
    return q

def chain(a, items):
    for t, b in items:
        a = after(a, t, b)
    return a

# ---------------- TDX Integration Contract ----------------
f = 'docs/TDX_Integration_Contract_v1.docx'
d = docx.Document(f)
def find(s): return [p for p in d.paragraphs if p.text.startswith(s)][0]

sc = find('Scope:')
setp(sc, sc.text + ' Part B (section 17): replacement of an accepted poster by its requester.')
setp(find('16.1 '), '16.1 Not in scope: production, attachments, ticket updates or closure, notifications, creation of people, and changes to storage, authentication or retention other than those defined in section 17.')

S = [
('17. Part B — Poster replacement', True),
('17.1 Decision of the owner (16 November 2026). The requester can replace the poster file of his own accepted request at any time, also after it was printed. The request keeps its PosterRequestId, its Poster ID and its TDX ticket. The application never creates a second ticket and never updates the ticket for a replacement (4.3).', False),
('17.2 Who. Only the requester of the request: a signed-in user whose oid equals PosterRequest.SubmittedByObjectId. A historical request that is not linked cannot be replaced. Technicians do not replace posters in this phase.', False),
('17.3 Where. Each poster in the requester\'s personal activity on the home page has the action "Replace poster", which opens the replacement page for that Poster ID.', False),
('17.4 Validation. The new file passes the same preflight as a new request (format, one page or slide, maximum dimensions, readable file). If it fails, nothing changes.', False),
('17.5 Note. The requester must write a short note explaining the error: trimmed, 1 to 500 characters, no control characters.', False),
('17.6 Archive folder. At the shared-storage root there is a folder named sustitutions. For each replacement the replaced poster file is moved into sustitutions/<date> <Poster ID> <ticket>/, where <date> is the replacement date as yyyy-MM-dd in the server\'s local time and <ticket> is TDX-<ticket number>, or TDX-PENDING when the request has no ticket number yet. The folder name does not change later. Inside, the replaced file is named <HHmmss> - Poster.pdf or <HHmmss> - Poster.pptx (time of the replacement), and the note is written next to it as <HHmmss> - Note.txt (UTF-8). Several replacements on the same day share the folder. Files in sustitutions are history: never printed, never served as the current poster, never overwritten.', False),
('17.7 Current file. The new file takes the place of the old one in the request\'s current poster directory, with the same name rules (Poster.pdf or Poster.pptx). PosterFile.StoragePath and the file metadata are updated. The Approval Sheet is not changed.', False),
('17.8 Printed posters. If the poster was printed, the replacement puts it back to be printed: the poster directory moves from the PRINTED folder to the corresponding TO BE PRINTED folder (Shared Storage Contract section 9, reverse direction), and Printed, Laminated, Notified, Date Out and Picked Up By are cleared. Received is kept. The technician then follows the normal workflow again.', False),
('17.9 Order and failure. One replacement at a time per request. Order: preflight; archive the old file and write the note; place the new file; move the directory when 17.8 applies; save the database changes in one transaction. If any step fails, the completed file steps are undone, the database is not changed, and the request keeps its previous file and state. A replacement is reported as successful only when all steps succeeded.', False),
('17.10 Data model. New table PosterReplacement: PosterReplacementId PK, PosterRequestId FK, ReplacedAt datetime2 (UTC), Note nvarchar(500), ArchivedFilePath nvarchar(400) (relative to the shared-storage root), ReplacedByObjectId uniqueidentifier. Created by the same migration as section 7 and registered in roadmap section 10.3.', False),
('17.11 Technician. The technician area marks a replaced request in red (--epx-blueignix-red) with the number of replacements, the date and note of the last one, and a link to the archived files. The technician library shows a fifth root folder card, sustitutions, with its label in red; the existing four cards do not change.', False),
('17.12 Requester dashboard. The home page shows a new stat card "Replacements" next to Posters Printed and Events Participated: the number of replacements on the requester\'s own requests.', False),
('17.13 Storage access. Every file operation of Part B goes through ISharedStorageAccess. No new storage exemption is needed. The Shared Storage Contract is amended on 16 November 2026 to authorize the sustitutions folder.', False),
('17.14 Not changed. The PHASE 14 temporary deletion and restoration do not touch the sustitutions folder.', False),
('17.15 Acceptance. (a) a replacement keeps the Poster ID and the ticket and creates no ticket; (b) the old file and the note are in the correct sustitutions folder and the new file is the current poster; (c) a printed poster returns to TO BE PRINTED with the stages after Received cleared; (d) a failed preflight or a failed step leaves everything unchanged; (e) only the requester can replace; (f) the dashboard counter, the red technician marker and the red library card are shown.', False),
]
chain(find('16.3 '), S)
d.save(f)

# ---------------- Shared Storage Contract ----------------
f = 'docs/Shared_Storage_Contract_v1.docx'
d = docx.Document(f)
last = [p for p in d.paragraphs if p.text.strip()][-1]
chain(last, [
('13. Amendment of 16 November 2026 — sustitutions folder', True),
('By explicit authorization of the project owner on 16 November 2026, a folder named sustitutions is authorized at the shared-storage root. It holds the poster files replaced by their requesters and the requesters\' notes, as defined in section 17 of docs/TDX_Integration_Contract_v1.docx, which is the sole authority for its structure: sustitutions/<date> <Poster ID> TDX-<ticket number or PENDING>/. Files there are history and are never a current poster. The technician library shows it as a fifth root card whose label is red, the existing product red --epx-blueignix-red (#f05252). A replacement of a printed poster moves its directory from PRINTED back to TO BE PRINTED and sets Printed to false, with the same success and failure rules as section 10. The boundary in section 12 is amended only to that extent; nothing else in this contract changes.', False),
])
d.save(f)

# ---------------- Roadmap ----------------
f = 'docs/Poster_Print_Request_System_Roadmap_v4_Inventory.docx'
d = docx.Document(f)
for p in d.paragraphs:
    if p.text.startswith('PosterRequest.TdxClaimedUntil — nuevo'):
        after(p, 'PosterReplacement — nueva entidad; una fila por sustitución del póster por su solicitante: fecha, nota, ruta del archivo archivado en sustitutions y oid de quien sustituyó (contrato TDX, sección 17).')
    if p.text.startswith('PosterRequest.TdxClaimedUntil — new'):
        after(p, 'PosterReplacement — new entity; one row per replacement of the poster by its requester: date, note, path of the archived file in sustitutions, and oid of the replacer (TDX contract, section 17).')
    if p.text.startswith('Autorización de la integración de FASE 15, 16 de noviembre de 2026'):
        setp(p, p.text.replace('Esta nota no cambia ninguna otra fase.', 'También forma parte de FASE 15 la sustitución de un póster aceptado por su solicitante (sección 17 del contrato), con la carpeta sustitutions autorizada por la enmienda del 16 de noviembre de 2026 del Shared Storage Contract. Esta nota no cambia ninguna otra fase.'))
    if p.text.startswith('PHASE 15 integration authorization, 16 November 2026'):
        setp(p, p.text.replace('This note does not change any other phase.', 'The replacement of an accepted poster by its requester (section 17 of the contract) is also part of PHASE 15, with the sustitutions folder authorized by the 16 November 2026 amendment of the Shared Storage Contract. This note does not change any other phase.'))
    if p.text.startswith('FASE 15 registra cuatro campos'):
        setp(p, p.text.replace('cuatro campos en PosterRequest', 'cuatro campos en PosterRequest y la entidad PosterReplacement'))
    if p.text.startswith('PHASE 15 registers four fields'):
        setp(p, p.text.replace('four fields on PosterRequest', 'four fields on PosterRequest and the PosterReplacement entity'))
d.save(f)
print('ok')
