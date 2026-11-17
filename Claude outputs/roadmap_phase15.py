import copy, sys
import docx

path = sys.argv[1]
d = docx.Document(path)
P = d.paragraphs

def insert_after(anchor, text, like):
    new = copy.deepcopy(like._p)
    for child in list(new):
        if not child.tag.endswith('}pPr'):
            new.remove(child)
    anchor._p.addnext(new)
    para = docx.text.paragraph.Paragraph(new, anchor._parent)
    para.add_run(text)
    return para

def chain(anchor, items):
    cur = anchor
    for text, like in items:
        cur = insert_after(cur, text, like)

def check(i, start):
    assert P[i].text.startswith(start), (i, P[i].text[:60])

# expected anchors
check(91, 'Los cuatro campos históricos'); check(101, 'La aplicación no crea, lee')
check(153, 'Autorización de FASE 15'); check(172, 'No existe dependencia técnica de TDX')
check(274, 'The four historical fields'); check(284, 'The application does not create, read')
check(336, 'PHASE 15 authorization'); check(356, 'No technical TDX dependency exists')
H2_es, N_es, B_es = P[83], P[84], P[85]
H2_en, N_en, B_en = P[266], P[267], P[268]

ES = {
 'note': 'Autorización de la integración de FASE 15, 16 de noviembre de 2026: Por autorización explícita del dueño del proyecto del 16 de noviembre de 2026, se autoriza la integración de TDX en PosterPrintRequest. Autoridad: docs/TDX_Integration_Contract_v1.docx es la única autoridad sobre la integración. Alcance: solo el sandbox de TDX; producción queda para una etapa posterior. Estado: contrato documentado; la implementación (etapa 2) empieza solo tras la revisión del contrato y el GO del dueño. La ejecución de aceptación del validador queda pendiente; para esta fase la sustituye la prueba general en el sandbox definida en la sección 14 del contrato, cuyos resultados se registran. La exención de prueba de la sección 9.5 del contrato requiere la enmienda de la sección 14 del Storage Access Contract por el dueño antes de la etapa 2. Build PASS y Tests PASS no constituyen aceptación. Esta nota no cambia ninguna otra fase.',
 's12': 'Enmienda de FASE 15, 16 de noviembre de 2026: Para las solicitudes nuevas aceptadas, la aplicación crea un ticket de TDX en el sandbox conforme a docs/TDX_Integration_Contract_v1.docx. Quedan sustituidas, solo en ese alcance, las reglas de esta sección según las cuales no hay integración automática, el solicitante crea el ticket manualmente y la aplicación no crea, lee, modifica ni sincroniza tickets de TDX, así como las menciones a la creación manual del ticket en las secciones 1, 2 y 11. La aplicación no asigna personas, no modifica, no comenta y no cierra tickets. Las solicitudes aceptadas antes de FASE 15 no reciben ticket.',
 'h103': '10.3 Campos TDX — FASE 15',
 'n103': 'FASE 15 registra tres campos en PosterRequest. docs/TDX_Integration_Contract_v1.docx es la única autoridad sobre su significado y uso.',
 'b1': 'PosterRequest.PosterTitle — nuevo; título del póster, obligatorio en solicitudes nuevas; NULL en solicitudes aceptadas antes de FASE 15.',
 'b2': 'PosterRequest.TdxTicketId — nuevo; número del ticket devuelto por TDX; único cuando no es NULL; se escribe una sola vez.',
 'b3': 'PosterRequest.TdxTicketState — nuevo; None, Pending, Created o Uncertain; None en las solicitudes existentes.',
 'c15': 'Enmienda de FASE 15, 16 de noviembre de 2026: para las solicitudes nuevas, la aceptación del póster sigue sin depender de TDX; la aplicación crea el ticket en el sandbox conforme a docs/TDX_Integration_Contract_v1.docx y lo reintenta sin duplicados cuando TDX falla.',
}
EN = {
 'note': 'PHASE 15 integration authorization, 16 November 2026: By explicit authorization of the project owner on 16 November 2026, the integration of TDX into PosterPrintRequest is authorized. Authority: docs/TDX_Integration_Contract_v1.docx is the sole authority for the integration. Scope: the TDX sandbox only; production is a later step. Status: contract documented; implementation (stage 2) starts only after the owner reviews the contract and gives GO. The validator\'s acceptance run is pending; for this phase it is replaced by the general sandbox test defined in section 14 of the contract, whose results are recorded. The test exemption in section 9.5 of the contract requires the owner\'s amendment of section 14 of the Storage Access Contract before stage 2. Build PASS and Tests PASS are not acceptance. This note does not change any other phase.',
 's12': 'PHASE 15 amendment, 16 November 2026: For new accepted requests, the application creates a TDX ticket in the sandbox as defined in docs/TDX_Integration_Contract_v1.docx. Superseded, only within that scope, are the rules in this section that there is no automatic integration, that the requester creates the ticket manually, and that the application does not create, read, update or synchronize TDX tickets, as well as the mentions of manual ticket creation in sections 1, 2 and 11. The application does not assign people to, update, comment on or close tickets. Requests accepted before PHASE 15 get no ticket.',
 'h103': '10.3 TDX Fields — PHASE 15',
 'n103': 'PHASE 15 registers three fields on PosterRequest. docs/TDX_Integration_Contract_v1.docx is the sole authority for their meaning and use.',
 'b1': 'PosterRequest.PosterTitle — new; poster title, required on new requests; NULL on requests accepted before PHASE 15.',
 'b2': 'PosterRequest.TdxTicketId — new; ticket number returned by TDX; unique when not NULL; written once.',
 'b3': 'PosterRequest.TdxTicketState — new; None, Pending, Created or Uncertain; None on existing requests.',
 'c15': 'PHASE 15 amendment, 16 November 2026: for new requests, poster acceptance still does not depend on TDX; the application creates the ticket in the sandbox as defined in docs/TDX_Integration_Contract_v1.docx and retries without duplicates when TDX fails.',
}

# insert from the highest index down so earlier indices stay valid
chain(P[356], [(EN['c15'], P[356])])
chain(P[336], [(EN['note'], P[336])])
chain(P[284], [(EN['s12'], P[285])])
chain(P[274], [(EN['h103'], H2_en), (EN['n103'], N_en), (EN['b1'], B_en), (EN['b2'], B_en), (EN['b3'], B_en)])
chain(P[172], [(ES['c15'], P[172])])
chain(P[153], [(ES['note'], P[153])])
chain(P[101], [(ES['s12'], P[102])])
chain(P[91], [(ES['h103'], H2_es), (ES['n103'], N_es), (ES['b1'], B_es), (ES['b2'], B_es), (ES['b3'], B_es)])

d.save(path)
print('ok')
