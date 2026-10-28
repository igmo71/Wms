# Current other warehouse workflows

These workflows remain available, but their behavior with dated or negative
stock from receiving is not established as production-ready:

| Workflow | Current boundary |
|---|---|
| Picking and shipping | Movement commands do not carry an expiration date; the workflow cannot identify the dated balance group to move or issue. |
| Intra-warehouse transfers | Movements are created without an expiration date, so a dated group cannot be preserved. |
| Inventory count | A draft covers one ordinary Storage location and snapshots only positive balances by SKU. It cannot represent several date groups of one SKU; negative balances are excluded. |

## Picking and shipping

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

## Intra-warehouse transfer

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
