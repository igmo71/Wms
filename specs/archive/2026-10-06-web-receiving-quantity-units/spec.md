# Web receiving quantity units

Status: **Frozen** — accepted on 2026-10-06 after the developer confirmed the Web unit display.

## Outcome

An operator can see whether each quantity in a Web receiving order is measured
in `упак`, `шт`, or another canonical SKU unit.

## Scope

- Load and show the SKU's base unit in the Web nomenclature table for ready,
  in-progress, and completed receiving orders, using the same catalog field as
  Mobile. Label the unit column `Ед изм` on both nomenclature and pallet tabs.
- Keep the existing plan/fact/remaining values, 1C mapping, LPN counting,
  weight calculation, and completion rules unchanged.
- Treat pack size text in the SKU name as part of its identity, not a quantity
  conversion. A different pack size is a different SKU.

## Acceptance

1. A SKU with base unit `упак` shows `упак` in the `Ед изм` column of both tabs,
   even if its name contains `(500 шт)`; a `шт` SKU shows `шт`.
2. A missing base unit displays `—` without hiding quantities.
3. Mobile quantity displays and receiving calculations remain unchanged.

## Verification

Developer builds WebApp and checks representative `упак` and `шт` order lines.
No new automated test is needed for this presentation change.

## Open questions

None.
