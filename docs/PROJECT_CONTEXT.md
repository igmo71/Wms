# WMS project context

## Purpose and boundaries

WMS is a warehouse-management MVP. 1C owns master data and source business
documents. WMS imports them, executes warehouse work, returns the required
result to 1C, and keeps the operational inventory fact by storage location.

The implemented product supports:

- receiving and putaway;
- picking and shipping;
- direct and transit intra-warehouse transfers;
- location-based inventory counts;
- balances, posted movements, turnovers, and employee-performance reporting;
- administration of warehouses, zones, storage locations, synchronized
  catalogs, and WMS users.

Reservations, warehouse tasks, recounts, storage-capacity enforcement,
packaging conversion, and characteristic-aware SKU identity are not
implemented.

This file owns current product boundaries and lasting business rules.
[`ARCHITECTURE.md`](ARCHITECTURE.md) owns engineering conventions,
[`ROADMAP.md`](ROADMAP.md) owns unfinished work, and `specs/` keeps detailed
decision history. Code and migrations remain the source of truth for
implementation details.

## System map

- `Wms` contains the domain, application services, EF Core persistence, and 1C
  integration.
- `Wms.WebApp` is the authenticated Blazor/MudBlazor operator application.
- `Wms.WebApi` hosts authenticated Mobile V1 and application
  endpoints plus separate 1C integration endpoints. Verification of 1C callers
  is not yet implemented.
- `Wms.Contracts` contains versioned Mobile V1 transport contracts.
- `Wms.Mobile` is the Android .NET MAUI client.

WebApp and Mobile share the same application services and server-side business
rules. The four mobile operational processes—transfer, inventory count,
receiving/putaway, and picking/shipping—are accepted. The verified embedded
scanner path uses Urovo TD50; the foundation and transfer flow were also
checked on a control Android smartphone.

## Authentication and authorization

`Wms.WebApp` uses ASP.NET Core Identity and two fixed roles:

- `Operator` can use operational pages and reports;
- `Administrator` has the same access and additionally manages configuration
  catalogs and WMS users.

Public self-registration is not linked and its retained route is restricted to
administrators. Administrator-created accounts are confirmed local accounts.
Each user has one WMS role. An administrator cannot block their own account or
remove their own administrator role, and the last active administrator cannot
be blocked or demoted.

`ApplicationUser.DisplayName` is the human-readable audit and report name. An
empty display name falls back to `UserName`. Operational records store the
Identity user id, so renaming a user changes historical display; a deleted user
falls back to the stored identifier.

Roles are initialized idempotently at WebApp startup. Existing users without a
role become operators. A missing bootstrap administrator is selected and
created through `IdentityBootstrap__AdministratorEmail`,
`IdentityBootstrap__AdministratorDisplayName`, and the secret
`IdentityBootstrap__AdministratorPassword`.

Mobile bearer login uses the same confirmed accounts and derives command
authors from the authenticated principal. A transient token-refresh failure
(transport, `408`, `5xx`, or an unreadable successful response) retains the
session and does not send the original operation with a stale token. A
definitive refresh rejection (`400` or `401`) or an authenticated API `401`
clears the session. The main screen reconciles its state whenever it reappears.

## Storage topology

A warehouse contains storage, transit, receiving, and shipping zones. A
storage location belongs to one warehouse and zone and may form an
arbitrary-depth tree. Fixed aisle/rack/level/bin types are not modeled.

- `IsFolder` distinguishes structural nodes from operational locations. Folder
  nodes are rejected by inventory operations and operational selectors.
- A location and its parent belong to the same active warehouse zone.
- `Zone.Code` is unique in a warehouse. `StorageLocation.Code` is a materialized
  numeric path unique in a zone. The displayed operational address is
  `{Zone.Code}-{StorageLocation.Code}`.
- Existing nodes cannot be moved or renumbered.
- A node with active children cannot be deactivated, and a child cannot be
  activated while its parent is inactive.
- `PickSequence` is optional; duplicates are allowed and the materialized code
  is the stable secondary ordering.
- The technical barcode is `WMSL:{storage-location-guid-N}` and is derived from
  the id rather than persisted.

An active operational location may have one active lock with an explicit
reason. A locked location remains visible in topology and history but is
excluded from operational selectors and cannot participate in final posting.
It cannot be deactivated or converted to a folder. Administrators manage
manual locks; inventory-count documents own their location lock until posting
or explicit draft deletion. Releasing a lock deletes the active lock record;
separate lock history is not retained.

Optional dimensions use meters, volume uses cubic meters, maximum weight uses
kilograms, and coordinates use the warehouse-local meter system.
`UsableVolume` is `Volume * (VolumeFactor ?? 1)`. Limits are stored but live
occupancy and capacity enforcement are not implemented.

