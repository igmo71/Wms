# Inventory-transfer concurrency implementation plan

This plan implements only
[`spec.md`](spec.md). Authentication, mobile idempotency, and other aggregates
remain outside the increment.

## Stage 1. Focused integration-test boundary

- Add one integration-test project using real SQL Server behavior for
  `rowversion` and filtered unique indexes.
- Create an isolated disposable test database per run or equivalent isolated
  test scope.
- Add only the warehouse, locations, SKU, balance, and transfer fixtures needed
  by this spec.
- Capture the current sequential direct movement and completion behavior.

Exit: the existing sequential workflow passes before concurrency changes.

## Stage 2. Transfer and index persistence guards

- Add `InventoryTransfer.RowVersion` and configure `IsRowVersion()`.
- Give the balance business-key index a stable explicit name.
- Add a stable explicitly named filtered unique index for transfer recorder line
  numbers only.
- Add one EF migration. Development databases may be recreated instead of
  receiving a compatibility backfill.

Exit: the model has no pending migration changes and valid receiving/shipping
movement multiplicity remains possible.

## Stage 3. Expected conflict translation

- Add a small internal persistence-conflict classifier for:
  - stale `InventoryTransfer`;
  - stale `InventoryBalance`;
  - the named balance unique index;
  - the named transfer-sequence unique index.
- Use it at `InventoryTransferCommandService` save boundaries.
- Return the document or balance conflict message as appropriate.
- Preserve all unrecognized exceptions.
- Do not add generic retry behavior.

Exit: known races are `OperationResult.Conflict`; unrelated failures still
surface as infrastructure errors.

## Stage 4. Parallel verification

- Run complete/move, move/move, delete/first-move, competing source balance, and
  concurrent destination-creation scenarios.
- Assert the winning transfer state, exact movement count and sequence, source
  and destination balances, and turnover before/delta/after values.
- Build the full solution, run `git diff --check`, and verify EF migration drift.

Exit: all acceptance criteria in the active spec pass.
