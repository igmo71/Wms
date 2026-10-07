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

Warehouse quantities retain at most three decimal places. Read-only Web
quantity tables show three places; compact TSD displays show up to three.
Each SKU quantity group shows its base unit once, with an unknown unit shown
explicitly. Quantity inputs retain their existing validation and compact
editing behavior. Counts of lines and pallets are integers, and weight is a
separate kg measure. Order progress does not total quantities across SKUs;
TSD receiving and shipping show progress by line count instead. TSD picking
requires discrepancy confirmation when any shipping line differs from its plan;
differences on separate SKUs cannot cancel each other.

This file owns current product boundaries and lasting business rules.
[`ARCHITECTURE.md`](ARCHITECTURE.md) owns engineering conventions,
[`ROADMAP.md`](ROADMAP.md) owns unfinished work, and frozen specifications keep
decision history. Code and migrations remain the implementation source of
truth.

During development the database may be cleaned or recreated when changing the
schema; migration compatibility is not a blocker for removing obsolete MVP
data. A full rebuild invalidates printed LPN labels; the cleanup scripts below
preserve the issued-code sequence. Production database changes are also
possible, but require a separate review of existing data and a more careful
rollout.

LPN receiving and initial putaway (I01–I08) were accepted on 2026-09-17.
The specification is frozen; status and future E1/E5/E6/E7 boundaries are in
[`DEVELOPMENT_PLAN.md`](DEVELOPMENT_PLAN.md), and the next task is in ROADMAP.
LPN tracks receiving provenance and initial putaway, not stored inventory.

