# Current client behavior

## Client behavior

Mobile is online-only. Urovo ScanWedge intent input and the ML Kit camera
fallback implement one scanner boundary; operational screens prefer the
embedded scanner when available. Manual SKU search, where present, uses name,
code, or barcode and rejoins the same operation as scanning. Decimal 1C GUID
barcodes accept and normalize leading zeroes.

The Mobile receiving and LPN putaway endpoint groups are temporarily unmapped.
Replacement contracts and handlers use the new Application commands and read
projections; they will be connected with the replacement Mobile screens. Their
route policy requires `Operator` or `Manager`; `Administrator` alone does not
grant receiving access.

The Urovo ScanWedge profile must emit barcode data through its broadcast Intent
only. Simultaneous keyboard-wedge output is disabled because its trailing Enter
can activate a focused UI action after the Intent scan has already navigated.
Scanner-driven pages suppress Android focus on action buttons. Receiving order,
LPN and LPN putaway pages ignore queued scan callbacks from a previous period of
page activity, including after leaving and returning to the same live page.
Independent putaway starts from its own LPN screen.
Scanning an accepted order shows its number, date and state; an unknown order
has a distinct not-found message. The receiving order shows that scanning an
available pallet adds it. Its nomenclature/pallet tabs and current action remain
below the scrolling content; pallet actions and uncertain-command retry use the
same fixed bottom area.

Web uses one receiving-order page across ready, receiving and received states.
The list distinguishes an accepted quantity difference from successful source
synchronization. Completion and 1C reconciliation stay above the content tabs;
an unavailable completion action gives a concise reason. Manager confirmation
is available there for entry and later audit.

The nomenclature tab shows aggregate order lines and each SKU's canonical unit.
A ready order shows its stored plan. During receiving, equal WMS and 1C plans
collapse to one column; any difference shows both plans for all lines. An absent
1C value is not equal, and after acceptance the 1C plan comes from the saved
completion snapshot. Web read-only quantities show three decimal places;
unchecked fact remains textual. A SKU can show the active pallets containing it
and each retained quantity. The pallet tab exposes each pallet's item audit and
putaway history, including placed and remaining quantities. Cancelled pallets
remain visible for audit, but deleted items are excluded from active totals.
Quantities of different SKUs are never summed across units. Web does not edit
the pallet composition.

The API address is packaged per build configuration and is not editable by the
operator.

Mobile text fields open the keyboard only on an explicit tap. Scanning,
opening search, and entering a quantity step never focus an input
automatically. Completing input or tapping outside the field (including
confirm/cancel actions) dismisses the keyboard.
Android fields suppress automatic keyboard display on native focus changes;
an explicit field click requests the keyboard separately.

Client organization, receipt compatibility, error codes and retry rules belong
in [architecture](../architecture.md#client-organization).
