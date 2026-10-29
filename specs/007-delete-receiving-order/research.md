# Research: Delete a receiving order

## Historical putaway is an absolute blocker

Existing single-LPN deletion can reverse putaways, but the order rule is stricter. Query putaway records for every LPN ever assigned to the order, including already deleted labels and prior cycles. Checking only current placed quantity misses history. Allowing deletion after a reversed putaway conflicts with the requested rule.

## Compensate movement history

`DeleteReceivingLpn` uses `InventoryMovement.CreateReversal` and `PostInventoryMovement.Handler`. Reverse each outstanding receipt/correction once; retain originals, turnover and unrelated operations. A unique index on `ReversedMovementId` guards duplicate reversals. Direct balance subtraction or physical movement deletion violates the stock ledger rule.

## One order preview and command

Several LPNs can affect the same dated balance group. Per-LPN confirmations cannot express the aggregate result or guarantee all-or-nothing deletion. One token covers order/LPN revisions, movement/reversal state, balances and active links. One command recomputes it and saves all effects together.

## Negative forecast is defensive

Current count, transfer and picking routes operate outside Receiving locations; no supported ordinary path was found that consumes an unplaced order's received stock there. Existing single-LPN deletion allows an explicitly confirmed negative forecast; old or independently corrected balances may still exist. Keep the safeguard without treating it as a routine case.

## Retain archive and release links

`ReceivingOrderSourceLink.DetachedAtUtc` retains history while the filtered unique active-link index permits later attachment elsewhere. Detach current links in the deletion transaction. Retain order ID/number, cycles, source versions, LPNs and audit. Exclude `Deleted` from active queries and transitions. Physical deletion would destroy provenance.
