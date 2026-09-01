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
- `Wms.Contracts` contains the versioned Mobile V1 wire contracts.
- `Wms.Mobile` is an Android .NET MAUI client with login, secure token storage,
  refresh handling, intent and camera scanning, and diagnostic server-side
  resolution of storage-location and SKU barcodes. Its implemented operational
  workflows cover direct and transit intra-warehouse movements and
  location-based inventory counts. The receiving and putaway Mobile V1 server
  contracts and endpoints, warehouse work queue, document scan, and mobile
  receiving and putaway workflows are implemented. Putaway supports explicit
  start, scanned destinations, split quantities, draft deletion, exact
  completion, and idempotent retry of uncertain changing responses. The
  picking and shipping mobile section implements the full online path from a
  warehouse-scoped queue or document scan through picking movements and result
  confirmation to a rescanned shipping location and final shipment.

The mobile foundation and the intra-warehouse transfer, location-based
inventory-count, and receiving/putaway workflows are accepted. Device checks
cover Urovo TD50 and, for the foundation and transfer workflow, a control
Android smartphone. Accepted behavior includes authentication and session
restoration, real refresh of an expired access token, automatic selection of
the embedded scanner or inline camera, contextual barcode resolution, and
atomic command idempotency.

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
- An active operational location may have one temporary active lock with an
  explicit reason. A manual lock blocks all inbound and outbound inventory
  movements; administrators manage it in the storage-location configuration.
  Unlocking removes the active lock rather than retaining lock history.
- A locked location remains visible in topology, balances, and history, but is
  excluded from operational selectors. It cannot be deactivated or converted
  to a folder while locked. Draft work may continue to exist, but final posting
  involving the location is rejected.
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
The accepted cross-process locking rules are in
[`specs/2026-08-27-storage-location-locking/spec.md`](../specs/2026-08-27-storage-location-locking/spec.md).

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
inventory balance. For receiving-order lines, both `Количество` and
`КоличествоУпаковок` are configured as nonnegative 1C numbers with length 15
and precision 3 and are exposed as nullable `Edm.Double`; the line also exposes
nullable `Упаковка_Key`. Packaging conversion and the exact relationship
between the two quantities are not implemented. Existing receiving lines in
the current deployment contain no nonempty packaging key, so WMS deliberately
keeps the current 1:1 import/export behavior. A future deployment that uses
packaging must provide real line and packaging examples and define conversion
before this assumption is changed. A 1C characteristic that distinguishes a
stock variant must eventually become part of SKU identity, but
characteristic-aware identity is not implemented. The current importer
therefore assumes one SKU per 1C nomenclature item.

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

Every posting validates active location locks before changing inventory and
advances the operational revision of each distinct participating location in
the same save operation. Acquiring or releasing a lock advances that revision
as well, so a concurrent movement and lock change cannot both commit from the
same location state. Document-owned locks are represented by the same active
lock model. A location inventory count acquires its document-owned lock when
the draft is created and releases it only when the count is posted or the draft
is explicitly deleted.

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

A nullable receiving fact distinguishes an unchecked line from an explicitly
confirmed zero. A mobile accepted SKU scan increments the selected order line
by one, while manual input sets its absolute nonnegative fact without changing
an existing web-entered comment. Every line must have an explicit fact before
receiving can complete. A new order therefore shows aggregate fact zero while
its confirmed-line count remains zero and each line is shown as unchecked;
the aggregate zero does not confirm those lines. The Mobile V1 server exposes
one warehouse work queue, document and line resolution, receiving commands,
draft putaway commands, and terminal command results. A 1C document barcode
resolves to the document GUID through the shared decimal codec; leading zeroes
in the scanned decimal payload are accepted and normalized.

The aggregate advances `ReceivingOrder.OperationalRevision` for every local
plan reconciliation, receiving change, state transition, and draft putaway
movement change. The revision is an optimistic-concurrency token shared by web
and mobile saves; a stale command returns a business conflict instead of
silently overwriting another operator's work.

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

