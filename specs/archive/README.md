# Archived specifications

Historical records only. Current rules live in
[PROJECT_CONTEXT.md](../../docs/PROJECT_CONTEXT.md), and remaining work lives in
[ROADMAP.md](../../docs/ROADMAP.md). Status labels inside older files describe
their original time; they do not make a specification active.

## Accepted records

- [Default warehouse for a WMS user](2026-10-08-default-user-warehouse/spec.md)
  — accepted on 2026-10-08: optional account preference, shared Mobile selection
  during an app run, and explicit choice when the assigned warehouse is unavailable.

- [Consistent quantity display across Web and TSD](2026-10-07-quantity-display-consistency/spec.md)
  — accepted on 2026-10-07: Web/TSD quantity display aligned; line-based
  progress and per-line picking discrepancy confirmation verified.

- [Finish receiving and putaway model cleanup](2026-10-07-receiving-model-cleanup/spec.md)
  — accepted on 2026-10-07: unused receiving paths and hidden Mobile queue data
  removed; completion and LPN audit data retained.

- [Remove direct receiving fact edits](2026-10-07-remove-direct-receiving-facts/spec.md)
  — accepted on 2026-10-07: obsolete direct line edits removed while
  LPN-derived facts and receiving synchronization remain.

- [Remove obsolete order-level putaway](2026-10-07-remove-order-putaway/spec.md)
  — accepted on 2026-10-07: old order-level status, commands, Mobile branch and
  database columns removed while LPN putaway remains.

- [Web pallet putaway progress](2026-10-07-web-pallet-putaway-progress/spec.md)
  — accepted on 2026-10-07: read-only LPN placement progress, direct pallet
  selection from the SKU menu, and removal of the unused order-level Web view.

- [Web receiving SKU distribution](2026-10-06-web-receiving-sku-distribution/spec.md)
  — accepted on 2026-10-07: a Web order SKU menu shows its retained quantities
  by active pallet without changing the table width.

- [Web receiving quantity format](2026-10-06-web-receiving-quantity-format/spec.md)
  — accepted on 2026-10-06: read-only Web receiving quantities use three
  decimal places; a broader UI review remains future work.

- [Web receiving quantity units](2026-10-06-web-receiving-quantity-units/spec.md)
  — accepted on 2026-10-06: `Ед изм` shows the SKU's canonical unit on both
  Web receiving tabs without changing quantity semantics.

- [Web receiving completion guidance](2026-10-06-web-receiving-completion-guidance/spec.md)
  — accepted on 2026-10-06: the Web completion action explains its unavailable
  state, and the discrepancy panel omits the misleading source-fix sentence.

- [One Web receiving order page](2026-10-06-one-web-receiving-order-page/spec.md)
  — accepted on 2026-10-06: one URL, shared views, discrepancies and compact
  audit, with the final WebApp build and startup confirmed.

- [Manual SKU selection in an open receiving LPN](2026-10-06-receiving-lpn-sku-selection/spec.md)
  — accepted on 2026-10-06: planned SKU search, return behavior and neutral styling.

- [Safe quantity confirmation before LPN putaway](2026-10-05-safe-putaway-quantity/spec.md)
  — TSD behavior accepted on 2026-10-06; camera fallback awaits separate verification.

- [Multiple WMS roles per account](2026-10-05-multiple-wms-roles/spec.md)
  — accepted on 2026-10-05: explicit role sets, atomic permission updates,
  last-active-administrator protection and bootstrap role preservation.

- [LPN receiving safety and command recovery](2026-09-18-lpn-receiving-safety/spec.md)
  — R1–R4 accepted on 2026-10-05: joining eligibility, Manager completion,
  uncertain-command recovery and stale scanner callback protection.

- [LPN receiving and putaway](2026-09-15-lpn-receiving-putaway/spec.md)
  — I01–I08 accepted on 2026-09-17: LPN issuance, participation and counting,
  exact/Manager completion, independent putaway, destination recommendations,
  corrected Mobile workflow and unified read-only Web order contents.

- [Minimal xUnit test strategy](2026-09-15-xunit-test-strategy/spec.md)
  — Replaced the executable test harnesses with one risk-based xUnit suite and
  documented the lasting test-selection rules.

- [LPN development planning](2026-09-14-lpn-development-planning/spec.md)
  — Planning completed on 2026-09-15. The current backlog lives in
  `docs/ROADMAP.md`; the planned I01–I08 receiving/putaway scope was accepted
  on 2026-09-17. Future stages require a separate specification.

- [Unified shipping rollback](2026-09-11-unified-shipping-rollback/spec.md)
  — Repeat-safe Web rollback implemented on 2026-09-11.

- [Unified picking commands](2026-09-11-unified-picking-commands/spec.md)
  — Shared draft add/update/delete implemented on 2026-09-11.
- [Unified putaway commands](2026-09-10-unified-putaway-commands/spec.md)
  — Shared start, draft movements and completion implemented on 2026-09-11.
- [Unified receiving facts](2026-09-10-unified-receiving-facts/spec.md)
  — Shared facts and line comments implemented on 2026-09-10.
