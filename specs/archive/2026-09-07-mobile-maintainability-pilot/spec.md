# Mobile maintainability pilot

Status: frozen reference. Accepted 2026-09-07.

Implementation is ready for review. The pilot uses receiving because it had
the largest page and a clear resolve-SKU/select-line/increment sequence. Its UI
code is grouped into cohesive partial files for lifecycle, commands,
search/quantity editing, and presentation; no second page received a process
object.

## Outcome

Mobile code is organized by business feature, and one receiving page proves a
small process-object boundary without hiding UI state or introducing a common
framework. The pilot must make the receiving command path easier to read before
the same shape is considered elsewhere.

## Scope

- Replace the all-feature `MobileApiClient` with concrete authentication,
  reference-data, inventory-transfer, inventory-count, receiving-order, and
  shipping-order clients over one internal HTTP transport.
- Split Mobile V1 contracts into feature files while preserving their namespace,
  type names, JSON shape, routes, and public API surface.
- Extract the repeated Android native-focus suppression into one technical
  helper and keep page event handlers local.
- Add one receiving-specific process for `ReceivingOrderReceivingPage` that owns
  its API command sequences, pending request ids, and uncertain-outcome retry
  compatibility.
- Keep page modes, scanner and camera lifecycle, controls, dialogs, navigation,
  rendering, search cancellation, and quantity text validation in the page.

## Out of scope

- Applying the process-object shape to a second page.
- A shared controller, process interface, page base, MVVM framework, workflow
  engine, generic retry coordinator, or scanner-session abstraction.
- Mobile API route or wire-contract changes.
- Business-rule or server-side workflow changes.
- New automated tests.

## Acceptance criteria

- No client consumed by a page spans unrelated business features.
- All feature clients share one authenticated HTTP transport and retain current
  response/error classification.
- Contract source files are grouped by feature without changing compiled
  contract identities.
- Android focus suppression has one platform-specific implementation.
- `ReceivingOrderReceivingPage` no longer owns pending request ids or the
  resolve-and-increment API sequence, but still visibly owns UI modes and scanner
  behavior.
- A transient or uncertain command result retains the same request id and
  semantic retry target; a definitive Mobile API error releases it.
- Other pages change only for feature-client injection and focus-helper use.
- Relevant projects build and `git diff --check` passes; no tests are created.
