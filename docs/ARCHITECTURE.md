# WMS architecture

## Goal

WMS favors a direct, readable implementation over framework-driven layering:

```text
UI or HTTP endpoint
  -> application service
  -> domain operation
  -> EF Core DbContext
```

Existing features move toward this structure when they are deliberately
refactored. The guide does not authorize unrelated cleanup.

## Responsibilities

### UI and endpoints

- Own form state and transport concerns.
- Create application requests at the operation boundary; do not use requests
  or domain entities as mutable form models.
- Display operation outcomes without repeating business rules.

### Application services

- Orchestrate one visible use case as a short sequence.
- Load persisted state, query cross-aggregate facts, call integrations, obtain
  the current user and time, and define the save boundary.
- Keep checks that require `DbContext`, another service, or an external system.
- Use a named private method when a substantial check would obscure the main
  operation. There is no general validation pipeline.

### Domain

- Own invariants and state transitions that require no external state.
- Expose operations rather than public mutation; use private setters,
  read-only collections, and immutable value objects for WMS-owned aggregates.
- Receive timestamps and user identifiers as operation inputs when needed.
- Remain independent of application requests, EF `DbContext`, UI, and 1C
  transport models.

### Data and integration

- EF configurations describe persistence, not business decisions.
- Integration services own the 1C protocol and mapping. A WMS aggregate may
  accept a domain import snapshot, but not a 1C DTO.

## Domain model categories

| Category | Examples | Expected style |
| --- | --- | --- |
| WMS-owned aggregate | `InventoryTransfer`, `InventoryCount` | Rich lifecycle, private state, read-only children |
| Integrated process aggregate | `ReceivingOrder`, `ShippingOrder` | Rich local workflow and explicit reconciliation |
| Inventory fact | `InventoryMovement`, `InventoryBalance`, `InventoryTurnover` | Controlled creation and posting; immutable history |
| WMS configuration | `Zone`, `StorageLocation` | Rich local invariants and controlled activation |
| 1C-owned catalog | `StockKeepingUnit`, `Partner`, `Individual`, `OrganizationalUnit`, `UnitOfMeasure` | Simple import model unless WMS owns a local rule |
| Read projection | report and list items | Data-only; immutable where practical |

`Warehouse` remains a simple 1C-owned import model until WMS owns a concrete
warehouse operation that justifies more behavior.

## Requests and queries

- A request is a typed parameter object, not a reason to introduce CQRS.
- Use one for several related inputs, cross-field validation, or an explicit
  editable-field boundary. Keep it immutable and beside its feature.
- Reuse a domain value object when it already represents exactly the editable
  state; do not create a duplicate update request.
- Queries may project directly to list or detail models. Command/query services
  are split only when their responsibilities are materially different.

## Validation and operation outcomes

Put a rule at the lowest layer that has all data needed to decide it:

1. value object — valid construction;
2. application request — self-consistent ranges and field combinations;
3. domain operation — local entity transition;
4. application service — database, authorization, integration, or
   cross-aggregate state;
5. database constraint — final concurrency-safe uniqueness and integrity.

`OperationResult`, `OperationResult<T>`, `OperationError`, and
`OperationErrorType` form a small shared kernel in `Wms.Common`. Domain,
application, and integration operations use them for expected outcomes such as
invalid input, a missing record, or a business conflict. This keeps expected
branching explicit without exception adapters.

Unexpected infrastructure failures and programming errors remain exceptions.
Do not catch every `Exception` inside domain or application operations merely
to turn failures into results; an outer UI or API boundary may provide the
last-resort user-facing response and logging.

## Persistence

- Application services use `ApplicationDbContext` directly; EF Core is the
  unit-of-work boundary, so repositories are not added by default.
- One operation normally has one explicit `SaveChangesAsync` boundary.
- Persistent invariant changes include a migration.
- Read-only domain collections use explicit EF backing-field configuration.

## Coding and verification

- New and changed C# follows Microsoft's common C# conventions and the root
  `.editorconfig`; control-flow statements use braces.
- Names describe business intent; helpers describe one rule or step.
- Each refactoring stage preserves documented behavior, builds the affected
  projects and complete solution, and checks EF migration drift when the model
  changes.
- Automated tests are intentionally deferred for the current MVP. Do not add a
  test project until that decision changes.

## Deliberate non-goals

The current architecture does not require MediatR, command/handler pairs,
repositories over EF Core, universal base entities, a validation framework,
domain events, event sourcing, or separate read storage. It also does not make
1C-owned catalogs rich merely for uniformity. Add such machinery only for a
demonstrated product need.
