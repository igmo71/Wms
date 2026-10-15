# Architecture and process-boundary review

Status: accepted 2026-09-07. This is the decision-oriented review summary. Supporting
evidence is kept in focused documents:

- [`command-paths.md`](command-paths.md) — client paths, side effects, saves,
  receipts, and concurrency;
- [`correctness-findings.md`](correctness-findings.md) — concrete operational
  and consistency findings;
- [`maintainability-findings.md`](maintainability-findings.md) — dependency,
  responsibility, large-file, and cleanup findings;
- [`operational-location-policy.md`](operational-location-policy.md) — paper
  prototype checked against current call sites;
- [`change-plan.md`](change-plan.md) — classification, delivery batches, and
  focused verification scenarios.

No production change is authorized by this document. No new automated tests
are created or proposed as an implementation prerequisite. Verification uses
builds, static inspection, existing checks, and focused manual scenarios.

## Agreed working assumptions

- Preserve observable behavior, API contracts, and the data model unless a
  concrete defect justifies a scoped change.
- Treat roughly 300 lines as a maintainability signal, not a mandatory limit.
- Optimize for a readable top-to-bottom business sequence and local,
  discoverable knowledge rather than for the number of types or layers.
- Use business-named internal collaborators inside a feature; introduce an
  interface only at a real boundary.
- Make the application/1C direction ready for later extraction of
  `Integration.OneS`; the physical project split is not a review outcome.
- Keep readable code and current `ARCHITECTURE.md` as the primary onboarding
  material; add no feature README without a concrete need.
- Complete and accept the review before changing production architecture.

## Review method

Each state-changing action is traced through UI/HTTP, application, domain,
integration, local persistence, concurrency, and Mobile receipt boundaries.
Priorities are P0 for plausible unrecoverable inventory/cross-system loss, P1
for pilot correctness or ambiguous state, P2 for boundary and maintainability
risk, and P3 for safe cleanup.

A developer new to a feature should be able to open its primary operation and
read the sequence without first navigating transport, projection, persistence,
or reusable eligibility details. A large cohesive algorithm may remain large.

## Finding summary

| Id | Priority | Finding | Proposed class |
| --- | --- | --- | --- |
| F-01 | P1 | Concrete OneS types cross both sides of the application boundary | boundary simplification |
| F-02 | P1 | `Stage...` sometimes persists a synchronization checkpoint | explicit two-phase command contract |
| F-03 | P1 | Web transfer creation bypasses conflict classification | focused correctness fix |
| F-04 | P2 | Synchronization orchestration is repeated in hosts | application-facing synchronization use case |
| F-05 | open | Shipping rollback is WebApp-only | confirm intentional client boundary |
| F-06 | P2 | Large services mix orchestration with different validation kinds | named policies and responsibility splits |
| F-07 | P1 | Mobile can return pre-synchronization order details with a new assessment | focused correctness fix plus boundary cleanup |
| F-08 | P1 | Mutable endpoint prechecks can bypass Mobile receipt replay | move mutable checks inside receipt boundary |
| F-09 | P1 | Web receiving completion performs a preliminary location save | one explicit operation or explicit earlier edit |
| F-10 | P1 pending consumers | Mapped legacy receiving API exposes divergent commands | verify consumers, migrate or remove |
| F-11 | P2 | Mobile technical containers span all business features | feature clients/contracts and repeated UI mechanics |
| F-12 | P2 | Rich order aggregates combine workflow and source reconciliation | cautious domain-boundary review |
| F-13 | P3 | Confirmed template, pass-through, commented, and telemetry cleanup | isolated cleanup batch |
| F-14 | P1 | Final posting does not consistently require active topology | posting fix plus topology concurrency decision |

Detailed evidence and consequences for F-02, F-03, F-07 through F-10, and
F-14 are in `correctness-findings.md`. The remaining findings are in
`maintainability-findings.md`.

## Candidate application/1C direction

```text
WebApp / Mobile V1 / notification adapter
        -> application receiving or shipping use case
             -> I...OrderSource
             -> I...OrderExecutionSink
             -> DbContext and domain aggregate

Integration.OneS
        -> implements the application ports
        -> owns OData DTOs, URI/status values, PATCH/POST mechanics
        -> owns notification parsing and delay
```

Application synchronization owns snapshot application, fingerprint
acknowledgement, and the decision whether an assessment allows work. The OneS
adapter owns protocol mechanics but not reconciliation rules. This is not a
handler-per-command design.

## Provisional implementation sequence

No item starts until the completed review is accepted.

1. Isolated P1 defects: transfer conflict mapping, stale Mobile details, and
   active-topology validation plus topology revision advancement.
2. Restore Mobile receipt replay before mutable business prechecks.
3. Remove or migrate the legacy receiving API after consumer confirmation.
4. Resolve Web receiving completion's preliminary save.
5. Introduce the narrow operational-location policy on one workflow, verify
   readability manually, then apply only to matching sites.
6. Make synchronization checkpoints and `Stage...` semantics explicit.
7. Introduce application-owned 1C ports and synchronization operations.
8. Split Mobile API client/contracts by feature, then pilot one page-specific
   process object; split other large files only along responsibilities proven
   by this review.
9. Apply the isolated P3 cleanup batch.

## Decisions awaiting operational evidence

These do not block acceptance. Until evidence changes them, the plan treats
the legacy receiving API as live and keeps shipping rollback Web-only as a
supervisory operation. Staging observations may reprioritize a later batch but
do not silently broaden it.
