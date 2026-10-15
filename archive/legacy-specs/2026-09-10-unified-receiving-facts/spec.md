# Unified receiving facts

Status: Frozen — implemented and verified on 2026-09-10.

## Outcome

Web and Mobile use ReceivingOrderCommandService through CommandExecutor for
increment and absolute fact quantity. Web line comments use a separate shared
application command. Quantity changes preserve the current persisted comment;
comment changes preserve nullable facts. Remove obsolete fact Stage methods and
fact orchestration from MobileReceivingOrderCommandService; retain its putaway
operations for the next scope.

## Contract

- Keep Mobile routes/DTOs and receiving-order.increment-fact / set-fact hashes:
  original order id, invariant line number, invariant G29 quantity. Return order id.
- Add receiving-order.set-item-comment with original nullable comment encoded
  unambiguously in its hash. No normalization before receipt lookup.
- Receipt lookup precedes mutable validation; facts, comment, order revision and
  receipt share one final save. No 1C calls/checkpoint or inventory posting here.
- Preserve domain editing states, unconfirmed null versus explicit zero, positive
  scan increment, decimal bounds and optimistic concurrency. No schema change.
- Web snapshots original inputs and request/user ids for explicit uncertain retry
  within component lifetime. Block fact/comment edits, completion, location edits
  and synchronization acknowledgement while an operation is active/pending.
- Putaway, picking, rollback and durable page-reload recovery remain out of scope.

## Acceptance

Legacy receipt compatibility and changed-input conflicts; scan counted once;
quantity/comment isolation; missing line, invalid scale/range and closed status
reject without effects/receipt; concurrent duplicate and distinct edits; retry
before later mutable state checks. Run real LocalDB tests and prior command and
receiving synchronization regressions. Build WebApp, WebApi and Mobile; document
verification limits, update current docs and freeze specification.

## Open questions

None blocking implementation.

## Verification results

- ReceivingFacts passed on disposable LocalDB: legacy hashes/replay, separate
  quantity/comment semantics, null/zero, invalid inputs and states, same-request
  recovery, concurrent duplicate scans and distinct edits, replay after closure.
  Throwing 1C doubles and empty inventory tables verify absence of external and
  inventory effects. No EF model drift.
- ReceivingCommands, ReceivingSynchronization, ShippingCommands, TransferCommands
  and CountCommands passed.
- Wms (through references), WebApp, WebApi and Mobile builds passed. Existing
  WebApi NU1903 remains; ReceivingSynchronization reported cached NU1900 audit
  source warning but passed.
- No schema migration or application-database changes. Web pending command input,
  mutual exclusion and retry were reviewed and compiled; browser/device UI was
  not manually exercised.
