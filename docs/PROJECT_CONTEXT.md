# WMS project context

## Product boundary

WMS is a warehouse-management MVP. 1C owns catalogs and source receiving and
shipping documents. WMS imports them, executes warehouse work, returns results
to 1C, and owns operational inventory by storage location.

Implemented workflows are receiving and putaway, picking and shipping, direct
and transit intra-warehouse transfers, location inventory counts, inventory
history, employee-performance reporting, topology administration, synchronized
catalogs, and WMS user administration.

The supported interactive clients are `Wms.WebApp` and Android `Wms.Mobile`.
`Wms.WebApi` exposes authenticated Mobile V1 plus separate 1C integration
endpoints.

This file owns current product boundaries and lasting business rules.
[`ARCHITECTURE.md`](ARCHITECTURE.md) owns engineering conventions,
[`ROADMAP.md`](ROADMAP.md) owns unfinished work, and frozen specifications keep
decision history. Code and migrations remain the implementation source of
truth.

The accepted LPN, receiving/putaway and addressing development plan is tracked in
[`DEVELOPMENT_PLAN.md`](DEVELOPMENT_PLAN.md). The active
[receiving and putaway specification](../specs/2026-09-15-lpn-receiving-putaway/spec.md)
defines the next implementation, not current behavior. Existing databases may
be recreated; legacy data migration is outside its scope. The accepted target
limits LPN to receiving and initial putaway, preserves location/SKU inventory,
and uses existing topology. The target uses one sequential LPN code for display
and barcode, and retained zero balances for historical-location suggestions.
The specification defines eight increments. I01 label issuance and A4 printing
and I02 receiving-order participation are implemented; LPN association and
I03–I08 remain future work.
Broader addressing work follows later. Consult the
specification registry for active scope; implemented rules remain below.

## Identity and access

WebApp uses ASP.NET Core Identity with two roles: `Operator` may run warehouse
operations and reports; `Administrator` additionally manages configuration and
users. Public self-registration is not offered. Administrator-created accounts
are confirmed local accounts with one WMS role. An administrator cannot block
or demote themselves, and the last active administrator cannot be blocked or
demoted.

`ApplicationUser.DisplayName` is used for audit and reports, falling back to
`UserName`. Operational records retain the Identity user id, so renamed users
change historical display and deleted users display the stored id.

Startup initializes roles idempotently, assigns `Operator` to existing users
without a role, and creates a missing bootstrap administrator from
`IdentityBootstrap` configuration.

Mobile uses the same confirmed accounts. A transient refresh failure
(transport, `408`, `5xx`, or an unreadable successful response) retains the
session and does not replay the request with a stale token. A definitive refresh
rejection (`400` or `401`), or an authenticated API `401`, clears the session.

## Storage and inventory

Database maintenance scripts run with WebApp, WebApi, and integration workers
stopped. `scripts/clear-wms-operational-data.sql` clears warehouse documents,
inventory, command receipts, and inventory-count locks while preserving
Identity, catalogs, warehouse topology, and manual locks.
`scripts/clear-database-except-identity.sql` also clears catalogs, topology, and
all locks. Both clear issued LPN and their batches, preserve the LPN sequence
(old codes are never reissued), preserve EF migration history, and execute in
a transaction. Labels printed before such cleanup are no longer valid.

A warehouse contains storage, transit, receiving, and shipping zones. Locations
form an arbitrary-depth tree within one active warehouse zone.

- Folder locations are structural and cannot participate in inventory
  operations.
- `Zone.Code` is unique within a warehouse. `StorageLocation.Code` is a stable
  numeric path unique within a zone; the displayed address is
  `{Zone.Code}-{StorageLocation.Code}`.
- Existing locations cannot be moved or renumbered. Activation follows the
  parent chain, and a node with active children cannot be deactivated.
- `PickSequence` is optional; duplicates use the location code as stable
  secondary ordering.
- Location barcodes are derived as
  `WMSL:{storage-location-guid-N}` rather than persisted.

