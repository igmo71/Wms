# Current receiving and LPN

This page records active receiving and putaway behavior. The base lifecycle
decisions are in [epic 001](../../../specs/001-receiving-stock-lifecycle/spec.md);
WMS numbering and source-composition decisions are in
[feature 002](../../../specs/002-receiving-workspaces/spec.md).

## Sources and WMS orders

One WMS receiving order has its own ID, immutable human-readable `yyMMdd-HHmmss`
number, and fixed warehouse. The number starts from the local second of creation;
if occupied, creation takes the next free second under a transaction lock. The
actual `CreatedAtUtc` remains unchanged by a number collision. It may combine
several distinct 1C documents identified by `Ref_Key`. Web can create the order
from free sources; Mobile creates it from the first scanned document and attaches
subsequent scans to the unfinished order. A linked document can open its WMS order.
The mobile queue does not load a list of free 1C documents. Active source links
retain history. Repeating the same complete selection returns the existing
order. Creation records the acting user, source versions, and command receipt
with an audit event in the same transaction. Repeating the same request ID
returns its original order without another creation event.
The number migration assumes an empty `ReceivingOrders` table. It adds a
required number column and unique index without historical backfill.
Receiving notifications update only the 1C source mirror and the current plan
of an already linked WMS order; they never create a WMS order.
The latest successful version supplies each source's own number, date, sender,
warehouse and business operations, and basis document in order details. Their
display values remain properties of individual sources.
The order also stores the common 1C sender GUID independently of the sender
reference type. Creation and attachment require a valid sender GUID and reject
documents from a different sender. After the first posted receiving LPN the
sender stays fixed even if all sources are detached, the LPN is deleted, or the
order is reopened. Before the first posted LPN, detaching the last source clears
the sender so another sender may be selected. Existing orders without a
confirmed sender need separate data review before new attachment.

Successful source checks retain separate source lines and immutable versions.
The current plan sums known contributions by SKU. An unposted, deleted, or
different-warehouse source contributes zero. An unresolved quantity or metadata
makes only the affected known SKU plan unknown; valid plans for other SKUs
remain visible. Former SKU rows remain stored with zero when their contribution
disappears, preserving fact, zero confirmations, and open LPN work. The current
Web and Mobile plan/fact list shows only SKU from the latest versions of linked
sources or active closed LPNs; former SKU without either is hidden. A posted
SKU without a current source line remains visible with known plan zero and its
fact. Newly known rows are added. An identical check does not advance
the order revision. A failed check records the error and retains the last
successful version and plan. Source changes do not rewrite the WMS warehouse,
fact, LPNs, movements, or lifecycle status.
If a linked 1C document changes its sender or loses a valid sender GUID,
its new version is rejected as a failed check; the previously accepted version
and plan remain in force.

An Operator or Manager may attach a freshly checked source through Mobile to an
unfinished order when its warehouse matches. An Operator may detach one through
Mobile before the first posted receiving LPN. Web source changes require Manager
and a matching preview token, including before the first posted LPN. After
completion, the Web-channel command permits attachment and detachment with the
same Manager requirement. Detaching the last source retains the order and its stored
SKU rows with zero plan. Web Managers can check all linked sources again,
including on a completed order; the view distinguishes an empty source list,
last successful checks, and latest attempt errors. Source changes and manual
checks retain command audit.
The Manager-only source-composition preview checks the target and shows changed
SKU plans before and after, posted fact, and projected excess without changing
the order, fact, or movements. For attachment
it first refreshes the free 1C source. The returned state token covers the order
revision, active links, source versions, and target link.
Stale Web preview state, including a changed fact, is rejected before its changes
are saved. The Web order shows current discrepancies separately from preserved
cycle snapshots and exposes manual source attachment/detachment audit with the
document, actor, and time. Command receipt replay still returns the original
result. Mobile source changes retain their existing
request and receipt behavior.

Mobile SKU search offers stored order SKU and the synchronized catalog by name,
code, or barcode. A scanned barcode resolves against the entire catalog even
when manual search is scoped to the order. Adding a catalog SKU creates one
stored order row with known plan zero and unchecked fact; it enters the current
plan/fact list when it appears in a linked source or a receiving LPN is posted.
Repetition does not duplicate it. Later source checks can update that row's plan.

## Cycle and receiving work

The WMS order lifecycle is ready for receiving, in receiving, or received.
Joining checks linked sources and their posting, deletion mark, and warehouse.
The first current-cycle participant selects an active unlocked Receiving
location in the fixed warehouse. Later participants use that same location.
Joining does not write to 1C. Mobile lists WMS orders for the selected warehouse;
the order screen shows plan and fact separately.