## SKU and inventory facts

Inventory uses one canonical unit per SKU. The importer normalizes physical
properties to kilograms per canonical unit (`WeightKg`) and cubic meters per
canonical unit (`VolumeM3`). Invalid, missing, incompatible, negative, or
non-finite values become `null`, not zero. Stored values are not rounded;
current UI precision is three decimals for weight and six for volume.

Operational warehouse quantities use C# `decimal` and are persisted in SQL
Server as `decimal(15,3)`, matching the current 1C `Number(15,3)` boundary.
Values outside that range or with more than three fractional digits are
rejected rather than rounded. Physical properties such as weight, volume,
dimensions, coordinates, and conversion coefficients remain `double`.

Physical properties are current catalog values, not historical snapshots.
Changing a SKU therefore changes displayed historical weights. A nonzero fact
with unknown unit weight makes its total explicitly incomplete.

The current deployment treats 1C receiving quantities and package quantities
1:1 because existing lines have no nonempty `Упаковка_Key`. Packaging
conversion requires real source examples and an agreed coefficient rule before
implementation. Characteristic-aware SKU identity is likewise deferred; the
current importer assumes one SKU per 1C nomenclature item.

`InventoryBalance` stores the current SKU quantity in one location.
`InventoryMovement` is a warehouse movement and becomes history when posted.
`InventoryTurnover` is the immutable before/delta/after record for each affected
location.

Posting validates locations and locks, prevents a negative source balance,
changes balances, and creates turnovers in the caller's save operation. Draft
picking and putaway movements do not affect inventory until their workflow
posts them. Drafts are excluded from the posted-movement list. Reservations and
aggregated available-to-promise quantities are not modeled.

Posting and lock changes advance `StorageLocation.OperationalRevision`, so a
concurrent movement and lock change cannot both commit from the same location
state. Transfers additionally use targeted optimistic concurrency through
`InventoryTransfer.RowVersion`, balance row versions, and named database
constraints. Only recognized inventory concurrency failures become business
conflicts; unrelated persistence failures remain exceptions.

## Operational workflows

### Receiving and putaway

Receiving orders are imported from 1C without discarding active local work.
WMS owns the receiving location, facts, local workflow, comments, timestamps,
and users. Completing receiving posts positive facts into the receiving
location. Putaway records possibly split draft movements from that location to
ordinary storage and posts them on completion.

A nullable receiving fact distinguishes an unchecked line from an explicitly
confirmed zero. A mobile SKU scan increments a selected line by one; manual
input sets an absolute nonnegative fact without overwriting a web-entered
comment. Every line must have an explicit fact before completion.

The mobile flow opens an order from a warehouse queue or document barcode,
requires a scanned receiving location, and supports draft putaway to scanned
destinations. `ReceivingOrder.OperationalRevision` protects plan
reconciliation, facts, transitions, and draft movements from stale web or
mobile saves.

### Picking and shipping

Shipping orders move from prepared to picking, ready for shipment, and shipped.
Picking creates possibly split draft movements from ordinary storage to the
selected shipping location. Completing picking first reconciles the fresh 1C
plan, updates its item tables, and posts the draft movements. Shipping then
posts the final issue from the shipping location.

Selecting the shipping location and starting picking form one local save
boundary. `ShippingOrder.OperationalRevision` protects plan reconciliation,
location and workflow transitions, draft movements, and rollback from stale
web or mobile saves.

Mobile starts picking against a physically scanned shipping location. An
operator selects a line by SKU scan, manual search, or line action, scans an
eligible source, and enters a safe quantity. Pressing `Отобрать` immediately
creates the still-deletable draft movement; there is no repeated review step.
Source availability is a hint and does not replace the physical source scan.

Completion presents the full, partial, or zero result and requires a separate
acknowledgement of any shortage. Final shipping shows the assigned location,
quantity, and irreversible-effect warning, then requires explicit confirmation
without rescanning the already assigned location. The server validates that
location from the order.

An unfinished cycle may be rolled back to prepared. Drafts are deleted and
posted movements from that cycle are offset by new reverse movements, preserving
turnover history. Final posting always rechecks physical balance.

### Inventory counts

An inventory count covers one ordinary storage location. Creating the draft
atomically locks the location and records every positive balance as an expected
row. A nullable counted quantity distinguishes an uncounted row from an
explicitly confirmed zero.

Each mobile SKU scan adds one physical unit. Manual input in either UI searches
by name, code, or barcode, sets an absolute nonnegative quantity, and may add an
unexpected SKU. Every row must be counted before posting. Posting records only
nonzero differences and releases the lock in the same save.

Leaving the screen preserves the draft. Explicit deletion removes the draft
and rows and releases the lock. There is no cancelled state, recount, assignment
queue, or separate mobile inventory-count model.

