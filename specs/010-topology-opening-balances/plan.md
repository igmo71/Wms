# Implementation Plan: Load warehouse topology and opening balances

**Branch**: `codex/010-topology-opening-balances` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

**Input**: The agreed two-stage warehouse cutover specification, the full 1C workbook and the reviewed 23-cell mixing decision.

## Summary

Replace the obsolete multi-file `.xls` map importer with one `.xlsx` reader and two Administrator commands: topology first, opening stock second. Persist a warehouse-unique import record binding both stages to the same source, then commit each stage atomically after a state-bound preview. Build picking, reserve, receiving and shipping zones from the export; preserve approved source corrections and exceptions. The opening-stock stage posts signed, dated source rows as WMS movements, balances and turnovers with one warehouse-wide replay guard. Adapt current receiving putaway and its Mobile scan to both picking and reserve destinations, while leaving old picking, transfer and count rewrites for later features.

The design decisions and rejected alternatives are in [research.md](research.md). The persisted relationships are in [data-model.md](data-model.md); the Web and Mobile interactions are in [contracts](contracts/).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: EF Core 10, SQL Server, ExcelDataReader 3.9.0, ASP.NET Core/Blazor WebApp, MudBlazor, existing MAUI Mobile V1 contracts

**Storage**: Existing SQL Server database through `ApplicationDbContext`; one new import table, a new audit relation and opening recorder constraint

**Testing**: Minimal xUnit integration checks against disposable SQL Server LocalDB for atomic stock posting, signed and dated balances, reviewed mixed-cell exception, and warehouse-wide replay; manual Web/Mobile verification by the developer

**Target Platform**: Existing WebApp and WebApi services plus Android Mobile client

**Project Type**: Multi-project warehouse application with Domain, Application, Infrastructure, Integration and client boundaries

**Performance Goals**: Preview and load the current 12,597-row workbook in one bounded upload without splitting it or rendering all rows at once

**Constraints**: SQL Server compatibility level 120; one snapshot per warehouse; separate atomic stage commits; no direct balance overwrite; no inferred SKU or unit conversion; no operational commands run by Codex without an explicit user request
**Scale/Scope**: Approximately 11,365 distinct cells and more than 8,000 source stock rows in the current sample; one selected warehouse per import

## Constitution Check

*Gate reviewed before research and rechecked after design: pass.*

| Principle | Design response |
|---|---|
| WMS owns inventory | Source rows become identifiable WMS opening movements and balances. Future corrections use ordinary movements; a later workbook cannot overwrite stock. |
| One posting path per physical event | A dedicated opening batch is the sole entry for opening stock. It stages movements, balances, turnovers, audit, flags, receipt and import status for one final commit. Ordinary receipt/putaway posting remains authoritative for later physical work. |
| Explicit 1C boundary | `Wms.Integration` parses source spreadsheet terms; Application decides WMS zones, approved exceptions, SKU resolution and stock effects. Unknown and absent source values stay distinct. |
| Direct feature-oriented implementation | Add one import use-case area and its Infrastructure store. Reuse existing zone, location, movement, balance and turnover entities. Remove the old map import path and its Excel dependency from the legacy project. |
| Risk-based verification | Add focused SQL checks for partial/duplicate stock, negative and dated balances, and the eight prohibited mixed cells. Extend existing coverage where possible. The developer owns build, test, launch and migration commands. |

No constitutional departure is required. The Administrator-only cutover command may enable an imported SKU's WMS-owned date flag as part of the opening transaction; ordinary manual flag edits retain their Manager rule. The import audit explicitly records this setup action.

## Phase 0: Research decisions

1. Read one ten-column `.xlsx` worksheet by header, retaining row numbers. Keep parsing in Integration and interpretation in Application. Do not rely on workbook cell colors or the old filename/tier conventions.
2. Keep existing `ZoneType.Storage` numeric value as the picking purpose and add Reserve. Derive zones by physical area and source kind. Use the existing rack/mezzanine folder codes; put gate addresses under a `Ворота` folder. Reject range generation beneath that special root rather than let it bypass address-path checks.
3. Store a `WarehouseOpeningImport` row with unique warehouse key, source hash and stage status. Recompute each preview on confirmation and compare a signature containing relevant saved state. The same source can be reuploaded for stock after the topology dialog or application restarts.
4. Use an opening batch posting path, not a loop around the receiving-only handler or the older three-dimensional posting service. Represent signed rows with positive movement magnitude and source/destination direction; maintain one four-dimensional balance per key.
5. Keep the exact reviewed source exceptions visible. The 15 approved locations permit mixing; the eight prohibited locations retain their actual initial mixed stock with permission off and a derived, visible violation. New unknown mixed locations block the snapshot.
6. Add a putaway-specific Mobile barcode context so current receiving putaway can scan either storage purpose without broadening old picking, transfer or count scans.

All research choices are resolved in [research.md](research.md); there are no remaining technical `NEEDS CLARIFICATION` markers.

## Phase 1: Design and delivery sequence

### 1. Source interpretation and topology stage