An active operational location may have one active lock. A lock keeps the
location visible but excludes it from operational selection and final posting.
A locked location cannot be deactivated or converted to a folder. Administrators
own manual locks; an inventory-count draft owns its lock until posting or
explicit deletion. Lock release removes the active record; separate lock
history is not retained.

Inventory uses one canonical unit per SKU. Operational quantities are C#
`decimal` persisted as SQL Server `decimal(15,3)`, matching the current 1C
`Number(15,3)` boundary; unsupported scale or range is rejected, not rounded.
Physical dimensions remain `double`. Imported weight is kilograms per canonical
unit and volume is cubic meters per canonical unit. Invalid or unavailable
physical data is `null`, never numeric zero, and reports mark affected totals as
incomplete. Physical properties are current catalog values rather than
historical snapshots.

Dimensions use meters, maximum weight uses kilograms, and coordinates use the
warehouse-local meter system. Usable volume is
`Volume * (VolumeFactor ?? 1)`. Capacity values are stored but not enforced.
The current integration requires document quantity and package quantity to
match 1:1. Package keys and characteristics do not participate in SKU identity.

`InventoryBalance` is current quantity at a location. Posted
`InventoryMovement` records warehouse movement, and `InventoryTurnover` is its
immutable before/delta/after history. Draft picking and putaway movements do not
change inventory.

Posting validates active topology, folder status, locks, the workflow-specific
zone route, and physical balance in the caller's save. Posting and
eligibility-changing topology or lock updates advance
`StorageLocation.OperationalRevision`. Transfers additionally use transfer and
balance concurrency tokens plus named database constraints. Only recognized
inventory concurrency failures become business conflicts.

## Warehouse workflows

### LPN issuance (I01)

The canonical issued-number entity is `LicensePlateNumber`; `lpn` is an
acceptable local abbreviation. One stored `Code` is used for display, barcode
payload and the text printed below the barcode: `LPN` followed by exactly twelve
digits (`LPN000000000001` through `LPN999999999999`). It is an internal WMS code,
encoded in ordinary Code 128, with no GS1/SSCC claim.

SQL Server allocates codes on insert from the global, non-cycling
`LicensePlateNumberSequence`. A unique index guards codes; gaps are allowed.
There is no independently stored barcode value or second business number.
`LicensePlateNumberBatch` retains issuance time and Identity user id.

Operator and Administrator can issue 1–500 labels through Web `/lpn-labels`,
search the issued list, and preview/reprint one label or its entire batch.
Issuance uses `LicensePlateNumberService` and `CommandExecutor`: all numbers,
the batch and `lpn-label.issue-batch` receipt commit in one save. Replay returns
the original batch. Web retains quantity, request and user during an uncertain
attempt and blocks another issuance until explicit retry resolves it. This
recovery lasts only for the live component.

Authenticated print requests only read persisted codes. The standalone A4
portrait layout contains two columns by five rows, with SVG Code 128 and the
same human-readable code. Printing does not create new numbers. Physical
printer output and target-scanner reading still require developer verification.
I01 does not associate numbers with orders or change receiving, putaway or stock.

### Receiving and putaway

The local `ReceivingOrder.Status` is the WMS lifecycle: ready for receiving, in
receiving, or received. The source document status is consumed only from the
integration snapshot during import and reconciliation; it is not persisted as a
second aggregate status. A later outbound integration must map local transitions
at its adapter boundary rather than add a provider status to the aggregate. If
several consumers require acknowledgements, retries, or conflict tracking, that
per-consumer delivery state belongs to integration storage outside the aggregate.

Receiving participation is stored in `ReceivingOrderParticipant`, unique by
order and Identity user. Mobile receiving separates the current user's active
orders from joinable orders for the selected warehouse. Opening or scanning an
already joined order reads it; opening or scanning an available in-progress
order joins the user. The first participant must scan an active unlocked
Receiving location for the order warehouse. Starting the local lifecycle,
assigning that shared location, adding the participant, and the command receipt
commit together. Later participants reuse the location. Concurrent first entry
uses the order operational revision; a losing caller reloads and joins the
winning location on explicit retry.

