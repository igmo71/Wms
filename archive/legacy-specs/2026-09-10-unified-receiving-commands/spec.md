# Unified receiving commands

Status: Frozen — pilot accepted by the user on 2026-09-10.

## Outcome and scope

WebApp and Mobile execute StartReceiving and CompleteReceiving through the same
public ReceivingOrderCommandService methods and CommandExecutor. Architectural
evaluation is complete; the shared execution approach is accepted. Fact entry, comments,
putaway, shipping, transfers, and counts retain their existing use cases.

## Agreed contract

- CommandExecutor owns receipt lookup/replay, winning receipt recovery, and the
  atomic final save of WMS effects plus receipt. Unknown persistence errors remain
  exceptions; recognized failures use PersistenceConflictClassifier.
- Receipt lookup precedes mutable business resolution and 1C access.
- Completion may call the explicitly named PersistCompletionCheckpointAsync
  before final effects. This independently persisted synchronization checkpoint
  is the only intermediate save in the pilot. Business completion helpers do not save.
- Completion accepts an optional location: null uses the assigned location after
  lookup; an explicit location preserves Web behavior. Hashes use original input.
- Existing Mobile command types, hashes, receipt key, routes, and transport
  contracts remain compatible. Per the subsequent user decision, rename the SQL
  table and request column as well as C# names using a data-preserving migration;
  no legacy mapping is needed.
- Receipt replay returns the saved resource id, not a historical HTTP response.
- Web retains request id and immutable input together for uncertain retries within
  the component lifetime. Reload/component recreation recovery is outside this
  pilot. Success or definitive rejection releases the attempt; failures retain it.
- No command bus, handler hierarchy, generic pipeline, outbox, or distributed
  transaction. Concurrent requests may both reach 1C; external target operations
  must remain repeatable.

## Acceptance and verification

1. Both clients call the same public methods, with caller-provided request ids.
2. Same input/id replays without business or 1C effects; changed input conflicts.
3. Previously persisted Mobile receipts for both commands still replay.
4. Final state and receipt commit atomically; concurrent final saves resolve via
   the winning receipt, while unknown persistence failures propagate.
5. Checkpoint survives a rejected final phase, and replay skips checkpoint.
6. Remove only pilot wrapper methods and orchestration-only Stage helpers;
   retain business-named helpers where they improve readability.
7. Verify migration preserves existing receipts and leaves no model drift; build Wms,
   Wms.WebApi, Wms.WebApp, and Wms.Mobile.
8. Update current documentation with lasting rules and report verification limits.

## Open questions

None within the accepted pilot. Further feature migrations are separate scopes
tracked in docs/ROADMAP.md.

## Pilot verification

- LocalDB integration checks pass: old-schema receipts survive migration and
  replay, hash conflicts, both completion inputs, one movement/turnover, checkpoint
  persistence on failure, retry, concurrent start requests, winning receipts,
  atomic rollback, known conflicts and unknown persistence exceptions.
- EF reports no pending model changes after the rename migration.
- Existing ReceivingSynchronization regression checks pass.
- Wms, WebApp, WebApi and Android Mobile build. Existing build warnings concern
  Microsoft.OpenApi 2.0.0 (NU1903) and duplicate Android native libraries (XA4301).
- The migration was exercised only on an isolated disposable LocalDB database.
  Browser interaction and live 1C were not exercised. Web pending state remains
  limited to the component lifetime as agreed.

Lasting behavior is documented in docs/PROJECT_CONTEXT.md and docs/ARCHITECTURE.md.
This frozen specification records the accepted pilot. Future feature migrations
require their own scope and do not extend this specification.
