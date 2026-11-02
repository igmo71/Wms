# Tasks: Load warehouse topology and opening balances

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts](contracts/)

**Organization**: One stage or user scenario per increment. Paths are relative to the repository root. `[P]` means the task touches separate files and has no dependency on another unfinished task in its phase. Operational builds, tests, launches, and EF migration creation/application belong to the developer under `AGENTS.md`; Codex reviews their results and generated migration before proceeding.

## Phase 1: Setup

**Goal**: Make the new XLSX source readable without changing warehouse state.

- [x] T001 Add typed source-row, parse-issue, and reader-port contracts retaining worksheet row number and missing-versus-zero stock fields in `Wms.Application/OpeningImports/IOpeningExportReader.cs`.
- [x] T002 Implement one ten-column `.xlsx` worksheet reader with header validation, bounded file/row limits above the 12,597-row sample, and original row values in `Wms.Integration/OneS/OpeningExportReader.cs`; add ExcelDataReader 3.9.0 to `Wms.Integration/Wms.Integration.csproj`, register it in `Wms.Integration/OpeningImportRegistration.cs`, and compose it in `Wms.WebApp/Program.cs`.

**Checkpoint**: The export can be parsed into explicit rows and issues without creating topology or stock.

## Phase 2: Foundation

**Goal**: Establish the shared zone and import identity before either confirmation command.

- [x] T003 [P] Preserve numeric `ZoneType.Storage = 1`, relabel it `Комплектация`, and append a distinct `Reserve` value labelled `Хранение`, retaining the current values of Transit, Receiving, and Shipping, in `Wms.Domain/Enums/ZoneType.cs`.
- [x] T004 [P] Add `WarehouseOpeningImport` with a unique `WarehouseId`, source name/hash, `TopologyImported`/`StockImported` status, bounded stage summaries, confirmation actor/time fields, concurrency revision, and nullable `DeclaredSnapshotAt` that becomes required on stock confirmation in `Wms.Domain/WarehouseOpeningImport.cs`, `Wms.Infrastructure/Data/Configurations/WarehouseOpeningImportConfiguration.cs`, and `Wms.Infrastructure/Data/ApplicationDbContext.cs`.
- [x] T005 Extend `WarehouseAuditEvent` with optional `WarehouseOpeningImportId` as an affected-object relation and configure it in `Wms.Domain/WarehouseAuditEvent.cs` and `Wms.Infrastructure/Data/Configurations/WarehouseAuditEventConfiguration.cs`; retain the existing audit and command-receipt links.

**Checkpoint**: Both stages can refer to one warehouse-specific import and audit target; no stock schema is changed yet.

## Phase 3: User Story 1 — Load the physical topology (P1)

**Goal**: Preview and atomically import every accepted operational address, including empty cells, with the reviewed zone and mixing decisions.

**Independent check**: On a clean warehouse, the current sample preview accounts for all 12,597 rows and reports 11,365 distinct addresses, including 4,348 empty cells. Topology confirmation creates no inventory balances. Conflicting existing cells or new source kinds block the stage.

