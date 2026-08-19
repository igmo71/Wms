# Architecture alignment

## Outcome

WMS should remain easy to follow while its business rules become harder to
bypass. The codebase will converge incrementally on `docs/ARCHITECTURE.md`,
without a big-bang rewrite or speculative frameworks.

## Audit summary

The 2026-08-19 audit found that most persisted models still expose public
mutation. `Zone` and `StorageLocation` established the rich-model pilot;
`InventoryTransfer`, `InventoryCount`, the inventory facts, receiving,
putaway, and the integrated shipping workflow have since been aligned. The
remaining review starts with catalogs and configuration models.

Large command services contain both necessary persistence decisions and local
domain rules. The goal is not to remove conditions, but to leave each condition
at the layer that has the information needed to decide it.

## Decisions

- Preserve the direct `UI -> application service -> domain -> EF Core` path.
- Enrich models selectively according to WMS ownership and actual behavior.
- Use the shared `OperationResult` family for expected domain, application, and
  integration outcomes; unexpected failures remain exceptions.
- Keep immutable, feature-local requests only where they clarify an operation.
- Pass time and user identifiers into audited domain transitions.
- Do not introduce CQRS infrastructure, repositories, base entities, or
  validation pipelines as part of alignment.
- Automated tests and test projects are explicitly deferred for the MVP.
- Deliver each functional stage independently and verify it by build, static
  inspection, EF migration checks when relevant, and focused manual scenarios.

## Delivery stages

### Stage 1: conventions and documentation

- Establish the architecture guide, root `.editorconfig`, and this staged
  backlog.
- Keep project context focused on product and business behavior; keep detailed
  engineering conventions in the architecture guide.

### Stage 2: shared result and pilot completion

- Rename the application-styled result family to the neutral
  `OperationResult` shared kernel.
- Let `Zone`, `StorageLocation`, and their value objects return expected
  validation failures directly.
- Remove the exception-to-result `DomainOperation` adapter.
- Keep request-only cross-field validation on
  `GenerateStorageLocationsRequest` and database checks in its service.

### Stage 3: WMS-owned inventory documents

1. `InventoryTransfer` (completed): factory, immutable transit context,
   start-on-first-movement, completion, audit state, and private mutation.
2. `InventoryCount` and `InventoryCountItem` (completed): factory, controlled
   row editing, posting transition, audit state, and read-only items.
3. Inventory facts (completed): controlled movement creation, draft editing and
   posting, balance adjustment, and immutable turnover creation.

Cross-location balance checks and the EF transaction remain in application
services.

### Stage 4: integrated order workflows

1. Receiving import/reconciliation and receiving transitions (completed):
   domain import snapshot, conflict-safe reconciliation, controlled fact
   editing, receiving location, status transitions, and audit state.
2. Putaway lifecycle and draft movement editing (completed): controlled draft
   creation, editing and removal, per-line quantity limits, completion checks,
   and audit state.
3. Shipping import/reconciliation and shipping transitions (completed): domain
   import snapshot, conflict-safe reconciliation, shipping location, controlled
   facts, status transitions, audit state, and read-only lines.
4. Picking and rollback lifecycle (completed): controlled draft creation,
   editing and removal, synchronized line facts, completion consistency,
   compensation creation, and rollback audit state.

This stage removes public workflow mutation while keeping 1C calls in
application and integration services.

### Stage 5: catalogs and configuration review

- Review `Warehouse` and synchronized catalogs only for actual WMS-owned rules.
- Keep data-only imports simple where no such rule exists.
- Encapsulate navigation collections when callers do not need mutation.

### Stage 6: queries, UI, and cleanup

- Keep UI form state separate from requests and domain models.
- Move feature-specific list contracts out of generic locations when touched.
- Remove obsolete requests, mutation paths, and duplicated validation.
- Split command/query services only when responsibilities justify it.

## Acceptance criteria for a functional substage

- Documented behavior is preserved unless a business-rule change is approved.
- Local invariants are executable in the domain and cannot be bypassed through
  public setters.
- External-state checks remain explicit in the application operation.
- UI and integrations compile against the revised boundary.
- The affected projects and complete solution build; `git diff --check` passes;
  EF has no pending model changes when persistence mapping changes.
- Relevant architecture and business documentation is updated.

## Non-goals

- A repository-wide mechanical rich-model rewrite.
- Making every persisted class an aggregate.
- Eliminating every `if` statement.
- Changing business workflows during architecture-only work.
- Adding CQRS, domain events, test projects, or persistence abstractions.
