# Current receiving and LPN

This page records the implemented receiving transition. Remaining replacement
rules are specified in [the active feature](../../../specs/001-receiving-stock-lifecycle/spec.md).

## New source-based order foundation

The new Application use cases can list unlinked 1C receiving sources and create
one WMS receiving order from several already synchronized `Ref_Key` values.
Creation gives the order its own ID, fixes its warehouse, attaches each distinct
source once, and stores one row per known SKU. Repeated SKU lines and source
contributions are summed; an unresolved contribution leaves only its SKU plan
unknown. Unposted or deletion-marked sources contribute zero. The order fact
starts unchecked. The source versions and active links are checked again in the
save transaction; repeating the same complete source selection returns the
existing order. Web can now create the order from several unlinked sources.

Each successful later source check recomputes the linked WMS order's current
plan from all active source links in the same transaction as the source version.
An unposted, deletion-marked or different-warehouse source contributes zero;
unknown source metadata or quantity makes only affected known SKU plans unknown.
Former SKU rows remain with zero when their contribution disappears, and newly
known SKU rows are added. Repeated identical checks do not advance the order
revision. A failed check retains the last successful plan and records the
attempt error. Source changes do not rewrite the WMS warehouse, fact, LPNs,
movements or order status.

The new Application close use case refreshes every linked 1C source before
receipt. It uses the current version plan on success or the last numeric order
plan, marked unconfirmed, on failure. Closing a positive single-SKU LPN posts
one movement to its original receiving location, updates the checked order
fact, and saves audit and command receipt atomically. Plan excess or an unknown
plan requires `Manager`.
Concurrent changes to the plan, SKU expiration flag, source version, order or
LPN reject the staged close. A Web Manager or responsible Mobile participant can
close an open receiving LPN through this use case.
An open LPN may retain quantity while its date is still unset; closing a
positive LPN with the current SKU flag enabled requires the date. A date before
the UTC calendar day of closing requires explicit confirmation by the closing
user, without a higher role; today's date does not. The chosen date and any
confirmation are retained in the receipt movement and close audit.

The new Application putaway use case moves a positive part of a closed receiving
LPN from its original receiving location to a Storage location, even while the
order is still in receiving. It takes the SKU and expiration date from that LPN,
checks both its unplaced quantity and the physical source balance, and saves
the LPN-linked movement, putaway history, audit and command receipt together.
The LPN revision rejects a concurrent change to the same LPN.
Mobile can use this putaway while the order is still in receiving.

A `Manager` can correct the quantity of a closed receiving LPN before its first
putaway, including after order completion. The new quantity is nonnegative;
the SKU and expiration date stay fixed. The difference posts at the LPN's
original receiving location and updates its item and the order fact in the
same command as the movement, turnover, audit and receipt. Any recorded
putaway blocks the correction. Concurrent changes to the LPN or order reject
the command.

The new Application deletion preview lists a closed receiving LPN's own posted
receipt, quantity corrections and putaways in reverse order, with their original
addresses and dates. It forecasts every affected balance group and flags a
negative result. A `Manager` confirms that exact snapshot; changed LPN state,
movement history or balance versions require a fresh preview. Negative results
require explicit confirmation. Deletion adds one linked reversal per original
movement, retains independent stock operations, marks the LPN `Deleted`,
recalculates the order fact and commits audit and command receipt atomically.
With no other closed LPN for the SKU, the fact returns to checked zero only if
the SKU had an earlier explicit zero confirmation; otherwise it is unchecked.
In the Web order card, a Manager can correct a closed LPN only before its first
putaway. Deletion shows the historical reversal route and forecast balances,
requires explicit confirmation for a negative forecast, and refreshes the
forecast after a conflict. Deleted LPNs remain visible with their former
composition, putaways and linked reversal history.

The new SKU query searches either the order composition or the synchronized
catalog by name, code or barcode. Barcode resolution always uses the entire
catalog, including SKU outside the order. An `Operator` or `Manager` can add a
catalog SKU to an order; the unique row starts with known plan `0` and unchecked
fact. Repeating the command does not add a second row. The addition, command
receipt and compact audit event are saved together. A Web Manager can search
the catalog and add a SKU to the order; Mobile participants can select from
the order or catalog and always resolve a scanned barcode against the catalog.

The new cycle use case refreshes linked sources before joining and checks their
posting, deletion mark and warehouse. The first participant selects an active,
unlocked receiving location for the order warehouse; later participants join
the same cycle and location. A participant can bind an issued label to an open
receiving LPN and edit one SKU, quantity and optional expiration date while it
is open. The responsible user can cancel that LPN, including after editing;
a `Manager` can edit or cancel another user's open LPN. These drafts do not
post inventory. A current-cycle participant can confirm a SKU as checked zero
when no active LPN contains it; the audit and command receipt are saved with
the fact update. Mobile receiving now uses these scenarios.

