# WMS architecture

This document describes the implemented code and its detailed engineering
conventions. The proposed receiving redesign belongs to the active
[feature plan](../../specs/001-receiving-stock-lifecycle/plan.md) until those
business changes are implemented. Project-wide principles live in the
[WMS Constitution](constitution.md).

## Current project map

| Project | Current responsibility |
|---|---|
| `Wms` | Legacy application services and 1C mapping adapter; consumes Application contracts and Infrastructure persistence. |
| `Wms.Domain` | Extracted domain entities, transitions and shared value/result types; no WMS project dependencies. |
| `Wms.Application` | New command execution, receiving source and order use cases and contracts; references Domain. |
| `Wms.Infrastructure` | EF Core context, configurations, Identity data types and existing migrations; references Application. |
| `Wms.Integration` | Receiving source HTTP reader and OData parser; references Application. |
| `Wms.Contracts` | Shared transport contracts. |
| `Wms.WebApi` | Authenticated Mobile API and 1C integration endpoints. |
| `Wms.WebApp` | Manager and warehouse Web interface. |
| `Wms.Mobile` | Android operator workflows. |
| `Wms.Tests` | Risk-based automated checks. |

The new project references point inward: Application references Domain;
Infrastructure and Integration reference Application. The legacy `Wms` project
also references Domain, Application, and Infrastructure. New projects do not reference `Wms`.
Application owns the explicit Manager permission check; Infrastructure resolves
membership from Identity storage for command execution.
WebApi and WebApp directly reference Application, Infrastructure, and Integration
as composition roots, while retaining the legacy project for existing services.
Their shared EF context and new command execution store registration come from Infrastructure;
Integration registers the receiving source HTTP reader and parser in both hosts;
Infrastructure registers the source and order stores and their use cases. `Wms.Mobile` uses
`Wms.Contracts`. In the current
receiving flow, closing an LPN changes its state while completing the order
posts receipt movements.
The target flow moves that posting point to LPN closure. A delivery increment
must remove the former path when the new one becomes callable.

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

An application use case may be an injectable class. Pass simple inputs as method
parameters; use an immutable command or query for a related set of inputs or
when required by the execution mechanism. Use a separate result type when it
conveys more than one ID or value. Return `OperationResult` for expected errors;
let unexpected failures surface as exceptions. Existing working use cases do
not need a structural rewrite for naming or type layout alone.

### Domain

Domain operations own local invariants and transitions, accept time and user
identity as inputs, and expose controlled state rather than public mutation.
Domain code does not depend on commands, EF Core, UI, or 1C DTOs.

Use rich lifecycle behavior for WMS-owned and integrated-process aggregates.
Inventory facts have controlled creation and immutable posted history.
1C-owned catalogs are simple import models.

### Persistence and integration

EF configuration describes persistence, not business policy. The single
`ApplicationDbContext` and its existing migration chain belong to
`Wms.Infrastructure`; their `Wms.Data` namespaces remain stable during this
project move. Legacy application services still use that context directly.
`Integration.OneS` owns OData DTOs, metadata names, protocol statuses,
notification parsing, and HTTP mechanics. Its implementations use 1C names;
application ports use WMS terminology.

One legacy operation normally has one explicit save boundary. New commands use
the Application command executor and its Infrastructure store: receipt lookup
precedes the operation, and one EF save commits staged business state, audit
events, and receipt. The legacy executor remains for existing callers until
their replacement. The two executors have distinct namespaces.
The new inventory posting handler loads tracked locations and four-dimensional
balances through an Infrastructure store bound to that command session. It
stages the movement, balance changes and turnovers for the executor's single
commit; no receiving or putaway endpoint calls it yet.

Existing warehouse mutations in Web/Mobile enter legacy application commands through
`CommandExecutor`: LPN issuance/counting/putaway, receiving participation and
completion, picking, shipping/rollback, transfers and inventory counts.
The executor owns receipt lookup before mutable validation and external access,
replay, winning receipt recovery after final-save races, and atomic final save
of business state plus receipt. Callers supply `CommandContext` with request
and user ids. A successful replay returns the recorded resource id without
repeating business effects, synchronization or outbound calls.
Persisted command types are stable strings, not CLR type names. Hashes use only
deterministic semantic input; Mobile V1 hash compatibility is preserved.