Shared command, replay and client-recovery rules are defined once in
[`ARCHITECTURE.md`](ARCHITECTURE.md#persistence-and-integration).
HTTP timeout and caller-cancellation behavior follows the
[client recovery convention](ARCHITECTURE.md#client-organization).

Corrections from the I01–I08 review are recorded in the
[frozen safety specification](../specs/2026-09-18-lpn-receiving-safety/spec.md).

## Identity and access

WebApp uses ASP.NET Core Identity with three roles: `Operator` may run warehouse
operations and reports; `Manager` additionally corrects and reopens receiving
LPNs; `Administrator` also has Manager operational rights and manages configuration
and users. Public self-registration is not offered. Administrator-created accounts
are confirmed local accounts with one or more explicitly assigned WMS roles.
Rights combine across assigned roles. Receiving quantity-difference approval
requires explicit `Manager` membership even for an `Administrator`; assign both
roles when both permissions are needed. An administrator may change their other
roles but cannot block themselves or remove their own `Administrator` membership.
The last active administrator cannot be blocked or lose that membership, including
through concurrent user-management requests.

User creation and updates save memberships and account state atomically. A changed
WMS role set or block state also updates the security stamp in that transaction;
reordering the same roles does not invalidate sessions. Web cookies and connected
circuits retain their existing 30-minute stamp-validation interval. Mobile rejects
an old stamp on refresh; an issued access token remains valid until expiration.
Signing in again loads all current role claims. Instant session revocation is not
provided.

`ApplicationUser.DisplayName` is used for audit and reports, falling back to
`UserName`. Operational records retain the Identity user id, so renamed users
change historical display and deleted users display the stored id.

Startup initializes roles idempotently, assigns `Operator` to existing users
without a role, and creates a missing bootstrap administrator from
`IdentityBootstrap` configuration. Startup preserves additional roles explicitly
assigned to the bootstrap administrator; it does not automatically grant `Manager`.

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
match 1:1. The canonical unit comes from the SKU's base unit of measure:
for example, a SKU named "(500 шт)" can be counted as five `упак` when its
base unit is `упак`; the number in the name does not multiply its quantity.
Refilling into another pack size is represented by a different SKU. Package
keys and characteristics do not participate in SKU identity.

`InventoryBalance` is current quantity at a location. Posted
`InventoryMovement` records warehouse movement, and `InventoryTurnover` is its
immutable before/delta/after history. Draft picking movements do not change
inventory; LPN putaway posts each confirmed movement immediately. Zero balance
rows are retained and support historical-location suggestions.

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

Operator, Manager and Administrator can issue 1–500 labels through Web `/lpn-labels`,
search the issued list, and preview/reprint one label or its entire batch.
Issuance saves the batch, codes and receipt atomically; replay returns the
original batch. Authenticated printing reads persisted codes without allocating
numbers or changing stock. A4 portrait contains two columns by five rows of
SVG Code 128 with the same human-readable code. Physical printing and target
scanner reading were accepted with I01; changed equipment requires verification.

### Receiving LPN association (I03)

An issued `LicensePlateNumber` starts as `Available`. A participant scans its
code inside an in-progress receiving order to bind it permanently to that order,
the creating Identity user, the responsible user, and the creation time. The
same code cannot move to another order. `Open`, `Closed`, and `Cancelled` are
persisted LPN states; association and cancellation retain their audit separately
from counting and closing.

One employee may have several open LPNs in an order. The aggregate operational
revision coordinates parallel creation, and SQL constraints independently guard
the label race. Scanning an existing LPN of the same order opens its server
state, including another employee's read-only LPN.
Only the responsible employee can cancel their empty open LPN. Cancellation
never makes the cancelled code available.
Every mutation and its command receipt commit together.

### Receiving LPN counting (I04)

An open LPN contains at most one retained row per order SKU. Quantity entry sets
an absolute nonnegative canonical-unit quantity; zero is an explicit counted
value, while removal marks the row deleted for audit and excludes it from the
composition. Receiving-order fact is the aggregate of non-deleted rows across
its non-cancelled LPNs. If a SKU has no such row its fact remains unchecked;
concurrent LPN changes are coordinated by the receiving-order revision.

The responsible employee may edit and close their open LPN, including an empty
one. A Manager or Administrator may edit or close any open LPN and may reopen a
closed LPN without closing the employee's other LPNs. An Operator cannot reopen
a closed LPN, including their own. Creation,
last update/removal, closing and reopening retain user and UTC time audit.
Closing and reopening do not post inventory or complete the receiving order.
Mobile uses one SKU → absolute quantity confirmation cycle and repeat-safe
commands; Web displays per-LPN composition and employee contribution.

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
be joined. The order view shows the source plan read-only and its LPN list.
Participation uses the source synchronization assessment. Completion checks
persist that source assessment separately from their returned completion
decision: unchecked facts and partial counts do not block joining. Open LPNs,
unchecked lines and plan/fact differences still restrict completion. Mobile
join availability uses the source assessment even when the same response shows
a blocking completion decision; a failed fresh source check also disables joining.
Direct facts and order-based putaway are removed from the Mobile API. Receiving
completion with a fresh exact plan/fact match is available to a participant in
Mobile. Quantity-difference approval and its reason remain a Manager-only Web
action. The Mobile completion action remains visible while the order is in
receiving. One compact yellow instruction below the order header shows the
first obstacle: a failed or blocking fresh source check, open pallets (count),
unchecked lines (count), then quantity differences requiring correction or a
manager. There is no second completion hint above the button. The panel and
completion availability use the same eligibility decision; the panel disappears
when completion is available. Busy and pending-command guards still apply.
The Mobile response exposes the persisted source synchronization level separately
from the completion assessment, so unfinished counting is not shown as a 1C error.

The Mobile order page has separate nomenclature and pallet tabs. An LPN scan is
accepted from either tab and opens its separate pallet page; an SKU scan is
accepted only on that pallet page and opens a separate absolute-quantity page.
An editable open pallet also offers manual search and selection of any planned
order SKU by name or code, including one not yet counted on that pallet. The
manual selection opens the same quantity page without creating a pallet row;
the pallet's main list continues to show only its retained item rows. Plan choices
and retained rows have neutral headings; plan cards are light gray and retained
rows white. Manual plan choices have a green border only when the order-wide
checked fact exactly matches the SKU plan across all active pallets; unchecked
and excess quantities do not receive this border. A successful quantity
save closes the manual selector on return to the pallet page; failed or cancelled
edits leave it open.
No quantity field is focused by navigation or scanning. The order SKU table is
read-only: unchecked fact is a red question mark, a mismatch is highlighted
yellow, and an exact match green. Tapping an order SKU opens its aggregate plan
and fact plus only the LPNs that contain that SKU, with quantity per LPN. An LPN
page lists only its retained item rows, not every planned order SKU. Pallet
actions remain visible below the scrolling composition; closing always returns
to the order's nomenclature tab, and an empty pallet uses a compact cancel
action. Cancelled LPNs are hidden from operational Mobile lists but remain
permanently used and auditable.
Mobile SKU names wrap and are never ellipsized.

Receiving imports never discard active local work. WMS owns the receiving
location, facts, local state, comments, timestamps, and users. A nullable fact
means unchecked; zero is an explicit result. Mobile changes facts only through
absolute quantities of LPN items; the aggregate fact is read-only. Every line
requires a fact before completion.

Ordinary exact completion is available to an order participant only after every active LPN is
closed and every source SKU has an explicit aggregate LPN fact, including zero.
It first persists a fresh 1C synchronization checkpoint. Source deletion,
unposting, unsupported quantity semantics, duplicate SKU, or a changed/ambiguous
line-to-SKU composition blocks completion. A quantity-only plan/fact difference
requires a reasoned server-side decision by the `Manager` role specifically;
`Administrator` does not receive that permission implicitly.

A Manager decision on a quantity difference does not require joining the order
as a receiving participant. It does not create participation or attribute LPN
counting to that Manager. The Manager supplies a reason and immediately finalizes
the order with atomic receipt posting and decision audit. All active LPNs must
still be closed, every line checked, and the source and LPN fingerprint fresh.
The participation requirement above continues to apply to ordinary exact
completion, including when the caller is a Manager.

The completion decision fingerprint covers both the fresh source snapshot and
the complete local LPN composition. A source or LPN change after display rejects
the command and requires a new decision. The original plan, current 1C plan,
fact, differences, source check, and optional Manager decision are retained on
the order as its immutable completion audit. Final transition, positive receipt
movements, balances, turnover, audit, and command receipt save atomically. A
successful replay reads the receipt before 1C and cannot receive stock twice.
Completion makes no outbound change to 1C; later notifications do not replace
the accepted facts or completion audit.

Independent putaway starts from a closed LPN rather than an order. Mobile uses
LPN → SKU → confirmed quantity → destination scan; scanning the destination immediately
posts one movement from the order's Receiving location to an active unlocked
Storage location in the same warehouse. Partial putaway is allowed. The
remaining quantity is the immutable accepted LPN-item quantity minus its posted
LPN putaway history, so later picking or other consumption from a destination
does not change receiving history or make quantity pending again.

The Mobile putaway screen keeps the navigation title and uses the LPN code as
the content heading, with a checkmark for completed, unblocked pallets. It omits
the order number and routine step explanations, while retaining blocking reasons
and quantity-confirmation feedback. Composition and history have no outer cards;
SKU code and unit share a row, and quantities use compact labelled columns.
Composition cards with zero remaining quantity have a light green background.
History shows the SKU, destination with quantity and unit, then employee name and
timestamp. Employee names follow the shared Identity display-name fallback rule;
stored audit user ids remain unchanged.

Each SKU selection starts with an empty, unconfirmed quantity and no automatic
input focus. Finishing input (Done or leaving the field) or explicitly choosing
"Entire remainder" confirms a positive supported quantity within the displayed
remainder. Editing or focusing the field revokes confirmation. Quantity and
recommendations remain on the same screen; there is no advance-to-scan button.
A focused, invalid or unconfirmed amount cannot post a destination scan, and
queued scans from before confirmation are discarded. Busy or uncertain commands
block changes and new scans; explicit retry follows the shared recovery convention.
After conflict refresh, an insufficient remainder revokes confirmation; an
unavailable SKU or LPN returns to the current selection/status. Success resets
quantity confirmation before the next SKU selection.

Putaway is blocked until the receiving order is completed. Each command checks
both the physical source balance and the separate remaining limit of its LPN
item. The LPN operational revision coordinates concurrent employees, while
balance row versions independently protect shared physical stock. Movement,
balances, turnover, LPN history and command receipt commit atomically. Receiving
orders have no placement status or order-level putaway commands. I06 does
not restrict the destination to a recommendation; any scanned destination is
validated at posting time.

The LPN quantity screen recommends active unlocked Storage locations in the
order warehouse. Locations with positive current stock of the selected SKU come
first, followed by empty locations retaining a zero balance row for that SKU,
then other empty locations. Empty means no positive balance of any SKU, and a
zero row for another SKU does not make the location historical for the selected
SKU. Each location appears in only its first matching category. Within each
category, locations with `PickSequence` sort first by that value, followed by
locations without it, then by full address and location id. Recommendations are
only a snapshot hint; posting revalidates the warehouse, active Storage topology,
folder status and lock. Capacity and storage groups are not considered, no zero
balances are pre-created, and movement history is not queried for recommendations.

Completion uses an explicitly supplied receiving location or the order's assigned
location after receipt lookup. Supported UI/API paths change facts only through
LPN items; order-line facts are recalculated from active LPN items and cannot be
edited directly. LPN mutations save audit,
aggregate fact and revisions without posting inventory or calling 1C.
Putaway has no 1C checkpoint. After a definitive destination rejection, Mobile
keeps the selected quantity and allows another destination scan.

### Picking and shipping

Picking starts against a scanned shipping location and creates split draft
movements from ordinary storage. Source availability is only a hint; Mobile
still requires the physical source scan. Completing picking reconciles the
fresh 1C plan, updates its item table, and posts drafts. Final shipping posts the
issue from the location already assigned to the order.

Picking draft edits save the movement, recalculated line fact and order revision
without posting stock or calling 1C. Concurrent deletion of an originally
unposted movement is a shipping conflict, even when SQL detects the missing
movement before the order revision. Shipping rollback saves draft deletion,
current-cycle compensation, audit reset and receipt together without 1C;
replay leaves later cycles untouched. Its hash retains the original reason,
while audit stores it trimmed. Web retains the chosen reason for uncertain retry
and blocks mutations while choosing it or awaiting the outcome.

A shortage requires explicit operator acknowledgement. An unfinished cycle may
be rolled back in WebApp: drafts are removed, posted work is offset by reverse
movements, local facts and shipping location are cleared, and turnover history
is preserved. The returned prepared order again follows an admissible 1C plan.
`ShippingOrder.OperationalRevision` protects reconciliation, location, state,
draft movements, and rollback from stale saves.

### Inventory count

Start/resume returns an existing draft for the location and warehouse, or creates
one. Barcode resolution and mutable checks follow receipt lookup; count changes,
lock, inventory effects and receipt save together without intermediate saves.

A count covers one ordinary storage location. Draft creation atomically locks
the location and snapshots positive balances as expected rows. Nullable counted
quantity means uncounted; zero is explicit. Scan adds one, and manual input may
set an absolute nonnegative quantity or add an unexpected SKU. Posting requires
every row, records only nonzero differences, and releases the lock. Leaving the
screen retains the draft; explicit deletion removes it and releases the lock.

### Intra-warehouse transfer

Transfer creation, movements, completion and Web draft deletion share application
commands. State, movements, balances, turnover and receipt save together without
intermediate saves or 1C calls.

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

Employee-performance reporting exposes receiving contribution in the
receiving-order LPN audit rather than the aggregate report. Picking remains attributed to
`ReadyForShipmentBy`, and its duration ends at ready-for-shipment. Weight totals
use current SKU data and identify incomplete results.

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

Mobile receiving omits the technical changed-field list from synchronization
messages, retaining verification errors, blocking guidance and required actions.

Starting work rejects a known unresolved assessment. Receiving completion,
picking completion, and final shipping fetch and persist a fresh checkpoint
before final effects. Receiving applies the LPN plan/fact and Manager-decision
rules above and never writes to 1C. Shipping may continue on an exact source
state or the exact repeat-safe outbound target; an unacknowledged decision
stops that transition. Technical failure or a blocking assessment stops both.
Repeated identical assessments do not advance the order revision.

Notification delivery uses an in-memory channel and can be lost on restart.
WMS-to-1C multi-step transitions are not atomic across both systems; pilot
operation therefore requires the recovery procedure tracked in the roadmap.
Shipping completion treats an already-applied exact 1C item-table target as
success, and its target status and posting calls are repeatable.

Historical receiving line comments are retained as WMS-owned annotations, not
warehouse facts. Current receiving has no direct line-comment command and does
not send status, item updates or comments to 1C. Comment differences
do not affect synchronization assessments or fingerprints and never block work.

## Mobile boundary

Mobile is online-only. Urovo ScanWedge intent input and the ML Kit camera
fallback implement one scanner boundary; operational screens prefer the
embedded scanner when available. Manual SKU search, where present, uses name,
code, or barcode and rejoins the same operation as scanning. Decimal 1C GUID
barcodes accept and normalize leading zeroes.

The Urovo ScanWedge profile must emit barcode data through its broadcast Intent
only. Simultaneous keyboard-wedge output is disabled because its trailing Enter
can activate a focused UI action after the Intent scan has already navigated.
Scanner-driven pages suppress Android focus on action buttons. Receiving order,
LPN and LPN putaway pages ignore queued scan callbacks from a previous period of
page activity, including after leaving and returning to the same live page.
The Mobile receiving queue API and screen show only the current user's orders;
other orders can be opened by scanning their document barcode. Independent
putaway starts from its own LPN screen.

Web uses one receiving-order page across ready, receiving and received states.
The list's "Сверка" indicator prioritizes an accepted quantity difference over
the successful source-synchronization mark; its tooltip distinguishes the two.
The route chooses between in-progress and ready/received views; successful Web
completion changes the view on the same URL. Its shared header shows status
and factual weight in the top row below the order number and date; participants
appear beside the receiving-operation audit and stack when there are several.
The shared "Расхождения" block appears above the order content; Manager
confirmation is a separate block in the same position for entry and later
review. The order header and list omit the obsolete order-level placement
status and timestamps. The page uses the
same read-only order-content layout while receiving and after completion. The
nomenclature tab shows aggregate order lines. A ready order shows its stored
"План" only. During and after receiving, equal WMS and 1C quantities on every
line collapse to one "План" column; otherwise the whole table shows "План WMS"
and "План 1С". A missing compared 1C value is not treated as equal. After
acceptance, "План 1С" uses the saved completion snapshot. The Web nomenclature
table shows each SKU's canonical unit alongside its quantities; Mobile already
shows the unit beside line plan and fact. Read-only Web receiving quantities
(plan, fact, remainder and pallet quantities) display three decimal places,
matching the 1C quantity precision; nullable unchecked facts retain their
textual state. Clicking a Web nomenclature name opens a compact menu next to
that row with the active pallets containing the SKU and each pallet's retained
quantity. Clicking a pallet in that menu switches to the pallet tab with that
pallet selected. Cancelled pallets and deleted pallet items are excluded. The
pallet tab shows a compact
selectable pallet list on the left and only the selected
pallet's item audit and putaway history on the right. Open pallets sort first,
and their nonzero count appears on the tab badge. Cancelled pallets remain
visible but muted for audit. After receiving completion, the pallet list shows
derived putaway progress and a badge counting retained item rows that are not
fully placed. Each retained pallet item shows placed and remaining quantity
from its LPN putaway history. History names the destination cell and keeps
technical movement IDs in the stored audit rather than presenting them as
nonfunctional links. Quantities of different SKUs are not summed across units.
Completion and 1C reconciliation
remain above the tabs; Web LPN composition editing is outside this result.
When Web completion is unavailable, a concise reason between the order title
and the action points to the next step; Mobile already shows completion
guidance below its order header as described in the receiving workflow above.

The API address is packaged per build configuration and is not editable by the
operator.

Mobile text fields open the keyboard only on an explicit tap. Scanning,
opening search, and entering a quantity step never focus an input
automatically. Completing input or tapping outside the field (including
confirm/cancel actions) dismisses the keyboard.
Android fields suppress automatic keyboard display on native focus changes;
an explicit field click requests the keyboard separately.

Client organization, receipt compatibility, error codes and retry rules belong
in [ARCHITECTURE.md](ARCHITECTURE.md#client-organization).

## Automated testing

The project uses one xUnit project, `Wms.Tests`, with only the minimum tests
needed to protect critical warehouse state. Routine changes default to no new
test; a new test requires a concrete material risk not already protected.
The developer runs builds and tests; agents run them only on explicit request.
[`ARCHITECTURE.md`](ARCHITECTURE.md#testing-strategy) owns the testing policy,
including selection and stopping rules. [`Wms.Tests/README.md`](../Wms.Tests/README.md)
contains local setup and execution commands.
