# Implementation plan

Each stage is independently reviewable and ends with a build-only technical
check. Tests are neither created nor run.

## Stage 1 — synchronization vocabulary and persisted state

- Add the three synchronization levels and compact state fields to receiving
  and shipping orders.
- Replace the boolean-only marker without keeping two competing policies.
- Add EF mappings and one migration.
- Preserve `OperationalRevision` behavior for synchronization state changes.

Manual check: existing lists and order workflows still open after migration.

## Stage 2 — pure structured comparison

- Introduce explicit comparison results and field-level differences.
- Compare all currently imported header, item, and base-item fields.
- Classify each known field as requires-decision or blocking.
- Calculate a deterministic fingerprint from relevant fresh 1C state.
- Clear the persisted issue on an exact match.

Manual check: representative metadata and quantity changes produce different
levels and readable field names.

## Stage 3 — expected WMS target states

- Recognize completed receiving facts without treating them as a new plan.
- Recognize full, partial, and zero shipping results, including residual rows
  and base items.
- Recognize the exact requested status for repeat-safe command recovery.
- Keep every third or ambiguous row state blocking.

Manual check: notifications following successful WMS operations do not create
a false conflict; a deliberately different row still does.

## Stage 4 — fresh assessment orchestration

- Reuse the existing 1C fetch and snapshot mapping boundaries without
  duplicating transport code.
- Run the same assessment from notifications and explicit order checks.
- Persist level, fingerprint, and check time without storing a second document.
- Return technical 1C failures separately from business differences.

Manual check: a missed notification is corrected by reopening the order.

## Stage 5 — WebApp details and acknowledgement

- Replace the binary list icon with the three-state presentation.
- Fetch and show structured differences on receiving and shipping details.
- Add acknowledgement for requires-decision only.
- Store user, time, and acknowledged fingerprint.
- Invalidate acknowledgement after a different fresh fingerprint.

Manual check: a metadata change can be reviewed and acknowledged, while a
quantity change has no bypass action.

## Stage 6 — command enforcement and minimal checks

- Reuse the opening assessment for the immediate start action.
- Require a fresh assessment before completing receiving, completing picking,
  and final shipping.
- Stop local posting and outbound mutation before side effects when the check
  fails, is unacknowledged, or is blocking.
- Leave putaway, scans, searches, facts, and draft movement edits free of 1C
  requests.

Manual check: each critical transition follows the agreed request count and
cannot pass a stale or blocking state.

## Stage 7 — Mobile V1 summary and enforcement

- Add synchronization level and concise changed-field summary to Mobile V1.
- Show the summary in receiving and shipping queues/details.
- Show changed comment content when applicable.
- Direct unacknowledged and blocking cases to WebApp/external resolution.
- Preserve all current scanning and idempotent command behavior.

Manual check: Mobile can continue after Web acknowledgement of the same
fingerprint and stops after a newer 1C change.

## Stage 8 — consistency pass

- Remove obsolete binary conflict wording and dead comparison paths.
- Align logs, error messages, `PROJECT_CONTEXT.md`, roadmap, and the
  specification registry with accepted behavior.
- Build affected projects and the solution without creating or running tests.

Manual check: receiving and shipping terminology is consistent in WebApp,
Mobile, logs, and documentation.

