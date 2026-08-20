# Inventory count backend

## Business outcome

Allow WMS to create a draft inventory count, record counted quantities by storage
location and SKU, and post the resulting inventory differences.

## Scope

- Draft inventory counts and intentionally incomplete rows.
- Expected quantity from the current `InventoryBalance`.
- Duplicate location-and-SKU validation within a count.
- Posting differences through `InventoryMovement` and the common balance/turnover
  posting service.
- Read queries for a count and a paged count list.
- Direct web UI for creating counts and editing or posting their draft rows.

## Domain behavior

- A count is created as a draft for one warehouse; its warehouse does not
  change afterward.
- A draft may contain incomplete rows while the operator records the count.
- Adding, editing, and deleting rows is allowed only in a draft and updates the
  document audit.
- When both location and SKU are selected, the expected quantity is refreshed
  from the current inventory balance. Counted and expected quantities are
  finite and nonnegative.
- A location-and-SKU pair is unique within the document.
- Posting requires every row to be complete. It makes the count and its rows
  immutable; zero-difference rows do not create inventory movements.
- Expected business rejections use `OperationResult`. Database-backed location,
  SKU, warehouse, and balance checks remain in the application service.

## Verification

- Incomplete draft rows can be created, edited, and deleted.
- Duplicate completed rows and posting with incomplete rows are rejected.
- Posting creates receipt or issue movements only for nonzero differences and
  records the posting user and time.
- A posted count rejects further row changes and repeated posting.
- The solution builds and EF reports no pending model changes.

## Non-goals

- Endpoints, approvals, recounts, reservations, or tasks.
- A database uniqueness constraint for a location-and-SKU pair within a count.
- Automated tests or a new test project during the current MVP stage.