Selecting the shipping location and starting picking form one local save
boundary. `ShippingOrder.OperationalRevision` advances on local plan
reconciliation, shipping-location and workflow transitions, draft-picking
movement changes, and rollback. It is an optimistic-concurrency token shared
by web and mobile commands, so concurrent work on one order returns a
business conflict instead of silently overwriting its facts, movements, or
status.

The authenticated Mobile V1 boundary for picking and shipping exposes a
warehouse work queue, document and line resolution, current-cycle picking
movements, and source-location availability. It can idempotently start picking
after a physical scan of an eligible shipping location; the location, local
transition, audit, and command receipt share one save boundary. It can also
idempotently add a draft movement from a scanned eligible storage location and
delete a draft movement; each movement change and its receipt share one save
boundary and reuse the Web picking rules. It can idempotently complete full,
partial, or zero picking through the existing 1C update and inventory posting,
and can ship only after rescanning the order's active unlocked shipping
location. The mobile UI implements the warehouse queues, document scan, stage
routing, and start-picking flow with stable retry identity. It also shows the
picking plan, facts, remaining quantities, and draft movements; an operator can
select a line by SKU scan or manual search, scan an eligible source, confirm a
safe quantity, split picking across sources, and delete a mistaken draft without
editing it in place. Source availability is only a hint: the source itself must
be physically scanned. The picking screen presents the full, partial, or zero
result before completion and requires a separate acknowledgement of any
shortage. Successful completion opens the final shipping screen with fresh
facts. Shipping requires rescanning the order's saved shipping location and an
explicit final confirmation; success returns to the refreshed work queue, where
the shipped order is no longer present.

An unfinished cycle may be rolled back locally to prepared: drafts are deleted
and already posted movements from that cycle are offset by new reverse
movements so turnover history remains intact. Drafts are not reservations and
final posting rechecks physical balance.

Detailed rules are in
[`specs/shipping-order-workflow/spec.md`](../specs/shipping-order-workflow/spec.md),
[`specs/picking-draft-movements/spec.md`](../specs/picking-draft-movements/spec.md),
and [`specs/shipping-order-rollback/spec.md`](../specs/shipping-order-rollback/spec.md).

### Inventory counts

An inventory count is a local WMS document for one ordinary storage location.
Creating it atomically locks the location and records every positive current
balance as an expected row. Expected quantities remain visible. A nullable
counted quantity distinguishes an uncounted row from an explicitly confirmed
zero.

Each accepted mobile SKU scan records one physical unit: the first scan sets
one and subsequent scans increment by one. Manual input in either UI searches
by name, code, or barcode, sets an absolute nonnegative quantity, and may add an
unexpected SKU. The web UI does not provide a separate repeated-scan `+1` mode.
Every row must be counted before posting. Posting creates movements only for
nonzero counted-versus-expected differences and releases the lock in the same
save.
Explicitly abandoning work physically deletes the draft and its rows and also
releases the lock; merely leaving the page preserves the draft. There is no
cancelled status or separate mobile inventory-count model. Recounts,
reservations, count assignments, and generated count tasks are not implemented.
The completed implementation record is in
[`specs/2026-08-27-location-inventory-count/spec.md`](../specs/2026-08-27-location-inventory-count/spec.md).

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
followed by local failure. Shipping-order completion recognizes an already
applied exact 1C item-table target as success, allowing the same mobile command
to continue after external success and a failed local save; target status and
posting calls are likewise deliberately repeatable.

## Mobile WMS direction

The first three mobile operational processes have been accepted.
The client is Android-only, online-only, and communicates exclusively through
an authenticated, versioned API. It reuses the same application services and
server-side business rules, derives the acting user from the authenticated
principal, and makes every state-changing command idempotent by a stable client
request id.

