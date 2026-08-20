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

The initial receiving, putaway, shipping/picking, inventory-count, and
intra-warehouse transfer flows are implemented. They remain subject to manual
validation and incremental MVP development. Outgoing orders are imported from
1C and use their own shipping workflow.

Shipping uses three WMS-controlled transitions: `Prepared -> ReadyForPicking`, then one of the permitted picking or verification statuses to `ReadyForShipment`, then `ReadyForShipment -> Shipped`. The picking duration KPI is always measured from `PickingStartedAtUtc` to `ReadyForShipmentAtUtc`. Draft picking movements post inventory from their source locations to the shipping location when the order is set ready for shipment; shipping then posts the issue from that location.

Picking records draft `InventoryMovement` rows from source storage locations to the shipping location. While the order is in picking or verification, their unposted quantity is the line's shipping fact. They change inventory balances and create turnovers only when `SetReadyForShipmentAsync` posts them.

Before an order is set ready for shipment, WMS reads a fresh external shipping order and updates its 1C table sections, including when the shipping fact equals the plan. A mismatch between the fresh 1C plan and the local WMS plan blocks the update as a conflict. The full 1C table sections are patched: shipped quantities become `Отгрузить`, unshipped quantities become `НеОтгружать`, and zero-fact base-order lines are omitted. WMS derives these actions from plan and fact and does not persist 1C's row action in its domain model. WMS preserves 1C-specific row fields from the fresh document and does not store them in its domain model. The MVP supports one line per SKU in each shipping table section for this update.

An unfinished shipping order may be rolled back locally to `Prepared`. Draft picking movements are deleted; posted movements created in the current picking cycle are offset by new reverse movements through the common posting service, preserving turnover history. The cancelled cycle's picking and ready-for-shipment timestamps and users are cleared, while rollback audit fields remain. Rollback is forbidden for `Prepared` and `Shipped`, does not call 1C, and does not change `DeletionMark` or `Posted`.

Picking reads expose draft movements by order line and source locations with a positive current physical balance for that line's SKU. The picking UI and command service prevent the current shipping order's draft movements from exceeding either the line plan or a source location's physical balance for the SKU; drafts of other orders are not reservations and remain subject to the final posting check.

The initial shipping UI has a filtered, paged list of shipping orders, a details page, and a picking work page. A prepared order requires the operator to select a shipping zone and location within the order warehouse before it can be set ready for picking; that location is fixed once picking starts. The picking page lets an operator select an order line, create, edit, and delete draft movements from available source locations, then set the order ready for shipment.

The operator UI exposes a paged inventory-balance list. It shows the current SKU quantity in each storage location and supports filtering by warehouse, storage location, and SKU; it does not aggregate balances or account for reservations.

The operator UI also exposes a paged inventory-turnover history. It shows each posted change in a location balance with the quantity delta, balances before and after, and a link to its source document when known. It supports filtering by date period, warehouse, storage location, SKU, and by the number of a receiving or shipping order; the default period is the current day.

Operational screens that show an SKU quantity also show its dynamically
calculated weight in kilograms using the current `StockKeepingUnit.WeightKg`.
Receiving and shipping orders show only factual line weights and a factual total;
if a nonzero line has no unit weight, its weight is shown as unavailable and the
known document total is marked as incomplete. Weight is not snapshotted in
documents or inventory history, so catalog weight changes affect historical
display.

The operator UI exposes a separate paged list of posted inventory movements. It supports filtering by date period, warehouse, storage location, SKU, and by the number of a receiving order, shipping order, inventory count, or transfer, with the current day as default. It shows source and destination storage locations and links a movement to its source document with its number and date when known. Draft movements are deliberately excluded.

