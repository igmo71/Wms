# Unified shipping rollback

Status: Frozen — implemented and verified on 2026-09-11.

## Outcome

Web shipping details and picking pages call ShippingOrderCommandService.RollbackAsync
through CommandExecutor with immutable order/reason and CommandContext. This closes
the remaining scoped warehouse command migration; no new Mobile route is added.

## Contract

- Stable shipping-order.rollback type. Hash original order id and JSON-encoded
  reason without trimming; domain retains reason validation and audit trimming.
- Receipt lookup precedes mutable state. Delete drafts, compensate only posted
  movements of the current picking cycle, reset facts/location/work timestamps,
  return to Prepared and save receipt atomically. Keep turnover history.
- No 1C call, checkpoint or new schema. Preserve route/lock/source balance checks,
  prepared/shipped prohibition and recognized concurrency classification.
- Replay must not undo work from a later cycle or recreate compensations. A new
  cycle needs a fresh request. Different input under a saved request conflicts.
- Web keeps original reason/request/user during component lifetime for explicit
  retry after uncertain failure, without reopening the reason dialog. Other
  mutations/acknowledgements remain blocked while choosing/executing/pending.
  Page reload recovery remains outside scope.

## Acceptance

Real SQL tests: draft-only rollback, posted compensation, lock/stock rejection
without effects and retry, unchanged history, audit reset, repeat after new cycle,
invalid state/reason, duplicate and distinct concurrent rollback, rollback/edit
race. Verify no external access and no model drift. Run command regressions and
WebApp/WebApi/Mobile builds. Update docs/registry and freeze with verification limits.

## Open questions

None blocking implementation.

## Verification

- ShippingRollback passed against an isolated migrated SQL Server LocalDB database,
  including duplicate/distinct races, rollback/edit conflict and new-cycle replay.
  The EF model has no pending migration changes.
- ReceivingCommands, ReceivingFacts, ReceivingSynchronization, ShippingCommands,
  TransferCommands, CountCommands, PutawayCommands and PickingCommands passed.
- WebApp, WebApi and Android Mobile builds passed. Existing warnings remain:
  WebApi NU1903 (Microsoft.OpenApi 2.0.0), Mobile XA4301 (duplicate native
  libraries), and ReceivingSynchronization NU1900 (NuGet audit source unavailable).
- No schema migration or real 1C calls were needed. Web dialog/retry behavior was
  reviewed in code and compiled, but was not manually exercised in a browser.
- Recovery remains limited to the component lifetime; cross-system operator
  recovery and pilot rehearsal remain in the roadmap.
