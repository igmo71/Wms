# Web receiving quantity format

Status: **Frozen** — accepted on 2026-10-06 after developer review.

## Outcome

Quantities on the Web receiving order use one predictable display precision.

## Scope

- Show three decimal places for read-only WMS plan, 1C plan, fact, remainder,
  pallet composition and pallet putaway quantities.
- Preserve the existing missing-value text and actual decimal values.
- Do not change weight formatting, quantity entry, Mobile screens, receiving
  calculations, or persistence.

## Acceptance

1. The same row displays `15,000`, `14,000` and `0,000` for plan WMS, plan 1C
   and fact when those are the values; fractional amounts retain three places.
2. Pallet quantities use the same precision; an unchecked fact still reads
   `Не проверено`.
3. Receiving behavior and stored quantities are unchanged.

## Verification

Developer builds WebApp and checks representative whole and fractional
quantities in the nomenclature and pallet tabs. No automated test is needed
for display formatting.

## Open questions

None.
