# Shipping order workflow

## Business outcome

Model the WMS-controlled lifecycle of a shipping order with separate picking,
ready-for-shipment, and shipment transitions.

## Scope

- Start picking: `Prepared -> ReadyForPicking`; synchronize 1C status
  `КОтбору` and post the document.
- Mark ready for shipment: `ReadyForPicking`, `ReadyForVerification`,
  `InVerification`, or `Verified` -> `ReadyForShipment`; send the current
  item facts, synchronize status `КОтгрузке`, and post the document.
- Ship: `ReadyForShipment -> Shipped`; synchronize status `Отгружен`, post
  the document, then decrease inventory balances and create turnovers.
- Persist separate users and timestamps for each transition. The picking KPI is
  measured from `PickingStartedAtUtc` to `ReadyForShipmentAtUtc`.

## Acceptance criteria

- Generic shipping `Start`/`Complete` domain and application operations are
  replaced by `StartPicking`, `MarkReadyForShipment`, and `Ship`.
- Inventory is not changed before shipment.
- `FactQuantity` is not changed by the existing import/update flow after an
  order becomes ready for shipment.
- The existing outbound item update semantics, including its TODO for line
  splitting, remain unchanged.

## Out of scope

- Special 1C line splitting by `Отгружать`/`НеОтгружать`.
- New UI or API endpoints for shipping.
