# Current client behavior

The receiving workspace and numbering decisions are in
[feature 002](../../../specs/002-receiving-workspaces/spec.md).

## Client behavior

Web and Mobile use `SKU` and `LPN` in user-facing labels and messages. LPN
status wording treats the number as masculine (for example, «Открыт» and
«Закрыт»). Integration identifiers and 1C field names retain their source names.

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
lists WMS orders for the selected warehouse. «Создать ордер» opens an initially
empty document screen. The first successful scan creates an order with that
document; each later scan immediately attaches another document and recalculates
its plan. Scanning on the queue opens the linked order directly or creates an
order from a free document and opens the document screen with that first row.
The queue accepts the device scanner without opening a document list; a compact
button starts the camera fallback when no embedded scanner is available.
The server rejects a document with a different sender ID. On the empty creation
screen, a scan of an already linked document offers to open its order. Returning
before the first scan creates nothing; returning after creation leaves the order in the
queue. After creation, the document screen shows the same SKU, LPN and 1C
section navigation as an existing order. A participant scans an issued LPN, selects or
globally scans its SKU, enters a positive quantity and may close it. WMS orders
carry their own number and factual creation time in Mobile V1 responses, while
their IDs remain the keys for commands; 1C numbers belong to individual sources. The
queue shows the number and separate local creation time. The SKU and LPN screens use the order
number as their heading and place status and receiving location on one line;
they omit creation time and warehouse from the compact heading. The existing
order has separate SKU, LPN, and document screens headed «Приёмка › SKU»,
«Приёмка › LPN», and «Приёмка › Документы 1С». A selected section is visibly
marked and does not respond to taps. Sections use the same default page transition. Switching replaces the current section in
the navigation stack, so Back returns to the order queue; a detail page returns
to the section that opened it. A shared join screen is reachable from all three
sections and returns to the originating section. Its entry button reads «Начать» for a ready order and «Присоединиться»
for an order in progress. A new cycle selects a receiving
location there; an ongoing cycle joins without selecting it again. Pending
commands prevent navigation until explicit retry. LPN scans work on both SKU and
LPN list screens, through either device scanner or camera. SKU scans remain
inside an opened LPN. The SKU list sorts by displayed name without row numbers,
using code and ID when a name is unavailable. Each SKU card keeps its name left
and semibold plan and fact on two right-aligned lines, with the unit shown once.
An unchecked fact is a red question mark on a neutral card. Equal known plan
and fact use green text and a soft green background; a checked difference uses
yellow text and background. An unknown plan leaves the card neutral. Each SKU
card opens its details on tap and keeps a compact zero-confirmation action
when eligible. Zero checking and direct putaway of
closed LPNs remain available before order completion. Closing and putaway use
separate retry-safe commands. The document screen shows «Новый ордер» before creation and the WMS order
number once the first document creates it. Each document row
shows number and date, then sender name and warehouse operation, with no field
labels; a tap reveals other source details and any latest check error. A failed
check marks its row with a warning symbol. Scanning immediately attaches one
document and recalculates the order plan. Consecutive scans attach consecutive
documents, and the newest row is highlighted gray. A sender mismatch stays on
the document screen with a clear error. A cross on the document row detaches it
before the first posted LPN; detaching the last document keeps the empty order.
Before the first document scan, Back leaves without creating an order and
section navigation is hidden. Once the order is created, the document screen
offers the shared section navigation and no «Готово» action. On the document
screen of an existing order, Back returns to the order queue. An uncertain
response retains the original barcode
or source ID and command ID for explicit retry, including the first create scan.
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
combined SKU plan and the Manager's create action. The active row and checked
rows share the theme's subtle gray background. On narrow screens the tables
stack vertically. The WMS order column contains a link for linked sources and
a selection checkbox for free sources when the user is a Manager. Both SKU
tables sort by displayed nomenclature name. A document number on the Web order
card links to this page with that document highlighted and its lines visible.
The Web order card shows the WMS number and creation time in its heading;
its route continues to use the internal ID.
Receiving lists and cards show synchronized warehouse and SKU names, local
receiving and putaway addresses, and a synchronized partner name for a 1C
partner sender. The Web order card shows the sender name without the partner-type
suffix. If a
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
The order heading shows the number and creation time on the first row. A bordered,
evenly spaced second row shows the warehouse name, status, receiving location,
and receiving number. Documents, link history, plan/fact, receiving LPNs, and
receivings use the same expandable panel style with raised headers. Document,
LPN, and receiving counts appear in muted badges. The LPN panel starts open; the other sections start
closed. Each receiving card shows its location and completing user in the header,
with space above the first card. Expanding it immediately shows its SKU plan/fact
table without a separate label. The plan/fact table sorts
SKU by displayed name. Rows with known, equal plan/fact use the theme's subtle success
hover color; rows with known, differing plan/fact use the warning hover color;
rows without a checked fact or known plan remain unshaded. Receivings and their
SKU lists follow the LPN panel.
LPN cards sort by code. Each SKU line includes placed and remaining quantity;
putaways appear below in a compact table with quantity, cell, date, and the
user's display name. A deleted LPN has no separate pre-deletion composition
label. The LPN delete button opens the existing preview and confirmation flow.
Deletion history starts collapsed, shows the confirming user's
name, and uses a dash for missing addresses; the original inventory movement
ID remains internal.
User-facing order screens call linked sources "Документы 1С" and receiving
cycles "Приёмки". Receiving history shows the completing user's display
name and a snapshot table with short plan and fact headers, without a version
list below the SKU rows. Link history shows the actor's display name.
A Manager can manually recheck every linked source from the card. In the Web
order list, ready orders have no color; active and completed orders use a subtle
warning row color when current SKU plan or fact is unknown or differs, and a
subtle success color otherwise. The list also marks completed orders whose
current discrepancy differs from their latest completion snapshot.
The Web order card shows linked documents in columns for number, date, sender,
warehouse, warehouse operation, and business operation, without a duplicate
summary above it. The recheck button shows the latest
attempt time across linked documents and rechecks all currently linked documents;
the action has a clarifying tooltip. A Manager opens a separate source-composition dialog to
filter free and linked documents by number as they type, clear either search,
then select one for attachment or detachment. The dialog shows the document,
warehouse, changed SKU plans, and
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