- [x] T006 [US1] Interpret source area/group/address/kind in `Wms.Application/OpeningImports/OpeningImportPolicy.cs`: classify every row as accepted, corrected, excluded, or blocking; implement exactly the seven agreed exclusions and guarded `9Z99A1` shipping correction; keep the 15 approved and eight prohibited mixed-SKU address lists independent of workbook color. Protect the approved stock-loss boundary with `Wms.Tests/OpeningImports/OpeningImportPolicyTests.cs`.
- [x] T007 [US1] Build the topology preview and state-bound plan signature in `Wms.Application/OpeningImports/PreviewOpeningTopology.cs`: derive distinct area-purpose zones, rack/mezzanine room-row-section-level folders, a flat `Ворота` root, row decisions, empty-cell counts, duplicate-address checks, and bounded/paged issues without mutation.
- [x] T008 [US1] Add EF topology queries and staging in `Wms.Infrastructure/Data/OpeningImportStore.cs`: match warehouse-wide address, parent, purpose, activation and `AllowsMultipleSkus`; create/reuse valid zones/folders/cells; reject conflicting matches and an already imported warehouse before any save.
- [x] T009 [US1] Implement Administrator-only topology confirmation in `Wms.Application/OpeningImports/ConfirmOpeningTopology.cs`: rebuild and compare the preview signature, stage the warehouse import, topology, one compact audit event and command receipt for one final commit; map unique/concurrency races to a business conflict. Protect atomicity and conflicting existing-cell rejection with one focused SQL scenario in `Wms.Tests/Infrastructure/OpeningTopologyImportTests.cs`.
- [x] T010 [US1] Replace the old multi-file XLS dialog and button with one bounded XLSX topology preview/confirmation flow, including correction/exclusion totals and paged issues, in `Wms.WebApp/Components/Pages/StorageLocationPages/OpeningImportDialog.razor`, `OpeningImportDialog.razor.cs`, `Index.razor`, and `Index.razor.cs`.
- [x] T011 [US1] Remove the obsolete XLS map importer, its models/dialog, registrations, stale entry points and obsolete importer-specific tests from `Wms/Application/StorageLocations/StorageMapImportService.cs`, `StorageMapImportModels.cs`, `Wms.WebApp/Components/Pages/StorageLocationPages/StorageMapImportDialog.razor`, `StorageMapImportDialog.razor.cs`, `Wms/Application/DependencyInjection.cs`, `Wms/Wms.csproj`, and `Wms.Tests/Infrastructure/StorageMapImportSafetyTests.cs`; remove the legacy ExcelDataReader reference after no legacy caller remains.
- [x] T012 [US1] Reject address-range generation below the special `Ворота` root and retain physical-path checks for ordinary rack/mezzanine folders in `Wms/Application/StorageLocations/StorageLocationBatchService.cs`.
- [x] T013 [US1] Accept both `Storage` and `Reserve` as receiving-putaway destinations and recommendations, applying the one-SKU rule to both purposes and rechecking at posting, in `Wms.Application/Inventory/PostInventoryMovement.cs`, `Wms.Application/Receiving/PutAwayReceivingLpn.cs`, and `Wms.Infrastructure/Data/ReceivingWorkQueryStore.cs`.
- [x] T014 [US1] Append Mobile V1 `PutawayDestination` request and `Reserve` response values without renumbering existing values in `Wms.Contracts/Mobile/V1/MobileReferenceDataContracts.cs`; resolve the new request to either storage purpose while keeping `Storage` strict in `Wms.WebApi/Mobile/MobileBarcodeEndpoints.cs`; switch only the receiving putaway scan in `Wms.Mobile/Pages/LicensePlateNumberPutawayQuantityPage.xaml.cs`.
- [x] T015 [US1] Permit and display `AllowsMultipleSkus` for both storage purposes in `Wms.WebApp/Components/Pages/StorageLocationPages/StorageLocationDialog.razor`, `StorageLocationDialog.razor.cs`, and `Index.razor`; leave direct shipping-pick source checks limited to `Storage`.
- [x] T016 [US1] Update implemented topology, receiving-route, Mobile-contract and architecture rules in `.specify/memory/current/inventory.md`, `.specify/memory/current/receiving.md`, `.specify/memory/current/clients.md`, and `.specify/memory/architecture.md` when US1 is delivered; describe only behavior actually present at that checkpoint.

**Checkpoint**: Topology and receiving putaway can be inspected independently before opening stock is posted. Old picking, transfers, and counts remain restricted to their existing contexts.

## Phase 4: User Story 2 — Load opening balances (P1)

**Goal**: Post the same workbook's signed and dated stock through one WMS history transaction after catalog synchronization.

**Independent check**: With topology and catalog ready, accepted nonzero source rows post to correct cell/SKU/date balances; negative shipping rows stay signed, required-date rows without a date stay unknown, and the eight prohibited mixed cells retain both SKUs with permission off. Any blocking issue leaves the topology-only state intact.

