# Specification registry

Specifications are temporary issue scopes and frozen decision history. Current
product and engineering truth belongs in `docs/`; do not read or update frozen
specifications unless a current task explicitly needs their rationale.

## Active

None.

## Frozen

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
  — Planning completed on 2026-09-15. Cross-stage plan lives in
  `docs/DEVELOPMENT_PLAN.md`; its I01–I08 receiving/putaway scope was accepted
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

## Lifecycle

Create substantial work under `specs/YYYY-MM-DD-<problem-slug>/` with one
outcome, scope, acceptance criteria, and genuine open questions. Normally only
one specification is active.

When accepted:

1. move lasting rules to the appropriate current document;
2. keep unfinished accepted work in `docs/ROADMAP.md`;
3. mark the specification frozen;
4. remove temporary implementation plans.
