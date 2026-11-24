# Current storage and inventory

## Storage and inventory

A warehouse contains pickable storage (`Storage`, displayed as `Комплектация`),
reserve (`Reserve`, displayed as `Хранение`), transit, receiving, and shipping
zone types. New receiving putaway can post into either Storage or Reserve.
Picking can use Storage and eligible one-SKU Reserve cells. Inventory counts
cover both zones. Transfers use Storage cells for their storage endpoints.

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

Availability is calculated by warehouse, cell, SKU and nullable expiration
date. Free quantity is the accounting balance minus active picking reservations
and disputed shortage quantity, with a minimum of zero. A cell hold makes the
entire cell unavailable for new work. When picking uses its own assembly's
hold, the hold itself does not reduce availability, and all active reservations
are still subtracted. The general stock report uses availability for new work.

Issuing a picking assembly's reserved quantity uses the accounting balance
after protecting other reservations and disputed quantity. A fully reserved
group can therefore have zero available for new work while covering the owning
assembly's issue. Physical scans, FEFO and assembly authorization are checked
by each workflow.

The shared posting handler supports a dated picking issue against the exact
reservation of a picking entry, a Reserve-to-Storage replenishment under its
own source hold, and stock corrections. A picking issue completes its reservation
in the movement's commit. Replenishment retains the source hold after posting.
Corrections preserve picked stock and can consume the disputed quantity of the
referenced shortage. The Manager use case resolves that report in the same
session. Receiving putaway cannot occupy a cell held by picking or replenishment.

## Expiration day