Joining never writes to 1C. Deleted, unposted, unreadable, unsupported, or
duplicate-SKU source documents remain visible with a blocking reason and cannot
be joined. The I02 order view shows the source plan read-only and an empty LPN
screen. Direct facts, receiving completion, and order-based putaway are removed
from the Mobile API and disabled in Web until their LPN increments are available.

Receiving imports never discard active local work. WMS owns the receiving
location, facts, local state, comments, timestamps, and users. A nullable fact
means unchecked; zero is an explicit result. Mobile scans add one to a selected
line, while manual input sets an absolute nonnegative fact without overwriting
a Web comment. Every line requires a fact before completion.

Starting receiving and selecting its scanned receiving location form one save.
Completion first persists a fresh 1C synchronization checkpoint, then applies
the selected location, transition, and positive inventory receipt for the final
save. Putaway supports split draft movements from receiving to ordinary storage
and posts them together on completion. `ReceivingOrder.OperationalRevision`
protects reconciliation, facts, transitions, and draft movements from stale
saves.

Starting and completing receiving share the same application commands in WebApp
and Mobile. Command receipt lookup precedes mutable validation and 1C access;
successful replay returns the recorded resource id without executing the command
again. The synchronization checkpoint remains independently persisted, while the
final WMS effects and receipt commit atomically. An explicit completion location
selects that location; an omitted location uses the order's assigned location
after receipt lookup. Hashes describe the original input, never resolved DB state.

Web retains the request id and original receiving command together during the
Interactive Server component lifetime. Uncertain failures retain the attempt;
success and definitive rejection release it. Pending attempts do not survive
page reload or component recreation. Shared execution for receiving start and
completion is accepted. Fact increment, absolute quantity and line comment edits
also use the shared executor. Quantity changes in both clients preserve the
persisted comment; comment edits preserve the nullable fact. Facts/comments and
order revision commit with the receipt without 1C calls or inventory posting.
Original Mobile fact hashes remain compatible. Web snapshots each line edit for
explicit retry and blocks other edits, completion, location selection and
synchronization acknowledgement while an attempt is active or pending.

Putaway start, draft add/update/delete and completion use the same
PutawayCommandService and CommandExecutor in Web and Mobile. Receipt lookup
precedes mutable checks; draft changes and order revision save with the receipt.
Completion validates full allocation, routes, locks and current source stock,
then confirms/posts all drafts and completes the order atomically with its receipt.
No 1C call or synchronization checkpoint is involved. Web retains original
movement inputs and request/user ids for explicit retries, disabling other
mutations and editor selections while pending. Update/delete carry the expected
order id. A draft edit racing its deletion is a business conflict even when SQL
reports the missing movement before the order revision check.

### Picking and shipping

Picking starts against a scanned shipping location and creates split draft
movements from ordinary storage. Source availability is only a hint; Mobile
still requires the physical source scan. Completing picking reconciles the
fresh 1C plan, updates its item table, and posts drafts. Final shipping posts the
issue from the location already assigned to the order.

Starting picking, completing picking, and final shipping use the same public
application commands and CommandExecutor in WebApp and Mobile. Existing Mobile
receipt types and hashes are preserved. Receipt lookup precedes order/location
validation and external access; picking completion and shipment each persist
their synchronization checkpoint before final effects and receipt are saved
atomically. Successful replay does not repeat synchronization or target calls.
Web retains each pending transition's original input and request id for the
component lifetime, including uncertain retries. Local movement editing and
rollback are unavailable while a transition is in flight or pending recovery.
Picking draft add/update/delete use PickingCommandService and CommandExecutor
in both clients. API parses the source-location barcode before the command;
original Mobile hashes remain compatible. Movement, recalculated line fact and
order revision commit with the receipt without posting stock or calling 1C.
Web retains original source, quantity, expected order/movement and request/user
for explicit uncertain retry during component lifetime. Other edits, completion,
acknowledgement and rollback are blocked while pending. Concurrent deletion of an
originally unposted picking movement is a shipping conflict, including when SQL
reports the disappeared movement before the owning order's revision check.
Web rollback also uses ShippingOrderCommandService and CommandExecutor. The
original order/reason and request/user ids are retained for explicit retry without
reopening the reason dialog. Other mutations are blocked while choosing a reason
or awaiting a result. Receipt lookup precedes state validation; draft deletion,
current-cycle compensation, audit reset and the receipt commit together without
1C access or a synchronization checkpoint. Replay leaves later picking cycles
untouched. The hash preserves the original reason; the audit stores it trimmed.

