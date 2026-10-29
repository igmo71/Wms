# Tasks: Add SKU barcodes during receiving

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/sku-barcodes.md](contracts/sku-barcodes.md)

**Organization**: Each task is one reviewable increment. US2 synchronization precedes US1 Mobile use because the current import would erase newly added local codes. US3 adds Web investigation and local removal. Developer-owned operational commands remain explicit gates.

## Phase 1: Foundation

**Goal**: Record source ownership without changing the existing pair key or current 1C lookup behavior.

- [x] T001 Add `SkuBarcodeSource` (`OneC`, `Wms`) in `Wms.Domain/Enums/SkuBarcodeSource.cs` and the source field to `Wms.Domain/SkuBarcode.cs`; configure existing rows to default to `OneC`, retain the `(SkuId, Value)` primary key, and add a nonunique `Value` index in `Wms.Infrastructure/Data/Configurations/SkuBarcodeConfiguration.cs`. Do not add global value uniqueness.
- [x] T002 Have the developer generate `AddSkuBarcodeSource` under `Wms.Infrastructure/Data/Migrations/` using `quickstart.md`; inspect the generated migration and snapshot for `OneC` backfill, retained composite key and nonunique index before the developer applies it. Generated migration `20261029122239_AddSkuBarcodeSource` passed review and the developer reported application complete.

## Phase 2: User Story 2 - Reconcile local and 1C barcodes (P1)

**Goal**: Both import modes preserve local pairs, promote matching pairs and remove stale imported pairs without a delete/insert gap.

**Independent check**: A WMS-owned pair survives single and full imports until 1C supplies that exact pair; a later successful 1C removal deletes it, and a failed import leaves the previous state intact.

- [x] T003 [US2] Implement one application reconciliation use case and EF store in `Wms.Application/SkuBarcodes/SynchronizeSkuBarcodes.cs`, `Wms.Infrastructure/Data/SkuBarcodeStore.cs`, and `Wms.Infrastructure/Data/DependencyInjection.cs`: validate and deduplicate incoming `(SkuId, Value)` pairs, reconcile a single-SKU or full snapshot atomically, promote matching `Wms` pairs, retain unmatched `Wms` pairs, remove missing `OneC` pairs, and accept same-value pairs for different SKUs. Coordinate overlapping barcode writes and source reads so an older import cannot overwrite a newer accepted scope.
- [x] T004 [US2] Route both methods of `Wms/Integration/OneS/Services/InformationRegister_ШтрихкодыНоменклатуры_Service.cs` through T003 and retire the destructive methods in `Wms/Application/SkuBarcodes/SkuBarcodeService.cs`; preserve existing 1C endpoint/notification routes and reject invalid or failed source responses without changing saved rows.
- [x] T005 [US2] Add the smallest SQL integration scenario for local survival, promotion, 1C removal and failed-save atomicity in `Wms.Tests/SkuBarcodes/SkuBarcodeSynchronizationTests.cs`; do not repeat generic command-executor coverage.

## Phase 3: User Story 1 - Register a barcode at receiving (P1)

**Goal**: A current-cycle worker scans and saves a missing code from a Mobile order SKU card.

**Independent check**: A new local code resolves the selected SKU; existing same-SKU and other-SKU values do not create a second or ambiguous manual pair; a nonparticipant or inactive order cannot add.

- [x] T006 [US1] Implement an Operator-only, receipt-backed add command with audit in `Wms.Application/SkuBarcodes/AddReceivingSkuBarcode.cs` and `Wms.Infrastructure/Data/SkuBarcodeStore.cs`; validate active order/current SKU/current-cycle participation, barcode length and trimmed value, same-SKU no-op, other-SKU conflict naming the existing SKU, and coordinate the final check/save with T003.
- [x] T007 [US1] Add `HasBarcode` to `Wms.Contracts/Mobile/V1/MobileReceivingOrderContracts.cs`, calculate it for current order SKUs in `Wms.Infrastructure/Data/ReceivingWorkQueryStore.cs`, and expose the add command under `Wms.WebApi/Mobile/MobileReceivingOrderEndpoints.cs` with standard Mobile error and command response mapping.
- [x] T008 [US1] Extend `Wms.Mobile/Services/MobileReceivingOrderClient.cs`, `Wms.Mobile/Services/ReceivingOrderReceivingProcess.cs`, and `Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderSkuPage.xaml`/`.xaml.cs` with the pale/prominent barcode action, isolated scanner/camera panel, SKU/value review and explicit save, uncertain-request retry, and order refresh after success.
- [x] T009 [US1] Review the shared barcode mutation lock and final cross-SKU check for concurrent manual additions. A dedicated concurrency test was omitted after the developer judged this scenario extremely unlikely; the SQL session lock still spans the command save, and existing executor tests cover receipt replay.

## Phase 4: User Story 3 - Find and resolve barcode duplicates (P2)

**Goal**: Web Managers find a barcode used by distinct SKUs and remove only WMS-owned associations.

**Independent check**: The duplicate filter finds all distinct-SKU groups before paging; deleting a local row removes its lookup and records audit, while an imported or promoted row remains protected.

- [x] T010 [US3] Replace the raw barcode entity list query with a server-paged read model and `OnlyDuplicates` filter in `Wms.Application/SkuBarcodes/ListSkuBarcodes.cs`, `Wms.Infrastructure/Data/SkuBarcodeStore.cs`, and `Wms.WebApp/Components/Pages/SkuBarcodePages/Index.razor.cs`; apply grouping on distinct SKU IDs before paging and retain search, sort and inactive-SKU behavior.
- [x] T011 [US3] Add a Manager-only, audited and repeat-safe removal command for current `Wms` pairs in `Wms.Application/SkuBarcodes/RemoveLocalSkuBarcode.cs` and `Wms.Infrastructure/Data/SkuBarcodeStore.cs`; recheck ownership at execution and leave imported/promoted pairs unchanged.
- [x] T012 [US3] Show source, duplicate-only filter and confirmed removal control for WMS rows in `Wms.WebApp/Components/Pages/SkuBarcodePages/Index.razor` and `.razor.cs`; refresh after removal or stale-source rejection.

## Phase 5: Documentation and verification

- [x] T013 Record implemented synchronization, barcode ownership, receiving access and client behavior in `.specify/memory/current/integration.md`, `.specify/memory/current/receiving.md`, `.specify/memory/current/identity.md`, and `.specify/memory/current/clients.md`; update `.specify/memory/architecture.md` only if a lasting technical convention changes.
- [x] T014 Give the developer exact focused build/test and interactive Mobile/Web checks from `specs/008-receiving-sku-barcodes/quickstart.md`; review the reported results and developer-created/applied migration, then mark `specs/008-receiving-sku-barcodes/spec.md` Completed. The focused SQL test and affected builds passed; the developer accepted the feature after Mobile review on 2026-10-29.

## Dependencies and execution order

T001 → migration gate T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010 → T011 → T012 → T013 → T014. T006 and T011 share the barcode write boundary established by T003. Do not expose the Mobile add action until T004 has removed destructive imports. No parallel agent work is required.

## Implementation strategy

The first increment is T001. Complete and inspect the migration gate before continuing with the synchronization replacement. US2 and US1 together form the safe receiving MVP; US3 makes source ambiguity visible and removable. Stop at developer-owned migration and verification gates, and keep each completed task reviewable with a concise handoff.
