# Wms: instructions for Codex

Before starting a task, read [the project constitution](.specify/memory/constitution.md),
the [current-system overview](.specify/memory/current/overview.md), and the
relevant subcontext in `.specify/memory/current/`.
For work on a feature with a specification, read its `spec.md`;
`.specify/feature.json` points to the current Spec Kit feature. A completed
feature is historical, even if that pointer still names it. Read other
specifications only when the user names them or the current feature directly
requires their decision history. The top-level
`archive/legacy-specs/` is excluded from normal context gathering.

- Treat current documentation as the guide to implemented behavior and feature
  specifications as intended behavior. The code remains the source of truth for
  implementation details.
- This is an MVP: prefer explicit, straightforward changes that fit the existing structure. Do not introduce abstractions, layers, or infrastructure unless the task needs them.
- When replacing a screen, route, or workflow, remove obsolete code and links. Keep compatibility aliases or redirects only for a concrete, supported need; do not accumulate them by default.
- Keep changes scoped to the request. Do not implement backlog processes prematurely.
- When a task changes implemented business behavior or an integration boundary,
  update the owning file in `.specify/memory/current/` in the same change.
  Update `.specify/memory/architecture.md` when implemented technical structure
  or lasting engineering conventions change.
- Keep each detailed rule in one owning document and link to it elsewhere.
  Current-state files describe implemented rules; the backlog describes remaining
  work. Neither contains implementation logs, test inventories, or repeated
  handoffs; preserve decision history in frozen specs.
- Scale documentation to the task. A small, bounded issue may proceed from
  discussion and resolved assumptions to implementation without a formal
  `plan.md` or `tasks.md`. Use the Spec Kit `specs/NNN-feature-slug/` structure
  when the business outcome needs a durable specification; add `plan.md` when
  the technical approach needs explanation, and `tasks.md` when dependent
  increments need tracking. Clarify material questions before implementation.
- When a specified feature is complete, distill lasting rules into
  `.specify/memory/current/` and retain its feature directory as decision
  history. Give a later substantial change its own issue, specification and
  branch instead of rewriting completed history. Small follow-ups may be
  resolved in their issue and reflected in current documentation when built.
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
  default for routine changes. Follow the [testing policy](.specify/memory/architecture.md#testing-strategy)
  when deciding whether a test is necessary.
- Keep intermediate updates to two or three sentences and final reports to no
  more than ten items.
