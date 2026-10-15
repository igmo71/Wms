# Current receiving and LPN

These are implemented legacy rules. The replacement is specified in [the active feature](../../../specs/001-receiving-stock-lifecycle/spec.md).

## New source-based order foundation

The new Application use cases can list unlinked 1C receiving sources and create
one WMS receiving order from several already synchronized `Ref_Key` values.
Creation gives the order its own ID, fixes its warehouse, attaches each distinct
source once, and stores one row per known SKU. Repeated SKU lines and source
contributions are summed; an unresolved contribution leaves only its SKU plan
unknown. Unposted or deletion-marked sources contribute zero. The order fact
starts unchecked. The source versions and active links are checked again in the
save transaction; repeating the same complete source selection returns the
existing order. The replacement UI and receiving posting path are not connected
yet.

The new SKU query searches either the order composition or the synchronized
catalog by name, code or barcode. Barcode resolution always uses the entire
catalog, including SKU outside the order. An `Operator` or `Manager` can add a
catalog SKU to an order; the unique row starts with known plan `0` and unchecked
fact. Repeating the command does not add a second row. The addition, command
receipt and compact audit event are saved together.

The new cycle use case refreshes linked sources before joining and checks their
posting, deletion mark and warehouse. The first participant selects an active,
unlocked receiving location for the order warehouse; later participants join
the same cycle and location. A participant can bind an issued label to an open
receiving LPN and edit one SKU, quantity and optional expiration date while it
is open. The responsible user can cancel that LPN, including after editing;
a `Manager` can edit or cancel another user's open LPN. These drafts do not
post inventory. A current-cycle participant can confirm a SKU as checked zero
when no active LPN contains it; the audit and command receipt are saved with
the fact update. The legacy receiving UI is still active until replacement.

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
Closing and reopening do not post inventory or complete the receiving order.
Mobile uses one SKU → absolute quantity confirmation cycle and repeat-safe
commands; Web displays per-LPN composition and employee contribution.

## Receiving and putaway

The local `ReceivingOrder.Status` is the WMS lifecycle: ready for receiving, in
receiving, or received. The source document status is consumed only from the
integration snapshot during import and reconciliation; it is not persisted as a
second aggregate status.

Receiving participation is stored in `ReceivingOrderParticipant`, unique by
order and Identity user. The Mobile queue lists the current user's active orders
for the selected warehouse; scanning an available order can join the user.
Opening or scanning an already joined order reads it. The first participant
selects an active unlocked Receiving location for the order warehouse by
scanning its barcode or searching the available locations by address or name.
Manual selection uses the same confirmation and server-side eligibility check as
scanning. Starting the local lifecycle,
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
and fact plus only the LPNs that contain that SKU, with quantity per LPN. The order
list initially hides exact-match green SKU cards; its fixed bottom area
shows how many are hidden and lets the operator show or hide them. White unchecked
and yellow mismatch cards remain visible, and the choice survives order refresh
and return from a pallet page. An LPN page lists only its retained item rows,
not every planned order SKU. Pallet
actions remain visible below the scrolling composition; closing always returns
to the order's nomenclature tab, and an empty pallet uses a compact cancel
action. Cancelled LPNs are hidden from operational Mobile lists but remain
permanently used and auditable.
Receiving imports never discard active local work. WMS owns the receiving
location, facts, local state, comments, timestamps, and users. A nullable fact
means unchecked; zero is an explicit result. Mobile changes facts only through
absolute quantities of LPN items; the aggregate fact is read-only. Every line
requires a fact before completion.

Ordinary exact completion is available to an order participant only after every
active LPN is closed and every source SKU has an explicit aggregate LPN fact,
including zero.
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

Mobile putaway uses three pages: pallet scanning, pallet composition/history,
and quantity with storage-cell recommendations. Native `NavigationPage` back
navigation returns through those pages. The composition page uses the LPN code as
its navigation title, with a checkmark for completed, unblocked pallets. It omits
the order number and routine step explanations, while retaining blocking reasons
and quantity-confirmation feedback. Composition and history have no outer cards;
SKU code and unit share a row, and quantities use compact labelled columns.
Composition cards are uncolored before putaway, light yellow when partially
placed, and light green when remaining quantity reaches zero.
When putaway is unavailable, its history appears if any movements exist. Each
entry shows the SKU, destination with quantity and unit, then employee name and
timestamp. Employee names follow the shared Identity display-name fallback rule;
stored audit user ids remain unchanged.
Putaway uses "палета" for the physical unit and "ячейка" for a storage
destination in operator text; the printed LPN code is unchanged. Pending or
busy commands block backward navigation. A successful movement returns from
quantity entry to the pallet composition, refreshed from server state.
The remaining and confirmed quantities share a row; "Entire remainder" shows
the remainder beside the quantity field. After confirmation the screen asks
for a storage-cell scan. "Next pallet" appears on the composition page when
further putaway is unavailable. It and repeat-safe retry on the quantity page
after an uncertain posting use fixed bottom action areas;
errors and progress occupy those areas only when present.

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
orders have no placement status or order-level putaway commands. A scanned
destination need not be recommended; any scanned destination is
validated at posting time.

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

Completion uses an explicitly supplied receiving location or the order's assigned
location after receipt lookup. Supported UI/API paths change facts only through
LPN items; order-line facts are recalculated from active LPN items and cannot be
edited directly. LPN mutations save audit,
aggregate fact and revisions without posting inventory or calling 1C.
Putaway has no 1C checkpoint. After a definitive destination rejection, Mobile
keeps the selected quantity and allows another destination scan.
