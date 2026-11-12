# Current other warehouse workflows

These workflows remain available, but their behavior with dated or negative
stock from receiving is not established as production-ready:

| Workflow | Current boundary |
|---|---|
| Intra-warehouse transfers | Movements are created without an expiration date, so a dated group cannot be preserved. |
| Inventory count | A draft covers one ordinary Storage location and snapshots only positive balances by SKU. It cannot represent several date groups of one SKU; negative balances are excluded. |

Legacy transfer and inventory count retain their original posting paths. Their
replacement will adopt the shared reservation and hold mechanism already used
by the replacement outbound processes.

## Inventory count

Start/resume returns an existing draft for the location and warehouse, or creates
one. Barcode resolution and mutable checks follow receipt lookup; count changes,
lock, inventory effects and receipt save together without intermediate saves.

A count covers one ordinary storage location. Draft creation atomically locks
the location and snapshots positive balances as expected rows. Nullable counted
quantity means uncounted; zero is explicit. Scan adds one, and manual input may
set an absolute nonnegative quantity or add an unexpected SKU. Posting requires
every row, records only nonzero differences, and releases the lock. Leaving the
screen retains the draft; explicit deletion removes it and releases the lock.
Posting follows the [Storage location SKU-mixing rule](inventory.md) on the
final counted state.

## Intra-warehouse transfer

Transfer creation, movements, completion and Web draft deletion share application
commands. State, movements, balances, turnover and receipt save together without
intermediate saves or 1C calls.

Direct, pick-to-transit, and put-from-transit movements post immediately. One
transit location belongs exclusively to one active transfer and must be empty
before completion. Posted movements and completed transfers are immutable; an
unused draft may be deleted. Rejected or uncertain Mobile commands remain on
the operation screen for recovery. Storage destinations follow the
[SKU-mixing rule](inventory.md).

Mobile transit transfers expose pick-to-transit and put-from-transit actions;
direct movement is offered only for transfers without a transit location.
Like WebApp, Mobile disables completion while the transit location has stock.
Before posting, Mobile can clear a selected transfer SKU and quantity and
return to scanning or search without losing the source location or transit
cart. Selection cannot be changed while posting or awaiting a repeat-safe
retry. A SKU with no available source stock does not advance to quantity entry.

Employee-performance reporting exposes receiving contribution in the
receiving-order LPN audit rather than the aggregate report. Picking now groups
positive fact entries by assembly and confirming worker; its duration spans
that worker's first and last confirmation. Weight totals use current SKU data
and identify incomplete results.
