# Data model: Receiving SKU barcodes

## SKU barcode association

- Identity: `(SkuId, Value)` remains the primary key. One barcode value may have several rows when they belong to distinct SKUs and arrive from 1C.
- `SkuId`: existing SKU reference; a deleted/inactive SKU cannot receive a new manual association.
- `Value`: existing required string, maximum 32 characters. Save and lookup use the same trimmed value convention.
- `Source`: current owner, `OneC` or `Wms`. Existing rows are backfilled as `OneC`. New 1C pairs are `OneC`; Mobile additions are `Wms`.
- Index: nonunique `Value` lookup index, supporting duplicate detection and cross-SKU lookup.

### Transitions

| Current | Event | Result |
|---|---|---|
| Absent | Mobile adds a new value | Create `Wms` pair and audit addition. |
| `Wms` | 1C supplies the exact pair | Set `Source=OneC`; original addition audit remains. |
| `Wms` | 1C scope omits pair | Keep `Wms` pair. |
| `OneC` | 1C scope omits pair | Remove pair. |
| `Wms` | Manager confirms removal | Remove pair and audit removal. |
| `OneC` | Manager attempts removal | Reject; correction belongs in 1C. |

An identical incoming 1C pair is collapsed. A different SKU with the same `Value` is a separate source pair. A manual add of such a cross-SKU value is rejected. Repeated save of an existing same-SKU pair does not add a row.

## Audit event

The existing `WarehouseAuditEvent` records manual add/remove with `StockKeepingUnitId`, optional `ReceivingOrderId` for Mobile addition, actor, UTC time and the barcode value in its compact before/after JSON. An event is retained when the live pair changes source or is removed. Existing command receipt identity links a user action to its audit.

## Source snapshot

The 1C adapter supplies the complete set of `(SkuId, Value)` pairs for either one SKU or the full barcode register. The application reconciles only that scope after successful read and validation. A failed fetch or invalid snapshot leaves the previous saved scope unchanged. No extra permanent snapshot table is required.
