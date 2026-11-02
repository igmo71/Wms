# Data model: topology and opening balances

## Persisted state

### WarehouseOpeningImport (new)

One row per target warehouse, unique by `WarehouseId`.

| Field | Purpose |
|---|---|
| `Id`, `WarehouseId` | Import identity and selected warehouse. |
| `SourceName`, `SourceSha256` | Identifies the exact workbook used in both stages. The workbook bytes are not stored. |
| `DeclaredSnapshotAt` | Nullable after topology import; required at stock confirmation. Cutover snapshot time supplied by the Administrator, distinct from confirmation times. |
| `Status` | `TopologyImported` or `StockImported`. No persisted partial stage. |
| `TopologyImportedAtUtc`, `TopologyImportedBy` | Topology confirmation record. |
| `StockImportedAtUtc`, `StockImportedBy` | Stock confirmation record; absent until stage two succeeds. |
| `TopologySummary`, `StockSummary` | Bounded counts and reviewed exception details for the import result. |
| `Revision` | Detects competing stage confirmations. |

Creating this row, zones and cells is one topology commit. Stock confirmation updates the same row in the same commit as all opening effects. A second import row for the warehouse conflicts at the database boundary. Another source file cannot advance the existing record.

### Zone and StorageLocation (existing)

- Keep the existing `Storage` type value for picking-purpose cells and label it `Комплектация`. Add `Reserve` for 1C `Хранение`. Existing `Receiving`, `Shipping`, and `Transit` retain their values.
- Create/reuse zones keyed by warehouse plus stable area-and-purpose code. A zone's type is immutable during import; a mismatch blocks. Rack/mezzanine zones cover each area and kind, while `Ворота` has distinct Receiving and Shipping zones.
- Rack/mezzanine folder keys retain the existing deterministic address hierarchy. Gate cells sit directly under a `Ворота` folder. Operational address uniqueness remains warehouse-wide.
- `AllowsMultipleSkus` is true for the 15 approved addresses and false for the eight prohibited mixed addresses. An initial mixing discrepancy is derived from false permission and more than one nonzero SKU; it needs no second location flag.

### StockKeepingUnit (existing)

`Id` matches the source GUID. `BaseUnitOfMeasure` is checked against source unit text. `TracksExpirationDate` is enabled for every accepted source SKU row marked `Да`; an import never turns an already enabled flag off. These changes share the stock-stage commit.

### InventoryMovement, InventoryBalance and InventoryTurnover (existing)

- Add `RecorderType.OpeningBalance`, with `RecorderId = WarehouseOpeningImport.Id` and `RecorderLineNumber = source worksheet row`.
- A source quantity `q > 0` creates a destination-only movement of `q`; `q < 0` creates a source-only movement of `-q`. `InventoryMovement.Quantity` remains positive. Every accepted nonzero source row has one movement and its matching turnover.
- The final balance key is `(WarehouseId, StorageLocationId, StockKeepingUnitId, ExpirationDate?)`; duplicate rows for one key update one balance, preserving each row's movement provenance. The opened state may be negative.
- A filtered unique index for opening recorder id and line number prevents duplicate opening movements. Existing transfer uniqueness is retained.
- Current ordinary stock-adding routes remain subject to mixing validation. Only the opening batch can stage the eight reviewed initial mixed states while permission is false.

### WarehouseAuditEvent (existing, extended)

Add an optional `WarehouseOpeningImportId` affected-object relation so topology and stock confirmation each have one compact batch-level audit event linked to their command receipt. The event includes source hash, counts and exception totals, while individual opening movements retain row provenance. It does not serialize thousands of movement IDs.

## Transient import model

| Concept | Fields and validation |
|---|---|
| `SourceRow` | Excel row number; raw area, group, address, kind, SKU name/GUID, unit, signed quantity, date text, date-required text. Parser retains missing versus zero. |
| `InterpretedRow` | Normalized address/area/group/purpose, optional SKU/date/quantity, and `Accepted`, `Corrected`, `Excluded` or `Blocking` outcome with reason. |
| `TopologyPlan` | Distinct zones, folders and cells; existing-cell matches; approved mixing settings; source hash and state-bound signature. |
| `StockPlan` | Resolved target cells and SKU IDs, parsed dates and units, signed quantities, initial mixing exceptions, expiration flags, issue list and state-bound signature. |

The parser checks exactly one expected table and all ten columns. The Application interpreter applies the reviewed source exceptions. Any new invalid address, unexpected kind, unknown SKU, unsupported quantity or unreviewed mixed cell blocks the corresponding stage. Source row decisions are visible in previews; persisted summaries remain bounded.

## State transitions

```text
No import for warehouse
  -> TopologyImported (zones and cells committed, no opening stock)
  -> StockImported (opening movements, balances, turnovers, SKU flags committed)
```

The same request can replay its recorded result. A second request or user cannot create another import for the warehouse. Failed validation or commit leaves the prior persisted state unchanged. A new rehearsal starts after an environment-level database cleanup, outside the import UI.
