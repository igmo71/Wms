# Wms: instructions for Codex

Before starting a task, read [the project context](docs/PROJECT_CONTEXT.md).

Before reading specifications, consult [the specification registry](specs/README.md).

- Treat the context as the current architectural and business guide. The code remains the source of truth for implementation details.
- This is an MVP: prefer explicit, straightforward changes that fit the existing structure. Do not introduce abstractions, layers, or infrastructure unless the task needs them.
- Keep changes scoped to the request. Do not implement roadmap processes prematurely.
- When a task changes a lasting business rule, integration boundary, workflow, or architectural convention, update `docs/PROJECT_CONTEXT.md` in the same change.
- Read only the specification marked **Active**, explicitly named by the user,
  or directly required by the active specification. Do not scan frozen or
  removed specifications by default.
- Ask which specification governs the task only when several candidates are
  active or the requested business boundary is genuinely ambiguous.
- Put a specification for a substantial issue under
  `specs/YYYY-MM-DD-<problem-slug>/`. Keep it focused on one business outcome,
  its scope, acceptance criteria, and open questions.
- When an issue is complete, distill lasting rules into current documentation,
  update the registry, and freeze the specification. Do not spread a new issue
  through completed specifications; create a new dated specification instead.
- Treat one implementation increment as one task and leave a concise handoff.
- Read only required file sections and keep command output bounded.
- Build affected projects during implementation; build the full solution once
  at the increment checkpoint. Stop after the required checks pass.
- Leave UI, device, and print verification as developer instructions unless the
  user explicitly requests automated UI control.
- Add automated tests only for material regression risks such as concurrency,
  replay/idempotency, atomic posting, data loss, and database constraints.
