# Order synchronization decisions

Status: **Active**

## Outcome

An operator can see why a receiving or shipping order differs from its fresh
1C document. Safe differences require an explicit decision, differences that
can corrupt warehouse facts block the workflow, and the exact result produced
by WMS itself is recognized as synchronized.

## Scope

- receiving and shipping orders imported from 1C;
- comparison of the local order with a freshly fetched 1C document;
- recognition of the source plan and of an exact WMS-produced target state;
- persisted synchronization marker, severity, check time, fingerprint, and
  latest operator acknowledgement;
- detailed WebApp presentation and acknowledgement;
- short Mobile V1 and Android status presentation;
- enforcement at the agreed workflow checkpoints;
- correction of the sticky `ExternalChangeDetected` behavior.

Durable delivery of 1C notifications, an outbox, periodic polling, a second
persisted copy of the complete 1C document, and automatic external rollback
are outside this delivery.

## Terms

### Synchronization levels

1. **Synchronized** — the fresh document matches either the applicable source
   state or the exact state that the current WMS operation is expected to
   produce.
2. **Requires operator decision** — differences do not alter warehouse,
   product identity, line composition, or quantities, but must be read and
   explicitly acknowledged before work continues.
3. **Blocking** — differences may alter warehouse facts, identify another
   business document, or represent an unexpected final or posted state. They
   cannot be bypassed by acknowledgement.

A failed or unavailable 1C check is a technical verification failure, not a
fourth business level. It blocks only a transition that requires a fresh
check; the last known persisted synchronization state remains visible.

### Shipping status phases

- `Prepared` (`Подготовлен`) — work has not started;
- `ReadyForPicking`, `ReadyForVerification`, `InVerification`, and `Verified`
  form the compatible picking/checking phase;
- `ReadyForShipment` (`К отгрузке`) is one concrete status after picking;
- `Shipped` (`Отгружен`) is final.

WMS writes the short path `Prepared -> ReadyForPicking -> ReadyForShipment ->
Shipped`. It recognizes the three additional checking statuses but does not
write them itself.

## Classification rules

### Requires operator decision

This level covers changed source metadata that does not change warehouse facts:

- comment;
- queue;
- document number or date;
- planned shipping date;
- delivery direction;
- shipper or receiver;
- a compatible non-final status inside the active workflow phase;
- another explicitly classified metadata field with no effect on warehouse,
  product identity, line composition, or quantity.

The acknowledgement is valid only for the fingerprint that the operator saw.
A later 1C change invalidates it automatically.

### Blocking

This level covers:

- warehouse change;
- deletion mark;
- unexpected posting;
- incompatible warehouse or business operation;
- changed SKU, line identity, line composition, or plan quantity;
- changed base-order identity or base-line composition;
- an unexpected `ReadyForShipment`, `Received`, `Shipped`, or other final state;
- 1C lines that match neither the WMS source plan nor the exact target state of
  the command being performed or safely retried;
- malformed, ambiguous, or incomplete source rows that cannot be compared
  unambiguously.

Blocking means that the operational transition cannot continue. It does not
silently replace the local plan. If an order has no local work, a later,
explicit plan-refresh resolution may be added separately; active local work is
never discarded by this synchronization flow.

## Recognizing WMS-owned changes

Notifications are never ignored merely because WMS recently wrote to 1C.
Instead, the comparison understands the exact expected external state.

For receiving, the expected completed state is derived from the preserved
local plan, confirmed facts, line comments, and requested 1C status. Values
written by WMS as facts must not be imported back as a replacement plan.

For shipping, the expected ready-for-shipment or shipped state is derived from
the preserved plan and picked facts. It recognizes the exact `Отгрузить` /
`НеОтгружать` rows, including a residual row for a partial pick, and the
corresponding base-item result.

Before repeating a WMS-to-1C command:

- an admissible source state allows the command to run;
- an exact target state means that the external step already succeeded and may
  be treated as repeat-safe;
- any third state is classified and blocks when required.

The local plan, facts, workflow state, and command intent provide the expected
state. A complete second 1C snapshot is not persisted.

## Checkpoints and notification policy

1. Opening or resuming an order performs one fresh comparison and returns the
   assessment with its details.
2. The immediately following start action may reuse that result; there is no
   duplicate request merely for pressing `Взять в работу` or `Взять в отбор`.
3. Completing receiving performs a fresh comparison before changing 1C or
   posting receiving movements.
4. Completing picking performs a fresh comparison before changing 1C or
   posting picking movements.
5. Final shipping performs a fresh comparison before changing 1C or posting
   the final issue.
6. Putaway is a local WMS process and does not fetch the source order again.
7. Scans, searches, quantity edits, and draft movement edits do not query 1C.

1C notifications continue through the existing in-memory channel. They run
the same comparison early for responsive UI, but are a best-effort hint rather
than the only safety boundary. This delivery does not make the channel durable.

## Persistence

Each order persists only the current compact synchronization state:

- level;
- detection or last-check time;
- fingerprint of the relevant fresh 1C state;
- fingerprint last acknowledged by an operator;
- acknowledgement user and time.

Structured differences are produced from a fresh document for presentation
and are not stored as another document snapshot. Exact agreement clears the
current issue, including a previously sticky conflict marker. The latest
acknowledgement remains an audit record but applies only when its fingerprint
equals the current one.

## Operator experience

### WebApp

- lists show synchronized, requires-decision, or blocking state;
- order details show each changed field with WMS and 1C values;
- consequences and the required next action are written in plain language;
- only requires-decision differences can be acknowledged;
- acknowledgement records the current user and time;
- blocking differences have no continue-anyway action.

### Mobile

- queues and details show a short synchronization summary;
- the changed field names and a changed comment are visible;
- Mobile does not acknowledge differences in this delivery;
- an unacknowledged decision or a blocking problem directs the operator to
  WebApp or external resolution;
- an acknowledgement already made in WebApp allows Mobile to continue for the
  same fingerprint.

## Acceptance criteria

- a formerly sticky issue clears after a fresh exact match;
- metadata differences are listed and require acknowledgement;
- quantity, identity, composition, warehouse, deletion, posting, and
  unexpected final-state differences block the relevant transition;
- an acknowledgement is invalid after any new relevant 1C state;
- receiving and shipping recognize their own exact external target states and
  do not overwrite the preserved local plan with WMS-produced result rows;
- order opening, receiving completion, picking completion, and shipping use
  the agreed minimal fresh checks;
- notifications remain best-effort and no durable queue or polling is added;
- WebApp shows details and audit while Mobile shows the agreed summary;
- no tests are added or run.

