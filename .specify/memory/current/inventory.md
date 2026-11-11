# Current storage and inventory

## Storage and inventory

A warehouse contains pickable storage (`Storage`, displayed as `Комплектация`),
reserve (`Reserve`, displayed as `Хранение`), transit, receiving, and shipping
zone types. New receiving putaway can post into either Storage or Reserve.
Older picking, transfer, and count flows still use their former Storage-only
rules and are not ready for reserve stock.

Locations form an arbitrary-depth physical tree within one warehouse. A folder
may have no functional zone; an operational location belongs to one active zone
of its warehouse.

- Folder locations are structural and cannot participate in inventory
  operations.
- `Zone.Code` is unique within a warehouse. `StorageLocation.Code` is the stable
  compact address, unique within the warehouse, independent of zone and parent.
  Manual addresses accept Latin letters and digits, with common visual
  separators ignored. This distribution center uses room, rack row, section,
  cell and level in that order, for example `1S02C3`. The physical tree groups
  by room, row, section, level, then cell; level and cell have reversed order
  relative to the address. Other warehouses may use a different tree.
- Existing locations cannot be moved or readdressed. Activation follows the
  parent chain, and a node with active children cannot be deactivated.
- `PickSequence` is optional; duplicates use the location code as stable
  secondary ordering.
- A location barcode carries the compact address, looked up within the selected
  warehouse. Printed Code 39 labels encode that value; scanner input must not
  add an unknown prefix or suffix.
- Administrators can preview and confirm topology from one bounded XLSX export
  containing both topology and source balances. This stage creates no inventory.
  It includes accepted empty cells, derives functional zones from physical area
  and cell kind, shares room-row-section-level folders between picking and
  reserve, and places gate cells directly under a `Ворота` root. Repeated source
  addresses create one cell; matching existing cells are reused. A changed
  preview, conflicting parent, zone, activation or SKU-mixing permission blocks
  confirmation. The preview shows excluded and corrected rows, including the
  excluded `0A00A0`, `0A00A1` and virtual addresses, the empty mismatched-room
  `4A10A1`, and the one `9Z99A1` correction to Shipping. Stock at `4A10A1`
  blocks confirmation until its disposition is decided.
- Administrators can generate up to 1,000 five-segment location addresses into
  a selected active folder. The preview expands room, row, section, cell, and
  level ranges, checks warehouse-wide code conflicts, and blocks creation when
  any imported folder in the physical path disagrees with the generated address.
  Range generation under the special `Ворота` root or its descendants is blocked.
  Creation saves the accepted batch atomically and does not make nested folders.
- The A4 location-label preview prints active operational addresses above a
  Code 39 barcode carrying the compact address. Five-segment addresses use
  middle-dot separators in the printed text; other addresses print as stored.
  One label or up to 120 direct child locations of a folder can be printed; labels
  are approximately 40 mm high. Exact adhesive-stock dimensions remain open.

## Stock availability

The Application stock-availability query distinguishes accounting quantity,
active picking reservations, disputed shortage quantity and whole-location
holds for each warehouse, cell, SKU and nullable expiration date. A negative
available result is shown as zero. A hold excludes the whole cell from new
work. Its owning picking assembly can continue selecting its own stock groups,
with confirmed quantities still subtracted as reservations.

Issuing a picking assembly's reserved quantity uses the accounting balance
after protecting other reservations and disputed quantity. A fully reserved
group can therefore have zero available for new work while covering the owning
assembly's issue. The reservation store changes reservations and holds inside
the command session under an update lock on the existing cell. Physical scans,
FEFO and assembly authorization belong to their operational use cases.

The shared posting handler supports a dated picking issue against the exact
reservation of a picking entry, a Reserve-to-Storage replenishment under its
own source hold, and stock corrections. A picking issue completes its reservation
in the movement's commit. Replenishment retains the source hold after posting.
Corrections preserve picked stock and can consume the disputed quantity of the
referenced shortage. The Manager use case resolves that report in the same
session. Receiving putaway cannot occupy a cell held by picking or replenishment.

The replacement picking-route builder uses the shared availability view. It
orders known expiration dates before unknown dates, excludes expired groups,
and can recommend a full one-SKU Reserve cell before Storage top-up. It reports
unmet demand separately. New picking application operations read a scanned
Storage or Reserve cell and show its actual SKU, date group, accounting balance,
available balance, and suggested quantity. A null date prompts a visual expiry
check only for a SKU that tracks expiration; the fact retains null. Confirming
a pick reserves its exact group and records editable fact without moving stock.
Reducing a fact requires a scan of its original cell and confirmation that the
released quantity is physically there; the matching reservation is reduced
in the same command. Reporting a physical shortage is a separate command:
the found quantity is reserved once, while the disputed free quantity is
blocked by a dated shortage record. A Manager either posts a stock correction
for that quantity or releases a mistaken block, preserving the report and
decision. A direct Reserve pick first holds the whole one-SKU cell; each
confirmed date group must be taken in full. The
hold can be released after a confirming scan only when no positive fact remains
from that cell. Replacement Mobile screens use these operations; the main
home-screen button still opens the legacy picking and shipping flow until the
final workflow switch.