A shortage requires explicit operator acknowledgement. An unfinished cycle may
be rolled back in WebApp: drafts are removed, posted work is offset by reverse
movements, local facts and shipping location are cleared, and turnover history
is preserved. The returned prepared order again follows an admissible 1C plan.
`ShippingOrder.OperationalRevision` protects reconciliation, location, state,
draft movements, and rollback from stale saves.

### Inventory count

WebApp and Mobile share InventoryCountCommandService and CommandExecutor for
start/resume, barcode increment, absolute quantity by item or SKU, unexpected
item removal, posting and draft deletion. Start returns an existing draft for
the requested location and warehouse; otherwise it creates one. Barcode resolution
and mutable checks run after receipt lookup. The original barcode and invariant
quantity hashes remain compatible with Mobile receipts. Changes, lock, inventory
effects and receipt commit atomically, without intermediate saves.
Web retains original inputs and request/user ids for explicit uncertain retries
within the component lifetime, disabling inputs and other mutations until the
attempt resolves. Page reload/component recreation does not retain attempts.

A count covers one ordinary storage location. Draft creation atomically locks
the location and snapshots positive balances as expected rows. Nullable counted
quantity means uncounted; zero is explicit. Scan adds one, and manual input may
set an absolute nonnegative quantity or add an unexpected SKU. Posting requires
every row, records only nonzero differences, and releases the lock. Leaving the
screen retains the draft; explicit deletion removes it and releases the lock.

### Intra-warehouse transfer

WebApp and Mobile call the same InventoryTransferCommandService methods through
CommandExecutor for creation, direct movement, pick, put and completion. Web
draft deletion also uses this boundary. All state, movements, balances, turnover
and the receipt commit together; these commands need no intermediate save.
Existing Mobile command identifiers and invariant input hashes remain compatible.
Web retains immutable inputs and the request id for an explicit retry after an
uncertain result, with other mutations and inputs disabled until resolution. This
recovery is limited to the current component lifetime.

Direct, pick-to-transit, and put-from-transit movements post immediately. One
transit location belongs exclusively to one active transfer and must be empty
before completion. Posted movements and completed transfers are immutable; an
unused draft may be deleted. Rejected or uncertain Mobile commands remain on
the operation screen for recovery.

Mobile transit transfers expose pick-to-transit and put-from-transit actions;
direct movement is offered only for transfers without a transit location.
Like WebApp, Mobile disables completion while the transit location has stock.
Before posting, Mobile can clear a selected transfer SKU and quantity and
return to scanning or search without losing the source location or transit
cart. Selection cannot be changed while posting or awaiting a repeat-safe
retry. A SKU with no available source stock does not advance to quantity entry.

Employee-performance reporting attributes receiving to `CompletedBy` and
picking to `ReadyForShipmentBy`; picking duration ends at ready-for-shipment.
Weight totals use current SKU data and identify incomplete results.

## 1C synchronization

Before warehouse work starts, an admissible source document remains owned by
1C. While WMS and 1C remain in matching initial states, synchronization replaces
source-owned metadata and the complete plan. Deletion marks, posting changes,
unexpected source status, malformed plans, and unsupported line semantics are
blocking. After work starts, synchronization never replaces the protected WMS
plan or warehouse fact. A successful shipping rollback returns the order to the
initial synchronization mode.

A document notification may create a missing source order. An explicit
synchronization check requires that the local order already exist.

