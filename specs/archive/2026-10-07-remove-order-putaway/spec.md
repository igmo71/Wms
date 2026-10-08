# Remove obsolete order-level putaway

Status: **Frozen** — accepted on 2026-10-07 after migration, tests and developer
review.

## Outcome

Receiving orders no longer carry a misleading order-level putaway state. Initial
putaway remains an independent, immediately posted LPN process.

## Scope

- Remove the unused order-level putaway commands, domain methods and persisted
  status/audit columns with a database migration. Do not alter LPN putaway
  history, inventory movements, balances or turnover.
- Remove order-level putaway status and completion from Web receiving views,
  filters and sorting.
- Remove the obsolete order-level putaway branch from Mobile receiving queries,
  contract fields and hidden queue layout. Keep receiving-order queue and SKU
  resolution functional. Keep the visible queue limited to the current user's
  orders; other orders remain reachable by scanning their document barcode.
- Retain shared receiving-location validation used by LPN placement.

## Acceptance

1. Receiving orders show no order-level placement state or completion date.
2. Mobile receiving order data and queue no longer expose the old order-level
   placement path; the visible queue and scanned order access remain as before,
   and independent LPN placement still works.
3. A standard EF migration drops only the five obsolete receiving-order columns.
4. The supported LPN putaway posting and replay tests remain intact.

## Verification

Developer builds the affected projects, generates and applies the standard EF
migration, then runs focused receiving/putaway tests. The agent does not run
builds or tests.

## Open questions

None. The development database may be cleaned; legacy order-level putaway
audit fields do not need preservation.