A participant binds an issued label to an open receiving LPN. Its order,
creator, responsible user, creation time, and original receiving location are
retained. The code cannot be reused for another order, and one employee may
hold several open LPNs. An open receiving LPN has at most one current SKU row
with an absolute nonnegative quantity and optional expiration date. The
responsible user may edit or cancel it, including after editing; a Manager may
edit or cancel another user's open LPN. Drafts and cancellation do not post
stock. A current-cycle participant may explicitly confirm a SKU as checked
zero when no active LPN contains it. An absent fact remains unchecked.

Closing requires one SKU and a positive quantity. It checks every linked 1C
source first and uses the current plan when that succeeds. On a failed check it
uses the last numeric plan, marked unconfirmed. Plan excess or an unknown plan
requires a Manager. The close posts one receipt at the LPN's original Receiving
location, updates the order's checked fact from closed LPNs, and commits
movement, audit, and command receipt together. Concurrent changes to its
decision inputs reject all staged effects. A closed LPN cannot reopen.
When every source has been detached during an active cycle, the known plan is
zero; closing a positive LPN then requires a Manager.

The local SKU expiration-tracking flag belongs to WMS. A Web Manager can switch
it with an audit event; catalog imports preserve it. An open LPN may keep a
quantity while its date is unset. Closing a positive LPN for an SKU whose flag
is enabled requires a date. A date before the UTC closing day requires explicit
confirmation by the closer, with no higher role; today's date does not. Saved
dates survive a later flag change and do not merge stock groups.

A current-cycle participant or Manager can complete an order when no receiving
LPN is open. Completion tries to check every linked source, then stores an
immutable snapshot of that cycle's stored plan/fact rows, source versions, and check
failure. Discrepancy or unchecked fact does not require Manager approval.
Completion posts no stock and does not write to 1C. A Web Manager may reopen a
completed order as a new cycle with a new location and participant set. Earlier
cycle snapshots, participants, and closed LPNs remain in history; current plan
and fact can change independently of those snapshots.

## Putaway and corrections

Mobile starts putaway from a closed receiving LPN, including before order
completion. It selects SKU, confirms a positive quantity, and scans a
destination. The command moves that quantity from the LPN's original Receiving
location to an active unlocked Storage location in the same warehouse. It
checks both the unplaced quantity of that LPN and physical source balance,
retains its SKU and expiration date, and commits the linked movement, history,
audit, and command receipt together. Receiving orders have no placement status
or order-level putaway command.
Putaway recommendations prioritize locations with a positive current quantity
of the selected SKU. A historical location is suggested only when it has a
saved zero balance row for that SKU and no nonzero balances; negative balances
are not presented as empty locations.

On the Mobile quantity screen, editing or focusing an amount revokes its
confirmation. Finishing input or choosing the entire remainder confirms a
valid positive amount. Scans queued before confirmation cannot post. After a
conflict, an insufficient remainder revokes confirmation. Busy or uncertain
commands use the shared explicit retry flow. A rejected destination leaves the
confirmed quantity available for another scan.

The destination suggestions show active unlocked Storage locations with
positive stock of the chosen SKU first, then empty locations with a retained
zero balance row for that SKU. Other empty locations are omitted. A location
with no `PickSequence` follows locations with a sequence; ties use address and
location ID. Suggestions are snapshots: any valid Storage destination may be
scanned, and posting rechecks its topology and lock. Capacity and storage
groups do not participate.

A Web Manager may correct a closed receiving LPN's quantity, including after
order completion, until its first putaway. The nonnegative replacement keeps
its SKU and expiration date. The difference posts at the original Receiving
location and updates the order fact. Any putaway blocks correction.

Before deleting a closed LPN, Web previews its receipt, corrections, and
putaways in reverse order with original addresses and dates, plus forecast
balances. A Manager confirms that exact snapshot; changed history or balance
requires a new preview. A negative forecast needs explicit confirmation.
Deletion posts one linked reversal per own movement, retains independent stock
operations, marks the LPN Deleted, and recalculates the order fact. With no
other closed LPN for that SKU, the fact returns to checked zero only when an
earlier explicit zero confirmation exists; otherwise it is unchecked. Deleted
LPNs and their history remain visible.

## LPN issuance

`LicensePlateNumber` is the issued-number entity; `lpn` is an acceptable local
abbreviation. Its stored `Code` is used for display, Code 128 barcode payload,
and printed text: `LPN` followed by exactly twelve digits. It is an internal
WMS code, with no GS1/SSCC claim. SQL Server allocates it from a global,
non-cycling sequence. Codes are unique and gaps are allowed.

Operator, Manager, and Administrator may issue 1–500 labels through Web
`/lpn-labels`, search them, and preview or reprint a label or batch. Issuance
saves codes and receipt atomically; replay returns the same batch. Printing
reads persisted codes without allocating more or changing stock.
