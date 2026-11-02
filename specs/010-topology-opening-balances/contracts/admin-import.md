# Web administrator import workflow

This contract describes the WebApp interaction; it does not introduce a public 1C endpoint. The user must have the `Administrator` role. The workbook is uploaded through the Web interface for each preview/confirmation and is bounded by an explicit size and row limit covering the agreed sample.

## Topology stage

| Action | Input | Result |
|---|---|---|
| Preview topology | Warehouse, workbook | Source hash, plan signature, proposed zones/folders/cells, empty-cell count, existing matches, mixing permissions, corrected/excluded rows and paged blocking/warning issues. No mutation. |
| Confirm topology | Same warehouse/workbook, plan signature, request ID | Import ID and durable topology result with source identity, actor/time, counts, corrections and exclusions. A stale plan or blocking issue rejects the confirmation. |
| View topology result | Warehouse or import ID | Persisted summary and status `TopologyImported` or `StockImported`. |

The preview shows the approved `9Z99A1` correction and the omitted contents of `0A00A0` and `0A00A1`. It does not infer policy from cell color in a separate workbook. Unknown source values remain visible and blocking. Results show aggregate counts with issue pagination; the UI does not render every source row at once.

## Opening-stock stage

| Action | Input | Result |
|---|---|---|
| Preview opening stock | Warehouse, the same workbook, declared snapshot time | Source-hash match, catalog/unit/address checks, signed totals by SKU and zone purpose, date anomalies, prohibited mixed-cell exceptions and paged issues. No mutation. |
| Confirm opening stock | Same inputs, plan signature, request ID | Import ID, `StockImported` status, actor/time, accepted/excluded/corrected row counts, signed and undated exceptions. A stale plan or any blocking issue rejects the confirmation. |
| View stock result | Warehouse or import ID | Durable result summary and source identity. |

The stock stage never auto-creates a missing cell or SKU and never treats another workbook as a stock delta. A failed stage leaves topology in place and opening stock absent. Confirmation is repeat-safe for the same command and guarded warehouse-wide against a second import.

## Issue shape

Each preview issue has `Stage`, `Severity` (`Blocking` or `Warning`), `SourceRow` when applicable, `Address` when applicable, and a Russian explanation. Corrections and exclusions are shown as explicit row decisions with the source and resulting values. A confirmation signature changes when the file, selected warehouse, interpretation or relevant saved state changes.
