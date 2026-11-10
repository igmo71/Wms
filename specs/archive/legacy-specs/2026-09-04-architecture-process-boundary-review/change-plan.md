# Prioritized change plan

This plan is the proposed outcome of the review, not authorization to change
production code. Each batch receives its own accepted scope. Batches are kept
small enough that a correctness change is not concealed by file movement.

No batch creates automated tests. Verification combines targeted builds,
static inspection of every affected client path, existing checks when useful,
and the named focused manual scenarios.

## Classification

| Finding | Class | Decision |
| --- | --- | --- |
| F-03, F-07, F-08, F-09, F-10, F-14 | correctness | implement in focused batches |
| F-01, F-02, F-04 | boundary simplification | implement after immediate defects |
| F-06, F-11 | maintainability simplification | pilot narrowly, then reassess |
| F-13 | safe cleanup | isolated low-risk batch |
| F-05 | product/client boundary | retain current behavior pending evidence |
| F-12 | optional redesign | defer; aggregate size is not sufficient cause |

## Batch A — isolated correctness

1. Route Web transfer creation through classified persistence (F-03).
2. Query Mobile order details after synchronization, not before it (F-07).

These changes are independent and may be delivered separately. Verify the Web
conflict response for competing transit-location assignment and that Mobile
returns the reconciled plan with the assessment produced by the same open.

## Batch B — operational topology integrity

Implement F-14 and the accepted parts of the operational-location paper API:

- final posting requires an active warehouse, location, and zone, a
  non-folder location in the movement warehouse, and an admissible lock;
- each completing feature revalidates its route's zone roles;
- eligibility-changing location writes advance their own operational revision;
- eligibility-changing zone and warehouse writes advance affected child
  location revisions in the same classified save.

Pilot the common location policy in one workflow before replacing matching
checks elsewhere. Verify inactive location, inactive/type-changed zone,
inactive warehouse, foreign lock, and a configuration-change/posting race.

## Batch C — Mobile receipt boundary

For F-08, ensure receipt lookup and request compatibility precede mutable
entity resolution and business preconditions. Keep authentication and
deterministic transport parsing outside. Treat inventory-count
open-existing/create semantics explicitly rather than as a shortcut around a
receipt.

Verify a retry after the first response is lost, a reused request id with
different semantic input, and a retry after relevant mutable state has moved
on.

## Batch D — receiving consistency

Resolve F-09 by choosing one explicit behavior: completion accepts and stages
the selected location, or location editing is an independently named action.
Do not retain an implicit save immediately before completion.

Treat the legacy route in F-10 as live until consumers are checked. If it must
remain, migrate it to the same application operation and explicit error
contract. Remove it only with positive evidence that no external caller uses
it.

## Batch E — explicit synchronization boundary

Address F-01, F-02, and F-04 together because they describe one ownership
problem:

- application-owned source and execution-sink ports point toward 1C adapters;
- application synchronization operations own reconciliation and assessment;
- hosts no longer compose concrete OneS synchronization services;
- checkpoint persistence is explicitly named, and `Stage...` again means no
  save in a caller-owned context.

Do not physically split the OneS project in this batch. Its success criterion
is acyclic dependency direction and a primary command path readable from top
to bottom.

## Batch F — maintainability pilots

1. Introduce the operational-location policy only where Batch B demonstrated
   that it improves the calling workflow (F-06).
2. Split Mobile API client and contract files by business feature while
   retaining one internal HTTP/session transport and unchanged wire contracts.
3. Extract the Android focus helper.
4. Decompose one receiving or picking page with a concrete feature process
   owning API sequences and retry ids; the page retains modes and UI.

Stop after the first page and review the result. Do not propagate the shape if
the business sequence became harder to follow. Scanner-session extraction is
optional and requires proven reduction on at least two pages. Leave aggregate
reconciliation extraction (F-12) deferred until a concrete change needs that
boundary.

## Batch G — safe cleanup

Apply F-13 alone: remove unused template artifacts and stale comments/pass-
through code, and correct the receiving telemetry category. Build affected
projects and inspect the resulting diff; no behavior change is intended.

## Current conservative decisions

- Mobile shipping rollback remains intentionally unavailable for now. The Web
  path is treated as a supervisory operation until pilot evidence creates a
  Mobile requirement.
- Legacy `/api/ReceivingOrder` consumers are unknown, so removal is not
  authorized.
- No additional staging observations are currently available. They may adjust
  priorities without reopening the traced architecture.
