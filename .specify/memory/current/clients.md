# Current client behavior

This page describes the implemented Web and Mobile interfaces.

## Shared behavior

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

The Urovo ScanWedge profile must emit barcode data through its broadcast Intent
only. Simultaneous keyboard-wedge output is disabled because its trailing Enter
can activate a focused UI action after the Intent scan has already navigated.
Scanner-driven pages ignore queued scan callbacks from a previous period of
page activity, including after leaving and returning to the same live page.

## Putaway history

Independent putaway starts from its own LPN screen. Its placement history shows
the destination cell address.

Web has a read-only placement history page under «Размещение» for Operator and
Manager. It lists the actor, LPN, SKU, quantity, destination address,
warehouse and local time, newest first, with server paging. The page filters
by inclusive local date range, LPN number, SKU name substring, user name or
login, and destination cell address; filters can be combined.

## Mobile receiving

The Mobile receiving queue lists WMS orders for the selected warehouse.
«Создать ордер» opens an empty document screen; the first successful scan creates
the order, and later scans attach documents. Scanning on the queue opens a
linked order or creates one from a free document. A scan of an already linked
document on the empty creation screen offers to open its order. Back before the
first scan creates nothing; afterward the order remains in the queue. The queue
accepts the device scanner or camera fallback without loading free documents, shows the
WMS number, status symbol and local creation time, and reads full detail only
when an order is opened.
The SKU, LPN and document screens show the order number with the same status
symbol; the SKU and LPN screens show «Место приёмки:» and its address on the
next line, or «Место не выбрано» until a location is selected. They omit
creation time and warehouse from the compact heading. An existing order has
separate SKU, LPN, and document screens headed «Приёмка › SKU»,
«Приёмка › LPN», and «Приёмка › Документы 1С». Each screen offers only the two
other sections as buttons, in SKU, LPN, 1C order. Sections use the same default
transition and replace the current section in the navigation stack; Back returns
to the queue, while a detail page returns to its originating section. A shared
join screen is reachable from all three sections and returns to the origin. Its
entry reads «Начать» for a ready order or «Присоединиться» for one in progress;
location selection appears only when starting a cycle. Pending commands block
navigation until explicit retry.

The SKU and LPN lists accept LPN and SKU scans through the device scanner or
camera. A closed LPN in the order opens read-only without joining the cycle;
a scanned SKU opens its distribution card. An SKU outside the order requires
confirmation before adding it with plan zero. Quantity entry stays in an open
LPN. The SKU list sorts by displayed name without row numbers,
using code and ID when a name is unavailable. Each SKU card keeps its name left
and semibold plan and fact on two right-aligned lines without labels (plan above
fact), with the unit shown once. The compact zero-confirmation button sits below
the fact in the quantity block.
An unchecked fact is a blue question mark on a neutral card. Equal known plan
and fact show a green «✓» beside the fact and a soft green background; a checked
difference shows a red «≠» beside the fact and a soft yellow background.
An unknown plan leaves the card neutral and its known fact has no comparison
symbol.
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
warning sits beside its number.
An open LPN opens one editor from its list or a SKU distribution. An empty LPN
starts in SKU search; a populated one shows its saved quantity. A closed LPN
opens read-only, without edit actions or a SKU scanning hint.
The editor has separate «Сохранить», «Закрыть» and «Отменить» actions in its
bottom row; the «🔍 SKU» search action sits above the SKU content and becomes
«✕ SKU» while search is open. «Сохранить» keeps the LPN open and returns to the
LPN list, including after a confirmed retry and when opened from a SKU screen.
«Закрыть» confirms receipt posting, saves changed SKU, quantity and date, then
closes the LPN and opens the order SKU list, whether the LPN was entered from
the SKU detail or LPN list. A failed close leaves the saved LPN open. Uncertain
save and close responses retain separate command IDs for explicit retry. «Отменить»
requires confirmation. Action descriptions remain available to a screen reader.
Receiving screens have no putaway actions; putaway starts from its own LPN screen.

