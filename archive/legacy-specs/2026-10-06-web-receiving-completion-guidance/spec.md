# Web receiving completion guidance

Status: **Frozen** — accepted on 2026-10-06 after the developer confirmed the Web build and presentation.

## Outcome

An operator viewing an in-progress receiving order can see why the Web
"Завершить" action is unavailable and what to do next.

## Scope

- Show one concise, actionable reason between the order title and the disabled
  Web completion action: missing receiving location, open pallets, unchecked lines,
  failed or blocking 1C check, lack of participation or Manager permission,
  or missing Manager reason.
- Keep the saved-command retry action and its recovery behavior intact.
- Omit the generic "исправления источника" sentence in the receiving
  "Расхождения" panel: a blocking completion check can also mean an unchecked
  local fact. Keep the difference table and the actionable completion hint.
- Preserve the server's completion checks and explicit Manager-only approval.
- Do not change Mobile screens, order quantities, LPN contents, or putaway.

## Acceptance

1. Each blocked Web completion state has a visible reason; open pallets and
   unchecked lines identify the next operational step.
2. A Manager sees the missing-reason hint when quantity approval is otherwise
   possible; a user without Manager membership sees the role restriction.
3. A ready completion action has no extra explanatory banner, and a pending
   completion still offers its saved retry.
4. The existing server validation remains authoritative.

## Verification

Developer builds WebApp and views representative blocked and ready states.
No automated test is added for presentation-only guidance.

## Open questions

None.
