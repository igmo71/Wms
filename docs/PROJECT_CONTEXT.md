# WMS project context

## Purpose and boundaries

WMS is a warehouse-management MVP. Its purpose is to execute basic warehouse operations and maintain the operational fact of inventory by storage location.

1C is the source of master data and business documents. WMS imports those documents, performs the warehouse work, returns the required result to 1C, and maintains operational inventory balances and movements by storage location.

WMS is responsible for these warehouse processes:

- receiving;
- putaway to storage locations;
- intra-warehouse transfers;
- inventory counting;
- picking from storage locations;
- shipping from the warehouse.

Receiving, the initial shipping/picking flow, and inventory-count backend are currently being implemented. Putaway remains a roadmap item. The agreed intra-warehouse transfer process is specified but not yet implemented. Outgoing orders are imported from 1C and use their own shipping workflow.

Shipping uses three WMS-controlled transitions: `Prepared -> ReadyForPicking`, then one of the permitted picking or verification statuses to `ReadyForShipment`, then `ReadyForShipment -> Shipped`. The picking duration KPI is always measured from `PickingStartedAtUtc` to `ReadyForShipmentAtUtc`. Draft picking movements post inventory from their source locations to the shipping location when the order is set ready for shipment; shipping then posts the issue from that location.

Picking records draft `InventoryMovement` rows from source storage locations to the shipping location. While the order is in picking or verification, their unposted quantity is the line's shipping fact. They change inventory balances and create turnovers only when `SetReadyForShipmentAsync` posts them.

When a shipping fact differs from the plan, WMS reads a fresh external shipping order before updating its 1C table sections. A mismatch between the fresh 1C plan and the local WMS plan blocks the update as a conflict. The full 1C table sections are patched: shipped quantities become `Отгрузить`, unshipped quantities become `НеОтгружать`, and zero-fact base-order lines are omitted. WMS preserves 1C-specific row fields from the fresh document and does not store them in its domain model. The MVP supports one line per SKU in each shipping table section for this update.

An unfinished shipping order may be rolled back locally to `Prepared`. Draft picking movements are deleted; posted movements created in the current picking cycle are offset by new reverse movements through the common posting service, preserving turnover history. The cancelled cycle's picking and ready-for-shipment timestamps and users are cleared, while rollback audit fields remain. Rollback is forbidden for `Prepared` and `Shipped`, does not call 1C, and does not change `DeletionMark` or `Posted`.

Picking reads expose draft movements by order line and source locations with a positive current physical balance for that line's SKU. The picking UI and command service prevent the current shipping order's draft movements from exceeding either the line plan or a source location's physical balance for the SKU; drafts of other orders are not reservations and remain subject to the final posting check.

The initial shipping UI has a filtered, paged list of shipping orders, a details page, and a picking work page. A prepared order requires the operator to select a shipping zone and location within the order warehouse before it can be set ready for picking. The picking page lets an operator select an order line, create, edit, and delete draft movements from available source locations, then set the order ready for shipment.

The operator UI exposes a paged inventory-balance list. It shows the current SKU quantity in each storage location and supports filtering by warehouse, storage location, and SKU; it does not aggregate balances or account for reservations.

The operator UI also exposes a paged inventory-turnover history. It shows each posted change in a location balance with the quantity delta, balances before and after, and a link to its source document when known. It supports filtering by date period, warehouse, storage location, SKU, and by the number of a receiving or shipping order; the default period is the current day.

The operator UI exposes a separate paged list of posted inventory movements. It supports filtering by date period, warehouse, storage location, SKU, and by the number of a receiving or shipping order, with the current day as default. It shows source and destination storage locations and links a movement to its receiving order, shipping order, or inventory count when the recorder is known. Draft movements are deliberately excluded.

Inventory counts are local WMS documents. A draft may contain incomplete rows while an operator records the count. When posted, each completed row creates a receipt or issue `InventoryMovement` for its positive or negative counted-versus-expected difference; the common posting service updates balances and turnovers in the same save operation. The operator UI provides a list, creates a count for a selected warehouse, and directly edits draft rows. Recounts, reservations, and inventory tasks are not implemented.

Intra-warehouse transfers are local WMS documents without planned item lines or
1C synchronization. They group an unrestricted chronological sequence of direct
storage-location movements and movements through one optional transit location,
such as a trolley. Each operator-confirmed physical action is immediately posted
through the common inventory service; pick, put, and direct actions may be freely
interleaved. A transit location is selected once as document context, starts
empty, and belongs exclusively to one active transfer document. Draft documents
without movements may be deleted, the first movement starts the document, and a
document may be completed explicitly only after its transit location is empty.
Posted movements and completed documents are immutable. The detailed process is
defined in `specs/intra-warehouse-transfers/spec.md`.

## MVP implementation principles

The project deliberately favors clear, direct code and fast iteration over enterprise-level generalization.

- UI and HTTP endpoints may call application services directly.
- CQRS, DTO layers, repositories, and additional abstractions are optional tools, not mandatory architecture.
- Detailed validation, resilience, and stabilization are added when they become useful for the MVP, not preemptively.
- Existing code and the explicitly stated business flow take priority over speculative future needs.

