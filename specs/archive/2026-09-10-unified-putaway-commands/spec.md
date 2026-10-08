# Unified putaway commands

Status: Frozen — implemented and verified on 2026-09-11.

## Outcome

WebApp and Mobile use PutawayCommandService through CommandExecutor for start,
add/update/delete draft movement and completion. Remove the remaining
MobileReceivingOrderCommandService and putaway Stage entry points.

## Contract

- Preserve existing Mobile routes, DTOs, command types and original hashes:
  receiving-order.start-putaway, add-putaway-movement, delete-putaway-movement,
  complete-putaway; invariant line numbers and G29 quantities.
- Immutable add/update/delete records include the expected order. Web update uses
  a new receiving-order.update-putaway-movement type. Mutable order/movement and
  location checks happen after receipt lookup. Return order or movement ids.
- Draft mutations retain received-order/putaway-state, allocation, positive
  quantities, source balance, zone and lock rules. They never post inventory.
  Completion requires full allocation, validates routes, confirms and posts all
  drafts with order state and receipt in one final save. No 1C or checkpoint.
- Web retains immutable input and request/user ids for explicit uncertain retry
  within component lifetime. Disable competing mutations and editable selections
  while in flight/pending. Repeat completion/start even after state changed.
- Preserve order operational revision. Classify a concurrent deletion of a draft
  putaway movement as a business conflict; leave unknown errors unclassified. No
  schema change. Picking, shipping rollback and durable reload recovery excluded.

## Acceptance

Legacy replay before mutable lookups and input conflicts; add/update/delete
replay including deleted movement; rejected commands leave no receipt/effects;
split allocations and once-only posting with real balances/turnover; lock and
source-stock revalidation at completion; concurrent duplicate and distinct
requests preserve order/allocation invariants. Build WebApp/WebApi/Mobile, run
new SQL tests and previous command regressions. Update docs and freeze scope
with verification limits.

## Open questions

None blocking implementation.

## Verification results

- PutawayCommands passed against disposable LocalDB: four old receipt types,
  invariant decimal hashes, replay after deletion, expected-order checks, split
  allocation, no draft posting, lock/source-stock rejection without partial
  effects, same-request retry and once-only completion. No EF model drift.
- Deterministic final-save races passed for duplicate start/add/complete, distinct
  allocation requests and update/delete of one draft. The latter exposed a
  missing conflict classification, now narrowly handled for originally unposted
  receiving movements between two locations. Unknown persistence errors remain
  exceptions, verified by ReceivingCommands.
- ReceivingCommands, ReceivingFacts, ShippingCommands, TransferCommands and
  CountCommands passed. Wms (through references), WebApp, WebApi and Mobile built.
  Existing WebApi NU1903 for Microsoft.OpenApi 2.0.0 remains.
- No migration or application-database changes. Web pending state was reviewed
  and compiled; browser/device interaction was not manually exercised.
