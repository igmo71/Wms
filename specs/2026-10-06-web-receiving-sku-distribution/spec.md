# Web receiving SKU distribution

Status: **Frozen** — final menu presentation accepted on 2026-10-07 after developer verification.

## Outcome

From an order's Web nomenclature table, an operator can see which pallets
contain a chosen SKU and how much of it is on each pallet.

## Scope

- Clicking a nomenclature name opens a compact read-only menu at that row,
  without resizing the order table or changing the pallet audit tab.
- List only non-cancelled pallets with a retained, non-deleted item for that
  SKU. Show pallet code and quantity with SKU unit.
- Show a clear empty state when the SKU has not been added to any pallet.
- Keep aggregate plan/fact values, pallet composition, receiving commands and
  persistence unchanged.

## Acceptance

1. Opening the menu for a SKU listed on two pallets shows exactly those pallets
   and each retained quantity; another SKU opens its own menu.
2. A SKU on no active pallet shows the empty state. Deleted items and cancelled
   pallets do not appear in the distribution.
3. The menu closes without moving the order table; the full pallet audit tab
   remains available.

## Verification

Developer builds WebApp and checks the distribution for a SKU on multiple
pallets and one on none. No automated test is added for this read-only view.

## Open questions

None.