Inventory counts are local WMS documents. They use a local `yyMMdd-HHmmss` number and the creation date. A draft may contain incomplete rows while an operator records the count. Each document and row timestamp is accompanied by the user who performed that operation. When posted, each completed row creates a receipt or issue `InventoryMovement` for its positive or negative counted-versus-expected difference; the common posting service updates balances and turnovers in the same save operation, and records the posting user as the movement confirmer. The operator UI provides a list, creates a count for a selected warehouse, and directly edits draft rows. Recounts, reservations, and inventory tasks are not implemented.
Detailed rules are defined in `specs/inventory-count-backend/spec.md`.

Inventory-transfer commands immediately post every confirmed direct, pick, or put action
together with balance and turnover changes in one `SaveChangesAsync` call,
allocate a chronological movement line, and start a draft on its first
movement. Transfer numbers use local creation time in `yyMMdd-HHmmss` format.
Each transfer movement stores the confirming application user. The
command and query backend and the operator web UI are implemented. The UI has a
filtered, paged transfer list and one work page for both initialization and
continued work. At start, the operator selects a warehouse and, when needed, an
empty transit location from that warehouse; both are then fixed for the
document. The transit location belongs exclusively to one active transfer.
The transit selector offers only empty, unassigned locations and states when
none are available. Before confirmation, movement controls are visible but disabled.
Pick, put, and direct movement remain separate freely interleaved actions.
Draft and in-progress documents open on the work page; completed documents open
on a separate read-only details page with their movement history.
The work page always shows the completion action for an unfinished document;
it is enabled only after a movement has started the transfer and, when a
transit location is used, only after it is empty.
Drafts without movements may be deleted. Posted movements and completed
documents are immutable. The detailed process is defined in
`specs/intra-warehouse-transfers/spec.md`.

## Document list UI convention

Document lists start with number, date, warehouse, and status. Order lists then
show queue, warehouse operation, and their resolved sender or receiver, followed by process-specific fields.
Receiving orders show putaway status; shipping orders show delivery direction;
transfers show their transit location. Separate action columns are omitted
because document numbers are links. Lists omit creation, update, and process
start timestamps and end with the current user name and timestamp for each
relevant completed process. Receiving orders show receiving and putaway
completion, shipping orders show picking completion (ready for shipment) and
shipping, while counts and transfers show their single completion event.

Document detail and work pages use row-based information headers. Order headers
have separate rows for core properties, including the resolved sender or
receiver, operational location/comment/actual weight, and action audit.
Receiving audit covers starting and completing both
receiving and putaway; shipping audit covers starting and completing picking,
shipping, and the latest rollback when present. Inventory-count and transfer
headers put their number and creation timestamp in the page title, then show
core properties and action audit in separate rows. Counts have no document-level
weight. A transfer header shows the dynamic weight of movements whose
destination is an ordinary storage location; movements onto a transit location
are excluded so trolley workflows do not count the same goods twice.
Order headers favor vertical compactness: completed locations render as plain
text, comments stay on one ellipsized line with their full value in a tooltip,
and each audit item keeps its user and timestamp on one line.
Page-level headings in the operator UI use MudBlazor `Typo.h5`.

## Reports

The operator UI has a separate reports navigation group. Its first report is
warehouse employee performance, available to every authenticated user. The
summary has one row per warehouse and completing user with the number of
completed documents, positive factual document lines, and factual weight. It
supports server-side warehouse, user, and completion-date filtering, sorting,
pagination, and totals over the complete filtered result rather than the
current page. The default period is the current local calendar month.

Receiving performance is attributed to `CompletedBy` and measured from
`StartedAtUtc` to `CompletedAtUtc`. Shipping-order performance covers picking,
is attributed to `ReadyForShipmentBy`, and is measured from
`PickingStartedAtUtc` to `ReadyForShipmentAtUtc`; the later transition to
`Shipped` is not part of this KPI. Attribution is to the completing user even
when a different user started the operation, so this report measures completed
results rather than exact personal working time.

The employee detail report carries the warehouse and period from the selected
summary row and lists its receiving and picking documents with positive factual
line count, factual weight, start and completion timestamps, and duration. It
supports partial document-number search, operation-type and completion-date
filters, sorting, and pagination. Document numbers link to their operational
details.

