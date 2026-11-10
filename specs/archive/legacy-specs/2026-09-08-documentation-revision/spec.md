# Documentation revision

Status: frozen.

Accepted on 2026-09-08. Redundant deployment notes, historical API notes,
unaccepted product ideas, and defensive lists of unused patterns were removed.
The staging configuration was reconciled with the repository.

## Outcome

Current WMS documentation is compact, non-contradictory, and divided by
purpose: product and business truth, engineering conventions, unfinished work,
and the staging runbook. Historical specifications remain decision history
rather than another current manual.

## Scope

- Critically revise `PROJECT_CONTEXT.md`, `ARCHITECTURE.md`, `ROADMAP.md`, and
  `STAGING.md` against current code and deployment configuration.
- Remove duplication, completed work, speculative backlog, generic advice, and
  implementation detail that belongs in code or another current document.
- Reduce the specification registry to active work and lifecycle rules; keep
  frozen specification files unchanged.
- Remove auxiliary deployment notes and examples that duplicate or contradict
  `STAGING.md`, and correct user-facing deployment guidance where necessary.

## Out of scope

- Rewriting or deleting frozen specifications.
- Changing product behavior, architecture, deployment topology, or secrets.
- Changing the documentation workflow or adding automated checks.
- Documenting hypothetical workflows or future mechanisms without an accepted
  requirement.

## Acceptance criteria

- Each lasting fact has one obvious current owner and is not repeated merely
  for emphasis.
- Current documentation agrees with source code, migrations, Mobile settings,
  Docker Compose, and Caddy configuration.
- The roadmap contains unfinished accepted work only.
- The staging runbook contains one first-time path, one routine-update path,
  and focused recovery notes without duplicate command lists.
- Local Markdown links resolve and `git diff --check` passes.
- No runtime code or automated tests are changed.