- [x] T017 [US2] Append `RecorderType.OpeningBalance` without renumbering existing recorder values and add a filtered unique `(RecorderId, RecorderLineNumber)` constraint for this type in `Wms.Domain/Enums/RecorderType.cs` and `Wms.Infrastructure/Data/Configurations/InventoryMovementConfiguration.cs`.
- [x] T018 [US2] Build the opening-stock preview and state-bound signature in `Wms.Application/OpeningImports/PreviewOpeningStock.cs`: require the topology-stage source SHA-256 and declared snapshot time; resolve loaded cells, source GUIDs and canonical units; validate signed `decimal(15,3)` quantities and supplied/unknown dates; block operational stock/work and unreviewed mixed cells; show signed totals and row decisions.
- [x] T019 [US2] Add tracked stock, SKU-flag, work-state and import-revision queries/staging to `Wms.Infrastructure/Data/OpeningImportStore.cs` and `Wms.Infrastructure/Data/InventoryPostingStore.cs`, preserving the balance key `(WarehouseId, StorageLocationId, StockKeepingUnitId, ExpirationDate?)` and SQL Server compatibility level 120.
- [x] T020 [US2] Implement one opening batch posting use case in `Wms.Application/OpeningImports/PostOpeningStock.cs`: one positive-magnitude movement and turnover per accepted nonzero worksheet row, destination-only for positive source quantity and source-only for negative; accumulate duplicate cell/SKU/date rows into one signed balance without direct balance replacement.
- [x] T021 [US2] Implement Administrator-only stock confirmation in `Wms.Application/OpeningImports/ConfirmOpeningStock.cs`: rebuild the preview/signature; require `TopologyImported`; set nullable `DeclaredSnapshotAt` to the supplied time; enable accepted source `Да` SKU expiration flags without disabling existing flags; preserve missing and past dates; stage 8 reviewed mixed-cell exceptions, movements, turnovers, balances, import status, one audit event and receipt for one final commit.
- [x] T022 [US2] Extend the XLSX import dialog with stock preview/confirmation and source-hash, catalog, date, signed-quantity and mixed-cell issue summaries in `Wms.WebApp/Components/Pages/StorageLocationPages/OpeningImportDialog.razor` and `OpeningImportDialog.razor.cs`; do not auto-create missing SKUs or cells.
- [x] T023 [US2] Show a derived violation while a storage-purpose cell with `AllowsMultipleSkus = false` contains more than one nonzero SKU, and keep ordinary inflow blocked but outward movement possible, in `Wms.Infrastructure/Data/OpeningImportStore.cs`, `Wms.Application/Inventory/PostInventoryMovement.cs`, and `Wms.WebApp/Components/Pages/StorageLocationPages/Index.razor`.
- [x] T024 [US2] Add the smallest SQL LocalDB integration scenarios for all-or-none posting, signed/date balance preservation including duplicate source keys, and the eight reviewed mixed-cell exception without ordinary-inflow bypass in `Wms.Tests/Inventory/OpeningStockImportTests.cs`; avoid repeating generic command-executor replay tests.
- [x] T025 [US2] Update the implemented opening-stock, local SKU-date-flag authorization and posting boundaries in `.specify/memory/current/inventory.md`, `.specify/memory/current/identity.md`, `.specify/memory/current/integration.md`, and `.specify/memory/architecture.md` when US2 is delivered.

**Checkpoint**: Source stock is traceable through movements and turnovers; a failed or repeated stock confirmation cannot add partial or duplicate balances.

## Phase 5: User Story 3 — Rehearse and inspect the transition (P2)

**Goal**: Make both stage results inspectable after a restart and make repeated rehearsals safe without an in-app reset or delta import.

**Independent check**: Read each persisted stage result, reject a different workbook or used warehouse, replay the same confirmed request without new stock, and repeat the full exercise only on a cleaned disposable database.

