# Architecture and process-boundary review

Status: frozen reference. Accepted 2026-09-07.

## Outcome

Produce an evidence-based, staged simplification proposal for the implemented
WMS workflows before changing the current architecture. Each business action
must have a recognizable server-side path shared by WebApp and Mobile, with
explicit ownership of local persistence, 1C calls, mobile idempotency, and
concurrency handling.

The accepted output of this issue is a review and a prioritized change plan,
not a broad refactoring.

## Scope

- Trace receiving, putaway, picking, shipping, shipping rollback, inventory
  count, and intra-warehouse transfer commands from WebApp or Mobile through
  HTTP/UI, application, domain, integration, and persistence code.
- Compare WebApp and Mobile orchestration for the same business action,
  including validation, save boundaries, result mapping, and mobile command
  receipts.
- Review the application boundary around 1C document reads, writes,
  synchronization, and notification handling so `Integration.OneS` can later
  be extracted without a circular project dependency.
- Review external-call and database-save ordering, retry assumptions,
  transaction boundaries, and the actual contract of `Stage...` methods.
- Identify concrete defects, unreachable code, accidental abstractions,
  oversized services, and domain rules hidden by persistence or transport
  details.
- Separate correctness fixes and safe cleanup from optional redesign. Any
  implementation that follows this review receives its own accepted scope.

## Out of scope

- Implementing the proposed architecture or performing broad refactoring.
- Creating new automated tests. Verification uses builds, static inspection,
  existing checks where available, and focused manual scenarios appropriate to
  each later accepted change.
- Adding roadmap product features, pilot security, an outbox, durable
  notifications, or new warehouse workflows.
- Reopening frozen specifications unless the current code contradicts a
  lasting rule that cannot be resolved from current documentation.
- Changing Mobile UX or 1C business semantics without new operational
  evidence.

## Review artifacts

- [`review.md`](review.md) contains the evidence, command-path matrix,
  finding summary and staged recommendations. It links to focused supporting
  evidence documents in the same directory.
- [`change-plan.md`](change-plan.md) contains the accepted classification,
  ordered implementation batches, and focused verification scenarios.

## Acceptance criteria

- Every in-scope process has a traced WebApp and Mobile path, or is explicitly
  identified as intentionally unavailable on one client.
- Findings cite concrete code paths and state the operational consequence.
- Local saves, 1C calls, retries, mobile receipts, and concurrency handling are
  explicit for every state-changing path.
- Direct WebApp and WebApi dependencies on concrete 1C synchronization types
  are accounted for in an extractable application-facing boundary proposal.
- Concrete defects and safe cleanup are separated from optional architectural
  redesign.
- Recommendations are prioritized by correctness risk and simplification
  value, and each proposed shape is supported by repeated evidence.
- Recommendations make the primary use-case path readable from top to bottom
  for a developer new to the feature. Files above roughly 300 lines are
  reviewed as a maintainability signal, but cohesive algorithms are not split
  merely to satisfy a line limit.
- Warehouse-user observations from staging can be added without blocking the
  initial review or silently changing its scope.
- No broad refactoring starts until the review is accepted.
- The review does not propose new automated tests as an implementation
  prerequisite.

## Questions retained for later operational evidence

- No additional warehouse-user failures were available when the review was
  accepted; later observations may reprioritize a focused batch.
- Shipping rollback remains a WebApp supervisory operation until a Mobile
  requirement is demonstrated.
- Additional staging evidence for repeat-safe 1C target-state operations is
  still needed for the separate operator-recovery work.
