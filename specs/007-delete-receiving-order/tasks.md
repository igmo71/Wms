# Tasks: Delete a receiving order

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [Web contract](contracts/receiving-order-deletion.md)

**Organization**: US1 delivers atomic deletion; US2 makes the deleted order readable and closes active paths. Each checked task is one reviewable increment. Developer-owned operational commands are explicit gates.

## Phase 1: Foundation

- [x] T001 Add terminal `ReceivingOrderStatus.Deleted`, paired nullable actor/time on `ReceivingOrder` and `LicensePlateNumber`, validated delete transitions, and EF field mapping in `Wms.Domain/Enums/ReceivingOrderStatus.cs`, `Wms.Domain/ReceivingOrder.cs`, `Wms.Domain/LicensePlateNumber.cs`, `Wms.Infrastructure/Data/Configurations/ReceivingOrderConfiguration.cs`, and `Wms.Infrastructure/Data/Configurations/LicensePlateNumberConfiguration.cs`; preserve existing numeric status values.
- [x] T002 Have the developer generate the EF migration for T001 in `Wms.Infrastructure/Data/Migrations/`, inspect migration and model snapshot, then have the developer apply it before interactive verification. Migration `20261029100141_AddReceivingOrderDeletion` was generated, inspected, and reported applied.

## Phase 2: User Story 1 - Delete an unplaced order (P1)

**Goal**: Manager previews and atomically deletes the order with correct stock compensation and released 1C links.

**Independent check**: Mixed open/closed/cancelled/deleted LPN order without any historical putaway deletes once; a putaway or stale preview blocks the entire operation.

- [x] T003 [US1] Load the complete deletion state and validate historical putaway, original/reversed movement provenance, links and dated balances in `Wms.Application/Receiving/PreviewReceivingOrderDeletion.cs` and `Wms.Infrastructure/Data/ReceivingOrderDeletionStore.cs`.
- [x] T004 [US1] Produce one deterministic order preview with aggregate balance forecast, negative flag and state token covering order/LPN revisions, links, movements and affected balance versions in `Wms.Application/Receiving/PreviewReceivingOrderDeletion.cs`.
- [x] T005 [US1] Implement the Manager-only all-or-nothing command with token recheck, LPN deletion, outstanding reversal posting, fact/plan reset, link detachment, order deletion, audit and repeat-safe receipt in `Wms.Application/Receiving/DeleteReceivingOrder.cs`, `Wms.Infrastructure/Data/ReceivingOrderDeletionStore.cs`, and `Wms.Infrastructure/Data/DependencyInjection.cs`.
- [x] T006 [US1] Add the Web preview/confirmation dialog with explicit negative acknowledgement and uncertain-request retry in `Wms.WebApp/Components/Pages/ReceivingOrderPages/ReceivingOrderDeletionDialog.razor`, `Wms.WebApp/Components/Pages/ReceivingOrderPages/Details.razor`, and `Wms.WebApp/Components/Pages/ReceivingOrderPages/Details.razor.cs`.
- [x] T007 [US1] Protect the distinct material risks of atomic multi-LPN stock compensation, historical putaway rejection, and stale/replayed commands with focused SQL integration scenarios in `Wms.Tests/Receiving/ReceivingSafetyTests.cs`.

## Phase 3: User Story 2 - Preserve readable history (P2)

**Goal**: Deleted orders remain inspectable in Web and cannot receive new work through stale clients or source synchronization.

**Independent check**: Former documents can start a new order; deleted order history remains visible in Web; active Mobile reads and every stale write are rejected.

- [x] T008 [US2] Exclude deleted orders from Mobile queue/direct reads, display deleted order actor/time and existing LPN/reversal history in Web, and remove mutation controls in `Wms.Infrastructure/Data/ReceivingWorkQueryStore.cs`, `Wms.Application/Receiving/IReceivingWorkQueryStore.cs`, `Wms.WebApp/Components/Pages/ReceivingOrderPages/Index.razor.cs`, `Wms.WebApp/Components/Pages/ReceivingOrderPages/Details.razor`, `Wms.WebApp/Components/Pages/ReceivingOrderPages/ReceivingStatusText.razor`, and `Wms.WebApi/Mobile/MobileReceivingOrderEndpoints.cs`.
- [x] T009 [US2] Audit all receiving/source/putaway command paths and reject `Deleted` at execution, including synchronization after link detachment, in `Wms.Application/Receiving/`, `Wms.Infrastructure/Data/ReceivingSourceStore.cs`, and `Wms.WebApi/Mobile/MobileLicensePlateNumberPutawayEndpoints.cs`; keep existing single-LPN deletion rule.

## Phase 4: Documentation and verification

- [x] T010 Record implemented behavior and role/client visibility in `.specify/memory/current/receiving.md`, `.specify/memory/current/identity.md`, and `.specify/memory/current/clients.md`; update `.specify/memory/architecture.md` only if lasting technical structure changes.
- [x] T011 Give the developer minimal focused build/test and interactive Web/Mobile checks from `specs/007-delete-receiving-order/quickstart.md`; review reported results and migration, then mark `specs/007-delete-receiving-order/spec.md` completed.

## Dependencies and execution order

T001 → developer migration gate T002 → T003 → T004 → T005 → T006 → T007. T008 and T009 depend on the new status and should be completed before accepting US1 as safe. T010 follows implemented behavior; T011 follows all code and documentation. No parallel agent work is required.

## Implementation strategy

The first increment is T001. Continue in order, stopping at the developer-owned migration and verification gates. Do not publish a partially usable deletion control before T008/T009 close the active paths. The feature is complete only after US1 and US2 pass the checks above.
