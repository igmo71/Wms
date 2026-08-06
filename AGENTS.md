# Wms: instructions for Codex

Before starting a task, read [the project context](docs/PROJECT_CONTEXT.md).

- Treat the context as the current architectural and business guide. The code remains the source of truth for implementation details.
- This is an MVP: prefer explicit, straightforward changes that fit the existing structure. Do not introduce abstractions, layers, or infrastructure unless the task needs them.
- Keep changes scoped to the request. Do not implement roadmap processes prematurely.
- When a task changes a lasting business rule, integration boundary, workflow, or architectural convention, update `docs/PROJECT_CONTEXT.md` in the same change.
- Put a specification for a substantial issue under `specs/<issue-id-or-slug>/`. Keep it focused on the business outcome, scope, acceptance criteria, and open questions.