The SKU expiration-tracking flag belongs to WMS. A `Manager` can switch it on
or off from the Web catalog; each actual switch records the previous and new
value, actor and time in warehouse audit history. Repeating the same command
does not add another switch. Catalog imports update only 1C-owned SKU fields
and preserve the flag. Switching it off does not change dates already saved on
LPNs, movements, turnovers or balances, and does not merge dated stock groups.

## LPN issuance

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
SVG Code 128 with the same human-readable code.

## Receiving LPN association

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

## Receiving LPN counting

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
The legacy close and reopen commands do not post inventory; the new close use
case above does. Neither completes the receiving order.
Mobile uses one SKU → absolute quantity confirmation cycle and repeat-safe
commands; Web displays per-LPN composition and employee contribution.

## Receiving and putaway

The local `ReceivingOrder.Status` is the WMS lifecycle: ready for receiving, in
receiving, or received. The source document status is consumed only from the
integration snapshot during import and reconciliation; it is not persisted as a
second aggregate status.

Receiving participation is stored in `ReceivingOrderParticipant`, unique by
order and Identity user. The Mobile queue lists WMS orders for the selected
warehouse; opening an available order allows the user to join its current cycle.
Opening or scanning an already joined order reads it. The first participant
selects an active unlocked Receiving location for the order warehouse by
scanning its barcode or searching the available locations by address or name.
Manual selection uses the same confirmation and server-side eligibility check as
scanning. Starting the local lifecycle,
assigning that shared location, adding the participant, and the command receipt
commit together. Later participants reuse the location. Concurrent first entry
uses the order operational revision; a losing caller reloads and joins the
winning location on explicit retry.

Joining never writes to 1C. The Mobile queue shows free 1C sources and WMS
orders for the selected warehouse. Scanning a free document selects it; scanning
a linked document opens its WMS order. Several selected sources can create one
order. The order screen shows current plan and fact, separate nomenclature and
LPN tabs, and an action to confirm an unchecked SKU as zero. A participant scans
an issued LPN, then scans a SKU against the entire catalog or searches manually
in the order or catalog. A SKU outside the order is added with plan zero before
the operator edits its absolute quantity. The LPN page shows retained items only;
cancelled LPNs remain auditable but are hidden from the operational list. Closing
a positive LPN posts receipt; a closed LPN cannot reopen and can be put away
without waiting for order completion. The old completion control and route are
absent; the replacement completion use case is pending.
Receiving imports never discard active local work. WMS owns the receiving
location, facts, local state, comments, timestamps, and users. A nullable fact
means unchecked; zero is an explicit result. Mobile changes facts only through
absolute quantities of LPN items; the aggregate fact is read-only. Every line
requires a fact before completion.

The legacy order-completion command no longer changes the order or posts stock.
Its previous fingerprint and quantity-difference decision are superseded by
the active receiving specification; the new completion use case is pending.

Independent putaway starts from a closed LPN either on its receiving-order page
or by scanning the LPN on the independent placement screen. Mobile uses
LPN → SKU → confirmed positive quantity → destination scan. The new Application
command posts one movement from the LPN's original Receiving location to an
active unlocked Storage location in the same warehouse. Partial putaway is
allowed. Remaining quantity is the current LPN quantity less its posted putaway
history, independent of later picking or other consumption from a destination.
The LPN screen shows its composition and putaway history; the quantity screen
shows recommendations and permits any valid Storage destination. Success returns
to the refreshed LPN screen. Pending or busy commands block backward navigation.

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

The new Mobile putaway command calls `PutAwayReceivingLpn`. Receiving
orders have no placement status or order-level putaway commands. A scanned
destination need not be recommended; the new command validates any selected
destination when posting.

The LPN quantity screen recommends active unlocked Storage locations in the
order warehouse. Locations with positive current stock of the selected SKU come
first and show that SKU's current quantity in its canonical unit, followed by
empty locations retaining a zero balance row for that SKU. Other empty locations
are omitted. Empty means no positive balance of any SKU, and a zero row for
another SKU does not make the location historical for the selected SKU. Within each
category, locations with `PickSequence` sort first by that value, followed by
locations without it, then by full address and location id. Recommendations are
only a snapshot hint; posting revalidates the warehouse, active Storage topology,
folder status and lock. Capacity and storage groups are not considered, no zero
balances are pre-created, and movement history is not queried for recommendations.

Draft LPN mutations do not post inventory or call 1C. The new close use case
uses the LPN's original receiving location and sets the order SKU fact from
closed LPNs; direct editing of the aggregate order fact is unavailable there.
Putaway has no 1C checkpoint. After a definitive destination rejection, Mobile
keeps the selected quantity and allows another destination scan.