Report weight uses current SKU weights and marks documents and totals as
incomplete when a positive factual line has no weight. Deleted Identity users
remain attributable by their stored identifier. Operations without a completing
user, complete timestamps, or a nonnegative duration are excluded as invalid
audit data. The detailed rules are defined in
`specs/employee-performance-report/spec.md`.

## Authentication and users

The operator web application uses ASP.NET Core Identity with two fixed WMS
roles. `Administrator` and `Operator` may both access operational and report
pages; only `Administrator` may access configuration catalogs and manage user accounts. Anonymous users
may access account endpoints needed to sign in but not WMS pages. Public local
self-registration is retained in the source for possible future use but is not
linked publicly and requires the administrator role; creation of a new account
through an external login is disabled.

The administrator user page creates confirmed local accounts and edits their
current display name, single WMS role, and sign-in block. An administrator
cannot block their own account or remove their own administrator role, and the
last active administrator cannot be blocked or demoted.

`ApplicationUser.DisplayName` is the human-readable name shown in operational
audit and employee reports. Existing accounts with an empty display name fall
back to Identity `UserName`. Operational records continue storing only the
Identity user identifier, so changing a display name also changes historical
display; deleted users remain identified by their stored identifier.

Roles are initialized idempotently at web application startup. Existing users
without a WMS role become operators. The first administrator is selected with
`IdentityBootstrap__AdministratorEmail`; when that account does not exist,
`IdentityBootstrap__AdministratorDisplayName` and the secret
`IdentityBootstrap__AdministratorPassword` are also required to create it.
The bootstrap password must not be committed to application settings.

## MVP implementation principles

The project favors clear, direct code and incremental enrichment over
enterprise-level generalization. Layer responsibilities, validation placement,
operation outcomes, coding conventions, and architectural non-goals are defined
once in [`docs/ARCHITECTURE.md`](ARCHITECTURE.md). The ordered refactoring
backlog is kept in
[`specs/architecture-alignment/spec.md`](../specs/architecture-alignment/spec.md).
Existing code and documented business behavior take priority over speculative
future needs.

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

SKU physical properties are normalized during 1C import to WMS units:
kilograms per canonical SKU unit for weight and cubic meters per canonical SKU
unit for volume. Refreshing the SKU catalog first refreshes the referenced 1C
units and uses their measurement type and nullable numerator/denominator, so it
does not depend on the order of manual catalog refreshes. Invalid enabled
physical properties become unknown rather than zero; the catalog UI warns how
many weight and volume values could not be imported.
Inventory remains expressed in one canonical unit per SKU. A 1C packaging is a
quantity-conversion/input representation rather than a separate inventory
balance, while a 1C characteristic that distinguishes stock forms part of SKU
identity. The current importer has not yet implemented packaging conversion or
characteristic-aware SKU identity. Storage-location capacity enforcement is
also a later increment. Those boundaries and the required source examples are specified in
[`specs/sku-physical-properties/spec.md`](../specs/sku-physical-properties/spec.md).

## Configuration UI

The operator UI exposes configuration screens under the `Конфигурация` navigation group.

- `Склады` supports server-side name search, sorting, pagination, inclusion of deactivated records, and a user-triggered refresh through its 1C catalog integration service. The UI disables a refresh button and displays indeterminate progress while it runs.
- `Зоны` supports server-side name search, sorting, pagination, warehouse filtering, inclusion of deactivated zones, and creation/editing in a dialog. A zone always belongs to one warehouse, has an explicit type, and has a required code unique within the warehouse.
- Zone types distinguish ordinary storage, transit, receiving, and shipping.
  Receiving and shipping location selectors and command services accept only
  their corresponding zone types; picking and inventory counts use ordinary
  storage zones.
