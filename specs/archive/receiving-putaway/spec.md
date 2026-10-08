# Receiving putaway

## Outcome

After a receiving order is received into its receiving location, an operator can
put the accepted quantities away into storage locations and WMS records the
corresponding inventory movements.

## Scope

- Putaway is a local WMS process attached to a receiving order; it does not call
  or update 1C.
- Receiving and putaway have separate statuses. A received order remains
  `Received` while its putaway status progresses through `Pending`,
  `InProgress`, and `Completed`.
- Before a non-zero receiving fact is completed, putaway is `Inactive`.
- Completing receiving with any positive fact changes putaway to `Pending`.
- An explicit start command changes putaway to `InProgress` and records the
  starting user and time.
- While putaway is in progress, the operator records editable draft movements
  from the order's receiving location to active locations in active storage
  zones of the same warehouse.
- Draft quantities are limited separately by each receiving-order line's fact.
  One line may be split across multiple storage locations.
- Completion requires the draft total for every line to equal its accepted fact.
  Completion posts all draft movements and records the completing user and time.
- The receiving location cannot be changed after receiving is completed.

## Non-goals

- Putaway directly to a shipping location.
- Location recommendations, capacity constraints, routes, tasks, or reservations.
- Rollback of completed putaway. Corrections use an inventory transfer.
- Updating existing received orders when the schema is introduced.
- Automated tests for this MVP increment.

## Acceptance criteria

- The receiving-order list and details expose receiving and putaway statuses
  separately and the list can be filtered by putaway status.
- A pending order can be explicitly started; an in-progress order can be left
  and reopened without changing its state.
- Draft movements can be added, edited, and removed only in `InProgress`.
- Server-side validation enforces warehouse, zone, active-location, positive
  quantity, line-fact, and receiving-location balance rules.
- Putaway cannot complete until all positive facts are allocated exactly and no
  movement exists for a zero-fact line.
- Completion posts inventory from the receiving location into the selected
  storage locations in one save operation.
- Completed putaway is read-only.

## Decisions

- Identical SKUs on different order lines retain separate line limits and
  movement traceability through `RecorderLineNumber`.
- A zero-total fact is an exceptional receiving outcome. Putaway remains
  `Inactive` and the UI explains why it cannot be started.
- Putaway drafts do not reserve inventory. The physical source balance is
  checked while editing and checked again by the common posting service at
  completion.
