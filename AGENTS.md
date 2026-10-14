# Wms: instructions for Codex

Before starting a task, read [the project constitution](.specify/memory/constitution.md)
and the relevant sections of [the current project context](docs/PROJECT_CONTEXT.md).
For work on a feature, read its `spec.md`; `.specify/feature.json` points to the
current Spec Kit feature. A completed feature is historical, even if that pointer
still names it. Read other specifications only when the user names them or the
current feature directly requires their decision history.

- Treat current documentation as the guide to implemented behavior and feature
  specifications as intended behavior. The code remains the source of truth for
  implementation details.
- This is an MVP: prefer explicit, straightforward changes that fit the existing structure. Do not introduce abstractions, layers, or infrastructure unless the task needs them.
- When replacing a screen, route, or workflow, remove obsolete code and links. Keep compatibility aliases or redirects only for a concrete, supported need; do not accumulate them by default.
- Keep changes scoped to the request. Do not implement roadmap processes prematurely.
- When a task changes a lasting business rule, integration boundary, workflow, or architectural convention, update `docs/PROJECT_CONTEXT.md` in the same change.
- Keep each detailed rule in one owning document and link to it elsewhere.
  Current docs describe current rules and remaining work, not implementation
  logs, test inventories, or repeated handoffs; preserve decision history in frozen specs.
- Create a substantial feature under the Spec Kit `specs/NNN-feature-slug/`
  structure. Keep `spec.md` about the business outcome, `plan.md` about the
  implementation approach, and `tasks.md` about bounded implementation work.
  Clarify material open questions before treating a draft as ready to implement.
- When a feature is complete, distill lasting rules into current documentation
  and retain its feature directory as decision history. Create a new feature
  specification for a later change instead of rewriting completed history.
- Treat one implementation increment as one task and leave a concise handoff.
- Minimize tool calls. Read only required file sections, batch independent reads,
  keep command output bounded, and do not expand verification after sufficient
  evidence is available.
- The developer runs operational commands: builds, automated tests, application
  launches, and migration creation and application. Do not run them unless the
  user explicitly asks. Provide exact, minimal commands, wait for the reported
  result, and inspect developer-created migration files before continuing.
- Leave UI, device, and print verification as developer instructions unless the
  user explicitly requests automated UI control.
- Add automated tests only for material regression risks such as concurrency,
  replay/idempotency, atomic posting, data loss, and database constraints,
  when existing tests do not already protect the risk. No new test is the
  default for routine changes. Follow the [testing policy](docs/ARCHITECTURE.md#testing-strategy)
  when deciding whether a test is necessary.
- Keep intermediate updates to two or three sentences and final reports to no
  more than ten items.
