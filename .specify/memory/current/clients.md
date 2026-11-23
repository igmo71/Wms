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

The Administrator-only «Конфигурация» menu links to a separate «Переход со
старой WMS» page. The page selects a warehouse, previews and confirms topology
first, then previews and confirms opening stock from a separately selected XLSX
after SKU synchronization. It pages topology cells and decisions plus stock
rows, totals and issues, shows source hashes and
signed/date/mixing exceptions, groups blocking stock issues by reason, and
requires review before confirmation. The import rules are in
[inventory](inventory.md).
Reopening the page and selecting the warehouse reads the saved stage result
without the XLSX file; the file must be selected again to preview the stock stage.
Its compact protocol shows separate topology and stock source identities,
actors and times, accepted and excluded counts, signed and undated stock
exceptions, approved exclusions and corrections,
and the current status of reviewed mixed cells.
The storage-location page shows the addresses and count of cells with a current
prohibited mixed-SKU balance, and the selected cell shows its distinct SKU count.
The location detail shows the multiple-SKU permission. In Web «Инвентаризации»,
a Manager selects a warehouse and cell to change that permission.

Web and TSD «Инвентаризация» show separate rows for the expiration groups of
each SKU, including the expected signed balance and the counted fact. The TSD
scan adds one to the confirmed group. When a scan needs a date, the operator
can choose a displayed date, enter a date from the label or select «Срок
неизвестен». Manual search can add the discovered group and set its quantity.
The [count rules](other-workflows.md#inventory-count) apply in both clients.

Operator SKU cards and lists in the replacement receiving, putaway and picking
workflows provide a small «i» action.
The read-only detail shows the SKU's locations and expiration groups in the
selected warehouse. Available quantity is primary; accounting quantity is
shown separately only when it differs because of reservations, shortage blocks
or a whole-cell hold. The unit is shown once, or marked unknown. The Web
receiving plan and LPN rows use the same view. In Web shipping, «План - Факт»
and the selected 1C shipping document's SKU table open it from their first column.
Mobile reads it through an Operator-only endpoint; Web uses the shared
application availability query with the same role check.

Mobile is online-only. Urovo ScanWedge intent input and the ML Kit camera
fallback implement one scanner boundary; operational screens prefer the
embedded scanner when available. Manual SKU search, where present, uses name,
code, or barcode and rejoins the same operation as scanning. Decimal 1C GUID
barcodes accept and normalize leading zeroes.

The receiving putaway destination scan accepts Комплектация and Хранение cells.
Mobile V1 uses the `PutawayDestination` request context for that scan and returns
the actual `Storage` or `Reserve` zone type. Other scans using `Storage` remain
restricted to Комплектация cells.

The Urovo ScanWedge profile must emit barcode data through its broadcast Intent
only. Simultaneous keyboard-wedge output is disabled because its trailing Enter
can activate a focused UI action after the Intent scan has already navigated.
Scanner-driven pages ignore queued scan callbacks from a previous period of
page activity, including after leaving and returning to the same live page.

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
picked SKU to its individual cell, expiration date, quantity and picker name
records. One «Документы 1С» panel lists linked documents with their status,
receiver, queue icon and name, planned shipping date and latest check. Each
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
expands to their original and current effective quantities. A Manager can enter
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

## Mobile picking API

The Mobile home «Отбор и Отгрузка» button opens the assembly queue.
Its cards show the assembly number and status on one line and list the linked
1C shipping document numbers below, each with its own queue icon, and exchange
warnings when applicable. The mobile «Документы 1С» section shows the icon and
queue name for each document. Mobile V1 includes the current queue type per
source in both the assembly list and details.
The Web «Отгрузка» navigation opens its source documents and WMS orders for
Operator and Manager.

Mobile assembly details contain the header, WMS plan and active source-document
statuses and exchange errors. Source JSON snapshots and detailed imported 1C
lines are read only by the Web detail query, not by this Mobile response.

The SKU screen receives plan/fact rows, free stock by zone, route coverage and
replenishment data from one replenishment response for the active owner's
assembly. The route response includes the work rows used to calculate its steps
and their cells' free stock. These screens
therefore do not request the same work rows separately on a successful refresh.
When opened from the queue, SKU reuses the just-read assembly details once.
Ordinary returns to SKU and swipe refresh fetch current data. A successful SKU
command reuses its refreshed assembly details when loading the new work state.
When a successful fact-page command returns directly to SKU, it passes its
just-read assembly details to that page for one appearance, avoiding a second
assembly lookup. This applies to pick confirmation, shortage reporting and
completed goods or pallet returns; work and availability are still refreshed.
The handoff checks the assembly and warehouse IDs and does not clear the
previously displayed work rows before the new data arrives. If the
replenishment request fails, SKU still reads its plan and fact separately and
leaves availability unknown.
After a pick or shortage from the route, the fact page uses the next route's
included work rows to refresh plan and fact without a separate work request.

The picking performance strip is off by default in both Debug and Release.
Set `EnablePerformanceDiagnostics` to `true` in `Resources/Raw/mobile-api.json`
before building the Mobile app to enable measurement and show the strip on the
picking queue, SKU, route, 1C-document and fact screens. It retains the latest
measured load and action, including action results after returning to SKU.
Measurements cover opening an
assembly, screen refreshes, cell checks and commands from SKU, fact and 1C
documents. Each result shows elapsed time, API-call count, summed HTTP time,
HTTP failures when present, build configuration and whether a debugger is attached.
The «Показать HTTP-запросы» control expands the latest load and action into
individual calls in start order, with their HTTP method, shortened API path,
duration and response status or absence of a response. The expansion choice
persists between picking screens; the bounded details area scrolls when needed.
Each call also shows «Сессия ТСД», the time preparing authentication on the device,
and «API», the server processing time up to the start of the response. Session
preparation includes secure-storage reads and any token-refresh request or wait.
An attempted token refresh is marked «обновление токена» in that call's details.
API time includes server authentication and endpoint processing; it excludes
the remaining response serialization, transfer and client deserialization.
Missing measurements are shown as a dash. These times are parts of HTTP duration,
not extra durations to add to it.
Elapsed time covers the measured client operation and its nested data loads;
it does not measure the first displayed frame. HTTP time includes authentication,
network, server processing and response download, rather than SQL time alone.

The replacement Mobile V1 picking API exposes the assembly work list with
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
selected warehouse until all documents are confirmed. The replacement TSD screens
show plan, free stock and confirmed fact per SKU with white,
yellow and green progress,
and preserve the unit next to the plan quantity. Picking uses the SKU base unit
`Description`, falling back to `Name`; the Mobile API exposes Russian display
names for 1C shipping document statuses. Its SKU, route and 1C document
sections share a compact bottom navigation row. Section changes replace
the current section in the stack, while pending commands block navigation;
unavailable work sections remain disabled. The first route card has a stronger
border and gray background. The SKU and route cards both show the assembly's
plan and confirmed fact per SKU. «Доступно» is the free accounting quantity
after reservations, disputed quantity and cell holds, independent of the
quantity suggested by the route. The SKU card totals active «Комплектация»
cells and adds a separate amount for the whole «Хранение» pallets offered in
the cell list. The route card totals the current SKU across all expiration
groups in its own cell. Included expired stock contributes to «Доступно» and
adds a warning icon alongside the corresponding quantity. Unknown availability
is shown as a dash. Eligibility to pick follows the
[inventory rules](inventory.md#stock-availability).
Route cards appear in route order without step numbers. Each card shows its
address prominently with «Взять» and the step quantity on the same line, the
SKU name on the left, and plan, available and fact in a right-aligned column.
SKU cards show «Не хватает к отбору» when the route cannot cover the remaining
plan. The route lists the same shortfall by SKU below its steps, including when
it offers no steps. Date and pallet details take a line only when present.
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
Tapping a row checks its specific expiration group through the same source
inspection used by cell scans. The operator confirms the offered quantity
explicitly. The fact and return pages show a compact SKU
card with plan, confirmed fact and remaining or return quantity, with the unit
beside the plan. The recommended or original cell has a separate large address;
after a pick scan, the actual cell address appears above its stock and expiry.
The fact and return pages show plan, fact and remaining or return quantity in
three compact rows beside the SKU, with small captions and ordinary sized SKU
and quantity values. Cell addresses remain 22 point. The assembly number is shown in smaller
type beside the page title, truncated visually if needed. From the route and
for returns, cell input uses the device scanner or camera. From SKU, both
scanner sources are available alongside list selection.
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
Holding a pallet does not leave the page. Its one confirmation button records a full offered pick normally.
A smaller entered quantity, including zero, opens a shortage confirmation:
the entered amount is the entire remaining physical quantity of that SKU and
expiration group in the scanned cell. The prompt shows the offered pick, free
accounting quantity, found amount and amount to block. Confirmation reserves
the found fact and reports the shortage in one replay-safe command; cancelling
changes nothing. The former separate picking-shortage button is removed.
When closure needs a Manager, the TSD shows the reasons in a visible dialog.
Returning a confirmed fact requires scanning its original cell and confirming
the physical return. Other SKU remain selectable when one is unavailable.
A fresh closure check prevents the TSD Operator from overriding
plan or source discrepancies.

Mobile V1 also exposes Operator-only replenishment proposals and existing tasks
for a picking assembly, an explicit order command, the replenisher's ordered
and active queue, and task detail with source cycles, actual addresses, dated
transfer history and available stock groups on the currently held source.
Source scans, dated transfers and source completion use separate commands.
Each mutation carries a client request ID. Callers retain the exact request
for an explicit retry after an uncertain response. These
routes and the client serve the replacement Mobile screens. The picking SKU
screen orders all current proposals for one SKU in one replay-safe command.
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
The LPN editor shows the date field immediately when a selected SKU tracks
expiration, with no preselected date or enable switch. Saving or closing the
LPN requires an explicit date no earlier than the current UTC day. A saved date
remains visible after the flag turns off; closing rejects it if it has expired.
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
