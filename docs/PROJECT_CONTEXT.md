# WMS project context

## Purpose and boundaries

WMS is a warehouse-management MVP. 1C owns master data and source business
documents. WMS imports them, executes warehouse work, returns the required
result to 1C, and keeps the operational inventory fact by storage location.

The implemented server-side web application supports:

- receiving and putaway;
- picking and shipping;
- intra-warehouse transfers;
- inventory counts;
- balances, posted movements, turnovers, and employee-performance reporting;
- administration of warehouses, zones, storage locations, synchronized
  catalogs, and WMS users.

Reservations, warehouse tasks, recounts, storage-capacity enforcement,
packaging conversion, and characteristic-aware SKU identity are not yet
implemented.

## Documentation ownership

This file records current product boundaries and lasting business decisions.
[`ARCHITECTURE.md`](ARCHITECTURE.md) owns engineering conventions, and
[`ROADMAP.md`](ROADMAP.md) owns unfinished work and pilot-readiness items.
Specifications under `specs/` contain detailed acceptance rules and decision
history; an implementation plan is not evidence that its feature is complete.
Code and migrations remain the source of truth for implementation details.

## Architecture and domain ownership

The normal path is `UI or endpoint -> application service -> domain operation
-> EF Core DbContext`. Application services orchestrate external state and the
save boundary; rich domain models own local invariants and transitions.

Models are enriched according to ownership, not for uniformity:

- WMS-owned documents and facts (`InventoryTransfer`, `InventoryCount`,
  movements, balances, and turnovers) have controlled creation and mutation;
- integrated orders (`ReceivingOrder` and `ShippingOrder`) own their local WMS
  workflow while 1C remains the source of their imported plan;
- WMS configuration (`Zone` and `StorageLocation`) owns local invariants;
- 1C catalogs such as `StockKeepingUnit`, `UnitOfMeasure`, `Partner`,
  `Individual`, and `OrganizationalUnit` remain straightforward import models
  unless WMS acquires a concrete local rule.

Detailed conventions and deliberate non-goals are defined once in
[`ARCHITECTURE.md`](ARCHITECTURE.md). The completed rich-model alignment record
is in [`specs/architecture-alignment/spec.md`](../specs/architecture-alignment/spec.md).

## Solution map and current implementation status

- `Wms` contains domain models, application services, EF Core persistence, and
  1C integration.
- `Wms.WebApp` is the authenticated Blazor/MudBlazor operator application.
- `Wms.WebApi` hosts the authenticated mobile V1 identity boundary, protected
  development application endpoints, and separate 1C integration endpoints.
  Verification of 1C callers is still unfinished.
- `Wms.Contracts` contains the first versioned mobile identity wire contracts.
- `Wms.Mobile` is an Android .NET MAUI client with login, secure token storage,
  refresh handling, intent and camera scanning, and diagnostic server-side
  resolution of storage-location and SKU barcodes.

Mobile development is the current resumed workstream. Authentication, shared
contracts/API client, diagnostic scanning, and contextual barcode resolvers are
present, but mobile business workflows and command idempotency are still to be
implemented.

## Authentication and authorization

`Wms.WebApp` uses ASP.NET Core Identity and two fixed roles:

- `Operator` can use operational pages and reports;
- `Administrator` has the same operational access and additionally manages
  configuration catalogs and WMS users.

Public self-registration is not linked and its retained route is restricted to
administrators. Accounts created by an administrator are confirmed local
accounts. Each user has one WMS role in the MVP. An administrator cannot block
their own account or remove their own administrator role, and the last active
administrator cannot be blocked or demoted.

`ApplicationUser.DisplayName` is the current human-readable audit and report
name. Existing users with an empty display name fall back to `UserName`.
Operational records store only the Identity user id, so renaming a user changes
historical display; a deleted user falls back to the stored identifier.

Roles are initialized idempotently at web-application startup. Existing users
without a WMS role become operators. The bootstrap administrator is selected by
`IdentityBootstrap__AdministratorEmail`. Creating a missing bootstrap account
also requires `IdentityBootstrap__AdministratorDisplayName` and the secret
`IdentityBootstrap__AdministratorPassword`; the password must not be committed.

These role rules protect `Wms.WebApp`, mobile V1 endpoints, and the development
application endpoints in `Wms.WebApi`. Mobile bearer login reuses the same
confirmed accounts; command authors come from the authenticated principal.
Verification of 1C callers remains a separate pilot prerequisite. Detailed
web-role behavior is in
[`specs/identity-roles-and-user-management/spec.md`](../specs/identity-roles-and-user-management/spec.md).

