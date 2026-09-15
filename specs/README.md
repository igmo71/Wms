# Specification registry

Specifications are temporary issue scopes and frozen decision history. Current
product and engineering truth belongs in `docs/`; do not read or update frozen
specifications unless a current task explicitly needs their rationale.

## Active

- [LPN receiving and putaway](2026-09-15-lpn-receiving-putaway/spec.md)
  — Agreed receiving, reconciliation and initial putaway workflow. Specification
  prepared and split into eight increments on 2026-09-15. I01 label issuance
  and its manual print/scanner path are accepted. I02 order participation and
  shared receiving-location selection are implemented. I03–I08 are not started;
  handoff in section 12.

## Frozen

- [Minimal xUnit test strategy](2026-09-15-xunit-test-strategy/spec.md)
  — Replaced the executable test harnesses with one risk-based xUnit suite and
  documented the lasting test-selection rules.

- [LPN development planning](2026-09-14-lpn-development-planning/spec.md)
  — Planning completed on 2026-09-15. Cross-stage plan lives in
  `docs/DEVELOPMENT_PLAN.md`; implementation follows the active specification.

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
