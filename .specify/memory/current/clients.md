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
The active Mobile order has a document-scan mode for adding a source and shows
linked documents with a detach action before the first posted LPN. Each change
reloads the order plan. An uncertain response keeps the original barcode or
source ID and command ID for explicit retry.
The Mobile quantity page shows the date field when the current SKU flag is on
or a date was already saved on the open LPN. It retains and displays that saved
date after the flag is turned off. If closing is rejected after the flag or
another closing input changes, the LPN page refreshes its server view and
prompts the user to check the date before retrying.

Web `/receiving-orders` lists unlinked 1C sources separately from WMS orders.
Operators and Managers can select several sources of one warehouse, inspect
their summed plan and create an order. The order page shows current plan, fact,
source versions and errors, LPN quantities, expiration dates and putaway history.
Each receiving LPN shows its own status; its active SKU row shows the expiration
date or "Не указан" without a separate item-state label.
Unchecked fact is textual; different SKU quantities are not totaled together.
A Web Manager can add a synchronized catalog SKU and close an open LPN, including
explicit past-date confirmation and an optional reason. Web no longer calls
the old order-completion command. Manager LPN quantity correction and deletion
are exposed. A current-cycle participant or Manager can complete the order in
Web; a Manager can reopen a completed order into a new cycle. The page retains
the command ID for a retry after an uncertain response and reloads the saved
status and cycle snapshots after success.
The order card separates current plan/fact discrepancies from immutable cycle
completion snapshots and shows source check failures and unknown-plan reasons.
A Manager can manually recheck every linked source from the card. The order
list highlights completed orders whose current discrepancy differs from their
latest completion snapshot.
Web users can search free 1C sources by number or `Ref_Key` and add one to an
unfinished order; they may also enter a `Ref_Key` directly for a fresh 1C read.
After completion, only a Manager can attach a source. An Operator can detach
one before the first posted LPN; later or after completion, only a Manager can
detach it in Web. The card reloads the aggregate plan after either change and
retains the command ID for an uncertain response.

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