## Storage topology and capacity data

A warehouse contains typed zones: ordinary storage, transit, receiving, and
shipping. A storage location belongs to exactly one warehouse and zone and may
form an arbitrary-depth tree inside that zone. Fixed aisle/rack/level/bin types
are deliberately not modeled.

- `IsFolder` distinguishes structural nodes from operational locations. Folder
  nodes are rejected by inventory operations and operational selectors.
- A location and its parent must belong to the same active warehouse zone.
- `Zone.Code` is unique in a warehouse. `StorageLocation.Code` is a
  materialized numeric path unique in a zone; the full displayed address is
  `{Zone.Code}-{StorageLocation.Code}`.
- Existing nodes cannot be moved or renumbered in the MVP.
- A node with active children cannot be deactivated, and a child cannot be
  activated while its parent is inactive.
- Optional dimensions use meters, volume uses cubic meters, maximum weight uses
  kilograms, and coordinates use the warehouse's local meter coordinate
  system. `UsableVolume` is `Volume * (VolumeFactor ?? 1)`.
- `PickSequence` is optional; duplicate values are allowed and the materialized
  code is the stable secondary ordering.
- The current technical barcode is `WMSL:{storage-location-guid-N}`. It is
  derived from the id and is not persisted separately.

The web configuration UI supports a zone tree, editing one node, and atomic
generation of immediate children with deterministic codes, coordinates, and
picking sequence. Weight and volume limits are stored but are not yet displayed
as live occupancy or enforced during posting. Detailed rules are in
[`specs/storage-location-topology/spec.md`](../specs/storage-location-topology/spec.md).

## SKU physical properties and 1C import

Inventory is expressed in one canonical unit per SKU. The current SKU importer
normalizes physical properties into:

- `WeightKg`: kilograms per canonical SKU unit;
- `VolumeM3`: cubic meters per canonical SKU unit.

Before importing SKU values, WMS refreshes the referenced 1C units of measure
and uses their measurement type and nullable numerator/denominator. Disabled,
missing, incompatible, negative, or non-finite properties become `null`, not
zero. Import continues for other values and the catalog UI reports counts of
invalid weight and volume values. Stored values are not rounded; the UI shows
weight to three and volume to six fractional digits.

Physical properties are current catalog values, not snapshots. Historical
document, balance, movement, turnover, and report weight therefore changes when
the SKU catalog changes. A nonzero fact with unknown unit weight is displayed as
unknown and makes a document/report total incomplete.

A 1C packaging is an input/conversion representation rather than a separate
inventory balance. Packaging conversion and the exact relationship between
`Количество` and `КоличествоУпаковок` still require real 1C examples. A 1C
characteristic that distinguishes a stock variant must eventually become part
of SKU identity, but characteristic-aware identity is not implemented. The
current importer therefore assumes one SKU per 1C nomenclature item.

Live occupied/free weight and volume and hard blocking of known capacity
excesses are later increments. The missing-data policy must remain distinct from
a numeric zero. Detailed source-field and staged rules are in
[`specs/sku-physical-properties/spec.md`](../specs/sku-physical-properties/spec.md).

## Inventory facts

`InventoryBalance` is the current SKU quantity in one warehouse storage
location. `InventoryMovement` represents a warehouse movement and becomes
historical when posted. `InventoryTurnover` is the immutable before/delta/after
record produced for each affected location.

The common posting service validates locations, posts movements, changes
balances, and creates turnovers within the caller's save operation. A source
cannot become negative. Draft picking and putaway movements do not change
inventory until their workflow posts them. The operator UI lists balances,
posted movements, and turnovers; drafts are excluded from the posted-movement
list. Reservations and aggregated available-to-promise quantities are not
modeled.

Transfer state uses targeted optimistic concurrency through
`InventoryTransfer.RowVersion`. Balance row versions, a named balance
business-key index, and a filtered unique transfer-line index are the final
inventory guards. The transfer application boundary translates only recognized
transfer and balance races into business conflicts; unrelated persistence
errors remain exceptions. Append-only turnovers and 1C-owned catalogs do not
receive version columns by default. The completed stabilization record is in
[`specs/2026-08-21-inventory-transfer-concurrency/spec.md`](../specs/2026-08-21-inventory-transfer-concurrency/spec.md).

## Operational workflows

### Receiving and putaway

Receiving orders are imported from 1C and reconciled without discarding active
local work. WMS controls the receiving location, factual quantities, workflow
status, comments, timestamps, and users. Completing receiving posts facts into
the receiving location. Putaway records draft movements from that location to
ordinary storage and posts them when putaway completes.