These principles can change as the product and its constraints mature.

## Solution map

- `Wms` contains domain entities, EF Core persistence, application services, and 1C integration.
- `Wms.WebApi` hosts HTTP API and 1C webhook endpoints.
- `Wms.WebApp` is the Blazor/MudBlazor operator UI.

Core domain concepts:

- warehouse, typed zone, and storage location; zone types distinguish ordinary
  storage from transit locations such as trolleys;
- SKU, unit of measure, and SKU barcode;
- inventory balance: current quantity of an SKU in a warehouse storage location;
- inventory movement: an editable draft of a warehouse movement, which becomes historical when posted;
- inventory turnover: an immutable balance change recording before/after and its originating inventory movement.

## Configuration UI

The operator UI exposes configuration screens under the `Конфигурация` navigation group.

- `Склады` supports server-side name search, sorting, pagination, inclusion of deactivated records, and a user-triggered refresh from 1C through `SynchronizedCatalogImportService`. The UI disables a refresh button and displays indeterminate progress while it runs.
- `Зоны` supports server-side name search, sorting, pagination, warehouse filtering, inclusion of deactivated zones, and creation/editing in a dialog. A zone always belongs to one warehouse and has an explicit type.
- Zone types distinguish ordinary storage, transit, receiving, and shipping.
  Receiving and shipping location selectors and command services accept only
  their corresponding zone types; picking and inventory counts use ordinary
  storage zones.
- `Ячейки хранения` supports server-side name search, sorting, pagination, warehouse and zone filtering, inclusion of deactivated locations, and creation/editing in a dialog. A storage location always belongs to one warehouse and one zone. Selecting a zone automatically selects its warehouse; selecting a warehouse limits the zone choices to that warehouse.

## Receiving

`ReceivingOrder` is WMS's local operational representation of the 1C document `ПриходныйОрдерНаТовары`.

It includes 1C document metadata, warehouse, planned item quantities, recorded actual quantities, a receiving storage location, status, and synchronization markers. An order item is identified by its order and 1C line number.

The active business flow is:

```text
1C document or notification
  -> WMS imports the document
  -> ReadyForReceiving (КПоступлению)
  -> operator selects a receiving storage location
  -> SetInReceiving
  -> InReceiving (ВРаботе)
  -> operator records actual quantities and comments
  -> SetReceived
  -> Received (Принят), 1C updated and posted,
     WMS records inventory balances and turnovers
```

The receiving location belongs to the whole order. At the current stage the operator selects a receiving zone and then a storage location from that zone on the order details page before the order can be started. Both choices are limited to the order's warehouse; the UI does not enable "Взять в работу" until a location is selected. The selected location is saved immediately before starting. A default receiving location/zone may be introduced later.

`ProcessingRequired` is currently only passed through from 1C's `ТребуетсяОбработка`. It is not part of the active operational flow, which is `ReadyForReceiving -> InReceiving -> Received`. Actual quantities are immutable after `Received` and are preserved when an inbound document is reconciled.

### Receiving UI

- `Index` lists receiving orders.
- `Details` shows one order and offers "Взять в работу" for a pending order when it can be started.
- `InProcess` lets the operator edit actual quantities and comments, then set the order received through `SetReceivedAsync`.

### Receiving application services

- `ReceivingOrderCommandService` imports and reconciles orders, sets orders in receiving or received, and updates item actual quantities.
- `ReceivingOrderQueryService` reads order details and paged, filtered lists.
- `BalanceAndTurnoverService` records inventory movements and updates balances when a receiving order is completed.

Inbound synchronization may create and reconcile receiving orders only in `ReadyForReceiving` and shipping orders only in `Prepared`. Once WMS work has started, any inbound difference leaves the local order unchanged, sets `ExternalChangeDetected`, and logs the conflict. Receiving facts are editable only in `InReceiving` or `ProcessingRequired`; shipping facts are editable only while picking or verification is in progress.

## 1C integration

The 1C OData client is configured through `OneCClient` settings. The integration uses explicit models named after 1C entities.

For `Document_ПриходныйОрдерНаТовары`:

- `Document_ПриходныйОрдерНаТовары_InboundService` fetches a document by `Ref_Key`, maps it to `ReceivingOrder`, and imports it.
- `Document_ПриходныйОрдерНаТовары_OutboundService` changes the document status, updates item facts when needed, and posts the document in 1C.
- `POST /api/1c/Document_ПриходныйОрдерНаТовары/notify` enqueues a notification; `NotifyBackgroundService` consumes it and imports the document asynchronously.

## Working conventions

Read this file before making project changes. Update it when a lasting rule, integration boundary, process flow, or architecture convention changes.

Intentional MVP limitations, pilot-readiness work, and deferred 1C decisions are
kept in `docs/ROADMAP.md`. They are not accidental omissions and should not be
implemented as incidental cleanup during unrelated feature work.

For substantial issue-level work, create `specs/<issue-id-or-short-slug>/` and keep a concise specification there. Recommended contents:

- problem and expected business outcome;
- scope and non-goals;
- affected process and integration behavior;
- acceptance criteria;
- open questions and decisions.

Do not create an issue specification for trivial, isolated edits.