- Storage locations form an arbitrary-depth tree inside a zone. Structural
  nodes use `IsFolder`; only non-folder nodes may participate in inventory and
  warehouse documents. Each location stores a materialized numeric path code
  unique inside its zone, while the displayed full address combines the zone
  code and location code. The configuration UI loads the complete tree for a
  selected zone and supports single-node editing and transactional generation
  of immediate children. Subtree moves are outside the MVP. Locations may have
  nullable dimensions, capacity, absolute warehouse coordinates, and a simple
  picking sequence. Detailed rules are in
  `specs/storage-location-topology/spec.md`.
- `Номенклатура`, `Партнёры`, `Физические лица`, `Структура предприятия`, `Штрихкоды` and `Единицы измерения` are 1C-synchronized catalogs with server-side search, sorting, pagination, an option to include deactivated records, and a user-triggered refresh from 1C. The UI disables a refresh button and displays indeterminate progress while it runs. Partners, individuals, and organizational units are displayed as flat lists; their imported `ParentId` hierarchies are not visualized. Individual catalog groups are stored and shown alongside people with an explicit row type.
- `Направления доставки` is a 1C-synchronized hierarchical catalog. It is displayed as an unpaginated tree built from `ParentId`; it deliberately has no search, so the complete hierarchy always remains visible. The UI also permits a user-triggered 1C refresh with indeterminate progress.

Manual list imports for 1C-synchronized catalogs call the corresponding 1C
integration service directly and return `OperationResult`; there is no common
catalog-import facade. Configuration pages show an explicit success or error
alert and reload displayed data only after a successful import. An invalid or
failed 1C response is an import failure. Large catalogs such as SKUs and
partners are loaded in independent parallel batches; completed batches are not
rolled back when another batch fails, and the failure warns that the catalog
may have been partially updated.
Small catalogs may be fetched in one 1C request and saved through one EF Core batch operation instead of issuing one database save per catalog item. The individuals and organizational-unit imports use this approach.

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

Receiving and putaway have separate statuses. Completing a receiving order with
any positive fact keeps its 1C-backed receiving status `Received` and sets its
local WMS putaway status to `Pending`; a zero-total fact leaves putaway
`Inactive` as an exceptional outcome. The receiving location cannot be changed
after the order is received.

The operator explicitly starts pending putaway, which records its starting user
and time and changes it to `InProgress`. While in progress, editable draft
`InventoryMovement` rows move quantities from the order's receiving location to
active locations in active ordinary storage zones of the same warehouse. Draft
limits and completion are evaluated separately by receiving-order line, even
when multiple lines contain the same SKU. A line may be split across multiple
storage locations.

Putaway completion requires every receiving-order line's draft total to equal
its fact quantity. All drafts are then posted through the common inventory
service in one save operation and putaway becomes immutable `Completed`.
Putaway is local to WMS and does not call or update 1C. Completed mistakes are
corrected through an ordinary inventory transfer. Direct putaway to a shipping
location, location recommendations, capacity constraints, tasks, and rollback
are not implemented.

`ProcessingRequired` is currently only passed through from 1C's `ТребуетсяОбработка`. It is not part of the active operational flow, which is `ReadyForReceiving -> InReceiving -> Received`. Actual quantities are immutable after `Received` and are preserved when an inbound document is reconciled.

### Receiving UI

- `Index` lists receiving orders.
- `Details` shows one order and offers "Взять в работу" for a pending order when it can be started.
- `InProcess` lets the operator edit actual quantities and comments, then set the order received through `SetReceivedAsync`.
- `Putaway` lets the operator distribute each received line across storage
  locations, edit drafts while putaway is in progress, complete and post the
  full allocation, and inspect completed placement read-only.

### Receiving application services

- `ReceivingOrderCommandService` imports and reconciles orders, sets orders in receiving or received, and updates item actual quantities.
- `ReceivingOrderQueryService` reads order details and paged, filtered lists.
- `PutawayCommandService` starts and completes putaway and manages its draft
  movements; `PutawayQueryService` reads putaway movements and valid storage
  destinations.