The replacement replenishment proposal calculator uses the remaining assembly
demand, suggested picking steps, shared stock availability and existing tasks.
It recommends all currently usable Reserve sources in expiration order, counts
unselected Storage stock before proposing extra transfers, and reports any
remaining warehouse deficit. An ordered task consumes its suggested source
quantity in the recommendation; a physically held cell is unavailable. A
Storage destination with the SKU is preferred, followed by an empty cell with
history for that SKU. This read-only calculation creates no replenishment task;
ordering and physical scans require their own checks. An Operator responsible
for an active assembly can explicitly order one currently offered proposal.
The command rechecks the route, stock, source, destination and existing tasks
in one transaction, then saves the task, assembly revision, audit and command
receipt together. Repeating its request ID returns the same task. No stock
movement or physical source hold occurs when the task is ordered.
The replenisher's active-task query lists ordered and in-progress tasks for
one warehouse. It reads the current queue of every linked 1C document on each
request and uses the highest priority in a multi-document assembly: live queue,
collect by date, own delivery, then unspecified. Equal priorities sort by the
original order time and task ID. The query does not change a started task's
assignee or order time.
Starting a replenishment source requires an Operator's source-cell scan. The
command checks an active Reserve cell in the task's warehouse, available SKU
stock after group-specific shortages, expiration order and other reservations before
holding the whole cell for the task. The actual scanned cell replaces its
recommendation. Other ordered tasks keep their original order time and receive
new source recommendations from remaining stock; a task without a free source
shows no recommendation. Task start, cell hold, recommendation changes, audit
and receipt commit together. A later source cycle can start only after the
previous pallet has been completed.
Confirming a replenishment transfer checks the scanned Storage destination,
its active hold and SKU mixing, the active source pallet held by this task,
and the earliest available expiration group on that pallet. One confirmed
quantity moves one SKU and one expiration group from Reserve to Storage through
the common inventory posting operation. The source hold remains until its
separate completion scan. The movement, both balances and turnovers, task
progress, transfer record, audit and command receipt are saved together;
repeating the request ID does not move stock again.
An Operator can report a shortage in the held source's SKU and expiration
group. The disputed quantity is blocked separately from the whole-cell hold;
the found quantity remains available for a dated transfer. A source cycle ends
only after scanning that same cell and confirming either the physical return
of its remaining pallet or an empty cell. Empty-cell confirmation checks all
SKU and expiration groups, excluding separately blocked disputed quantities.
The command releases the whole-cell hold but keeps shortage blocks. If the
ordered quantity remains incomplete, the same task awaits another source;
its prior transfers and assigned replenisher remain recorded.
The Manager can correct a posted replenishment step by entering its actual
transferred quantity and a reason. The original step remains immutable; a
separate dated stock-correction movement, recorded against that step, transfers
the difference between its original source and destination. The shared posting
checks current balances, reservations, shortage blocks, cell holds and SKU
mixing. Task progress reflects the corrected total in the same command. The
assembly audit retains the reason, old and new quantities and movement ID.

Closing the replacement picking assembly is the sole outbound stock posting
point. It rechecks each positive entry against its own active reservation and
protects other reservations and shortage blocks before issuing from the
original cell and date group. All movements, signed turnovers, balances,
allocation rows, closure result, released pallet holds, audit and command
receipt commit together. A zero fact creates no stock movements. There is no
intermediate Shipping-zone balance.
Closing cancels replenishment orders that have not scanned a source. Started
tasks continue independently of the closed picking fact and retain their
transfers in the destination cell.

A Manager can request cancellation of an unclosed picking assembly only after
every picked quantity has been returned through its original-cell scan, all
active picking reservations are gone, and any direct-pick pallet hold has been
released after its return scan. Cancellation posts no stock movement. Ordered
replenishments are cancelled in the same command; completed transfers remain
in their destination cells. An already started replenishment keeps the assembly
in `CancellationPending` until that task finishes. An unresolved physical
shortage remains a separate blocked stock group for Manager review.
When the last started task finishes and no linked 1C source remains, the same
source-completion command finishes the pending assembly cancellation.

## Opening stock cutover