- Move the ExcelDataReader dependency to `Wms.Integration`. Implement one parser port returning typed source rows, row numbers and parse issues. Bound upload bytes and row count above the current sample. Treat a missing stock quartet as an empty cell, not a zero stock fact.
- In Application, normalize addresses and classify each row. Apply only the explicitly approved exclusions and the guarded `9Z99A1` correction; any changed or new anomalous value blocks preview. Derive the source content hash and row-decision summary. Keep the 15/8 address decisions in one reviewable import policy, not in cell formatting.
- Add Reserve to domain zone types while retaining existing enum values. Build the rack/mezzanine physical paths and flat gate folder, then create/reuse zones and cells. Validate the entire plan before staging changes. Reuse a matching existing cell, but reject wrong purpose, parent, activation or SKU-mixing setting.
- Introduce `WarehouseOpeningImport`, its unique warehouse constraint and topology status. Stage the import row, zones, folders, cells, one audit event and a command receipt in one commit. A stale preview or concurrent import returns a business conflict. Keep the old map importer, dialog, button and obsolete tests out of the delivered workflow.

### 2. Opening-stock stage

- Reupload the same workbook and require its hash to match the topology stage. Ask for the declared source snapshot time. Verify target cells and zone purposes, catalog GUIDs, canonical units, quantities and dates. Block existing warehouse stock/work and any unreviewed mixed cell. Build one complete stock plan before mutation.
- Add `RecorderType.OpeningBalance` and batch posting through Application and an Infrastructure store. Each accepted source row produces a movement and turnover; balances are accumulated by warehouse/cell/SKU/nullable date in memory so duplicate rows use one balance. Permit negative opening values, including shipping, only in this opening use case. Reuse domain quantity, movement, balance and turnover invariants and the existing SQL balance key.
- Enable each source `Да` SKU's date flag in the same commit; keep missing date unknown and preserve supplied past dates. Retain the eight reviewed prohibited mixed states as the one-time opening exception. A derived discrepancy query/report shows them until outward movements resolve them; ordinary inflow continues to fail while mixed.
- Extend `WarehouseAuditEvent` with an import relation and write one compact event per stage, linked to the command receipt. Add a filtered unique opening recorder-line constraint and classify its violation plus the warehouse-unique import constraint as business conflicts. Stage movements, turnovers, balances, flags, import status, audit and receipt for one final save; any failure leaves the topology-only state intact.

### 3. Active receiving and client boundary

- Update the new receiving posting route, destination recommendations and storage-mixing rule to regard Picking and Reserve as storage destinations. Keep direct shipping pick sources limited to Picking.
- Add `PutawayDestination` and `Reserve` Mobile V1 context values without renumbering existing values. Make the receiving putaway page request the new context; keep older `Storage` requests strict. Recheck eligibility at final putaway posting after scan.
- Show mixing permission and derived initial violation on both storage-purpose locations in Web topology administration. Do not expand old transfer or inventory count by changing a shared zone predicate; their dated/signed replacements are separate features and are required before operational use of reserve and shipping stock.

### 4. Verification and documentation checkpoints

- Use the smallest SQL integration scenarios that prove all-or-none opening effects, warehouse-wide duplicate protection across users/files, signed/date balance preservation, and the red-cell exception without normal inflow bypass. Do not reproduce generic command-executor replay tests.
- The developer generates/applies the EF migration and runs builds/tests. Codex reviews the generated migration, SQL Server 2014-compatible constraints and reported results before progressing. Web/Mobile/device checks follow [quickstart.md](quickstart.md).
- When implementation lands, update `.specify/memory/current/inventory.md`, `receiving.md`, `identity.md` and `.specify/memory/architecture.md` for the implemented rules and structure. Keep the 1C reconciliation report, picking/replenishment, transfers and inventory-count replacement in backlog. Do not present planned behavior as current before it is built.

## Project Structure

### Documentation (this feature)

```text
specs/010-topology-opening-balances/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── admin-import.md
│   └── mobile-putaway.md
└── checklists/requirements.md
```

The dependent implementation increments are in [tasks.md](tasks.md).

### Source Code (repository root)

```text
Wms.Domain/
├── Enums/ZoneType.cs, Enums/RecorderType.cs
├── WarehouseOpeningImport.cs (new)
└── InventoryMovement.cs, InventoryBalance.cs, InventoryTurnover.cs,
    StorageLocation.cs, WarehouseAuditEvent.cs
Wms.Application/
├── OpeningImports/ (new parser port, preview and confirmation use cases)
├── Inventory/PostInventoryMovement.cs
└── Receiving/PutAwayReceivingLpn.cs
Wms.Infrastructure/Data/
├── OpeningImportStore.cs (new), InventoryPostingStore.cs,
    CommandExecutionStore.cs, ReceivingWorkQueryStore.cs
├── Configurations/ and Migrations/
└── DependencyInjection.cs
Wms.Integration/OneS/
└── OpeningExportReader.cs (new)
Wms.WebApp/Components/Pages/StorageLocationPages/
└── replacement import dialog and topology page
Wms.Contracts/Mobile/V1/ and Wms.WebApi/Mobile/
└── putaway scan contract and endpoint context
Wms.Mobile/Pages/
└── receiving LPN putaway scan
Wms/Application/StorageLocations/
└── remove obsolete StorageMapImportService; constrain gate range generation
Wms.Tests/
└── focused import safety scenarios
```

**Structure Decision**: New parsing, use cases and persistence follow existing project boundaries. The obsolete importer alone leaves `Wms`; manual topology and older processes remain until their scheduled replacement. No new project or generic import framework is introduced.

## Post-design Constitution Check

The Phase 1 model preserves the initial gate: import effects have one WMS owner and one commit per stage; source-format code stays in Integration; Application decides exceptions and routes; the legacy XLS path is removed; verification targets data-loss and duplicate-stock risks. The only new lasting persistence is the warehouse import record and a narrow audit reference. No complexity exception is needed.
