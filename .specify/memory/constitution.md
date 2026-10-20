# WMS Constitution

## Core Principles

### I. WMS owns warehouse inventory

1C owns source documents and imported catalog fields; WMS may own local
operational SKU flags. WMS owns receiving work, storage locations, movements,
balances and operational history. After successful synchronization, a change
in a source document MUST update the current plan without silently rewriting
an already posted warehouse fact. Warehouse stock changes MUST be represented by
movements; corrections add movements and preserve the original operation.

### II. One posting path for each physical event

Every physical receipt, placement, pick, shipment or correction MUST have one
authoritative posting point. The movement, balance, turnover, provenance,
business audit and command receipt MUST be committed consistently. A retry of
the same command MUST NOT duplicate warehouse effects. Replacing a workflow
requires removing its former posting path before the replacement is usable.

### III. Explicit 1C boundary

1C transport and document formats belong in the integration adapter. WMS
application use cases decide how source state affects WMS plans and operations.
An unreadable or unsupported source value MUST remain distinguishable from a
known zero. Receiving currently has no outbound write to 1C; any future write
requires a separately specified business decision.

### IV. Direct feature-oriented implementation

Use a recognizable application use case for each operation, domain behavior for
local invariants, and EF Core for persistence and cross-aggregate state. Add
projects, interfaces or layers only for a concrete boundary needed by the
feature. Domain has no WMS project dependencies; new Application references
Domain; new Infrastructure and Integration reference Application. The legacy
`Wms` project may reference Domain and Infrastructure while its services are
replaced; new projects MUST NOT reference the legacy assembly. Dependency
cycles and duplicate operational paths are not acceptable.

### V. Verification follows material risk

Add automated tests when existing tests do not protect a material risk such as
duplicate stock, data loss, non-atomic posting, unsafe replay or concurrency.
Routine edits do not require new tests. Apply the detailed policy in
[`architecture.md`](architecture.md#testing-strategy).
Execution ownership for builds, tests, launches and EF migrations is defined
in [`AGENTS.md`](../../AGENTS.md).

## Documentation Ownership

`AGENTS.md` owns instructions for Codex. This constitution owns lasting
engineering principles. `current/` describes implemented behavior by
subcontext, and `architecture.md` describes current technical structure and
engineering conventions. `backlog.md` contains deferred work; operational
runbooks belong in `.specify/runbooks/`. When used, `spec.md` describes the
intended business outcome, `plan.md` explains its implementation, and `tasks.md`
records bounded increments. Keep each detailed rule in one owning document and
link to it elsewhere. A planned change MUST NOT be presented as implemented.
Use lowercase file names for project documentation; keep the conventional
Spec Kit names `constitution.md`, `spec.md`, `plan.md`, and `tasks.md`.

Completed feature directories remain historical records. Later changes that
need a specification get a new feature directory; small follow-ups can be
resolved in their issue and discussion. Lasting accepted rules move into the
relevant current-state subcontext as the implementation becomes real.
Top-level `archive/legacy-specs/` preserves older material but is not a source
for current decisions.

## Delivery Workflow

Scale the workflow to the size and uncertainty of the change. Resolve material
questions through discussion; a bounded change may then go straight to
implementation. Use a new `spec.md` when the business outcome needs a durable
record, `plan.md` when the approach or sequence needs explanation, and `tasks.md`
when multiple dependent increments need tracking. A specification alone does
not require the other artifacts. This keeps small corrections moving while
preserving decisions that matter. Analyze contradictions before coding and
check delivered work against any agreed artifacts. Work in reviewable
increments. The developer runs operational verification and EF migration
commands as stated in `AGENTS.md`; the agent reviews their reported results
and created migrations.

## Governance

Every implementation plan MUST check these principles and justify a departure
before implementation. Amend this constitution when a lasting principle
changes, describing the reason and updating affected guidance in the same
change. Increment MAJOR for incompatible governance changes, MINOR for new or
materially expanded rules, and PATCH for clarification. User instructions take
precedence over this document.

**Version**: 1.2.0 | **Ratified**: 2026-10-14 | **Last Amended**: 2026-10-20
