# Strict receiving and shipping workflows

## Business outcome

Keep 1C document imports limited to the pre-WMS state and make all local
warehouse transitions explicit by their target status.

## Scope

- Create/reconcile receiving orders only while local status is
  `ReadyForReceiving`; create/reconcile shipping orders only while local
  status is `Prepared`.
- Flag, log, and preserve the local order for every later inbound difference.
- Validate the status returned by 1C after a WMS status PATCH before posting.
- Use explicit `Set…` transition commands and enforce fact-editable statuses.
- Post receiving inventory only at `Received` and shipping inventory only at
  `Shipped`.

## Non-goals

- Partial reconciliation after WMS work starts.
- Outbound `Отгружать` / `НеОтгружать` line splitting.
