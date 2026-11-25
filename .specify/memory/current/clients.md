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

Administrators open «Переход со старой WMS» from «Конфигурация». They review
and confirm topology first, then select an XLSX file to preview and confirm
opening stock after SKU synchronization. The page lists blocking issues by
reason and shows warnings for mixed cells that need manual correction.
The Administrator acknowledges the warnings before confirming. The saved
protocol includes each stage's source hash, actor, time, accepted and excluded
counts, approved exceptions, and each imported mixed cell's current violation
status. It is available after selecting the warehouse again. A new stock preview
requires selecting the file again. See the
[opening stock rules](inventory.md#opening-stock-cutover).
The storage-location page shows the addresses and count of cells with a current
prohibited mixed-SKU balance, and the selected cell shows its distinct SKU count.
The location detail shows whether the cell allows multiple SKUs.

Operator SKU cards and lists in receiving, putaway and picking provide an «i»
action for [availability for new work](inventory.md#stock-availability) by cell
and expiration group in the selected warehouse. The report shows the accounting
balance separately when it differs from «Доступно». The unit appears once or is
marked unknown. The Web receiving plan and LPN rows use the same view. In Web
shipping, «План - Факт»
and the selected 1C shipping document's SKU table open it from their first column.
Mobile reads it through an Operator-only endpoint; Web uses the shared
application availability query with the same role check.

Mobile is online-only. Urovo ScanWedge intent input and the ML Kit camera
fallback implement one scanner boundary; operational screens prefer the
embedded scanner when available. Manual SKU search, where present, uses name,
code, or barcode and rejoins the same operation as scanning. Decimal 1C GUID
barcodes accept and normalize leading zeroes.

The receiving putaway destination scan accepts Комплектация and Хранение cells.

The Urovo ScanWedge profile must emit barcode data through its broadcast Intent
only. Simultaneous keyboard-wedge output is disabled because its trailing Enter
can activate a focused UI action after the Intent scan has already navigated.
Scanner-driven pages ignore queued scan callbacks from a previous period of
page activity, including after leaving and returning to the same live page.

## Inventory count

Web and TSD «Инвентаризация» show expected signed stock and counted fact by
SKU and expiration group. On TSD, a scan adds one to the confirmed group.
When a date is required, the operator can choose an existing date, enter the
label date or select «Срок неизвестен». Manual search can add a discovered
group and set its quantity. A Web Manager can change the cell's SKU-mixing
permission from «Инвентаризация» by choosing the warehouse and cell. See the
[count rules](other-workflows.md#inventory-count).

## Putaway history

Independent putaway starts from its own LPN screen. Its placement history shows
the destination cell address.

Web has a read-only placement history page under «Размещение» for Operator and
Manager. Its LPN number links to the associated WMS receiving order. It lists
the actor, LPN, SKU, quantity, destination address, warehouse and local time,
newest first, with server paging. The page filters
by inclusive local date range, LPN number, SKU name substring, user name or
login, and destination cell address; filters can be combined.

## Web picking assemblies

The Web «Отгрузка» group contains «Документы 1С» and «Расходные ордера WMS»
for Operator and Manager. The first page searches imported shipping orders by 1C number and
shows their stored source lines, warehouse, receiver, latest check and
active WMS assembly link when that assembly is accessible to the user. The
«Очередь / Статус» column shows the queue icon and name above the 1C status.
The assembly detail links back to each source document. The source page reads the
last saved WMS projection, not live 1C;
an unsuccessful check leaves the saved lines visible with its error. The second
page is the existing picking assembly list and detail, without a Web creation
action. Its aggregate SKU plan/fact rows are neutral before picking starts,
green when equal after it starts, and yellow when different. A linked 1C
document's plan versus allocated fact is colored only after assembly closure.

The Manager «Расходные ордера WMS» page lists picking assemblies across warehouses;
Operator sees all closed assemblies awaiting 1C exchange and can retry that
exchange regardless of who closed them. The detail header shows the WMS order
number, creation date, status and responsible user. «План - Факт» expands each
picked SKU to show the cell, expiration date, quantity, picker name and
confirmation time for each entry. One «Документы 1С» panel lists linked documents
with their status, receiver, queue icon and name, planned shipping date and latest check. Each
document expands to its SKU comparison, which shows «План WMS», «План 1С»
from the latest saved source and either the proposed or posted allocation. A SKU row can expand
when it combines multiple imported 1C lines from that document. A pending
manual allocation is identified in the comparison.

For a Manager, the header offers «Сверить с 1С» and «Закрыть и отгрузить».
The close action posts the shipment as part of assembly closure in Web and on the TSD,
using the same [outbound exchange](integration.md#outbound-source-and-exchange).
«Обновить» reloads saved WMS data and resets the current closure review. It
also clears the unsubmitted allocation when there is no close request to retry.
A fresh check retrieves each document, shows blockers and review issues near
the actions and offers «Распределить недобор» when a shared SKU needs a split.
The card prompts for the next step, and closing becomes available after the
check allows it and the Manager enters a reason. During picking, the Manager
sees «Изменить план» in each expanded document row. The action refreshes 1C
before opening the editor. Saving requires a reason and the reviewed source
snapshot. The close action checks 1C again and retains the same request ID
if its response is uncertain.
After warehouse closure, the same action retries exchange for unconfirmed
1C documents and shows their individual stages and errors. For a new source
discrepancy, the Manager reads and reviews the fresh 1C document, records a
reason, and then retries the exchange without changing the warehouse fact.
Physical picking quantities remain editable only through the TSD workflow.
«Подпитки» lists the assembly's ordered, in-progress, completed and cancelled
tasks with their ordered and transferred quantities. Each task with posted steps
expands to their original and current effective quantities, the replenisher who
confirmed each step and its confirmation time. A Manager can enter
the actual transferred quantity and reason. The dialog keeps its command ID for
an uncertain response. The separate
correction movement and task progress are described in [inventory](inventory.md).
The same Manager detail page lists picking shortage reports with separate SKU
and cell columns, expiration group, expected and found quantities, current held fact, blocked
quantity and resolution. Its decision dialog explains the stock effect:
confirming a shortage posts a correction for the blocked quantity; rejecting
the report releases the block without a stock movement. Both require a reason
and preserve the command ID for an uncertain reply. Resolving the cell shortage
does not change the assembly plan or approve shipping fewer units. When fact
is below plan, the Web card directs the Manager to recheck 1C, allocate a shared
SKU if needed, and close with a reason or wait for further picking. The cancellation action
requires a reason and zero remaining picked fact; the server also checks
reservations and returned pallets. An accepted cancellation may wait for
started replenishment and 1C status restoration.

## Mobile picking

The Mobile home «Отбор и Отгрузка» button opens the assembly queue.
Its cards show the assembly number and status on one line and list the linked
1C shipping document numbers below, each with its own queue icon, and exchange
warnings when applicable. The mobile «Документы 1С» section shows the icon and
queue name for each document. Mobile V1 includes the current queue type per
source in both the assembly list and details.

### Loading and diagnostics

Mobile assembly details contain the header, WMS plan and active source-document
statuses and exchange errors. Source JSON snapshots and detailed imported 1C
lines are read only by the Web detail query, not by this Mobile response.

SKU gets plan, fact, route availability and replenishment data from one response for
the responsible operator's active assembly. If that request fails, SKU loads
plan and fact separately and shows availability as unknown. The route response
includes the work rows used to calculate its steps. When navigation supplies a
just-read assembly for the same ID and warehouse, SKU reuses it once and still
refreshes work and availability. Ordinary returns and swipe refresh load
current assembly details. After a pick or shortage from the route, the fact
page refreshes plan and fact from the recalculated route.

The picking performance strip is off by default. Set
`EnablePerformanceDiagnostics` in `Resources/Raw/mobile-api.json` before
building Mobile to show the latest load and action on picking screens. Each
result shows elapsed time, API call count, total HTTP time and failures, build
configuration and debugger status. «Показать HTTP-запросы» expands it into calls
in start order with method, path, duration, status, «Сессия ТСД» and «API».
Missing timings appear as a dash. The expanded state persists between picking
screens. See [measurement boundaries](../architecture.md#client-organization).

### Assembly work and closure

The Mobile V1 picking API exposes the assembly work list with
per-SKU plan and confirmed fact, original-cell fact entries, a recalculated
route, SKU availability, scanned-cell inspection, pallet hold/return, pick
confirmation, scanned return and shortage reporting. It also exposes a fresh
closure check and operator-only warehouse close. Every mutating request carries
a client request ID and the expected assembly revision. A closed assembly
accepts a replay of its original close request without posting stock again;
the same close action with a new request ID retries unconfirmed 1C documents.
After the initial WMS close is confirmed from SKU or Route, Mobile returns to
the assembly queue, including when 1C exchange is still pending.
The close response includes the result for each document. On the Mobile 1C
page, each closed assembly's document card shows its exchange stage and any
exchange error. Closed
assemblies awaiting exchange remain in every Operator's work list for their
selected warehouse until all documents are confirmed. Picking uses the SKU base unit
`Description`, falling back to `Name`; the Mobile API exposes Russian display
names for 1C shipping document statuses. Its SKU, route and 1C document
sections share a compact bottom navigation row. Section changes replace
the current section in the stack, while pending commands block navigation;
unavailable work sections remain disabled. The first route card has a stronger
border and gray background. The SKU and route cards show the assembly's plan,
confirmed fact and availability from the [reserved route](inventory.md#picking)
per SKU. The SKU card breaks the total down into «Комплектация» and eligible
whole «Хранение» pallets, while each route card shows the SKU's total across
its steps. The unit appears next to plan. SKU cards use green when fact equals
plan, yellow for a positive fact that differs from plan and white for zero
fact against an outstanding plan. An expired reservation can add a warning
alongside the quantity. Unknown availability is shown as a dash.
Route cards appear in route order without step numbers. Each card shows its
address prominently with «Взять» and the step quantity on the same line, the
SKU name on the left, and plan, available and fact in a right-aligned column.
SKU cards show «Не хватает к отбору» when the route cannot cover the remaining
plan. The route lists the same shortfall by SKU below its steps, including when
it offers no steps. When the fact covers the plan, the empty route shows
«Отбор по маршруту завершён». The scan instruction and scanner are available
while there are route steps. The Route page refreshes the assembly status and
owner before requesting working steps and offers closing to the active owner.
Date and pallet details take a line only when present.
Scanning a cell on the current route
opens the first matching step's fact page with the cell automatically checked;
quantity and any whole-pallet action still require confirmation. An unmatched
scan stays on the route with an explanation. The route supports the device
scanner and camera. The picking SKU, route and 1C sections share the short
«Отбор · номер» navigation title; the active bottom button identifies the
section. The title uses a left-aligned, bounded MAUI Shell.TitleView to keep
the whole heading visible. The SKU, route and 1C pages label an active assembly
«В работе» and one awaiting the 1C transition «Запрос в 1С КОтбору».
Status and responsible worker share one line on all three pages, where
applicable. The worker receives the remaining width after a bounded status
label. The SKU section omits its redundant heading. Picking
sections and the assembly queue use animated operation indicators, while
swipe refresh retains the native RefreshView spinner.
The SKU card opens stock by cell through its info button. Its compact action row
places information and replenishment on the left and confirmed facts on the right.
Each fact retains its original cell address, quantity, expiration date when known,
and a return icon for the current owner of an active assembly. Facts from different
cells or expiration groups stay separate without repeating the SKU name in a
second list. Tapping the name/quantity block opens picking; action buttons and fact
rows do not open it. The card uses 36-by-36 action buttons with 24-by-24 symbols.
A replenishment icon
on that card uses blue for an available order action, yellow for ordered,
orange for in progress, and green only when currently pickable quantity covers
the whole remaining SKU plan and this SKU has a non-cancelled replenishment
task. The icon is absent when the SKU was already fully pickable without
replenishment. Where shown, it sits beside the information action. When the
remaining plan is not yet covered, in-progress tasks take precedence over
ordered tasks, and both retain their status color when another proposal exists.
Tapping that status opens a dialog with «Заказать ещё» when applicable.
A gray icon with a crossed arrow means no further
replenishment is currently available; tapping it shows a short explanation.
The icon is hidden when the SKU plan is already confirmed. The picking worker
does not see a separate replenishment section or individual task details.
### Cell selection, confirmation and return

Unavailable demand may require a Manager decision, and a wrong-cell message
identifies stocked SKU by name rather than a GUID. Visible cell addresses use
`StorageLocation.Name`, falling back to `Code`; scanning and validation continue
to use `Code`. Addresses are larger on picking and replenishment work screens,
including the shared stock-by-cell view. From SKU, the fact screen lists
positive accounting balances in active «Комплектация» cells and the free
expiration groups of eligible whole pallets in «Хранение». Each row shows
«Остаток» and «Доступно» for one cell and expiration group and flags included
expired stock. Shelf and pallet icons distinguish the two zones in the SKU
totals, route cards and cell list. The cell list sorts by
expiration ascending with unknown dates last, then by available quantity
descending and warehouse address descending. The info button opens the full
stock-by-cell report. «Доступно» is red when the row can be
picked now and its available quantity is below the remaining SKU demand.
Tapping a row uses the same source check as scanning its cell. The operator
confirms the offered quantity explicitly. The fact and return pages show plan,
confirmed fact and remaining or return quantity beside the SKU, with the unit
beside plan. They show the recommended or original address separately from the
scanned cell's stock and expiry. The device scanner and camera are available
from both SKU and the route, with list selection also available from SKU.
An animated MAUI ActivityIndicator shows loading and command execution.
Confirmation and pallet actions stay below the scrolling content. The shortage
hint appears only for a scanned pick, not a return. Inputs and actions are
disabled while a command is running or awaiting an explicit retry.
Opened from the route, the fact card refreshes the next route step in place.
When no route steps remain after a pick or shortage confirmation, the app
returns to the SKU list, where all outstanding quantities are visible.
Opened from SKU, it returns to that SKU list after each successful pick or
shortage confirmation, even if the SKU is not complete. An uncertain response
keeps the page open; a successful explicit retry follows the same navigation.
Holding a pallet keeps the page open. Entering less than the offered quantity,
including zero, opens the [shortage](inventory.md#picking) confirmation with
the offered pick, expected physical quantity for the whole group, found amount
and amount to block.
When closure needs a Manager, the TSD shows the reasons in a visible dialog.
Returning a confirmed fact requires scanning its original cell and confirming
the physical return. Other SKU remain selectable when one is unavailable.
A fresh closure check prevents the TSD Operator from overriding
plan or source discrepancies.

## Mobile replenishment

Mobile V1 exposes Operator-only replenishment proposals and existing tasks
for a picking assembly, an explicit order command, the replenisher's ordered
and active queue, and task detail with source cycles, actual addresses, dated
transfer history and available stock groups on the currently held source.
Source scans, dated transfers and source completion use separate commands.
Each mutation carries a client request ID. Callers retain the exact request
for an explicit retry after an uncertain response. From SKU, the operator
orders the current proposals for one SKU in one action.
The home screen opens the replenisher's warehouse queue in shipping priority
order. Queue cards show the priority icon and label next to the small assembly
number, then the SKU, separate address rows with outgoing and incoming icons,
posted quantity out of ordered quantity, and status. Mobile V1 provides the
assignee's display name in the queue response. The card shows it next to
«В работе» when available.

Task detail shows the held source, available quantities by expiration
for the requested SKU, other SKU for information, and posted transfers. The
«Хранение» card shows the recommended address before removal and then the
actual source address for return. A separate card shows the recommended
«Комплектация» destination. A scan or a tap on an address card opens the
confirmation dialog for that step. The worker verifies the expiration group
and transferred quantity on the page before scanning or tapping the
destination. When a recommendation is absent, the worker scans the address.
While the source is held, scanning its address opens source completion,
including after a partial transfer or when the pallet has no goods left.
Additional scans are ignored while the dialog or command is active. If the
task still has a remaining quantity, the page prompts for the next source. It
retains the same command ID for explicit retry after an uncertain response and
reconstructs unfinished work from server detail.

## Mobile receiving


The Mobile receiving queue lists unfinished WMS orders for the selected
warehouse; received orders are hidden from the queue. A linked received order
can still be opened by scanning its 1C document.
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
A barcode action follows the zero-confirmation button when it is present and
occupies the same action area otherwise. Its icon is prominent when the SKU has
at least one barcode and pale when it has none; either state opens the add panel
for a current-cycle participant. The panel captures device or camera scans
without triggering the SKU list's usual scan action, shows the selected SKU and
captured value, and requires an explicit save. A confirmed save closes the panel
and refreshes the order; an uncertain response keeps the same request for an
explicit retry.
If a scanned value belongs to another SKU, the conflict shows its name, or its
code when the name is missing, without a technical identifier.
Closing the add panel or starting a new scan clears its previous error.
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
The empty receiving LPN editor immediately lists the same current SKU as the
order SKU screen, sorted by displayed name. It hides matched SKU by default:
a match has a known plan and a checked equal fact, including a confirmed zero.
An eye button in the bottom action row shows or hides matched SKU; it starts
crossed out for each newly opened LPN. Hiding changes only this list, not SKU
selection by scan or catalog search. The adjacent search button opens manual
search across the entire synchronized catalog and closes it back to the order
list. The eye button is absent while catalog search is open. With no selected
SKU, these buttons share the bottom row with Cancel; after selection the row
shows Save, Close, and Cancel. Both the order list and catalog results sort by
displayed name and show right-aligned plan and fact. Known matches use a soft
green card background, checked differences a soft yellow one, and unchecked or
unknown-plan candidates a neutral background.
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
starts with the current order SKU list; a populated one shows its saved quantity.
A closed LPN opens read-only, without edit actions or a SKU scanning hint.
The editor has separate «Сохранить», «Закрыть» and «Отменить» actions in its
bottom row after SKU selection; while no SKU is selected that row has the eye,
«🔍 Поиск», and «Отменить» actions. Search becomes «✕ Поиск» while catalog
search is open. «Сохранить» keeps the LPN open and returns to the
LPN list, including after a confirmed retry and when opened from a SKU screen.
«Закрыть» confirms receipt posting, saves changed SKU, quantity and date, then
closes the LPN and opens the order SKU list, whether the LPN was entered from
the SKU detail or LPN list. A failed close leaves the saved LPN open. Uncertain
save and close responses retain separate command IDs for explicit retry. «Отменить»
requires confirmation. Action descriptions remain available to a screen reader.
Receiving screens have no putaway actions; putaway starts from its own LPN screen.

The document screen shows «Новый ордер» before creation and the WMS number
after the first scan. Documents within an order sort by 1C number ascending,
with missing numbers last. Each document row
shows number and date, then sender name and warehouse operation, with no field
labels; a tap reveals other source details and any latest check error. A failed
check marks its row with a warning symbol. The newest attached document is
highlighted gray. A sender mismatch shows an error without leaving the screen.
A cross detaches a document before the first posted LPN. Before the first scan,
section navigation is hidden; afterward it appears without a «Готово» action.
Back from an existing order returns to the queue. An uncertain response retains
the barcode or source ID and command ID for explicit retry, including creation.
The LPN editor shows an empty date field when a selected SKU tracks expiration.
A saved date remains visible after the flag turns off. See the
[receiving expiry checks](receiving.md#cycle-and-receiving-work) for saving and closing.
A rejected close after a changed input refreshes the LPN and shows the specific
server error.
When an open LPN has no selected SKU, the editor shows the current order SKU
list and accepts SKU scans. A selected SKU replaces the list and has a cross
beside its name.
The cross discards an unsaved selection locally or, after confirmation, clears
the saved SKU, quantity and expiration date on the server. The LPN remains open
and the filtered order SKU list appears again. A closed LPN has no cross.

A current-cycle Operator can complete an order from the SKU or
LPN list when no LPN remains open. Mobile confirms the decision, retains the
command ID for uncertain retry and shows the completed order's SKU list on
success, including after a confirmed retry.

## Web receiving

The Web menu group «Приёмка» opens separate 1C documents and WMS orders pages.
`/receiving-orders` lists only WMS orders by their own numbers and actual
creation times, including deleted orders. `/receiving-sources` lists both free
and linked 1C documents by 1C number ascending, with missing numbers last,
and shows their WMS order
link. A direct link to a document brings it to the top of the filtered list.
Managers may select free documents of one warehouse,
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
Only a Manager may complete or reopen an order in Web. The lifecycle and order
deletion buttons share one aligned row with spacing. When open LPNs block
completion, its disabled button explains that they must be closed or cancelled
in its title. The reopen button's title explains that reopening starts a new
cycle without a location or participants and retains prior data. An uncertain
completion retains its command ID for explicit retry; success reloads status
and cycle snapshots.
The order card separates current plan/fact discrepancies from immutable cycle
completion snapshots and shows source check failures and unknown-plan reasons.
Below the heading, the order shows warehouse, status, receiving location and
cycle number. Plan/fact, LPNs, documents, link history and cycles use expandable
panels; plan/fact starts open, the others closed. Cycle cards head with location
and completing user and expand directly to a SKU plan/fact table. The current
plan/fact table sorts by SKU name and shades known matches and differences;
unchecked facts or unknown plans remain unshaded.
The LPN table sorts by SKU name, with empty LPNs last and code breaking ties.
It shows status, a placement checkmark when any putaway exists for that LPN,
SKU, date, order plan and fact, and LPN quantity. Manager correction and
deletion use compact icon buttons with title text and dialogs; deletion
previews reverse movements and forecast balances. An LPN with putaway or
deletion history expands to placed and remaining quantity, putaway quantity,
cell, date and user, and the deletion actor. Missing addresses use a dash.
A Manager sees the order deletion button
until the order is deleted. Once any LPN has putaway history, the button is
disabled and its title explains why. The dialog shows all LPNs, compensating
movements, and one combined balance forecast; a negative final balance needs
explicit acknowledgement. An uncertain response retains the request ID for
retry. Deleted orders remain in the Web list and open read-only, with the
deletion actor and time, LPN deletion details, movement history, and detached
source history. Mobile hides deleted orders from active work and direct reads.
User-facing screens call linked sources "Документы 1С" and receiving cycles
"Приёмки". Cycle history shows the completing user's name and a plan/fact
snapshot; link history shows the actor's name. In the order list, ready orders
are uncolored; active and completed orders show subtle warning or success color
for current discrepancies. Completed orders also mark a change since their
latest cycle snapshot.
The order card lists linked documents by 1C number ascending, with missing
numbers last, and shows number, date, sender, warehouse and operations. Its
recheck button shows the latest attempt time and checks all
linked sources. A Manager changes composition in a separate dialog with live
number filters. Before confirmation it shows the document, warehouse, changed
SKU plans and fact-preservation warning; a stale preview needs a new selection.
An uncertain response retains its request ID for retry; success reloads plan,
fact and links. Operators view composition without change controls.

## Web barcode catalog

The barcode list searches values and SKU names or codes, sorts and pages on the
server, and can include inactive SKUs. A «Только дубли» switch shows values
linked to more than one distinct SKU among the visible catalog rows. Each row
shows its source and duplicate status. A Manager can confirm removal of a
WMS-owned association; 1C-owned rows have no removal control. A stale
confirmation for a removed or promoted row produces a refreshed list and an
error message. An uncertain
removal retains its request ID for explicit retry.

## Mobile input

The API address is packaged per build configuration and is not editable by the
operator.

Mobile text fields open the keyboard only on an explicit tap; scans, search and
quantity steps do not focus them automatically. Completing input or tapping
outside dismisses the keyboard. Android suppresses the keyboard on native focus
changes and requests it on explicit field clicks.

Client organization, receipt compatibility, error codes and retry rules belong
in [architecture](../architecture.md#client-organization).