The document screen shows «Новый ордер» before creation and the WMS number
after the first scan. Each document row
shows number and date, then sender name and warehouse operation, with no field
labels; a tap reveals other source details and any latest check error. A failed
check marks its row with a warning symbol. The newest attached document is
highlighted gray. A sender mismatch shows an error without leaving the screen.
A cross detaches a document before the first posted LPN. Before the first scan,
section navigation is hidden; afterward it appears without a «Готово» action.
Back from an existing order returns to the queue. An uncertain response retains
the barcode or source ID and command ID for explicit retry, including creation.
The LPN editor shows the date field immediately when a selected SKU tracks
expiration, with no preselected date or enable switch. Saving or closing the
LPN requires an explicit date no earlier than the current UTC day. A saved date
remains visible after the flag turns off; closing rejects it if it has expired.
A rejected close after a changed input refreshes the LPN and shows the specific
server error.
When an open LPN has no selected SKU, the editor immediately shows search in
the order composition, with a switch to the whole catalog, and accepts SKU
scans. A selected SKU replaces the search panel and has a cross beside its name.
The cross discards an unsaved selection locally or, after confirmation, clears
the saved SKU, quantity and expiration date on the server. The LPN remains open
and the order-scoped search appears again. A closed LPN has no cross.

A current-cycle Operator can complete an order from the SKU or
LPN list when no LPN remains open. Mobile confirms the decision, retains the
command ID for uncertain retry and shows the completed order's SKU list on
success, including after a confirmed retry.

## Web receiving

The Web menu group «Приёмка» opens separate 1C documents and WMS orders pages.
`/receiving-orders` lists only WMS orders by their own numbers and actual
creation times. `/receiving-sources` lists both free and linked 1C documents
with their WMS order link. Managers may select free documents of one warehouse,
inspect their summed plan and create an order. Clicking a source shows its last
saved 1C lines, including when linked; selecting free sources shows their
combined plan. The source and SKU tables sit side by side on wide screens and
stack on narrow ones; the active or selected source has a subtle gray background.
Both SKU tables sort by displayed name. A document number on the order card
links to its highlighted row and lines on the source page.
Receiving lists and cards show synchronized warehouse, SKU and partner names,
local receiving and putaway addresses, and IDs for unresolved references. The
order card omits the sender-type suffix and heads with its WMS number and actual
creation time; its route uses the internal ID. It shows current plan and fact,
source versions and errors, LPN quantities, dates and putaway history.
Each receiving LPN shows its own status; its active SKU row shows the expiration
date or "Не указан" without a separate item-state label.
Unchecked fact is textual; different SKU quantities are not totaled together.
A Web order card has no controls to select an SKU or enter a quantity into an
open LPN; physical counting stays in Mobile. A Manager may close a saved,
positive LPN after reviewing its SKU, quantity, plan and date. Closing rejects
a changed LPN revision or an expired date; expired stock cannot be confirmed.
Only a Manager may complete or reopen an order in Web. An uncertain completion
retains its command ID for explicit retry; success reloads status and cycle
snapshots.
The order card separates current plan/fact discrepancies from immutable cycle
completion snapshots and shows source check failures and unknown-plan reasons.
Below the heading, the order shows warehouse, status, receiving location and
cycle number. Plan/fact, LPNs, documents, link history and cycles use expandable
panels; plan/fact starts open, the others closed. Cycle cards head with location
and completing user and expand directly to a SKU plan/fact table. The current
plan/fact table sorts by SKU name and shades known matches and differences;
unchecked facts or unknown plans remain unshaded.
The LPN table sorts by SKU name, with empty LPNs last and code breaking ties.
It shows status, SKU, date, order plan and fact, and LPN quantity. Manager
correction and deletion use dialogs; deletion previews reverse movements and
forecast balances. An LPN with putaway or deletion history expands to placed
and remaining quantity, putaway quantity, cell, date and user, and the deletion
actor. Missing addresses use a dash.
User-facing screens call linked sources "Документы 1С" and receiving cycles
"Приёмки". Cycle history shows the completing user's name and a plan/fact
snapshot; link history shows the actor's name. In the order list, ready orders
are uncolored; active and completed orders show subtle warning or success color
for current discrepancies. Completed orders also mark a change since their
latest cycle snapshot.
The order card lists linked documents with number, date, sender, warehouse and
operations. Its recheck button shows the latest attempt time and checks all
linked sources. A Manager changes composition in a separate dialog with live
number filters. Before confirmation it shows the document, warehouse, changed
SKU plans and fact-preservation warning; a stale preview needs a new selection.
An uncertain response retains its request ID for retry; success reloads plan,
fact and links. Operators view composition without change controls.

## Mobile input

The API address is packaged per build configuration and is not editable by the
operator.

Mobile text fields open the keyboard only on an explicit tap; scans, search and
quantity steps do not focus them automatically. Completing input or tapping
outside dismisses the keyboard. Android suppresses the keyboard on native focus
changes and requests it on explicit field clicks.

Client organization, receipt compatibility, error codes and retry rules belong
in [architecture](../architecture.md#client-organization).
