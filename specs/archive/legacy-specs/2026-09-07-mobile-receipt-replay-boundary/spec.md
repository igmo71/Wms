# Mobile receipt replay boundary

Status: frozen reference. Completed 2026-09-07.

## Outcome

A retry of a successfully persisted Mobile command is resolved from its
receipt before current order, location, inventory-count draft, or SKU state can
reject or redirect it. Reusing the same request id with different semantic
input remains a conflict.

## Scope

- Move mutable receiving-order and location resolution for start receiving and
  add-putaway movement behind `MobileCommandExecutor` receipt lookup.
- Make inventory-count start one receipt-protected command: it either returns
  the current draft for the requested location or creates a new draft.
- Resolve an inventory-count scan barcode to the current active SKU only inside
  the receipt-protected action.
- Hash the original semantic input or its deterministic parsed identifier.
- Keep authentication and deterministic storage-location QR parsing at the
  HTTP boundary.
- Preserve routes, contracts, response shapes, business rules, and receipt
  persistence model.

## Out of scope

- A generic validation pipeline, automatic validator discovery, or a validator
  per command.
- Endpoint, client, contract, or command-service file decomposition.
- Changes to shipping, transfer, and Mobile query-only barcode resolution paths
  that already enter their command receipt boundary without mutable prechecks.
- New automated tests.

## Acceptance criteria

- A successful start-receiving retry returns its receipt even if the order or
  receiving location has since become ineligible.
- A successful add-putaway retry returns its original movement id even if the
  order or destination location has since changed.
- Inventory-count start always consults a compatible receipt first; without a
  receipt it explicitly opens the current same-location draft or creates one.
- A successful inventory-count SKU scan retry returns its receipt even if the
  barcode mapping or SKU availability has since changed.
- Reusing a request id with a different parsed location, barcode, quantity,
  line, order, or warehouse returns the existing request-conflict result.
- Relevant projects build and `git diff --check` passes; no tests are created.

## Result

- Start-receiving and add-putaway endpoints now perform only authentication and
  deterministic location-QR parsing before calling their Mobile command.
  Current order and location eligibility are checked by the existing staged
  operations after receipt lookup.
- Putaway destination eligibility moved to `ReceivingOrderLocationPolicy`, so
  the common staged operation preserves the former Mobile warehouse, zone,
  location, folder, and lock checks for both clients.
- Mobile inventory-count start now explicitly opens a current same-location
  draft or creates one inside the receipt-protected action. Its persisted
  command-type string remains unchanged for compatibility with old receipts.
- Inventory-count SKU scan hashes the submitted barcode and resolves it through
  the existing SKU service using the executor's `DbContext`, after receipt
  lookup and before the staged count change.
- `Wms`, `Wms.WebApi`, and `Wms.WebApp` build successfully. WebApi retains the
  known `NU1903` warning for `Microsoft.OpenApi 2.0.0`.
- `git diff --check` passes and no automated tests were created.
