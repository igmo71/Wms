# Consistent quantity display across Web and TSD

## Outcome

An operator can compare quantities within a screen without guessing precision or unit. Display changes do not alter stored quantities, input validation, or the rule that one 1C package is one WMS base unit for the current SKU.

## Audit findings

- Web receiving order tables show `N3` and an `Ед изм` column. Web inventory count and transfer details use `0.###`; balances, movements, turnover, and shipping tables mostly rely on unformatted numeric columns. Several of those tables do not show a unit beside each SKU.
- TSD receiving and putaway mix `g` and `0.###`; shipping and putaway cards often show multiple quantities without an adjacent unit. Inventory count already shows one unit for its quantity summary. Numeric entry accepts at most three decimal places and should remain compact.
- The TSD receiving and shipping queues and order headers sum quantities across different SKUs. This can mix units and makes the total misleading; line counts already provide meaningful progress.

## Agreed display rule

- Web read-only quantity columns: three decimal places, with the SKU base unit in the row when units may differ. A single unit label can cover several quantities for one SKU.
- TSD read-only quantities: up to three decimal places to save space, with the base unit once in the SKU card or next to an isolated quantity. Input text keeps its current editing behavior.
- Do not total quantities across different SKUs or units; show counts of lines or pallets instead. Keep counts as integers and weight as a separate `кг` measure.
- Mobile order-progress responses carry line counts instead of mixed-SKU quantity totals; per-SKU quantities remain available on their lines.
- Picking deviation confirmation compares each shipping line with its own plan. Opposite differences on separate SKUs cannot cancel in the UI.
- Apply this across receiving, putaway, shipping, stock, transfers, and inventory counts. Preserve sorting by numeric values and keep unknown units visible as unknown rather than guessing.

## Acceptance criteria

- Comparable quantities on the same screen use one display convention and retain all three supported decimal places.
- Each quantity group has an unambiguous unit; totals never combine unlike SKUs.
- Numeric editing, persistence, and warehouse accounting semantics remain unchanged.
- Picking discrepancy confirmation remains required whenever at least one shipping line differs from its plan.

## Open questions

None. The rule and replacement of mixed-SKU totals were agreed on 2026-10-07.

## Verification

Accepted on 2026-10-07. The developer reported a successful solution build and focused tests, and confirmed that a picking discrepancy requires confirmation. The receiving screen was also reviewed on TSD. The agent did not run builds or tests.
