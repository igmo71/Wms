# WMS architecture

## Goal

WMS favors a direct, readable implementation over framework-driven layering:

```text
UI or HTTP endpoint
  -> application service
  -> domain operation
  -> EF Core DbContext

Manual 1C synchronization UI
  -> corresponding 1C integration service
  -> catalog application service
  -> EF Core DbContext
```

Existing features move toward this structure when they are deliberately
refactored. The guide does not authorize unrelated cleanup.

## Responsibilities

### UI and endpoints

- Own form state and transport concerns.
- Create application commands at the operation boundary; do not use commands
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
- Remain independent of application commands, EF `DbContext`, UI, and 1C
  transport models.

### Data and integration

- EF configurations describe persistence, not business decisions.
- Integration services own the 1C protocol and mapping. A WMS aggregate may
  accept a domain import snapshot, but not a 1C DTO.
- A UI action whose explicit purpose is manual synchronization with 1C may call
  the corresponding integration service directly. Do not add a facade that
  only hides those concrete dependencies.

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

## Application organization

- Organize application code by business feature, not under a technical
  `Services` folder. A feature keeps its services, commands, queries, and read
  models together.
- Use separate command and query services when they represent distinct public
  responsibilities. Database reads required to execute a command remain in its
  command service.
- A small 1C-owned catalog may keep one cohesive service for import persistence
  and reads; splitting it merely for naming symmetry is not required.
- Do not create a folder or handler for every operation. Introduce another
  nesting level only when the feature folder is no longer easy to scan.

## Commands and queries

- A command is an immutable typed input describing an application operation
  that changes state. It does not imply MediatR, a handler type, or full CQRS.
- Use a command object for several related inputs, cross-field validation, or
  an explicit editable-field boundary. Methods with a few obvious parameters
  do not need a command class.
- Reuse a domain value object when it already represents exactly the editable
  state; do not create a duplicate command.
- A query describes a read operation or its criteria and may project directly
  to list or detail models.
- Reserve `Request` for an actual UI, HTTP, or integration transport contract.

## Validation and operation outcomes

Put a rule at the lowest layer that has all data needed to decide it:

1. value object — valid construction;
2. application command — self-consistent ranges and field combinations;
3. domain operation — local entity transition;
4. application service — database, authorization, integration, or
   cross-aggregate state;
5. database constraint — final concurrency-safe uniqueness and integrity.

`OperationResult`, `OperationResult<T>`, `OperationError`, and
`OperationErrorType` form a small shared kernel in `Wms.Common`. Domain,
application, and integration operations use them for expected outcomes such as
invalid input, a missing record, or a business conflict. This keeps expected
branching explicit without exception adapters.
`OperationError` factories are intentionally non-generic: the entity type is
not part of the error contract. Every error requires an informative message;
for a missing record it names the object and includes its identifier when one
is available in the operation context.

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

- New and changed C# follows Microsoft's common C# conventions pragmatically,
  with readability and consistency of the surrounding code taking priority
  over mechanical formatting.
- User-facing messages, expected operation errors, and log messages are written
  in Russian. External protocol values and technical identifiers retain their
  original spelling.
- Names describe business intent; helpers describe one rule or step.
- Each refactoring stage preserves documented behavior, builds the affected
  projects and complete solution, and checks EF migration drift when the model
  changes.
- Repository-wide `dotnet format` runs are not part of ordinary feature work.
- Automated tests are intentionally deferred for the current MVP. Do not add a
  test project until that decision changes.

## Deliberate non-goals

The current architecture does not require MediatR, command/handler pairs,
repositories over EF Core, universal base entities, a validation framework,
domain events, event sourcing, or separate read storage. It also does not make
1C-owned catalogs rich merely for uniformity. Add such machinery only for a
demonstrated product need.