- [Unified inventory count commands](2026-09-10-unified-count-commands/spec.md)
  — Shared start, facts, posting and draft deletion implemented on 2026-09-10.
- [Unified inventory transfer commands](2026-09-10-unified-transfer-commands/spec.md)
  — Shared creation, movements, completion and draft deletion implemented on 2026-09-10.
- [Unified shipping transitions](2026-09-10-unified-shipping-commands/spec.md)
  — Shared start-picking, complete-picking, and ship implemented on 2026-09-10.
- [Unified receiving commands](2026-09-10-unified-receiving-commands/spec.md)
  — Receiving start/completion pilot accepted on 2026-09-10.

## Earlier records

Older material was not part of the active registry. It is retained for decision
history, not as a current requirement.

- [2026-08-21-inventory-transfer-concurrency](2026-08-21-inventory-transfer-concurrency/spec.md)
- [2026-08-25-mobile-command-idempotency](2026-08-25-mobile-command-idempotency/spec.md)
- [2026-08-27-location-inventory-count](2026-08-27-location-inventory-count/spec.md)
- [2026-08-27-storage-location-locking](2026-08-27-storage-location-locking/spec.md)
- [2026-08-29-mobile-receiving-putaway](2026-08-29-mobile-receiving-putaway/spec.md)
- [2026-08-31-mobile-picking-shipping](2026-08-31-mobile-picking-shipping/spec.md)
- [2026-09-01-mobile-wms-stabilization](2026-09-01-mobile-wms-stabilization/spec.md)
- [2026-09-01-operator-location-mobile-ux](2026-09-01-operator-location-mobile-ux/spec.md)
- [2026-09-02-decimal-warehouse-quantities](2026-09-02-decimal-warehouse-quantities/spec.md)
- [2026-09-02-order-synchronization-decisions](2026-09-02-order-synchronization-decisions/spec.md)
- [2026-09-02-order-synchronization-simplification](2026-09-02-order-synchronization-simplification/spec.md)
- [2026-09-02-order-synchronization-state-cleanup](2026-09-02-order-synchronization-state-cleanup/spec.md)
- [2026-09-03-initial-order-plan-synchronization](2026-09-03-initial-order-plan-synchronization/spec.md)
- [2026-09-03-standalone-staging](2026-09-03-standalone-staging/spec.md)
- [2026-09-04-architecture-process-boundary-review](2026-09-04-architecture-process-boundary-review/spec.md)
- [2026-09-07-application-onec-boundary](2026-09-07-application-onec-boundary/spec.md)
- [2026-09-07-immediate-correctness-fixes](2026-09-07-immediate-correctness-fixes/spec.md)
- [2026-09-07-mobile-maintainability-pilot](2026-09-07-mobile-maintainability-pilot/spec.md)
- [2026-09-07-mobile-receipt-replay-boundary](2026-09-07-mobile-receipt-replay-boundary/spec.md)
- [2026-09-07-operational-topology-integrity](2026-09-07-operational-topology-integrity/spec.md)
- [2026-09-07-receiving-command-consistency](2026-09-07-receiving-command-consistency/spec.md)
- [2026-09-07-safe-cleanup](2026-09-07-safe-cleanup/spec.md)
- [2026-09-08-documentation-revision](2026-09-08-documentation-revision/spec.md)
- [architecture-alignment](architecture-alignment/spec.md)
- [document-information-headers](document-information-headers/spec.md)
- [document-list-layout](document-list-layout/spec.md)
- [employee-performance-report](employee-performance-report/spec.md)
- [identity-roles-and-user-management](identity-roles-and-user-management/spec.md)
- [individual-import](individual-import/spec.md)
- [intra-warehouse-transfers](intra-warehouse-transfers/spec.md)
- [inventory-balances](inventory-balances/spec.md)
- [inventory-count-backend](inventory-count-backend/spec.md)
- [inventory-turnovers](inventory-turnovers/spec.md)
- [mobile-wms](mobile-wms/spec.md)
- [operational-hardening](operational-hardening/spec.md)
- [order-parties](order-parties/spec.md)
- [order-workflow-consistency](order-workflow-consistency/spec.md)
- [organizational-unit-import](organizational-unit-import/spec.md)
- [partner-import](partner-import/spec.md)
- [picking-draft-movements](picking-draft-movements/spec.md)
- [receiving-putaway](receiving-putaway/spec.md)
- [shipping-order-items-odata-update](shipping-order-items-odata-update/spec.md)
- [shipping-order-rollback](shipping-order-rollback/spec.md)
- [shipping-order-ui](shipping-order-ui/spec.md)
- [shipping-order-workflow](shipping-order-workflow/spec.md)
- [sku-physical-properties](sku-physical-properties/spec.md)
- [storage-location-topology](storage-location-topology/spec.md)
- [strict-order-workflows](strict-order-workflows/spec.md)
- [synchronized-catalogs-ui](synchronized-catalogs-ui/spec.md)
- [weight-in-wms](weight-in-wms/spec.md)
