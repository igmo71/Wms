# WMS architecture

## Direction

Prefer a direct feature-oriented implementation:

```text
UI or HTTP endpoint
  -> application use case
       -> EF Core context for persisted and cross-aggregate state
       -> domain operations for local invariants and transitions
       -> application-owned port when the use case crosses into 1C

1C notification endpoint
  -> Integration.OneS adapter (protocol parsing and delay)
  -> application synchronization use case
```

The application layer owns orchestration and save boundaries. Domain code owns
rules decidable from in-memory state. EF Core is the unit of work; 1C transport
is an adapter. Introduce an abstraction for a demonstrated cohesive
responsibility in a real use case.

## Boundaries

### UI and HTTP

UI owns form and presentation state; endpoints own transport parsing and result
mapping. They call application use cases and do not repeat business rules or use
domain entities as mutable form models. Mobile endpoints decode 1C document
barcodes before passing document ids to application queries.

### Application

An application service presents one recognizable use case. It loads state,
queries external facts, invokes domain operations or integrations, obtains time
and user identity, and makes persistence explicit. Database-backed,
authorization, cross-aggregate, and external-system checks remain here.

Application owns the source and execution-sink ports for receiving and shipping
and the synchronization operations that apply snapshots. UI and endpoints do
not compose concrete 1C services.

Organize application code by business feature. Separate command and query
services only when they are distinct public responsibilities; reads needed by a
command remain with that command. Add a type or folder for a distinct
responsibility rather than for each method.

### Domain

Domain operations own local invariants and transitions, accept time and user
identity as inputs, and expose controlled state rather than public mutation.
Domain code does not depend on commands, EF Core, UI, or 1C DTOs.

Use rich lifecycle behavior for WMS-owned and integrated-process aggregates.
Inventory facts have controlled creation and immutable posted history.
1C-owned catalogs are simple import models.

### Persistence and integration

EF configuration describes persistence, not business policy.
`Integration.OneS` owns OData DTOs, metadata names, protocol statuses,
notification parsing, and HTTP mechanics. Its implementations use 1C names;
application ports use WMS terminology.

Application services use `ApplicationDbContext` directly, and one operation
normally has one explicit save boundary.

Receiving start/completion (including Manager completion) and fact/comment edits, all putaway commands,
picking draft add/update/delete, shipping start-picking/complete-picking/ship/rollback,
transfer creation/movements/completion/draft deletion, and all inventory-count
mutations enter the shared `CommandExecutor` from their public application methods. The executor
owns receipt lookup/replay, winning receipt recovery after final-save races,
and the atomic final save of business
state plus receipt. Callers supply `CommandContext` with request and user ids.
Persisted command types are stable strings, not CLR type names. Hashes use only
deterministic semantic input; Mobile V1 hash compatibility is preserved.

Receiving completion calls `PersistCompletionCheckpointAsync`; shipping uses
`PersistReadyForShipmentCheckpointAsync` and `PersistShippedCheckpointAsync`.
These explicitly named operations run after receipt lookup and before final
effects. Each is an independent synchronization commit and survives a failed
final phase. Other
intermediate saves require explicit documented application semantics. Business
completion helpers do not save. SQL and 1C remain separate boundaries; receipt
uniqueness does not prevent concurrent external calls before final persistence.

Warehouse commands no longer expose staging methods for Mobile receipt
orchestration. Use business-named private helpers only where they clarify
substantial logic; do not add a dispatcher or pipeline. Administrative mutations
and synchronization operations retain their own explicit save boundaries.

Migrations are immutable after they may have reached a non-disposable database.
Schema correction then requires a new migration. Replacing history is permitted
only for an explicit pre-production baseline reset of every affected database.

Use optimistic concurrency only where a stale save threatens a documented
invariant. Shared-resource coordination may use a targeted operational revision.
Only recognized concurrency failures and named constraint violations become
business conflicts; other persistence failures remain exceptions. Their shared
classification belongs under `Wms.Application.Persistence`.

## Commands, validation, and outcomes

A command is an immutable application input. Introduce one for related inputs,
cross-field rules, or a meaningful editable boundary; keep a few obvious
parameters as parameters. `Request` is reserved for UI, HTTP, or integration
transport.

Place a rule at the lowest layer with enough information:

1. value object — valid construction;
2. command — input combinations;
3. domain operation — local transition;
4. application service — database, authorization, integration, or
   cross-aggregate state;
5. database constraint — final concurrent integrity.

A repeated database-backed eligibility rule may become one narrow named policy.
It does not own workflow, quantity, or persistence.

`OperationResult` and `OperationError` represent expected invalid input, missing
records, and business conflicts across domain, application, and integration.
Errors carry an informative user-facing message. Unexpected infrastructure or
programming failures remain exceptions and are handled at the outer boundary.

## Client organization

MAUI pages are grouped by operator workflow. Scanner adapters, feature HTTP
clients, session handling, and platform helpers remain technical components.
Feature clients and Mobile V1 contract source files are grouped by business
feature over one authenticated transport.

A page retains controls, modes, scanner lifecycle, navigation, dialogs, and
rendering. A concrete feature process may own retry-aware API sequences and
stable request ids when that materially clarifies the page.

Treat roughly 300 lines as a review signal, not a limit. Extract a
business-named or clearly technical cohesive responsibility only when it
reduces the context required to follow the primary path. Keep cohesive
algorithms and aggregates intact.

## Conventions

- User-visible messages and expected-operation errors are in Russian; external
  protocol values keep their original spelling.
- Cross-project and deliberate assembly boundaries are `public`; implementation
  details are `internal`.
- Verification is proportional to change risk. Schema changes require a
  migration and migration-drift check. Adding, changing or running automated
  tests requires an acute need and advance user agreement on scope; use targeted
  review, builds and focused manual verification by default.
