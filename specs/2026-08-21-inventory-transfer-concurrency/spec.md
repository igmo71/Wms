# Inventory-transfer concurrency stabilization

Status: **Frozen reference**
Created: **2026-08-21**

## Business outcome

Concurrent commands for one `InventoryTransfer` either commit one consistent
warehouse change or return an expected conflict. They must not leave a
completed transfer with a later movement, duplicate transfer movement sequence,
or inventory history that disagrees with balances.

This is the single active stabilization increment before development continues
with inventory-changing mobile commands.

## Problems

- `CompleteAsync` and `PostMovementAsync` load and save the transfer through
  independent `ApplicationDbContext` instances without a concurrency token.
- Transfer movement sequence uses `Max + 1`, so two concurrent requests can
  choose the same next number.
- `InventoryBalance.RowVersion` already detects concurrent updates, and the
  balance business-key index detects concurrent first creation, but callers do
  not translate either expected database result to `OperationResult.Conflict`.

## Scope

- add `RowVersion` to `InventoryTransfer`;
- keep one `SaveChangesAsync` transaction for transfer, movement, balance, and
  turnover changes;
- add a uniquely named filtered index that prevents duplicate
  `RecorderLineNumber` only for `InventoryTransfer` recorder rows;
- give the existing `InventoryBalance` business-key index a stable name;
- recognize stale transfer/balance row versions and the two named unique-index
  violations;
- return `OperationError.Conflict` with a safe Russian message for those known
  races.

## Decisions

### Targeted row version

Only `InventoryTransfer` gains a new row version in this increment. Every
transfer movement calls `RecordMovement`, while completion calls `Complete`, so
both commands update the aggregate root and contend on the same version.

There is no universal versioned base entity. `InventoryMovement`, receiving,
shipping, inventory count, configuration, and catalogs are outside this
increment.

### Transfer movement sequence

`RecorderLineNumber` is not globally unique per recorder. Picking and putaway
may intentionally create several movements for one source document line.

The current `Max + 1` allocation remains. A filtered unique index applies only
when `RecorderType == InventoryTransfer`. The transfer row version serializes
normal commands; the index is the final integrity guard.

### Inventory-balance conflicts

The following outcomes mean that inventory changed after the command read it:

- `DbUpdateConcurrencyException` involving `InventoryBalance`;
- SQL Server error `2601` or `2627` for the named balance business-key index.

They become:

```text
Conflict: Остаток изменился. Обновите данные и повторите операцию.
```

A stale `InventoryTransfer` or violation of the transfer-sequence index becomes:

```text
Conflict: Перемещение изменилось. Обновите данные и повторите операцию.
```

The application must identify the affected EF entries or the known named index.
It must not convert every `DbUpdateException` into a business conflict.

### Retry behavior

The server does not automatically retry a conflicting command. In particular,
completion is a lifecycle decision that must be reconsidered after refreshed
state is shown to the caller.

The losing `SaveChangesAsync` transaction is discarded completely. No partial
movement, balance, turnover, or transfer status may remain.

## Required scenarios

- completion and direct movement start from the same transfer version;
- two direct movements start from the same transfer version;
- draft deletion races with the first movement;
- two transfers issue the same SKU from one source balance;
- two transfers concurrently create the same destination balance;
- receiving/shipping movements can still share their source document line;
- a recognized race returns `OperationErrorType.Conflict`;
- an unrelated database failure remains an exception.

## Acceptance criteria

- Exactly one of concurrent transfer completion and movement commits.
- A stale command cannot add a movement to a completed transfer.
- Concurrent movements cannot persist duplicate transfer sequence numbers.
- A losing transaction leaves balances, movements, and turnovers unchanged.
- Known balance update and creation races return conflict rather than an
  infrastructure failure.
- Unknown persistence failures are not hidden as conflicts.
- Existing sequential direct, pick, put, and completion behavior is preserved.
- The solution builds and EF reports no migration drift.

## Non-goals

- Web API authentication and mobile token flow;
- mobile `ClientRequestId` idempotency;
- `RowVersion` on `InventoryMovement` or other workflow documents;
- concurrency audit of picking, putaway, shipping, receiving, or inventory
  count;
- universal exception middleware, repositories, or transaction abstractions;
- automatic concurrency retries;
- automated concurrency tests in the current increment;
- production-data migration compatibility while development databases remain
  disposable.

## Open questions

None block this increment. Authentication, idempotency, and concurrency of
other workflows remain separate roadmap issues and receive their own spec only
when selected as active work.
