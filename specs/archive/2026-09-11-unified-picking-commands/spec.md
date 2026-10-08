# Unified picking commands

Status: Frozen — implemented and verified on 2026-09-11.

## Outcome

Web and Mobile use PickingCommandService through CommandExecutor for draft
movement add/update/delete. Remove MobileShippingOrderCommandService and picking
Stage entry points. Completion remains the existing shared shipping transition;
rollback is a separate scope.

## Contract

- Preserve Mobile routes/DTOs and shipping-order.add-picking-movement and
  shipping-order.delete-picking-movement hashes. API parses location barcode to
  GUID before calling the command, preserving old protocol behavior and hash.
- Immutable records include expected order, line or movement, source and quantity.
  Web update has shipping-order.update-picking-movement type. Original invariant
  line/G29 quantity inputs define hashes. Return saved movement id.
- Lookup receipts before mutable checks. Movement, recomputed order fact and
  operational revision save atomically with receipt. Drafts never post inventory
  or access 1C. Keep plan bounds, source availability, zone/lock checks and
  allowed states. No schema change.
- Web retains original input/request/user for explicit uncertain retry within
  component lifetime. Block competing edits, completion, acknowledgement and
  rollback while pending. Rollback implementation itself remains unchanged.
- Classify concurrent deletion of an originally unposted picking movement as a
  shipping conflict, mirroring putaway's targeted handling; unknown errors remain
  exceptions.

## Acceptance

Old receipts and changed-input conflicts; split add/update/delete and fact
recalculation; replay after deletion and closed state; invalid quantity, wrong
order, over-plan/stock and locked source reject without receipts or effects;
duplicate requests and concurrent edits respect revisions; integration with
existing picking completion posts once. SQL tests, previous command regressions,
WebApp/WebApi/Mobile builds; current docs and frozen spec with verification limits.

## Open questions

None blocking implementation.

## Verification results

- PickingCommands passed on disposable LocalDB: legacy receipt/hash compatibility,
  changed input, split movements and facts, expected-order checks, no draft stock
  effects, invalid quantities/plan/stock/zone/lock rejection, same-request retry,
  replay after deletion/closure, concurrent add/delete replay, allocation and
  update/delete conflicts. No EF model drift or migration.
- ShippingCommands passed using the shared picking mutation, including real
  completion posting, balances/turnover, checkpoint and 1C retry checks.
- ReceivingCommands, ReceivingFacts, PutawayCommands, TransferCommands and
  CountCommands passed; unknown persistence exceptions remain unclassified.
- Wms (through references), WebApp, WebApi and Mobile built. Existing WebApi
  NU1903 for Microsoft.OpenApi 2.0.0 remains.
- Application databases were not modified. Web pending state and mutual exclusion
  were reviewed and compiled; browser/device interaction was not manually tested.
