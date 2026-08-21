# Inventory-transfer concurrency implementation plan

This plan implements only
[`spec.md`](spec.md). Authentication, mobile idempotency, and other aggregates
remain outside the increment.

## Stage 1. Transfer and index persistence guards

- Add `InventoryTransfer.RowVersion` and configure `IsRowVersion()`.
- Give the balance business-key index a stable explicit name.
- Add a stable explicitly named filtered unique index for transfer recorder line
  numbers only.
- Add one EF migration. Development databases may be recreated instead of
  receiving a compatibility backfill.

Exit: the model has no pending migration changes and valid receiving/shipping
movement multiplicity remains possible.

## Stage 2. Expected conflict translation

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

## Stage 3. Verification

- Build the full solution, run `git diff --check`, and verify EF migration drift.
- Inspect the generated migration and final diff against the required race
  outcomes. Automated parallel scenarios are deferred by project decision.

Exit: persistence guards are present, known failures have narrow conflict
translation, and the solution and EF model are internally consistent.
