# Receiving and shipping workflow consistency

## Business outcome

Keep local WMS workflow state and actual quantities protected from failed 1C
operations and conflicting later inbound synchronization.

## Scope

- Persist a local transition only after its 1C PATCH/Post operation succeeds.
- Flag and log inbound status conflicts instead of replacing a local
  WMS-controlled state.
- Prohibit receiving fact changes after `Received`.
- Preserve existing item facts during inbound reconciliation.
- Keep inventory posting at `Received` for receiving and at `Shipped` for
  shipping only.

## Non-goals

- Redesigning integration architecture or retry behavior.
- Splitting outgoing 1C item rows by `Отгружать` and `НеОтгружать`.
