# Implementation Plan: Control SKU mixing by location

## Scope and constitution check

- WMS owns the location setting and stock invariant. The rule is evaluated in the one authoritative posting path of each current workflow, preserving movements, balances, turnover, audit, and command receipt as one save.
- Keep the current two posting services because receiving and older workflows already have separate authoritative paths. Add one narrow shared domain predicate only if it makes both checks clearer; do not add a new service layer.
- Keep current zone routing. The mixing rule applies to current Storage destinations. Receiving, Transit, and Shipping remain outside it. A later replenishment zone will extend routing and this rule in its own feature.
- No 1C contract changes or old-system topology mapping are needed.

## Data and administration

Add a nonnullable permission to StorageLocation with a false default. The developer creates the EF migration; review it before application. Manual, batch, and map-import creation rely on the default, while the Administrator edit form exposes the permission for a storage location. Disabling it checks nonzero balances of distinct SKUs and rejects mixed stock. A permission change advances OperationalRevision.

Existing warehouse data will be cleaned before deployment. No mixed-location backfill is planned. Folders never permit mixing as an operational setting.

## Stock posting

The receiving posting path checks the final destination state after applying the movement in its command session. The legacy posting path checks the final state after applying its entire movement batch, so count replacement from A to B works regardless of movement order. Both checks include tracked changes and newly staged balance rows, ignore zero rows, treat negative rows as nonzero, and count dates of one SKU as one SKU.

Only Storage destinations are checked; stock removal remains allowed. Current rollback uses the legacy path and conflicts atomically if returning stock would cause mixing. Location revision changes on every posting already provide an optimistic guard against simultaneous placement and setting changes.

Keep destination recommendations unchanged; a scanned address is validated at final posting. A rejected putaway retains the confirmed quantity under the existing Mobile behavior.

## Verification and handoff

Add the smallest SQL-backed regression tests for a second SKU rejected in each posting path, a count batch that replaces A with B, and a concurrent or stale-setting race that would otherwise allow mixing. Extend existing inventory suites rather than creating a separate framework. The developer runs builds, tests, and EF migration commands under AGENTS.md; inspect the generated migration before further work. UI and device checks remain developer instructions.

## Delivery steps

1. Add location permission, administration validation, and UI.
2. Enforce the rule in receiving and legacy posting paths.
3. Add focused regression tests and update current inventory documentation.
4. Request the developer's migration generation and verification commands, inspect resulting files, and finish the handoff.
