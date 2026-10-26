# Current client behavior

The receiving workspace and numbering decisions are in
[feature 002](../../../specs/002-receiving-workspaces/spec.md). The accepted
Mobile receiving screen decisions are in
[feature 005](../../../specs/005-mobile-receiving-ui/spec.md).

## Client behavior

Web and Mobile use `SKU` and `LPN` in user-facing labels and messages. LPN
status wording treats the number as masculine (for example, «Открыт» and
«Закрыт»). Integration identifiers and 1C field names retain their source names.
SKU lists and SKU search results in Web and Mobile default to ascending displayed
SKU name. Server-paged views apply that order before paging. Web grids honor an
explicit sort chosen by the user. Lists of inventory movements and other event
history retain their event order.

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
The Mobile V1 queue response contains only the order ID, number, creation time,
warehouse ID, and status. Opening an order reads its full detail separately.
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
queue shows the number with a status symbol and separate local creation time.
The SKU, LPN and document screens show the order number with the same status
symbol; the SKU and LPN screens show «Место приёмки:» and its address on the
next line, or «Место не выбрано» until a location is selected. They omit
creation time and warehouse from the compact heading. The existing
order has separate SKU, LPN, and document screens headed «Приёмка › SKU»,
«Приёмка › LPN», and «Приёмка › Документы 1С». Each screen offers only the two
other sections as buttons, in SKU, LPN, 1C order. Sections use the same default
page transition. Switching replaces the current section in
the navigation stack, so Back returns to the order queue; a detail page returns
to the section that opened it. A shared join screen is reachable from all three
sections and returns to the originating section. Its entry button reads «Начать» for a ready order and «Присоединиться»
for an order in progress. A new cycle selects a receiving
location there; an ongoing cycle joins without selecting it again. Pending
commands prevent navigation until explicit retry. The SKU and LPN lists omit
general scanning instructions. LPN scans work on both screens, through either
device scanner or camera. Scanning a closed LPN
already in the displayed order opens its details for reading without joining a
current cycle or sending an LPN open command. Scanning a SKU on either list
opens its distribution card; an SKU outside the order first uses the existing
confirmation to add an order row with plan zero. Quantity entry still happens
inside an open LPN. The SKU list sorts by displayed name without row numbers,
using code and ID when a name is unavailable. Each SKU card keeps its name left
and semibold plan and fact on two right-aligned lines without labels (plan above
fact), with the unit shown once. The compact zero-confirmation button sits below
the fact in the quantity block.
An unchecked fact is a red question mark on a neutral card. Equal known plan
and fact show a green «✓» beside the fact and a soft green background; a checked
difference shows a yellow «≠» beside the fact and a soft yellow background.
An unknown plan leaves the card neutral and its known fact has no comparison
symbol. Each SKU card opens its details on tap and keeps a compact zero-confirmation action
when eligible.
In the SKU detail, a current-cycle participant can scan an issued available or
empty open LPN even when the SKU already has other LPN rows. The resulting LPN editor has that SKU selected
for quantity entry; an LPN already containing another SKU or in a final status
does not open for editing. Existing open and closed LPNs shown for that SKU open
from the detail list by tap or scan; a closed LPN opens for reading even outside
an active receiving cycle. Each LPN row shows the responsible employee's display
name instead of an internal user ID. A delayed response uses the same explicit
retry flow.
Each receiving LPN card shows its number and status, its one SKU or «SKU не выбран»,
and the order plan, fact, and LPN quantity in a right-aligned block. The unit
appears only with the plan. A saved expiration date remains visible even if
the SKU no longer requires one; otherwise the expiration line appears only
when the SKU requires a date. The Mobile LPN list follows SKU name order, with
empty LPNs last and code breaking ties. The card omits putaway quantities.
Manual SKU search sorts by displayed name and uses the
same right-aligned plan and fact, with a colored border for a known match or
difference; unverified or unknown-plan candidates keep a neutral border.
The receiving status vocabulary is shared across lists and details: «◇» means
ready order or available LPN; «○» means order in progress or open LPN; «✓»
means received order, closed LPN, or matched SKU; «≠» means a checked SKU
difference; «?» means unchecked fact; «⚠» means a failed 1C document check;
«⊘» means cancelled LPN and «×» deleted LPN. Waiting and unchecked states
use blue, active states amber, completed and matched states green, and
cancelled, deleted, failed checks and SKU differences red in both themes.
The outlined «◇», «○» and «⊘» use bold font weight for legibility.
Web receiving order and LPN statuses show the same colored symbols before their text.
Each status symbol has its full meaning in an accessible description. Order and LPN symbols sit beside their
numbers; SKU comparison symbols sit beside fact quantities; the document
warning sits beside its number. The SKU scanning hint is hidden
when the LPN is not editable.
Tapping an editable open LPN from its list or a SKU distribution opens one
editor with SKU selection, quantity, optional expiration date, saving, cancellation,
and eligible closing. An empty LPN opens with SKU search; an LPN with a SKU opens
with its saved quantity. Tapping a closed LPN in the list opens the same details
for reading, with quantity and expiration inputs disabled and no edit actions.
The expiration date picker appears beside its switch so that it stays visible
above the fixed action row on compact mobile screens.
The editor has separate «Сохранить», «Закрыть» and «Отменить» actions in its
bottom row; the «🔍 SKU» search action sits above the SKU content and becomes
«✕ SKU» while search is open. «Сохранить» keeps the LPN open and returns to the
LPN list, including after a confirmed retry and when opened from a SKU screen.
«Закрыть» confirms receipt posting, saves changed quantity, SKU, and date first,
then closes the LPN. A failed close leaves the saved LPN open on the editor for
review; uncertain save and close responses retain their respective command IDs
for explicit retry. «Отменить» cancels the open LPN after confirmation. The
full action descriptions remain available to a screen reader. The receiving
screens have no putaway actions; the independent putaway screen starts that
workflow by scanning a closed LPN. Zero checking remains available before
order completion. Closing and the separate putaway flow use retry-safe
commands. The document screen shows «Новый ордер» before creation and the WMS order
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
The Mobile LPN editor shows the date field when the current SKU flag is on
or a date was already saved on the open LPN. It retains and displays that saved
date after the flag is turned off. If closing is rejected after the flag or
another closing input changes, the LPN page refreshes its server view and
shows the specific server error without appending a generic retry message.
When an open LPN has no selected SKU, the editor immediately shows search in
the order composition, with a switch to the whole catalog, and accepts SKU
scans. A selected SKU replaces the search panel and has a cross beside its name.
The cross discards an unsaved selection locally or, after confirmation, clears
the saved SKU, quantity and expiration date on the server. The LPN remains open
and the order-scoped search appears again. A closed LPN has no cross. The bottom
actions remain «Сохранить», «Закрыть» and «Отменить».

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
entry; physical counting remains in Mobile. A Web Manager can close an open LPN
with a saved positive quantity using the receiving close command after reviewing
its SKU, quantity, plan, and expiration date. The confirmation rejects a changed
LPN revision; expired dates require explicit confirmation. Web no longer calls
the old order-completion command. Manager LPN quantity correction and deletion
are exposed. Only a Manager can complete an order in Web or reopen a completed
order into a new cycle. In Mobile, a current-cycle participant or Manager can
complete the order from its SKU or LPN list once every open LPN is closed or cancelled.
The mobile action confirms the decision and retains its command ID for retry
after an uncertain response. On successful completion or retry, Mobile returns
to the receiving order list, which refreshes on appearing. The Web page retains
the command ID for a retry after an uncertain response and reloads the saved
status and cycle snapshots after success.
The order card separates current plan/fact discrepancies from immutable cycle
completion snapshots and shows source check failures and unknown-plan reasons.
The Web plan/fact table also shows an SKU that currently has an open LPN, with
its stored plan and fact state, even before it enters the posted composition.
The order heading shows the number and creation time on the first row. A bordered,
evenly spaced second row shows the warehouse name, status, receiving location,
and receiving number. Plan/fact, receiving LPNs, documents, link history, and
receivings use the same expandable panel style with raised headers. Document,
LPN, and receiving counts appear in muted badges. The plan/fact panel starts open; the other sections start
closed. Each receiving card shows its location and completing user in the header,
with space above the first card. Expanding it immediately shows its SKU plan/fact
table without a separate label. The plan/fact table sorts
SKU by displayed name. Rows with known, equal plan/fact use the theme's subtle success
hover color; rows with known, differing plan/fact use the warning hover color;
rows without a checked fact or known plan remain unshaded. Receivings and their
SKU lists follow the document and link-history panels.
The Web receiving LPN panel uses a table sorted by displayed SKU name, with
LPNs without a selected SKU last and the LPN code breaking ties. Its columns show LPN,
status, SKU, expiration date, the current order plan and fact for that SKU, and
the LPN quantity. Managers can correct eligible closed LPNs and delete closed
LPNs through dialogs; deletion shows reverse movements and forecast balances
before confirmation. Clicking an LPN number with putaways or deletion history
expands its details. These show placed and remaining quantity, putaways with
quantity, cell, date and user, and deletion history with the confirming user's
name. Missing addresses use a dash; the original inventory movement ID remains
internal.
User-facing order screens call linked sources "Документы 1С" and receiving
cycles "Приёмки". Receiving history shows the completing user's display
name and a snapshot table with short plan and fact headers, without a version
list below the SKU rows. Link history shows the actor's display name.
A Manager can manually recheck every linked source from the card. In the Web
order list, ready orders have no color; active and completed orders use a subtle
warning row color when current SKU plan or fact is unknown or differs, and a
subtle success color otherwise. The list also marks completed orders whose
current discrepancy differs from their latest completion snapshot.
The Web order list reads a compact order summary with document and current SKU
counts, discrepancy count, and the post-completion discrepancy marker; the order
card reads full detail when opened.
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