### Intra-warehouse transfers

Transfers are local WMS documents. Direct, pick-to-transit, and
put-from-transit actions post immediately. A transit location belongs
exclusively to one active transfer and must be empty before completion. Posted
movements and completed transfers are immutable; an unused draft may be
deleted.

Mobile opens an active transfer as server-backed movement history. Direct and
transit movements use sequential scan screens. A transit SKU may also be
selected from displayed positive transit balance. Successful commands return
to refreshed history; rejected or uncertain commands remain on the operation
screen.

## Reports and operator UI

The employee-performance report attributes receiving to `CompletedBy` and
picking to `ReadyForShipmentBy`. Picking duration ends at ready-for-shipment.
Totals cover the complete filtered result and use current SKU weights;
incomplete weight data is shown explicitly.

Operational WebApp screens identify storage locations by full address rather
than potentially repeated names. Mobile step headings are semantic and
unnumbered. Changing actions use short explicit verbs; an icon without text is
reserved for a safe local action such as closing search.

## 1C integration

1C owns catalog data and source document plans. WMS owns local warehouse facts
and workflow state. Active document reconciliation must not silently discard
recorded WMS work.

Notification delivery uses an in-memory channel and has no durable retry
guarantee. WMS-to-1C multi-step transitions have no outbox, so pilot operations
need an operator recovery procedure for external success followed by local
failure. Shipping completion recognizes an already-applied exact 1C item-table
target as success; target status and posting calls are deliberately repeatable.

Document notifications and explicit fresh checks use the same receiving or
shipping synchronization service. A successfully fetched document produces a
structured synchronization assessment even when business differences exist;
only transport, malformed-response, missing-local-order, and persistence
failures are returned as operation errors. Notifications may create a new
source order, while an explicit check requires the order to exist in WMS.

Web order lists distinguish synchronized, operator-decision, and blocking
states. Details show each changed field with WMS and 1C values. Only an
operator-decision assessment can be acknowledged. Acknowledgement performs a
fresh fingerprint check, applies only source-owned metadata from 1C to WMS,
and records the user and time. A changed fingerprint or blocking assessment
cannot be acknowledged; quantity, identity, composition, and warehouse facts
are never overwritten by this action.

Starting receiving or picking uses the synchronization result obtained when
the order was opened and refuses a known unresolved state. Completing
receiving, completing picking, and final shipping each fetch a fresh 1C
snapshot before local transitions, inventory posting, or outbound mutation.
An exact source state or the exact repeat-safe target of the requested command
may continue; technical verification failure, an unacknowledged decision, or a
blocking assessment stops the transition and preserves the new compact state.

Mobile receiving and shipping queues show the last known synchronization
level. Opening active receiving, picking, or final shipping performs the fresh
check and returns concise changed-field names plus the changed 1C comment when
applicable. Mobile cannot acknowledge a discrepancy: an operator-decision
state points to WebApp, while a blocking state points to external resolution.
The corresponding start and completion actions remain unavailable until the
same fingerprint is resolved. Opening the local putaway process does not query
1C again. If a fresh Mobile check fails technically, the order and its last
known synchronization level remain visible with the verification error, while
critical transitions stay unavailable.

Orders persist the current synchronization level and fingerprint, the
detection time of the current issue, and acknowledgement audit. They do not
persist the time of every successful check. Repeating an exact assessment
therefore does not advance the order's `OperationalRevision`; a changed level
or fingerprint still does.

## Mobile platform rules

The client is Android-only and online-only and communicates through
authenticated Mobile V1. Scanning is vendor-neutral: Urovo ScanWedge intent
output and the Google ML Kit camera fallback implement the same scanner
boundary. Operational screens prefer an available embedded scanner and use the
camera otherwise. The Urovo adapter detects `android.device.ScanManager`; the
device must still be configured for intent output.

Where manual SKU search is provided, it searches by name, code, or barcode and
continues through the same operation as scanning. Camera preview is embedded in
the current step and hidden while scanning is not expected. A 1C document
barcode resolves through the shared decimal GUID codec; leading zeroes are
accepted and normalized.

Every changing warehouse command uses a persisted receipt keyed by
authenticated user, command type, and client request id. The receipt contains
a deterministic request hash and result resource id and is saved atomically
with the WMS change. Reusing an id with different input is a conflict.

The client retains the same request id after transport failure, `408`, `5xx`,
or an empty, malformed, truncated, or incompatible successful response. Those
outcomes do not prove whether the server committed the command. A definitive
business/client `4xx` releases the request id. Mobile errors use the stable
codes `invalid_command`, `resource_not_found`, `request_conflict`, and
`command_failed`.
