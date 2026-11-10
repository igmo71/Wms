# Application and 1C boundary

Status: frozen reference. Completed 2026-09-07.

## Outcome

Receiving and shipping orchestration belongs to the application layer. The 1C
integration implements application-owned source and execution-sink ports, while
WebApp and Mobile API use application synchronization services. Critical
completion commands keep their established synchronization checkpoint, but
expose the two-phase save boundary explicitly and never save from a method
named `Stage...`.

## Scope

- Add one execution-sink port for receiving and one for shipping, using WMS
  terminology and only the operations required by each process.
- Move snapshot application, explicit checks, notification imports, and
  acknowledgement orchestration from concrete 1C synchronization services into
  receiving and shipping application synchronization services.
- Keep OData transport, 1C statuses and DTO mapping in `Integration.OneS`.
- Keep notification identifier parsing and configured notification delay in the
  integration adapter.
- Replace concrete 1C synchronization dependencies in WebApp, Mobile API, and
  notification dispatch with application services.
- Name the synchronization-checkpoint-plus-command operations explicitly and
  make all remaining `Stage...` methods caller-owned, save-free mutations.
- Preserve routes, contracts, business decisions, repeat-safe outbound behavior,
  and persistence ordering.

## Out of scope

- Physical project splitting.
- A generic integration, validation, command, or synchronization framework.
- Outbox or distributed transaction infrastructure.
- Endpoint/page decomposition unrelated to the dependency boundary.
- New automated tests.

## Acceptance criteria

- Application command services do not reference concrete `Integration.OneS`
  types.
- WebApp and Mobile API do not reference concrete 1C synchronization services.
- 1C document notification handling still applies the configured delay, may
  create missing source orders, and rejects malformed identifiers.
- Explicit checks still require a local order; acknowledgement still performs a
  fresh fingerprint check.
- Completing receiving, completing picking, and shipping persist the fresh
  synchronization checkpoint before later local and outbound effects.
- No method named `Stage...` calls a save operation, directly or indirectly.
- Relevant projects build and `git diff --check` passes; no tests are created.

## Result

- Added receiving and shipping execution-sink ports beside the existing source
  ports. Concrete 1C outbound services now implement those ports and remain
  responsible for statuses, OData mapping, PATCH/POST, and repeat-safe target
  recognition.
- Added application synchronization services for explicit checks,
  notification imports, fresh acknowledgement, reconciliation, assessment, and
  persisted completion checkpoints. The former concrete 1C synchronization
  services were removed.
- WebApp and Mobile API now request application synchronization services. The
  notification adapter retains configured delay and identifier parsing before
  invoking the application operation.
- Removed the remaining `Application -> Integration.OneS` reference by decoding
  Mobile document barcodes at the HTTP boundary and passing a document id to
  application queries.
- Critical receiving, picking-completion, and shipping paths expose their
  synchronization-checkpoint phase in the operation name. Their nested
  `Stage...` methods no longer persist, while Mobile receipts remain part of the
  final local save after the checkpoint.
- Receiving and shipping command services were reduced by separating
  synchronization responsibility; the remaining larger bodies retain cohesive
  workflow, inventory, and editing operations.
- The complete solution builds successfully. Existing Android duplicate-native-
  library warnings and the known `Microsoft.OpenApi 2.0.0` `NU1903` warning
  remain. `git diff --check` passes and no automated tests were created.
