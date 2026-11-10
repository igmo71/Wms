# Unified inventory transfer commands

Status: Frozen — implemented and verified on 2026-09-10.

## Outcome and scope

WebApp and Mobile use InventoryTransferCommandService through CommandExecutor
for creation, direct movement, pick to transit, put from transit, and completion.
Web draft deletion also uses the same receipt/final-save boundary. Remove the
MobileInventoryTransferCommandService wrapper completely and obsolete Stage entry
points. Retain the existing business rules and private movement/validation helpers.

## Contract

- Preserve existing Mobile routes, DTOs, command-type strings, keys and semantic
  hashes, including invariant decimal G29 formatting and null transit selection.
- Introduce immutable records for related create/movement inputs. Complete/delete
  take the transfer id and CommandContext. Return recorded resource ids.
- Perform every mutable resolution and check after receipt lookup. Movement,
  balances, turnover, transfer state and receipt share one final save. There is no
  1C call or synchronization checkpoint in these commands.
- Keep SQL constraints/concurrency classification, transit exclusivity, empty
  transit completion, topology/locks, positive quantities and available stock.
- Web retains one immutable input/request-id attempt at a time during component
  lifetime. Exceptions/Failure retain it for an explicit retry; success or
  definitive rejection release it. Disable other mutations and input editing while
  pending. Reload/component recreation recovery stays outside scope.
- No new framework, storage schema, routes, or workflow changes. Inventory counts,
  receiving facts, putaway/picking and shipping rollback are separate work.

## Acceptance and verification

1. Both clients call the same five public command methods; Web deletion is also
   repeat-safe after the deleted resource disappears.
2. Replay of existing receipts bypasses mutable state; changed input conflicts.
3. Exercise direct and transit cycles with real SQL balances/turnover and no
   repeated effects. Test failure without receipts/effects and successful retry.
4. Check occupied transit, nonempty completion, insufficient stock and concurrency:
   identical requests return a winning receipt; distinct conflicting requests do
   not double-post or acquire the same transit location.
5. No EF model drift; build Wms, WebApi, WebApp and Mobile and run existing shared
   command regressions.
6. Update lasting documentation, record verification limits and freeze this scope.

## Open questions

None blocking implementation.

## Verification results

- TransferCommands passed on an isolated disposable SQL Server LocalDB database:
  legacy receipt/hash compatibility, direct and transit cycles, repeat-safe draft
  deletion, insufficient-stock rejection without effects and retry, nonempty
  transit completion, simultaneous duplicate creates/movements, exclusive transit
  acquisition, competing stock withdrawals and conserved nonnegative balances.
- ReceivingCommands and ShippingCommands regressions passed.
- Wms (through project references), WebApp, WebApi and Mobile builds passed.
  Existing WebApi NU1903 and Mobile XA4301 warnings remain.
- No EF model drift and no new schema migration. Application databases were not
  modified. Browser/device interaction was not manually exercised; Web retry
  state was reviewed in code and compiled.
