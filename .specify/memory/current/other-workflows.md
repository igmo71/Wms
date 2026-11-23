# Current other warehouse workflows

## Inventory count

A count covers one active Комплектация or Хранение cell. Opening a cell resumes
its existing draft. A foreign cell hold, active picking reservation or unresolved
shortage blocks a new draft. Start atomically takes an exclusive cell hold and
snapshots every nonzero signed balance by SKU and nullable expiration date.
Leaving the screen retains the draft and hold. Deleting a draft releases the
hold without changing stock.

The Operator scans to add one or manually sets a nonnegative quantity for a
SKU and expiration group. A missing fact is uncounted and zero confirms that
the group is absent. For a SKU that tracks expiration or has dated count rows,
positive fact requires an explicit date or confirmation that the date is
unknown. A readable expired date is recorded as found. A date change is the
fact of the original and the discovered groups, each with its own quantity.

Posting verifies the complete fact, current balances against the signed
snapshot and the [SKU-mixing rule](inventory.md) against the final count.
Each nonzero difference posts for its own SKU and expiration group. Posting
commits the document, movements, balances, turnovers, audit, repeat-safe
command receipt and release of the hold together. A repeat of the same command
does not duplicate movements. A rejected posting retains the draft and hold.
If mixing caused the rejection, a Manager can allow mixing and the Operator
can post the same draft. To redistribute the goods first, the Operator deletes
the draft, the warehouse moves the goods and the Operator recounts the cell.

## Intra-warehouse transfer

The legacy transfer path creates movements without an expiration date and
uses the former Storage-only rules for storage cells.
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

## Employee performance

The receiving-order LPN audit shows each worker's receiving contribution.
The picking report groups positive fact entries from closed assemblies by
assembly and confirming worker. The time span runs between that worker's
first and last confirmations. Weight totals use current SKU data and mark
incomplete results.