- `InventoryPostingService` records inventory movements and updates balances
  when receiving or putaway is completed.

Inbound synchronization may create and reconcile receiving orders only in `ReadyForReceiving` and shipping orders only in `Prepared`. Once WMS work has started, any inbound difference leaves the local order unchanged, sets `ExternalChangeDetected`, and logs the conflict. Receiving facts are editable only in `InReceiving` or `ProcessingRequired`; shipping facts are editable only while picking or verification is in progress.

## 1C integration

The 1C OData client is configured through `OneCClient` settings. The integration uses explicit models named after 1C entities.

All supported notification imports return `OperationResult`. The background
dispatcher logs expected import failures in one place and logs unexpected
exceptions separately. Notification delivery still uses the non-persistent
in-memory channel described in the roadmap.

`Catalog_Партнеры_Service` imports `Catalog_Партнеры` into the local `Partner` catalog. Full synchronization reads `$count` and processes batches of 1000 records with at most 10 batches in parallel. A 1C notification triggers an individual partner update. The backend import, diagnostic endpoints, and partner configuration page are implemented.

`Catalog_ФизическиеЛица_Service` imports `Catalog_ФизическиеЛица` into the local `Individual` catalog. Full synchronization fetches the complete small catalog in one request and persists it in one EF Core batch save; a 1C notification triggers an individual record update. Catalog groups are stored with `IsFolder` and displayed in the same flat configuration list as people.

`Catalog_СтруктураПредприятия_Service` imports `Catalog_СтруктураПредприятия` into the local `OrganizationalUnit` catalog. Full synchronization fetches the complete small catalog in one request and persists it in one EF Core batch save; a 1C notification triggers an individual organizational-unit update. The imported hierarchy is retained in `ParentId`, while the configuration UI displays a flat list.

Receiving and shipping orders retain a party reference as a 1C identifier plus `PartyType`. `PartyQueryService` resolves that polymorphic reference to a common `PartyInfo` across warehouses, partners, individuals, and organizational units; EF polymorphic foreign keys and a duplicated common party table are deliberately avoided. Its batch `GetManyAsync` groups distinct references by type and performs at most one local database query per represented type, avoiding N+1 queries on order lists. It never calls 1C on demand, includes deactivated catalog records for historical display, and excludes individual-catalog folders. Receiving and shipping query services enrich both individual orders and paged lists with non-persisted `Shipper` or `Receiver` information. Missing local records remain visible as unresolved in the UI; party sorting and filtering are not implemented.

For `Document_ПриходныйОрдерНаТовары`:

- `Document_ПриходныйОрдерНаТовары_InboundService` fetches a document by
  `Ref_Key`, maps it to a domain import snapshot, and lets `ReceivingOrder`
  create or reconcile its local state.
- `Document_ПриходныйОрдерНаТовары_OutboundService` changes the document status, updates item facts when needed, and posts the document in 1C.
- `POST /api/1c/Document_ПриходныйОрдерНаТовары/notify` enqueues a notification; `NotifyBackgroundService` consumes it and imports the document asynchronously.
- Manual batch import of receiving documents is not implemented, so no public
  batch-import endpoint is exposed.

For `Document_РасходныйОрдерНаТовары`:

- `Document_РасходныйОрдерНаТовары_InboundService` fetches a document by
  `Ref_Key`, maps it to a domain import snapshot, and lets `ShippingOrder`
  create or reconcile its local state.
- `Document_РасходныйОрдерНаТовары_OutboundService` changes the document
  status, updates its table sections from WMS picking facts, and posts the
  document in 1C.
- `POST /api/1c/Document_РасходныйОрдерНаТовары/notify` enqueues a notification;
  `NotifyBackgroundService` consumes it and imports the document asynchronously.
- Manual batch import of shipping documents is not implemented, so no public
  batch-import endpoint is exposed.

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