- [x] T026 [US3] Expose a read-only persisted import-result query with source identity, actor/time, accepted/corrected/excluded counts, signed/undated exceptions and status in `Wms.Application/OpeningImports/GetOpeningImportResult.cs` and `Wms.Infrastructure/Data/OpeningImportStore.cs`.
- [x] T027 [US3] Add a durable result view in `Wms.WebApp/Components/Pages/StorageLocationPages/OpeningImportDialog.razor` and `OpeningImportDialog.razor.cs`, including the approved loss of zero-prefix stock, `9Z99A1` correction and the eight current mixing violations, without rendering all source rows at once.
- [x] T028 [US3] Enforce warehouse-wide one-snapshot replay and different-workbook rejection across users/requests in `Wms.Application/OpeningImports/ConfirmOpeningTopology.cs`, `ConfirmOpeningStock.cs`, and `Wms.Infrastructure/Data/OpeningImportStore.cs`; an exact command replay returns its recorded result and a new rehearsal requires environment cleanup.
- [x] T029 [US3] Add a focused SQL LocalDB check for cross-user/concurrent opening confirmation and unique warehouse/recorder constraints in `Wms.Tests/Inventory/OpeningStockImportTests.cs`, extending T024 rather than creating another suite.
- [x] T030 [US3] Update the cutover rehearsal and review procedure in `specs/010-topology-opening-balances/quickstart.md` and `.specify/runbooks/staging.md`: finish or separately resolve old work, stop the old WMS, export, preview/confirm topology, synchronize catalog, preview/confirm stock, inspect protocol, then resume only in the new WMS; document cleanup as an environment operation.

**Checkpoint**: A failed stock stage leaves a usable topology result; a second file cannot become an unrecorded stock delta.

## Phase 6: Delivery and cross-cutting review

- [x] T031 Review the developer-generated EF migration and `Wms.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs` for the unique warehouse import, optional audit relation, concurrency guard, filtered opening recorder-line index and SQL Server 2014-compatible SQL; request correction through a new migration if one was already applied outside a disposable database.
- [ ] T032 Review developer-reported `dotnet build Wms.slnx -nologo -v:minimal` and `dotnet test Wms.Tests/Wms.Tests.csproj -nologo -v:minimal` results, then use `specs/010-topology-opening-balances/quickstart.md` for developer-run Web/Mobile and sample-workbook checks; inspect failures before closing tasks.
  Earlier build and test results were reported successful, and the developer confirmed the workbook scenario. A full build/test rerun and Mobile smoke check after the final shipping-cell adjustment were not reported; this verification remains open in the historical checklist.
- [x] T033 Check the implemented feature against `specs/010-topology-opening-balances/spec.md`, remove any remaining old XLS entry point, and leave deferred picking/replenishment, transfer, count and 1C reconciliation work accurately identified in `.specify/memory/backlog.md`.

## Dependencies and execution order

```text
Setup T001-T002
  -> Foundation T003-T005
  -> US1 T006-T016 (topology + safe receiving putaway)
  -> US2 T017-T025 (opening stock)
  -> US3 T026-T030 (durable cutover inspection/replay)
  -> Delivery T031-T033
```

- US1 can be reviewed after topology confirmation without opening balances. Its Mobile and putaway tasks depend on `Reserve` from T003, but do not require US2.
- US2 requires US1's persisted topology, the 1C SKU catalog, and the import identity. US2's stock transaction must finish before US3's full rehearsal.
- US3 uses the persisted stage results from US1/US2 and does not implement in-app database cleanup.
- T003 and T004 can proceed in parallel. Within US1, T012 and T014 can proceed in parallel after T003; T006 and the reader from T002 are prerequisites to T007. Within US2, T017 can proceed alongside preview design T018, and T024 can be prepared while the posting path is implemented. Shared files in later tasks are edited sequentially.

## Implementation strategy

Complete the topology-only MVP through US1, inspect its actual result, then implement opening stock as the next increment. After stock posting is verified, add the persistent rehearsal/result workflow and complete the developer-run migration, build, test and device checks. Keep current-state documentation synchronized at each implemented increment. Do not enable old picking, transfer or count flows for reserve/shipping by widening shared predicates.
