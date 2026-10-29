# Data model: Delete a receiving order

## ReceivingOrder

- Add terminal `Deleted` after existing status values; do not reinterpret stored values.
- Add nullable `DeletedAtUtc` and `DeletedBy` (same user-id length as `StartedBy`/`CompletedBy`). Set together on deletion; null before it.
- Preserve ID, immutable number, warehouse, sender, cycles, source links, items and `OperationalRevision`.
- `ReadyForReceiving`, `InReceiving`, `Received` → `Deleted` only with no historical LPN putaway. No transition out.

## LicensePlateNumber

- Preserve assignment, batch, code, items, prior cancellation/closure fields and putaways.
- Add nullable `DeletedAtUtc` and `DeletedBy` for delete transitions. Both are null for active LPNs; no backfill of pre-feature data is required.
- Order deletion changes `Open`, `Closed`, `Cancelled` → `Deleted`; already `Deleted` remains unchanged. Assigned labels cannot be `Available`.
- `OperationalRevision` increments for each transition and protects concurrency.

## ReceivingOrderSourceLink and ReceivingOrderItem

- Set `DetachedAtUtc` on active links only; prior links stay unchanged. The filtered unique active-source index permits a new order.
- Current plan becomes known zero. Recompute fact from active closed LPNs; with none, retain checked zero only after explicit zero confirmation, otherwise unchecked. Completed-cycle snapshots remain immutable.

## InventoryMovement, InventoryTurnover, InventoryBalance

- Each outstanding posted original receipt/correction receives one reversal with `ReversedMovementId`, order/LPN provenance and swapped sides.
- Every reversal side creates turnover and adjusts the exact warehouse/location/SKU/expiration-date balance. Originals and unrelated records stay unchanged.
- Existing unique reversal index and balance concurrency tokens protect replay and competing stock changes.

## Deletion preview

- Ephemeral order ID, state token, LPN summary, reversal list, before/after balance groups and `RequiresNegativeConfirmation`.
- No preview table. Confirmation supplies the token and acknowledgement, not client-calculated stock quantities.
