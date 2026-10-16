# Current client behavior

## Client behavior

Mobile is online-only. Urovo ScanWedge intent input and the ML Kit camera
fallback implement one scanner boundary; operational screens prefer the
embedded scanner when available. Manual SKU search, where present, uses name,
code, or barcode and rejoins the same operation as scanning. Decimal 1C GUID
barcodes accept and normalize leading zeroes.

Mobile receiving and LPN putaway use the new Application commands and read
projections through their replacement endpoint groups. Their
route policy requires `Operator` or `Manager`; `Administrator` alone does not
grant receiving access.

The Urovo ScanWedge profile must emit barcode data through its broadcast Intent
only. Simultaneous keyboard-wedge output is disabled because its trailing Enter
can activate a focused UI action after the Intent scan has already navigated.
Scanner-driven pages ignore queued scan callbacks from a previous period of
page activity, including after leaving and returning to the same live page.
Independent putaway starts from its own LPN screen. The Mobile receiving queue
lists free 1C sources and WMS orders for the selected warehouse. Scanning a
document selects a free source or opens its linked order; several selected
sources create one WMS order. A participant scans an issued LPN, selects or
globally scans its SKU, enters a positive quantity and may close it. The order
page offers zero checking and direct putaway of closed LPNs before order
completion. Closing and putaway use separate retry-safe commands.

Web `/receiving-orders` lists unlinked 1C sources separately from WMS orders.
Operators and Managers can select several sources of one warehouse, inspect
their summed plan and create an order. The order page shows current plan, fact,
source versions and errors, LPN quantities, expiration dates and putaway history.
Unchecked fact is textual; different SKU quantities are not totaled together.
A Web Manager can add a synchronized catalog SKU and close an open LPN, including
explicit past-date confirmation and an optional reason. Web no longer calls
the old order-completion command. The replacement completion and later manager
correction actions are not yet exposed.

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