Picking and replenishment use the application server's local date for expiry
checks and warnings. A dated group expires after its stated date. Receiving
uses the [UTC day](receiving.md#cycle-and-receiving-work) for receiving LPNs.

## Picking

The route applies FEFO to free stock: known expiration dates come first,
earliest first, followed by unknown dates. It excludes expired groups, can
recommend an eligible whole one-SKU Reserve cell before Storage top-up, and
reports unmet demand separately. Picking can inspect a selected or scanned
Storage or Reserve cell. An unknown date prompts a visual expiry check for a
SKU that tracks expiration. The confirmed fact retains the unknown date.
Confirming a pick reserves its exact group and records editable fact without
moving stock.
Reducing a fact requires a scan of its original cell and confirmation that the
released quantity is physically there; the matching reservation is reduced
in the same command. A normal pick confirms the full quantity currently offered
for the scanned SKU, cell and expiration group. When the operator finds less,
including zero, the found quantity means the entire remaining physical stock
of that group in the cell. The shortage command checks the scanned free
accounting quantity, reserves the found positive fact once, and blocks all
other free accounting quantity of that group in the same transaction. The
disputed group therefore does not return as a route step. A Manager either
posts a stock correction for the blocked quantity or releases a mistaken block,
preserving the report and decision. This stock decision does not approve a
shipping plan/fact discrepancy. A direct Reserve pick first holds the whole
one-SKU cell; each confirmed date group must be taken in full. Its hold can be
released after a confirming scan when no positive fact remains from that cell.

## Replenishment

The proposal calculator starts with the remaining assembly demand and deducts
the quantities covered by picking steps and active tasks for the same assembly
and SKU. It considers free stock in expiration order, using Storage before
Reserve within the same date group. It proposes transfers only for Reserve
quantities that cover the remaining demand and reports any uncovered deficit.
An ordered task consumes its suggested source quantity in new proposals, and
physically held cells are excluded. A recommended Storage destination has a
positive balance of that SKU and permits its SKU mix. Eligible destinations
sort by code. If no destination qualifies, the worker selects one by scan.

An Operator responsible for an active assembly can order all currently offered
proposals for one SKU in a single command. The command rechecks the route, stock,
sources, destinations and existing tasks in one transaction, then saves all
tasks, one assembly revision, their audit events and one command receipt
together. Repeating its request ID returns the same assembly without creating
more tasks. No stock movement or physical source hold occurs when the tasks
are ordered.

When a confirmed pick or Manager plan change reduces an assembly's remaining
demand for a SKU to zero, the same command cancels its ordered replenishment
tasks for that SKU. A task whose source has already been scanned continues at
its original ordered quantity. Cancelled tasks remain in the assembly's history;
if demand later rises, a new task must be ordered explicitly.

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
previous pallet has been completed. When the worker scans a new source for
the same task, previously completed sources do not take precedence over it
in the expiration check. Other work continues to use the warehouse balances.
Confirming a replenishment transfer checks the scanned Storage destination,
its active hold and SKU mixing, the active source pallet held by this task,
and the earliest available expiration group on that pallet. One confirmed
quantity moves one SKU and one expiration group from Reserve to Storage through
the common inventory posting operation. The source hold remains until its
separate completion scan. The movement, both balances and turnovers, task
progress, transfer record, audit and command receipt are saved together;
repeating the request ID does not move stock again.
A source cycle ends after the worker returns the pallet if it still holds goods,
rescans the same cell and confirms completion. The command releases the cell
hold without posting stock. If the ordered quantity remains incomplete, the
same task awaits another source and retains its prior transfers and assigned
replenisher. The worker confirms only the quantity actually transferred.
Physical discrepancies during replenishment are handled administratively
outside WMS.
The Manager can correct a posted replenishment step by entering its actual
transferred quantity and a reason. The original step remains immutable; a
separate dated stock-correction movement, recorded against that step, transfers
the difference between its original source and destination. The shared posting
checks current balances, reservations, shortage blocks, cell holds and SKU
mixing. Task progress reflects the corrected total in the same command. The
assembly audit retains the reason, old and new quantities and movement ID.

## Picking assembly closure and cancellation

«Закрыть и отгрузить» posts the warehouse shipment when the picking assembly
closes. This is the sole outbound stock posting point. The command rechecks
each positive entry against its own active reservation and protects other
reservations and shortage blocks before issuing from the
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
cutover page, using a separately selected XLSX file after SKU catalog
synchronization. The stock file may differ from the topology file. The preview
requires a previously imported topology, a declared snapshot time, active
target cells with matching functional purposes, known SKU GUIDs, and
matching canonical units. It blocks operational stock or work, unsupported
quantities, and changed warehouse state. Missing SKUs and cells are not
created during stock confirmation. Only one opening snapshot can
be confirmed per warehouse between operational-data resets. An exact retry with
the original command key returns the recorded result; a new request for the same
or another workbook conflicts while that snapshot exists. The operational-data
cleanup resets the stock stage and its command receipts while preserving topology;
the next snapshot is previewed and confirmed against the retained cells.

Accepted source rows with nonzero quantities produce one posted movement of
positive magnitude and one signed turnover per row. A positive source quantity
has only a destination; a negative quantity has only a source. Shipping stock
may remain negative. Balances accumulate by warehouse, cell, SKU and nullable
expiration date, so repeated source rows share one balance while retaining
separate movements and turnovers. A supplied past date remains as supplied;
an absent required date remains unknown. An accepted source `Да` enables the
SKU's local expiration requirement without disabling any existing requirement.
Accepted zero rows make no movement. The import status, declared time, stock
file name and hash, source summary, stock effects, SKU flags, audit and command
receipt commit together; failed confirmation leaves the topology-only stage intact.

The imported topology permits several SKUs in these cells: `1C09A1`, `1G09A1`,
`1K09A1`, `1M09A1`, `1O09A1`, `1Q09A1`, `1U11A1`, `2O01A1`, `2O02A1`,
`2O03A1`, `2O04A1`, `2O05A1`, `2O06A1`, `2O07A1`, `2O08A1`. In the opening
snapshot, a Комплектация or Хранение cell with mixing disabled may retain
several SKUs with nonzero balances across dates. The preview warns about each
such cell for manual correction after cutover, and confirmation records the
addresses in the import summary and audit. Shipping and Receiving cells may
contain several SKUs in the opening snapshot. Normal operations apply the
[cell mixing rules](#cell-holds-and-sku-mixing). The current violation clears
when the cell contains at most one SKU with a nonzero balance or the Manager
enables mixing.

## Cell holds and SKU mixing

An active operational location may have one active lock. A lock keeps the
location visible and excludes it from competing warehouse work.
A locked location cannot be deactivated or converted to a folder. Administrators
own manual locks; an inventory-count draft owns its lock until posting or
explicit deletion. Lock release removes the active record; separate lock
history is not retained.

The Manager controls the permission to hold several SKUs for Storage and Reserve
locations through a separate Web action. The Manager can change it while a
count holds the cell. It is off by default for manual creation and range generation.
New receiving putaway applies it to both Storage and Reserve destinations;
Receiving, Transit, and Shipping positions remain flow locations. With permission off,
the final recorded stock in a storage-purpose location may contain nonzero balances
of only one distinct SKU. Different expiration dates of that SKU are allowed;
retained zero rows do not occupy the location, while negative balances count
as nonzero. A Manager cannot turn permission off while several SKUs
remain. Receiving putaway, direct and transit-backed transfers, inventory
count posting, and current picking rollback reject a final mixed state
without partial effects. A count may replace one SKU with another when its
final state contains only the latter. Destination recommendations retain
their existing scope; a scanned destination is checked at posting.

## Quantities and dimensions

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

## Balances, movements, and turnovers

`InventoryBalance` is current signed quantity by warehouse, location, SKU,
and nullable expiration date. SQL Server keeps one balance key even when the
date is `null`. Posted `InventoryMovement` records warehouse movement with
its LPN provenance when applicable, and `InventoryTurnover` is its immutable
before/delta/after history. Closing a receiving LPN posts the receipt at its
original Receiving location. Each confirmed partial putaway posts immediately
from there to Storage or Reserve; order completion posts nothing. Zero balance
rows are retained and support historical-location suggestions.
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

Transfer and inventory-count boundaries are in
[other warehouse workflows](other-workflows.md). The outbound stock rules are
described above; the 1C exchange rules are in [integration](integration.md).