`CommandReceipts` is keyed by user, command type and request id and stores the
request hash and result resource id. An id reused with different input conflicts.
Hashes describe original input, never resolved database state. Mobile V1 retains
the `ClientRequestId` name; schema renames preserve existing receipt keys,
types, hashes and results. Location barcode parsing may precede the application
command at the API boundary; inventory-count barcode resolution follows receipt lookup.

Receiving completion calls `PersistCompletionCheckpointAsync`; shipping uses
`PersistReadyForShipmentCheckpointAsync` and `PersistShippedCheckpointAsync`.
These explicitly named operations run after receipt lookup and before final
effects. Each is an independent synchronization commit and survives a failed
final phase. Other
intermediate saves require explicit documented application semantics. Business
completion helpers do not save. SQL and 1C remain separate boundaries; receipt
uniqueness does not prevent concurrent external calls before final persistence.

Warehouse commands do not expose staging methods for Mobile receipt
orchestration. Use business-named private helpers only where they clarify
substantial logic. Administrative mutations and synchronization operations
retain their own explicit save boundaries.

Migrations are immutable after they may have reached a non-disposable database.
Schema correction then requires a new migration. Replacing history is permitted
only for an explicit pre-production baseline reset of every affected database.

Use optimistic concurrency only where a stale save threatens a documented
invariant. Shared-resource coordination may use a targeted operational revision.
Only recognized concurrency failures and named constraint violations become
business conflicts; other persistence failures remain exceptions. Their shared
legacy classification belongs under `Wms.Application.Persistence`. The new
Infrastructure command store classifies stock write conflicts before the
Application executor maps them to command conflicts; Application has no EF
dependency.

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

Web and Mobile retain the original command inputs, request id and user for
explicit retry after transport failure, `408`, `5xx` or an unreadable successful
response. Success or definitive business/client rejection releases the attempt;
conflicting inputs and actions remain disabled while it is active or uncertain.
Recovery lasts only for the live page/process or Interactive Server component:
reload/recreation reads current server state and does not recover or replay the
old intent automatically. Mobile error codes remain `invalid_command`,
`resource_not_found`, `request_conflict` and `command_failed`.

Mobile transport converts HTTP timeout cancellation into `HttpRequestException`,
so existing uncertain-result handlers retain the command for explicit retry.
Explicit cancellation of the caller's token remains `OperationCanceledException`;
it is not a business rejection or evidence that a dispatched command did not
commit. The transport never retries automatically.

A multi-page workflow passes its retry process along the navigation chain rather
than making it an application singleton. One unresolved command retains its
original inputs and request id; refresh may update the display without replacing
that intent. Retry remains available while conflicting actions and back
navigation are blocked. The process is not persisted across a restart.

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
  migration and migration-drift check; the task specification defines any
  additional checks.

## Testing strategy

This section owns the project's testing policy. `Wms.Tests` is the only
automated test project and uses xUnit. Keep only the minimum tests needed to
protect critical warehouse state. Coverage percentage, test count and a test
for every feature are not objectives. Routine changes default to no new test.

Before adding a test, identify the concrete failure it prevents, its material
consequence, and why existing tests do not already protect it. Justified risks
include lost or duplicated stock, data loss, non-atomic posting, unsafe replay,
and database constraint or concurrency failures. A changed file, new handler,
or conceivable edge case alone is not a reason to add a test.

Prefer integration tests with disposable SQL Server LocalDB databases when the
guarantee depends on EF Core, migrations, constraints, transactions, optimistic
concurrency, `InventoryBalance`, `InventoryMovement`, `InventoryTurnover`, or
`CommandReceipt`. Use a unit/domain test only when the complete invariant lives
in memory. UI, device, printer, and live-1C checks remain explicit developer
verification unless a task specifically automates them.

Cross-cutting command guarantees are tested once around `CommandExecutor` and
must not be repeated in every vertical slice. Feature tests protect only their
own business invariant. Do not add tests for trivial CRUD, accessors,
constructors, straightforward mapping, framework behavior, or multiple inputs
that exercise the same rule. Prefer extending a relevant existing scenario
over creating another suite, provided it keeps one clear purpose. Use the
smallest test that demonstrates the distinct risk; a feature may ship without
a new test when existing protection is sufficient. Do not expand test helpers
or infrastructure unless the necessary test requires it.

Execution ownership and verification checkpoints are specified in
[`AGENTS.md`](../../AGENTS.md). Local setup and commands belong in
[`Wms.Tests/README.md`](../../Wms.Tests/README.md).
