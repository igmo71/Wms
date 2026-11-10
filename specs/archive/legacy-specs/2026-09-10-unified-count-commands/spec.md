# Unified inventory count commands

Status: Frozen — implemented and verified on 2026-09-10.

## Outcome

WebApp and Mobile use InventoryCountCommandService through CommandExecutor for
start/resume, barcode increment, absolute quantity by item or SKU, unexpected
item removal, posting and draft deletion. Remove the Mobile wrapper and Stage
entry points. Preserve Mobile routes, DTOs, persisted types and original-input
hashes (including raw barcode and invariant G29 decimal formatting).

## Rules

- Start returns an existing draft for the location in the requested warehouse,
  including in Web. Otherwise creation snapshots positive balances and acquires
  the count lock atomically. Receipt lookup precedes all mutable checks.
- Domain rules remain: nullable uncounted quantity, explicit zero, scan adds one,
  unexpected SKU entry/removal, all rows counted before posting, nonzero
  differences only, expected-balance check, own lock required, lock released on
  posting/deletion, posted documents immutable.
- Business changes, lock, movements, balances, turnover and receipt share one
  final save; no checkpoint or schema change is expected.
- Web retains immutable input and request/user ids for explicit uncertain retries
  during the component lifetime. Disable other mutations and editable inputs
  while pending; success/definitive rejection releases the attempt. Navigation
  and component recreation do not provide durable recovery.
- Other workflows and a general UI command framework are outside this scope.

## Acceptance

1. All callers use the common public commands; legacy receipts replay before
   mutable lookup and changed input conflicts.
2. Real SQL tests cover snapshot/lock, increment once, absolute values, unexpected
   removal replay, rejected posting without effects then retry, posting differences
   once, deletion replay and lock release.
3. Concurrent identical requests replay the winner; competing starts, edits and
   posting respect locks/concurrency without duplicate effects or lost updates.
4. No EF model drift; build WebApp, WebApi and Mobile, run count integration tests
   and existing receiving/shipping/transfer command regressions.
5. Update lasting docs and registry, freeze scope with verification limits.

## Open questions

None blocking implementation.

## Verification results

- CountCommands passed against a disposable LocalDB database: all seven legacy
  receipts, raw-barcode and invariant decimal hashes, snapshot, lock, repeated
  scan, absolute counts, unexpected item/deletion replay, zero, positive/negative
  differences, rejected posting then same-request retry, stock drift, manual
  lock, simultaneous start/scan/post and conflicting starts/edits.
- ReceivingCommands, ShippingCommands and TransferCommands regressions passed.
- Wms (through project references), WebApp, WebApi and Mobile builds passed.
  Existing WebApi NU1903 for Microsoft.OpenApi 2.0.0 remains.
- No EF model drift or new migration; application databases were not modified.
- Web pending inputs and disabled actions were reviewed in code and compiled;
  browser/device interaction was not manually exercised.
