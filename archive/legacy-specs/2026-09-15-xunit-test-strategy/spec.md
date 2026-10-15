# Minimal xUnit test strategy

Status: **Frozen**. Completed on 2026-09-15.

## Outcome

Replace the custom executable projects under `tests/` with the single xUnit
project `Wms.Tests`. Preserve only checks that protect costly inventory,
transaction, concurrency, integration, or data-integrity failures.

## Scope and acceptance criteria

- Share disposable SQL Server LocalDB setup and small handwritten helpers.
- Test generic command replay, payload conflict, receipt race, and atomic losing
  transaction once at the infrastructure boundary.
- Keep feature tests only for distinct critical rules: LPN uniqueness and
  non-reuse, one receiving location under a first-participant race, complete and
  atomic putaway, competing stock withdrawal, count correction and stock drift,
  shipping posting and compensating rollback, and receiving synchronization
  quantity protection.
- Do not retain tests for trivial validation variants, CRUD, mapping, framework
  behavior, UI rendering, historical hash permutations, or the same receipt
  guarantee repeated in every feature.
- Run the complete new suite, remove every old executable project, and update
  current documentation.
- After this migration checkpoint, agents run only one or two focused tests per
  change. A later complete suite run is manual unless the user explicitly asks
  for it; the handoff carries the command.

## Result

`Wms.Tests` contains 14 tests. They run against migrated disposable LocalDB
databases where persistence semantics matter; the receiving synchronization
comparison remains a pure domain test. The old executable projects were removed.
The lasting selection and implementation rules live in
`docs/ARCHITECTURE.md` and `docs/PROJECT_CONTEXT.md`.
