# Research: topology and opening balances

## Source and parser boundary

**Decision**: Accept one `.xlsx` export with the ten agreed headers. Parse it in `Wms.Integration` into typed rows retaining the Excel row number and raw source values. The Application import use cases classify rows, apply the reviewed exceptions and build the two stage plans. Accept a single worksheet with the expected table; reject missing/duplicate headers, unsupported values and unexpected cell kinds.

**Rationale**: The current `Wms/Application/StorageLocations/StorageMapImportService.cs` reads named, multi-file `.xls` maps with `CreateBinaryReader`, one zone per area and no stock columns. It cannot interpret empty cells or stock and belongs to the obsolete workflow. The existing ExcelDataReader package can be moved from `Wms` to `Wms.Integration`; no new spreadsheet infrastructure is needed.

**Alternatives considered**: Continue the `.xls` importer and add columns (its filename/tier rules and gate exclusion are incompatible); use the TXT extract (it omits stock fields); store the whole workbook in the database (unneeded when the same source can be reuploaded and checked by hash).

## Two stage import and replay

**Decision**: Persist one `WarehouseOpeningImport` per warehouse with a unique warehouse key, source hash, stage status and compact stage results. Topology confirmation creates the record together with cells/zones; stock confirmation requires the same workbook hash, records the declared snapshot time and atomically advances it. The snapshot time is absent until stock confirmation. Each preview has a signature of the source, selected warehouse, agreed interpretation and relevant current state. Each confirmation rebuilds the plan before comparing the signature. Request receipts handle exact command replay; the import record prevents another user or file from double loading a warehouse.

**Rationale**: The current command receipt is keyed by user, command type and request id, so it does not enforce one opening snapshot per warehouse. An atomic stage lets the team inspect topology and fix catalog data before stock posting. A failed stock stage leaves topology ready for retry. A new rehearsal starts on a cleaned database; the application has no reset action.

**Alternatives considered**: One combined topology-plus-stock transaction (less reviewable); use receipt alone (does not guard another user or file); delete/rewrite stock on reimport (violates movement history and the cutover agreement).

## Physical and functional topology

**Decision**: Preserve the numeric value of current `ZoneType.Storage` for pickable `Комплектация` and give it the correct user label. Add `Reserve` for 1C `Хранение`; use existing `Receiving` and `Shipping`. Create/reuse functional zones by source area and kind. Use `Группа ячеек` as the physical root: rack and mezzanine rooms retain the existing `FROOM/FROW/FSECTION/FLEVEL` address-derived hierarchy; gates use one `Ворота` root with its eight operational addresses directly underneath. Do not derive fake rooms 8 and 9 from gate address prefixes. Disallow range generation under the gate root unless that generator gains an explicit gate-aware rule.

**Rationale**: One 1C area can contain two purposes, while WMS zones each have one type. Gate addresses span 8* and 9* but the source identifies one physical group. The existing range generator validates imported rack folders by their deterministic codes and otherwise accepts an arbitrary parent, which must not accidentally permit an unconstrained gate range.

**Alternatives considered**: One zone per 1C `Помещение` (cannot represent both picking and reserve); assign gates to address-derived rooms 8 and 9 (invented physical structure); rewrite the whole location hierarchy (unnecessary).

## Agreed source exceptions and mixing

**Decision**: Keep the seven agreed excluded values/addresses, including the empty mismatched-room `4A10A1`, and the `9Z99A1` shipping reclassification in one explicit import policy, shown in preview and result. Reclassify its area, group and kind together, preserving the one-unit chain row. Configure the 15 approved cells to allow several SKUs; leave the eight prohibited cells false. The opening stock batch alone may record their existing mixed fact. A visible violation is derived from false permission plus more than one nonzero SKU; ordinary inflow remains blocked until resolved. Unknown mixed cells or changed meaning of a corrected row block import.

**Rationale**: The reviewed spreadsheet's red fill is evidence of a decision, not a durable machine-readable business rule. The physical stock must not be invented elsewhere or dropped. A separate permanent exception flag is unnecessary because the violation can be derived from current balances and disappears after correction.

**Alternatives considered**: Read red fill on every import (fragile and absent from the source export); set the eight cells to `AllowsMultipleSkus=true` temporarily (permits further mixing); discard or move their second SKU (falsifies opening stock).

## Opening-stock posting and audit

**Decision**: Add a batch opening-posting use case in `Wms.Application` with an Infrastructure store/session. It validates the complete snapshot and stages one opening `InventoryMovement` per accepted nonzero source row, using positive movement magnitude, destination-only for positive stock and source-only for negative stock. It maintains one balance per `(warehouse, cell, SKU, nullable date)` and creates corresponding turnover history. All movements, balances, turnovers, import status, expiration flags, audit event and command receipt commit together. A filtered unique recorder-line index protects opening movement provenance. The batch audit links to the import record; it stores aggregate counts, not thousands of movement IDs.

**Rationale**: The new `PostInventoryMovement.Handler` requires an LPN and ReceivingOrder recorder and is designed for one positive receipt/putaway. The legacy posting service uses a three-dimensional balance key and disallows the required signed opening case. The domain movement, balance and turnover types already model the necessary signed effects when the opening use case permits them explicitly.

**Alternatives considered**: Loop the current receiving handler thousands of times (wrong preconditions and repeated database loads); use the legacy posting service (loses expiration date); insert balances directly (no movement, turnover or provenance).

## SKU authorization and validation

**Decision**: The one-time cutover import is an Administrator action, including enabling WMS-owned expiration tracking for SKUs marked `Да` in the source. Keep the existing Manager rule for ordinary manual flag edits. Require a previously synchronized SKU GUID and matching canonical unit; never infer package conversion from the SKU name. Store a missing date as unknown even when tracking is enabled. Validate the full set before mutation and guard tracked SKU flags at final save.

**Rationale**: The flag update is part of initial warehouse configuration, not an ordinary catalog edit. The source workbook does not carry a trustworthy package-conversion key. Current WMS stock already distinguishes unknown dates from zero or a known date.

**Alternatives considered**: Require both Administrator and Manager for the cutover (unnecessary mixed-duty requirement); create missing SKUs from the stock file (violates 1C catalog ownership); invent missing dates for date-required sample rows (changes source facts).

## Operational route boundaries

**Decision**: Extend current receiving putaway, its recommendations and the Mobile putaway scan to accept both picking and reserve. Add a putaway-specific Mobile scan context; retain the existing strict `Storage` context for older picking, transfer and count clients. Apply the one-SKU restriction to both storage purposes. Do not broaden old transfer or count workflows to reserve or shipping merely by changing a shared predicate; their dated/signed replacements remain separate work.

**Rationale**: A global `Storage` context expansion would make reserve appear as a valid pick source before the new picking/replenishment workflow is ready. The current inventory count also omits negative and dated balances, so enabling shipping alone would misrepresent its capability.

**Alternatives considered**: Change every `ZoneType.Storage` check to include Reserve (unsafe); leave putaway limited to picking (contradicts the accepted rule); include a full picking, transfer and count rewrite in this import feature (scope expansion).
