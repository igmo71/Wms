# Manual SKU selection in an open receiving LPN

Status: **Frozen** — accepted on 2026-10-06.

Current rules belong to [Receiving and putaway](../../../docs/PROJECT_CONTEXT.md#receiving-and-putaway).

## Outcome

An employee can open the quantity page for any SKU in the receiving order plan
from an editable LPN, including a SKU that has not yet been counted in that LPN.
The barcode path and the LPN composition remain unchanged.

## Scope and rules

- Add an explicit manual selection action on the LPN page. The list contains
  distinct planned SKUs, showing name and code, and can be searched by either.
- The LPN's main item list continues to show only retained composition rows.
  Opening selection does not create a row or change aggregate facts.
- Selection is available only while this user can edit the LPN. It is blocked
  while a command is running or waiting for an explicit repeat-safe retry.
- Selection opens the existing absolute-quantity page for the chosen SKU. That
  page reloads the current order and LPN state and applies its existing edit
  permission checks before saving.
- The manual selector does not focus the search field or open the keyboard.
  ScanWedge input continues through the scanner boundary.
- After a successful quantity save, including explicit retry after a lost
  response, the selector closes when returning to the LPN page. Failed or
  cancelled edits do not close it.
- The plan selection and retained LPN composition have separate neutral
  headings. Plan cards are light gray; retained LPN cards are white. Color
  does not imply that a counted quantity is correct.

Other receiving screen redesign, completion guidance and server changes are
outside this increment.

## Acceptance

1. An empty editable LPN offers manual SKU selection; selecting a planned SKU
   opens its quantity page, where saving creates its LPN item and updates fact.
2. Search finds a planned SKU by name or code. The selector does not show
   duplicate entries for repeated plan lines of one SKU.
3. Merely opening or closing selection does not create a row. The visible LPN
   composition remains limited to counted items.
4. A read-only/closed LPN cannot use selection; an active or uncertain command
   prevents selection and navigation to quantity entry.
5. Barcode SKU and LPN switching still work, and no input opens the keyboard
   without an explicit tap.
6. After «Применить» succeeds, the selector is closed and the counted card is
   visible. Failed save leaves the selector open; successful repeat-safe retry
   closes it. Plan choices and counted items are visually distinguishable.

## Verification

The developer confirmed on 2026-10-06 that the selection flow works on TSD and
accepted the return behavior and neutral card styling. No new automated test was
required: posting and replay are already covered by receiving safety tests,
and this increment adds only a UI navigation path.

## Open questions

None.
