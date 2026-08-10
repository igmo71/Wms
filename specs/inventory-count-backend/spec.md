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

## Non-goals

- Inventory-count UI, endpoints, approvals, recounts, reservations, or tasks.
- A database uniqueness constraint for a location-and-SKU pair within a count.
