# Unified shipping transitions

Status: Frozen — implemented and verified on 2026-09-10.

## Outcome and scope

Extend the accepted shared-command approach to StartPicking, SetReadyForShipment
(Mobile complete-picking), and SetShipped (Mobile ship). WebApp and Mobile use the
same public ShippingOrderCommandService methods and CommandExecutor.

Picking movement creation/editing/deletion and Web rollback retain their current
execution paths. Transfers, counts, receiving facts, and putaway are outside scope.
No database migration, transport-contract changes, dispatcher, or retry framework.

## Contract

- Keep the persisted types shipping-order.start-picking,
  shipping-order.complete-picking, and shipping-order.ship and their existing
  deterministic hashes. A start command contains order and shipping location ids;
  the two finishing commands take an order id and caller-provided CommandContext.
- Receipt lookup precedes mutable resolution and external access. Move deterministic
  start QR parsing from the Mobile wrapper to its HTTP endpoint.
- Preserve PersistReadyForShipmentCheckpointAsync and PersistShippedCheckpointAsync
  inside application orchestration after lookup and before final business effects.
  Final state, movements, balances, turnover and receipt commit atomically.
- Remove the three Mobile wrapper methods and obsolete Stage entry points. Keep
  business-named private helpers for substantial completion logic.
- Web retains request id and original input together for uncertain retries during
  the component lifetime. Success or definitive rejection releases the attempt;
  exceptions and Failure retain it. Reload/component recreation recovery remains
  outside scope. Prevent local editing/rollback while a transition is pending.
- Receipt guarantees apply to WMS persistence; 1C target calls remain repeatable
  and are not part of a distributed transaction. Existing business rules, shortage
  behavior and integration semantics remain unchanged.

## Acceptance and verification

1. Both clients use the three shared public command methods with stable request ids.
2. Existing receipts replay without loading mutable resources or calling 1C;
   changed input with the same id conflicts.
3. Execute a full picking/shipping cycle with real SQL posting, balances, turnover
   and receipts; repeats produce no additional effects.
4. Checkpoint persists after rejected final work; recovery uses the same attempt.
5. Concurrent identical starts resolve via winning receipt; distinct stale starts
   produce a recognized conflict. Unknown errors remain exceptions.
6. Existing Receiving command and synchronization checks continue to pass; no EF
   model drift; Wms, WebApp, WebApi and Mobile build.
7. Update current documentation and report any verification limits.

## Open questions

None blocking this scope.

## Verification result

- ShippingCommands LocalDB checks pass: all three existing receipt protocols,
  input conflicts, real picking/shipping movements and turnover, inventory
  quantities, checkpoint persistence, exact external target recovery, blocking
  assessments, identical concurrent starts and distinct stale starts.
- ReceivingCommands and ReceivingSynchronization regression checks pass.
- No EF model drift and no schema migration in this scope.
- Wms, WebApp, WebApi and Android Mobile build. Existing warnings remain for
  Microsoft.OpenApi 2.0.0 (NU1903) and duplicate Android native libraries (XA4301).
  The synchronization test's cached NuGet audit reports NU1900; the test passes.
- Tests use isolated disposable LocalDB databases and 1C test doubles. Browser
  interaction and live 1C were not exercised. Web pending state remains limited
  to the component lifetime.

Lasting rules are in docs/PROJECT_CONTEXT.md and docs/ARCHITECTURE.md; remaining
command migrations are in docs/ROADMAP.md. Further features require a new scope.
