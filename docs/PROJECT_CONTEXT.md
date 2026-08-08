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

Only receiving is currently being implemented. The other processes are roadmap items. Outgoing orders will be imported from 1C and are expected to follow a pattern analogous to receiving orders.

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

- warehouse, zone, and storage location;
- SKU, unit of measure, and SKU barcode;
- inventory balance: current quantity of an SKU in a warehouse storage location;
- inventory turnover: an immutable movement recording balance before/after and its source document.

## Configuration UI

The operator UI exposes configuration screens under the `Конфигурация` navigation group.

- `Склады` supports server-side name search, sorting, pagination, inclusion of deactivated records, and a user-triggered refresh from 1C. The UI calls `WarehouseImportService`, which delegates to the 1C warehouse catalog importer.
- `Зоны` supports server-side name search, sorting, pagination, warehouse filtering, inclusion of deactivated zones, and creation/editing in a dialog. A zone always belongs to one warehouse.
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
  -> Start
  -> InReceiving (ВРаботе)
  -> operator records actual quantities and comments
  -> Complete
  -> Received (Принят), 1C updated and posted,
     WMS records inventory balances and turnovers
```

The receiving location belongs to the whole order. At the current stage the operator selects a receiving zone and then a storage location from that zone on the order details page before the order can be started. Both choices are limited to the order's warehouse; the UI does not enable "Взять в работу" until a location is selected. The selected location is saved immediately before starting. A default receiving location/zone may be introduced later.

`ProcessingRequired` is currently only passed through from 1C's `ТребуетсяОбработка`. It is not part of the active operational flow, which is `ReadyForReceiving -> InReceiving -> Received`.

### Receiving UI

- `Index` lists receiving orders.
- `Details` shows one order and offers "Взять в работу" for a pending order when it can be started.
- `InProcess` lets the operator edit actual quantities and comments, then complete the order through `CompleteOrderAsync`.

### Receiving application services

- `ReceivingOrderCommandService` imports and reconciles orders, starts and completes them, and updates item actual quantities.
- `ReceivingOrderQueryService` reads order details and paged, filtered lists.
- `BalanceAndTurnoverService` records inventory movements and updates balances when a receiving order is completed.

External updates to an already imported order are controlled by `WmsSettings` according to its status. This protects in-progress warehouse work from being overwritten by 1C changes.

## 1C integration

The 1C OData client is configured through `OneCClient` settings. The integration uses explicit models named after 1C entities.

For `Document_ПриходныйОрдерНаТовары`:

- `Document_ПриходныйОрдерНаТовары_InboundService` fetches a document by `Ref_Key`, maps it to `ReceivingOrder`, and imports it.
- `Document_ПриходныйОрдерНаТовары_OutboundService` changes the document status, updates item facts when needed, and posts the document in 1C.
- `POST /api/1c/Document_ПриходныйОрдерНаТовары/notify` enqueues a notification; `NotifyBackgroundService` consumes it and imports the document asynchronously.

## Working conventions

Read this file before making project changes. Update it when a lasting rule, integration boundary, process flow, or architecture convention changes.

For substantial issue-level work, create `specs/<issue-id-or-short-slug>/` and keep a concise specification there. Recommended contents:

- problem and expected business outcome;
- scope and non-goals;
- affected process and integration behavior;
- acceptance criteria;
- open questions and decisions.

Do not create an issue specification for trivial, isolated edits.
