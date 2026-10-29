# Implementation Plan: Delete a receiving order

**Branch**: `codex/007-delete-receiving-order` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

## Summary

Add a Manager-only Web deletion command for a WMS receiving order with no historical LPN putaway. Preview the complete stock effect, then atomically mark the order and all assigned LPNs deleted, detach active 1C links, reverse outstanding LPN receipts and corrections, recalculate current plan/fact, and retain operational history. A deleted order remains readable in Web and is excluded from active Mobile work.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Dependencies**: ASP.NET Core, Blazor/MudBlazor, EF Core
**Storage**: SQL Server; `decimal(15,3)` inventory balances, movements and turnovers
**Testing**: Existing xUnit SQL Server LocalDB integration suite for material stock, replay and concurrency risks; developer executes tests
**Target Platform**: WebApp and authenticated Mobile V1 API
**Project Type**: Existing Domain/Application/Infrastructure receiving feature with WebApp and Mobile clients
**Performance Goals**: One preview and one confirmation for an ordinary order; no new batch-size promise
**Constraints**: One database transaction, immutable stock history, unique reversal per original movement, no 1C write, developer-owned migration
**Scale/Scope**: One order, all assigned LPNs and affected dated balance groups; no bulk deletion

## Constitution Check

**Before design**: Pass. WMS owns stock; corrections use movements. Reuse the authoritative receiving posting path and command receipt. Keep one application use case and EF store. Stop all active paths to deleted orders. Focus integration protection on atomic compensation, replay, and concurrent putaway. Developer owns builds, tests and EF migrations.

**After design**: Pass. `DeleteReceivingOrder` uses `PostInventoryMovement.Handler` for each reversal in one `CommandExecutor` session. Source links remain historical; there is no 1C call. Order/LPN revisions and balance concurrency guard competing work. Web/Mobile queries and commands reject the new status. No departure is needed.

## Design

1. Extend `ReceivingOrderStatus` with `Deleted`; add `DeletedAtUtc`/`DeletedBy` to `ReceivingOrder` and LPN. Preserve prior cancellation/closure fields, assignments and unique numbers.
2. Load **all** assigned LPNs, their putaways/items, active links, posted receiving movements/reversals, affected balances/locations and explicit zero confirmations. Reject any historical putaway. Validate each outstanding original is a receipt or correction at the LPN's original receiving location. Previously deleted LPNs contribute no new reversal.
3. Build one deterministic preview with LPNs, reversals and aggregate before/after balance groups keyed by warehouse, location, SKU and expiration date. Token covers order/LPN revisions, active links, movement/reversal state and relevant balance IDs, quantities and row versions. Confirmation reloads and recomputes; a changed token fails. Negative final forecast needs explicit acknowledgement.
4. In one `CommandExecutor` session: require Manager; recheck eligibility/token; mark active LPNs deleted; reverse outstanding movements using `PostInventoryMovement.Handler`; recompute current fact; detach active links and zero current plan; mark order deleted; save one audit and receipt with all effects. Duplicate request returns the original receipt. Concurrency or unique-reversal conflicts abort the save.
5. Web lists a visibly deleted historical row; its card shows actor/time, LPNs and reversal history with no mutation controls. Mobile queue/direct lookup excludes deleted orders. Every command that can touch the order rejects `Deleted`; 1C source discovery treats detached links as free. Single-LPN deletion keeps its existing broader rule.

## Project Structure

### Documentation

```text
specs/007-delete-receiving-order/
  spec.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/receiving-order-deletion.md
  tasks.md
  checklists/requirements.md
```

### Source Code

```text
Wms.Domain/ReceivingOrder.cs
Wms.Domain/LicensePlateNumber.cs
Wms.Domain/Enums/ReceivingOrderStatus.cs
Wms.Application/Receiving/PreviewReceivingOrderDeletion.cs
Wms.Application/Receiving/DeleteReceivingOrder.cs
Wms.Infrastructure/Data/ReceivingOrderDeletionStore.cs
Wms.Infrastructure/Data/ReceivingWorkQueryStore.cs
Wms.Infrastructure/Data/Configurations/ReceivingOrderConfiguration.cs
Wms.Infrastructure/Data/Configurations/LicensePlateNumberConfiguration.cs
Wms.WebApp/Components/Pages/ReceivingOrderPages/
Wms.WebApi/Mobile/MobileReceivingOrderEndpoints.cs
Wms.Tests/Receiving/ReceivingSafetyTests.cs
.specify/memory/current/receiving.md
.specify/memory/current/identity.md
.specify/memory/current/clients.md
```

**Structure Decision**: Extend existing receiving and inventory boundaries. A developer-created migration under `Wms.Infrastructure/Data/Migrations/` follows model changes; inspect generated operations before further implementation.

## Delivery Gates

- After model configuration, give the developer the exact EF migration command and inspect the generated migration before applying it.
- Give the developer exact minimal build/focused test commands. Leave Web and Mobile interaction checks to the developer.
- Complete the spec only after migration and verification results are reviewed. Update owning current-state documents with implemented behavior.