Synchronization assessments are `Synchronized`,
`RequiresOperatorDecision`, or `Blocking`. WebApp may acknowledge only an
operator-decision assessment after a fresh fingerprint check; acknowledgement
copies source-owned metadata only. Mobile shows the latest assessment but
cannot acknowledge it.

Mobile receiving, picking, and shipping screens support pull-to-refresh for a
fresh synchronization check. A rejected completion conflict also refreshes the
assessment; unresolved or failed verification disables completion. Refresh
does not acknowledge source changes or replay pending commands.

Starting work rejects a known unresolved assessment. Receiving completion,
picking completion, and final shipping fetch and persist a fresh checkpoint
before local effects or outbound mutation. An exact source state, or the exact
repeat-safe target of the requested command, may continue. Technical failure,
an unacknowledged decision, or a blocking assessment stops the transition.
Repeated identical assessments do not advance the order revision.

Notification delivery uses an in-memory channel and can be lost on restart.
WMS-to-1C multi-step transitions are not atomic across both systems; pilot
operation therefore requires the recovery procedure tracked in the roadmap.
Shipping completion treats an already-applied exact 1C item-table target as
success, and its target status and posting calls are repeatable.

Receiving line comments are WMS-owned annotations, not warehouse facts. They
are sent to 1C with receiving item updates, but differences in these comments
do not affect synchronization assessments or fingerprints and never block work.

## Mobile boundary

Mobile is online-only. Urovo ScanWedge intent input and the ML Kit camera
fallback implement one scanner boundary; operational screens prefer the
embedded scanner when available. Manual SKU search, where present, uses name,
code, or barcode and rejoins the same operation as scanning. Decimal 1C GUID
barcodes accept and normalize leading zeroes.

The API address is packaged per build configuration and is not editable by the
operator.

Mobile text fields open the keyboard only on an explicit tap. Scanning,
opening search, and entering a quantity step never focus an input
automatically. Completing input or tapping outside the field (including
confirm/cancel actions) dismisses the keyboard.
Android fields suppress automatic keyboard display on native focus changes;
an explicit field click requests the keyboard separately.

Mobile clients and V1 contracts are grouped by business feature over one
authenticated transport. A feature-specific process may own a multi-call
sequence and retry ids, while pages retain UI state, scanning, navigation,
dialogs, and rendering.

Every changing Mobile command has a persisted receipt keyed by user, command
type, and client request id. Its deterministic request hash and result resource
id are saved atomically with the WMS change. Reusing an id with different input
is a conflict. After transport failure, `408`, `5xx`, or an unreadable successful
response, the client retains the same id and semantic retry target; a definitive
business/client `4xx` releases it. Stable Mobile error codes are
`invalid_command`, `resource_not_found`, `request_conflict`, and
`command_failed`.

Receipts are shared application persistence (`CommandReceipts`), also used by
Web receiving start/completion and line edits, putaway, picking movements,
shipping transitions and rollback, transfers and inventory counts. Mobile V1 retains the
`ClientRequestId` transport name. The receipt schema rename preserves existing
keys, command types, hashes,
and result ids, so previously completed Mobile attempts remain replayable.

## Automated testing

The project uses one xUnit project, `Wms.Tests`, with a minimal risk-based suite.
It does not pursue coverage targets or automatically add a test for every new
handler, command, or feature. New tests are reserved for critical business and
inventory invariants, material transactional guarantees, SQL-backed concurrency,
and serious regressions whose failure can damage persisted warehouse state.

Persistence, stock, receipt, and concurrency guarantees use disposable SQL
Server LocalDB integration tests. Generic `CommandExecutor` replay, hash,
receipt, rollback, and race behavior is tested centrally and is not duplicated
inside receiving, putaway, shipping, transfer, count, or LPN slices. Feature
work may require no new automated test when it introduces none of these risks.
Agents run at most one or two directly relevant tests while implementing a
change. They do not launch the complete suite without an explicit request and
instead leave the full manual verification command in the handoff.