The Administrator confirms opening stock separately from topology on the Web
cutover page, using the same XLSX file after SKU catalog synchronization. The
preview requires the topology-stage source SHA-256, a declared snapshot time,
active target cells with matching functional purposes, known SKU GUIDs, and
matching canonical units. It blocks operational stock or work, unresolved mixed
cells, unsupported quantities, and changed warehouse state. Missing SKUs and
cells are not created during stock confirmation. Only one opening snapshot can
be confirmed per warehouse. An exact retry with the original command key returns
the recorded result; a new request for the same or another workbook conflicts.
Another rehearsal starts with a cleaned database.

Accepted source rows with nonzero quantities produce one posted movement of
positive magnitude and one signed turnover per row. A positive source quantity
has only a destination; a negative quantity has only a source. Shipping stock
may remain negative. Balances accumulate by warehouse, cell, SKU and nullable
expiration date, so repeated source rows share one balance while retaining
separate movements and turnovers. A supplied past date remains as supplied;
an absent required date remains unknown. An accepted source `Да` enables the
SKU's local expiration requirement without disabling any existing requirement.
Accepted zero rows make no movement. The import status, declared time, source
summary, stock effects, SKU flags, audit and command receipt commit together;
failed confirmation leaves the topology-only stage intact.

Exactly these reviewed cells permit several SKUs: `1C09A1`, `1G09A1`,
`1K09A1`, `1M09A1`, `1O09A1`, `1Q09A1`, `1U11A1`, `2O01A1`, `2O02A1`,
`2O03A1`, `2O04A1`, `2O05A1`, `2O06A1`, `2O07A1`, `2O08A1`. These eight
reviewed cells retain their mixed opening stock with mixing disabled:
`1H05A7`, `1K03A5`, `1O08C6`, `1R04A5`, `1R05B1`, `1T03C3`, `2A02F2`,
`3E07B1`. Their addresses are recorded in the import summary. The Web page
applies this reviewed mixing policy to Storage and Reserve; Shipping and
Receiving cells may contain several SKUs in the opening snapshot. It derives a
violation whenever a Комплектация or Хранение cell with mixing
disabled contains more than one distinct SKU with nonzero balance, across
dates. The violation disappears when outward movement leaves at most one such
SKU; ordinary inflow that would retain mixed stock is rejected.

An active operational location may have one active lock. A lock keeps the
location visible but excludes it from operational selection and final posting.
A locked location cannot be deactivated or converted to a folder. Administrators
own manual locks; an inventory-count draft owns its lock until posting or
explicit deletion. Lock release removes the active record; separate lock
history is not retained.

The Web editor exposes the administrator-controlled permission to hold several
SKUs for Storage and Reserve locations and shows it in their detail view. It is
off by default for manual creation and range generation.
New receiving putaway applies it to both Storage and Reserve destinations;
Receiving, Transit, and Shipping positions remain flow locations. With permission off,
the final recorded stock in a storage-purpose location may contain nonzero balances
of only one distinct SKU. Different expiration dates of that SKU are allowed;
retained zero rows do not occupy the location, while negative balances count
as nonzero. An administrator cannot turn permission off while several SKUs
remain. Receiving putaway, direct and transit-backed transfers, inventory
count posting, and current picking rollback reject a final mixed state
without partial effects. A count may replace one SKU with another when its
final state contains only the latter. Destination recommendations retain
their existing scope; a scanned destination is checked at posting.

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

`InventoryBalance` is current signed quantity by warehouse, location, SKU,
and nullable expiration date. SQL Server keeps one balance key even when the
date is `null`. Posted `InventoryMovement` records warehouse movement with
its LPN provenance when applicable, and `InventoryTurnover` is its immutable
before/delta/after history. Closing a receiving LPN posts the receipt at its
original Receiving location. Each confirmed partial putaway posts immediately
from there to Storage or Reserve; order completion posts nothing. Draft picking
movements do not change inventory. Zero balance rows are retained and support
historical-location suggestions.
The Web balance list shows one row per warehouse, location, SKU and expiration
date, with an explicit "Не указан" for a balance without a date. Different dates
remain separate even after the SKU's expiration-tracking flag is turned off.
The separate Web report for undated expiration stock lists current nonzero
balances without a date for SKUs whose local expiration flag is now enabled.
It shows warehouse, location, SKU and signed quantity, including negative
balances; zero rows and dated groups do not appear. The report only reads stock.

Posting validates active topology, folder status, locks, and the
workflow-specific zone route in the caller's save. Putaway also requires
enough physical balance at the source for the selected SKU and date.
Receiving correction and reversal can leave a signed negative balance when
their own rules permit it; the Web deletion preview makes this explicit.
Posting and eligibility-changing topology or lock updates advance
`StorageLocation.OperationalRevision`. Transfers additionally use transfer and
balance concurrency tokens plus named database constraints. Only recognized
inventory concurrency failures become business conflicts.

The current boundaries of picking, shipping, transfers, and inventory counts
are in [other warehouse workflows](other-workflows.md).
