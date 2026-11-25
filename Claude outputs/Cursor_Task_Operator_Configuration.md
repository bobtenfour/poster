# Cursor Task — Operator Configuration Reorganization (approved presentation change, 23 November 2026)

## Precondition (check first, otherwise STOP)

Implement only if both are true. If either is missing, stop and report which one.

1. `docs/Operator_Configuration_Presentation_Contract_v1.docx` shows the status **"Approved by the owner on 23 November 2026, with GO for implementation"**.
2. `docs/Poster_Print_Request_System_Roadmap_v4_Inventory.docx` contains the record **"Approved presentation change, 23 November 2026 — Operator Configuration reorganization"** (and its Spanish twin). By the owner's decision this change has no phase number.

## Authority

- `docs/Operator_Configuration_Presentation_Contract_v1.docx` is the only authority for this task. Read it completely before changing anything.
- The roadmap `.docx` is the only authority for phase order and status. This change creates no phase. Do not edit the roadmap and do not create or promote any phase.
- The functional decisions in section 2 of the contract (O1 to O8) are final. Do not question, reinterpret or redesign them.
- If the contract conflicts with another contract outside its section 10, stop and report the conflict.

## What to build

| Route | Content | Rendering |
|---|---|---|
| `/technician/configuration` | Landing with three cards, in this order: **Departments**, **Printers**, **Consumables & Materials** (contract §4) | Static, no `SessionGuard` |
| `/technician/configuration/departments` | Panels **Departments** and **Reason / Events**, unchanged (§5) | Interactive, `SessionGuard` |
| `/technician/configuration/printers` | Panel **Printer models**, unchanged, plus a new panel **Printer compatibility** (§6) | Interactive, `SessionGuard` |
| `/technician/configuration/consumables` | Panels **Consumable types** and **Consumables / Materials**; the compatibility checkboxes leave the consumable edit form (§7) | Interactive, `SessionGuard` |

- Page components: `ConfigurationDepartments`, `ConfigurationPrinters` and `ConfigurationConsumables`. `TechnicianConfiguration` becomes the landing.
- All four routes use `TechnicianLayout` and `PosterAccess.OperatorPolicy`.
- Each window has the page header with kicker **Configuration** and its own title, plus a link back to the landing.
- **Cards (§4.3–4.4):** one shared style rule in `foundation.css`. Same size and emphasis for all three. One row at 768 px and wider; stacked below 768 px in the same order. The whole card is the link.
- **Printer compatibility (§6):**
  - Choose one active printer model.
  - Show the catalog consumables grouped by type in `DisplayOrder`, with the Show filter Active / Inactive / All (default Active).
  - One checkbox per consumable, applied with **Save** through the existing `OperatorConfiguration.SetCompatibilityAsync`, one call per changed pair.
  - Keep the Consumables Inventory Contract 4.3 rule: when the consumable's type does not use printer compatibility, the consumable appears only while a link exists, and the link can only be removed.
- **Catalog row (§7):** remove the compatibility checkboxes from the edit form and keep the read-only summary. Change the helper text exactly as §7.2 says.
- **Menu (§8.1):** one **Configuration** entry, active on all four routes. The Dashboard entry is not active on any of them.
- **Help (§8.3):** update `OperatorHelp` to say that Configuration opens three windows. Change no other help text.

Move the existing panel markup and code-behind into the new pages. Keep the element ids, texts, validations and messages. Do not change `IOperatorConfiguration`, `OperatorConfiguration` or any other service, model, migration, DI registration or policy.

## Do not touch (restriction O7, contract §9)

- Inventory (`/technician/inventory`), its panels, the lot dialog, the dashboard and View Inventory.
- Printing Consumables and consumable-type rules, data and services; the ERD; migrations; Entra; identity; shared storage; TDX.
- No modal changes. No color or typography changes outside the card rule.

## Tests (§11.6)

- Move the existing expectations of `OperatorConfigurationTests`, `RoleSeparationTests`, `EntraSessionTests` and `PrintingInventoryTests` to the new routes, **without weakening any expected result**.
- Add tests for:
  - the landing: three cards, their order, their links, and the single shared card rule;
  - the authorization of the four routes;
  - compatibility from Printers: link, unlink, the 4.3 rule and Save;
  - the checkboxes being gone from the catalog row;
  - the menu active state;
  - the `SessionGuard` list (Authentication Contract 5.9 as amended in §10).
- No stubs, fakes in production code, TODOs or FIXMEs.

## Documents

Apply the amendments in section 10 of the contract **verbatim** to the four contracts listed there. Change no other sentence.

## Report

1. Changed files.
2. The test results.
3. For each acceptance item 11.1–11.5, the evidence (route, test name, screenshot).

Build PASS and Tests PASS are not acceptance. The owner accepts by reviewing the running application. Do not commit unless asked.
