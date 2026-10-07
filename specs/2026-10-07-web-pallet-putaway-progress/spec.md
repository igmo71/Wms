# Web pallet putaway progress

Status: **Frozen** — accepted after developer review on 2026-10-07.

## Outcome

The receiving order shows how far each of its pallets has been placed without
turning placement back into an order-level process.

## Scope

- After receipt, derive a short placement state per non-cancelled LPN from its
  retained items and posted LPN putaway history. Badge the count of retained
  item rows with any unplaced quantity; hide a zero badge.
- In the selected pallet's item audit, show placed and remaining quantity for
  each retained item; deleted rows retain audit display but no live progress.
- Keep the existing detailed putaway history below the item table.
- Clicking a pallet in a SKU's distribution menu selects that pallet and opens
  the existing `Палеты` tab on the same order page.
- Label the history destination column `Ячейка`. Remove the raw movement-GUID
  link, which does not select that movement in the journal.
- Remove the unreachable, disabled order-level Web putaway component. Do not
  change the legacy persisted status or command services in this increment.
- Do not add posting actions or sum quantities across SKUs with different units.

## Acceptance

1. An accepted pallet with no putaway is marked not placed, partial putaway is
   marked partial, and fully posted positive items are marked placed.
2. Each retained item shows its own posted and remaining quantities; deleted
   rows show `—`. The detailed movement history remains available.
   The pallet badge counts unfinished item rows without mixing SKU units.
3. In-progress receiving does not present placement progress, and no Web action
   can post an order-level putaway.
4. A distribution-menu click opens the matching pallet's composition and
   history; direct tab switching and pallet selection still work.

## Verification

Developer builds WebApp and reviews an accepted order with untouched, partial,
and fully placed LPNs plus a pallet containing different SKU units. No new
automated test is needed for this read-only projection.

## Open questions

None.