Scanning stays vendor-neutral. Storage locations use the existing
`WMSL:{storage-location-guid-N}` payload; SKU barcodes remain imported strings
from 1C. The verified Urovo TD50 path uses ScanWedge intent output behind the
neutral scanner interface. The Android camera fallback uses Google ML Kit via
`BarcodeScanning.Native.Maui`. Operational screens should automatically prefer
an available embedded scanner and use the camera when no embedded scanner is
available, without requiring the operator to switch scanning mechanisms.
The current Urovo adapter detects the embedded scanner through the runtime
presence of `android.device.ScanManager`, not a manufacturer or model string;
the device profile must still be configured for intent output. On a camera-only
device, the preview is embedded in the current step card below its instruction
and is hidden while the operator enters quantity or reviews confirmation.
The first vertical is intra-warehouse movement, including direct actions and
actions through one transfer-owned transit location.

An active mobile transfer opens as a server-backed movement history. Starting
a new movement opens a separate sequential scanning screen; a successful
command returns to the refreshed history and highlights the new movement, while
a rejected or uncertain command remains on the scanning screen.

A transit transfer is created or reopened through an explicitly scanned transit
location. Its document screen keeps pick, put, direct, and completion actions
above the current transit balance and movement history. Pick and put use
separate sequential screens; a transit SKU may also be selected directly from
the displayed positive balance. The transit location remains immutable and the
server rejects completion while it has positive inventory.

On the SKU step, scanning remains the primary path. If a label cannot be read,
the operator may explicitly open an inline search by name, code, or barcode.
The search is limited to SKUs with a positive balance in the already selected
source location, does not take focus until the operator opens it, and selecting
a result continues through the same quantity and confirmation workflow.

The second accepted mobile process is a full count of one ordinary storage
location. Scanning the location creates or reopens its draft and acquires a
document-owned location lock. Expected rows remain visible; repeated SKU scans
accumulate one unit at a time, while manual search records an absolute quantity
and can add an unexpected SKU. Posting or explicitly deleting the draft
releases the lock; merely leaving the screen preserves the draft.

The third accepted mobile process is receiving and putaway of one 1C receiving
order. A warehouse queue or document barcode opens the applicable stage. Every
receiving line remains unchecked until an accepted SKU scan or absolute manual
input confirms it, including an explicit zero. Completion posts positive facts
to the scanned receiving location; putaway then records and posts exact,
possibly split draft movements to scanned ordinary storage locations.

The fourth accepted mobile process is picking and shipping of one 1C shipping
order. A warehouse queue or document barcode opens the applicable stage.
Picking starts against a scanned shipping location, records possibly split
draft movements from scanned ordinary storage locations, and explicitly accepts
a full, partial, or zero result. Final shipment requires rescanning the saved
shipping location and removes the shipped order from the mobile work queues.

The accepted foundation and first-vertical scope are retained under
[`specs/mobile-wms/`](../specs/mobile-wms/) as a frozen reference. Each
subsequent mobile warehouse process is specified separately.

Changing mobile commands use persisted command receipts. The idempotency key is
the authenticated user, stable command type, and client-generated request id;
the receipt stores a deterministic request hash and result resource id. A
receipt and its WMS change are saved by the same `ApplicationDbContext` and
`SaveChangesAsync`, so a retry or concurrent duplicate cannot commit a second
warehouse action. Reusing the key with different input is a conflict. The
current implementation protects creation of direct and transit
inventory-transfer drafts, direct movement, pick to transit, put from transit,
completion of the transfer, every changing inventory-count command, and every
changing receiving, putaway, picking, and shipping command. The
repeated receipt lifecycle is centralized inside the respective mobile command
service while each business action remains an explicit internal staged
operation.

The client retains the same command request id after transport failure, HTTP
`408`, HTTP `5xx`, or an unreadable successful response because those outcomes
do not prove whether the server committed the command. A definitive `4xx`
business/client rejection releases the request id.