The receiving and putaway rules are defined in
[`specs/receiving-putaway/spec.md`](../specs/receiving-putaway/spec.md) and the
strict workflow behavior in
[`specs/strict-order-workflows/spec.md`](../specs/strict-order-workflows/spec.md).

### Picking and shipping

Shipping uses WMS-controlled transitions from prepared to picking, ready for
shipment, and shipped. Picking creates draft movements from ordinary storage to
the selected shipping location. Setting the order ready for shipment first
reconciles the fresh 1C plan, updates the 1C table sections, then posts those
movements. Shipping posts the final issue from the shipping location.

An unfinished cycle may be rolled back locally to prepared: drafts are deleted
and already posted movements from that cycle are offset by new reverse
movements so turnover history remains intact. Drafts are not reservations and
final posting rechecks physical balance.

Detailed rules are in
[`specs/shipping-order-workflow/spec.md`](../specs/shipping-order-workflow/spec.md),
[`specs/picking-draft-movements/spec.md`](../specs/picking-draft-movements/spec.md),
and [`specs/shipping-order-rollback/spec.md`](../specs/shipping-order-rollback/spec.md).

### Inventory counts

Inventory counts are local WMS documents. A draft may contain incomplete rows.
Posting a completed row creates a receipt or issue movement for the
counted-versus-expected difference and records the posting user. Recounts,
reservations, and generated count tasks are not implemented. Detailed rules are
in [`specs/inventory-count-backend/spec.md`](../specs/inventory-count-backend/spec.md).

### Intra-warehouse transfers

Transfers are local WMS documents. Direct, pick-to-transit, and
put-from-transit actions post immediately. A transit location belongs
exclusively to one active transfer and must be empty before completion. Posted
movements and completed transfers are immutable; an unused draft may be
deleted. Detailed rules are in
[`specs/intra-warehouse-transfers/spec.md`](../specs/intra-warehouse-transfers/spec.md).

## Reports and UI conventions

The employee-performance report attributes receiving to `CompletedBy` and
picking to `ReadyForShipmentBy`. Picking duration ends at ready-for-shipment,
not at shipping. Totals cover the complete filtered result and use current SKU
weights; incomplete weight data is shown explicitly. Detailed rules are in
[`specs/employee-performance-report/spec.md`](../specs/employee-performance-report/spec.md).

Document lists and headers share compact conventions, but those presentation
details are not business boundaries. Their focused specifications are
[`specs/document-list-layout/spec.md`](../specs/document-list-layout/spec.md) and
[`specs/document-information-headers/spec.md`](../specs/document-information-headers/spec.md).

## 1C integration

1C catalog and document transport DTOs remain inside `Wms.Integration.OneS`.
Integration services map them to simple catalog models or domain import
snapshots. Manual synchronization pages may call the corresponding integration
service directly. Expected integration failures use `OperationResult`; network,
protocol, or programming failures remain observable infrastructure errors.

Notification delivery currently uses an in-memory channel and has no durable
retry guarantee. WMS-to-1C multi-step transitions have no outbox, so pilot
operations require a documented recovery procedure for external success
followed by local failure.

## Mobile WMS direction

Mobile development has resumed, but the web application remains the only
operational user channel until a mobile vertical passes its acceptance checks.
The client is Android-only, online-only, and communicates exclusively through
an authenticated, versioned API. It must reuse the same application services
and server-side business rules, derive the acting user from the authenticated
principal, and make every state-changing command idempotent by a stable client
request id.

Scanning stays vendor-neutral. Storage locations use the existing
`WMSL:{storage-location-guid-N}` payload; SKU barcodes remain imported strings
from 1C. The verified Urovo TD50 path uses ScanWedge intent output behind the
neutral scanner interface. The Android camera fallback uses Google ML Kit via
`BarcodeScanning.Native.Maui` and is activated only by an explicit user action.
The first vertical is direct intra-warehouse movement, followed by the transit
workflow.

The accepted scope and delivery order are under
[`specs/mobile-wms/`](../specs/mobile-wms/). Those documents describe the active
development target, not already implemented behavior.

Changing mobile commands use persisted command receipts. The idempotency key is
the authenticated user, stable command type, and client-generated request id;
the receipt stores a deterministic request hash and result resource id. A
receipt and its WMS change are saved by the same `ApplicationDbContext` and
`SaveChangesAsync`, so a retry or concurrent duplicate cannot commit a second
warehouse action. Reusing the key with different input is a conflict. The first
implementation protects creation of a direct inventory-transfer draft.
