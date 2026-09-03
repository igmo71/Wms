# Initial order plan synchronization

Status: frozen reference.

## Outcome

Until warehouse work begins, WMS follows an admissible initial 1C plan without
requiring an operator to resolve ordinary source edits. Once work begins, the
existing synchronization decisions continue to protect the WMS plan and fact.

## Scope

- Receiving can refresh while its local status is `ReadyForReceiving`,
  `StartedAtUtc` is absent, and 1C remains in the corresponding initial state.
- Shipping can refresh while its local status is `Prepared`,
  `PickingStartedAtUtc` is absent, and 1C remains in the corresponding initial
  state. A successful rollback returns shipping to this mode.
- An initial refresh accepts source-owned metadata, warehouse, parties,
  document references, and the complete plan-line composition.
- A deletion-mark change, posting change, departure from the initial source
  status, malformed plan, or unsupported initial line semantics remains
  blocking and is not copied into the local workflow.
- Active orders continue through the existing detailed comparison,
  acknowledgement, and blocking rules.
- Web and Mobile start receiving through the same server-side operation that
  selects the receiving location and starts work in one local save boundary.
- The broader application architecture review is outside this change.

## Acceptance criteria

- A valid 1C quantity, SKU, composition, warehouse, or metadata edit refreshes
  an unstarted initial order and leaves it synchronized.
- The same edit after work starts is classified by the existing rules and does
  not overwrite the working plan.
- A shipping order becomes freely refreshable again after successful rollback.
- An externally withdrawn, deleted, unexpectedly posted, or structurally
  invalid initial order is blocked without creating an invalid WMS workflow.
- Exact repeated synchronization does not advance `OperationalRevision`.
- Web receiving cannot persist only the selected location when starting work
  fails before the local save.
- No obsolete reconciliation helper remains unreachable.
- The solution builds without errors; no tests are created or run.

## Open questions

None.
