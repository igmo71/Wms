# Current client behavior

The receiving workspace and numbering decisions are in
[feature 002](../../../specs/002-receiving-workspaces/spec.md).

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
globally scans its SKU, enters a positive quantity and may close it. WMS orders
carry their own number and factual creation time in Mobile V1 responses, while
their IDs remain the keys for commands; 1C numbers belong to individual sources. The
queue and order page display that number and the separate local creation time.
The order page offers zero checking and direct putaway of closed LPNs before order
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

The Web menu group «Приёмка» opens separate 1C documents and WMS orders pages.
`/receiving-orders` lists only WMS orders by their own numbers and actual
creation times. `/receiving-sources` lists both free and linked 1C documents
with their WMS order link; only a Manager can create an order from that page.
The source list and its SKU table sit side by side on wide screens. Clicking any
source row shows the lines of its latest saved 1C version, even when linked to
an order, and clears checked sources. Checking free sources instead shows their
combined SKU plan and the Manager's create action. The active row has a darker
gray tint; checked rows use a lighter gray tint. On narrow screens the tables
stack vertically. The WMS order column contains a link for linked sources and
a selection checkbox for free sources when the user is a Manager. Both SKU
tables sort by displayed nomenclature name.
The Web order card uses the WMS number as its heading and shows the actual
creation time separately; its route continues to use the internal ID.
Receiving lists and cards show synchronized warehouse and SKU names, local
receiving and putaway addresses, and a synchronized partner name for a 1C
partner sender. The raw 1C partner type is labelled "партнёр 1С". If a
reference has no local name, its ID remains visible so the operator can
identify the unresolved record. Receiving tables use MudBlazor table controls.
Web Managers can select several free sources of one warehouse, inspect their
summed plan and create an order. The order page shows current plan, fact,
source versions and errors, LPN quantities, expiration dates and putaway history.
Each receiving LPN shows its own status; its active SKU row shows the expiration
date or "Не указан" without a separate item-state label.
Unchecked fact is textual; different SKU quantities are not totaled together.
A Web order card shows open LPNs without controls for SKU selection, quantity
entry or closing; physical receiving remains in Mobile. Web no longer calls
the old order-completion command. Manager LPN quantity correction and deletion
are exposed. Only a Manager can complete an order in Web or reopen a completed
order into a new cycle. A participant can still complete it in Mobile. The page retains
the command ID for a retry after an uncertain response and reloads the saved
status and cycle snapshots after success.
The order card separates current plan/fact discrepancies from immutable cycle
completion snapshots and shows source check failures and unknown-plan reasons.
A Manager can manually recheck every linked source from the card. The order
list highlights completed orders whose current discrepancy differs from their
latest completion snapshot.
The Web order card shows a compact count and numbers of linked 1C sources,
the latest check summary, and expandable source details with errors. It offers
manual rechecking. A Manager opens a separate source-composition dialog to
find a free document by number or `Ref_Key`, or select a linked document for
detachment. The dialog shows the document, warehouse, changed SKU plans, and
fact-preservation warning before confirmation; a stale preview requires a new
selection. An uncertain command response keeps its request ID for a repeat
while the dialog remains open. After success the card reloads plan, fact, and links. The Web
source-change command requires a Manager and a matching preview token. Mobile
source changes retain their previous rules. Operators only view source
composition in Web.

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
