# Feature Specification: Load warehouse topology and opening balances

**Feature Branch**: `codex/010-topology-opening-balances`

**Created**: 2026-10-30

**Status**: Completed (accepted 2026-11-02)
**Input**: Prepare one warehouse for the transition from the previous warehouse system by loading its complete 1C cell export and opening balances in two reviewable stages.

## User Scenarios & Testing

### User Story 1 - Load the physical topology (Priority: P1)

An Administrator selects a warehouse and the complete topology-and-balances export. A preview shows every operational address to create or reuse, including empty cells, its physical room and functional zone, the agreed exclusions, duplicate addresses, and conflicts. The Administrator confirms the topology stage before any opening stock is posted. Missing cells may later be created through ordinary topology administration.

**Why this priority**: Warehouse staff need a trustworthy address tree before stock can be assigned to it.

**Independent Test**: Preview and load the current full export into a clean warehouse, inspect empty and occupied cells, zones, folder structure, exclusions, and the 23 reviewed mixed-SKU addresses without loading balances.

**Acceptance Scenarios**:

1. **Given** a valid row with an address and no SKU or quantity, **when** topology is loaded, **then** the operational cell exists with no opening balance.
2. **Given** one physical area with both picking and reserve cells, **when** topology is loaded, **then** the cells share the appropriate physical hierarchy and have distinct functional purposes.
3. **Given** the agreed excluded or corrected source rows, **when** the preview is shown, **then** their exact treatment is visible before confirmation.
4. **Given** an unexpected address conflict or unknown cell kind, **when** topology is previewed, **then** confirmation is blocked until it is resolved.
5. **Given** a matching previously created cell, **when** the topology stage is retried, **then** it is reused without a duplicate; a conflicting existing cell blocks confirmation.

---

### User Story 2 - Load opening balances (Priority: P1)

After topology and the 1C SKU catalog have been loaded, an Administrator previews the same export's stock rows against the selected warehouse. The preview identifies unknown SKUs, unit mismatches, signed quantities, missing or past dates, mixed-SKU exceptions, exclusions, and the stock that will be recorded at each address. Confirmation records the accepted snapshot as WMS opening stock and preserves its provenance. A failed stage changes no balances or stock history.

**Why this priority**: The warehouse must start with the actual signed stock snapshot, including shipping positions, while retaining an explainable opening fact.

**Independent Test**: Load topology, synchronize the SKU catalog, then preview and post the current export. Compare accepted address/SKU/date quantities with the source after the agreed corrections and exclusions; retry the confirmed import and verify no second stock effect.

**Acceptance Scenarios**:

1. **Given** a source stock row whose address, SKU and unit are valid, **when** opening balances are confirmed, **then** its signed quantity is recorded at that cell with its supplied date or an explicitly unknown date.
2. **Given** a shipping row with a negative quantity, **when** opening balances are confirmed, **then** the negative balance is preserved and remains visible as shipping stock.
3. **Given** `9Z99A1` shown once as a picking cell with one unit of chain and otherwise as a shipping cell, **when** the agreed correction is applied, **then** one shipping cell is created and the chain's one unit is included in its shipping balance.
4. **Given** one of eight cells where mixed stock exists but mixing is prohibited, **when** the initial snapshot is posted, **then** both actual SKUs are retained, the cell remains prohibited for new mixed placement, and the discrepancy remains visible until corrected.
5. **Given** any unknown SKU, incompatible unit, missing target address, unsupported quantity or unresolved source conflict, **when** opening balances are previewed, **then** confirmation is blocked without dropping or guessing affected stock.
6. **Given** a repeated confirmation or an interrupted request with an uncertain outcome, **when** it is retried, **then** opening stock is not posted twice.

---

### User Story 3 - Rehearse and inspect the transition (Priority: P2)

The transition team can run multiple rehearsals on a cleaned WMS database, inspect each stage and its exceptions, clear the rehearsal data through the environment's operational procedure, and repeat with a newer export. At actual transition, operations in the previous warehouse system are completed and stopped before a final export is loaded; work resumes in WMS after loading and review.

**Why this priority**: A cutover needs evidence of what was imported and a safe way to repeat a rehearsal without treating a newer snapshot as an incremental stock update.

**Independent Test**: Import topology, inspect it, import stock, inspect the import record, then repeat the exercise in a cleaned database. Attempt to load a different opening snapshot into a warehouse that already has opening stock and verify it cannot silently overwrite or add to it.

**Acceptance Scenarios**:

1. **Given** topology was loaded successfully but stock validation failed, **when** a catalog issue is corrected without changing the workbook, **then** the stock stage can be retried without recreating topology. A corrected workbook starts a new rehearsal after environment cleanup.
2. **Given** opening stock already exists from a confirmed snapshot, **when** a different snapshot is submitted, **then** WMS refuses to treat it as a replacement or delta in that warehouse.
3. **Given** either stage succeeds, **when** the Administrator opens its result, **then** it shows counts, exclusions, corrections, discrepancies, source identity, actor and confirmation time.

### Edge Cases

- A missing date remains unknown even when the SKU requires a date; it is never invented from the export time. Past supplied dates are preserved as opening facts.
- The source row labelled `Невозможно скомплектовать` has no addressable stock in the agreed export and is excluded. If a later export assigns stock to such an unsupported bucket, preview blocks rather than silently discarding it.
- Multiple source rows for the same warehouse, cell, SKU and date must produce the correct net opening balance without duplicate posting; excluded rows remain visible in the import record.
- A warehouse already used for operations is not an empty cutover target. The opening stock stage must not blend a new snapshot into operational stock.
- Existing physically mixed stock in a prohibited cell is a visible initial exception. Ordinary placement cannot increase or extend that violation; removing stock to resolve it remains possible.

## Requirements

### Functional Requirements

- **FR-001**: Administrators MUST be able to preview and confirm topology and opening balances as separate ordered stages for one selected warehouse using the complete 1C spreadsheet export.
- **FR-002**: The topology stage MUST include valid cells without stock and preserve the warehouse-wide stable compact address. Rack and mezzanine cells MUST follow the physical room, row, section and level hierarchy; gate cells MUST be grouped under `Ворота` without inventing physical rooms from their address prefixes. An address MUST identify at most one operational cell in the warehouse.
- **FR-003**: The source `Помещение` MUST identify a physical area, `Группа ячеек` its room grouping, and `Ячейка` the compact address. A physical area may contain several functional zones; the source cell kind determines the operational purpose of each cell.
- **FR-004**: `Комплектация` MUST become pickable storage, `Хранение` reserve storage unavailable for direct shipping pick, `Отгрузка` shipping, and `Приемка` receiving. Both picking and reserve cells MUST be valid receiving-putaway destinations, subject to normal cell and stock checks.
- **FR-005**: The agreed export-specific exclusions MUST be visible and applied consistently in both stages: `+`, `Невозможно скомплектовать`, `0A00A0`, `0A00A1`, `1W01W1` (`Перемещение на другой склад`), `3W03W3` (`Общая ячейка помещения`), and the empty mismatched-room address `4A10A1` are not imported as operational cells or opening stock. The approved loss of the signed `0A00A1` stock and the exclusion of `4A10A1` MUST appear in the preview/result. If `4A10A1` contains stock in a later export, confirmation MUST block for a new decision.
- **FR-006**: The `9Z99A1` source row labelled `Комплектация` MUST be treated as a classification error: the address belongs to `Отгрузка`, and its one unit of chain MUST remain in opening stock at that shipping address. This correction MUST be shown in the preview/result.
- **FR-007**: Multiple-SKU permission MUST be enabled for exactly these 15 reviewed cells in the agreed export: `1C09A1`, `1G09A1`, `1K09A1`, `1M09A1`, `1O09A1`, `1Q09A1`, `1U11A1`, `2O01A1`, `2O02A1`, `2O03A1`, `2O04A1`, `2O05A1`, `2O06A1`, `2O07A1`, `2O08A1`. The decision MUST be visible as cell configuration, independent of spreadsheet coloring.
- **FR-008**: Multiple-SKU permission MUST remain off for these eight reviewed storage cells despite their mixed opening stock: `1H05A7`, `1K03A5`, `1O08C6`, `1R04A5`, `1R05B1`, `1T03C3`, `2A02F2`, `3E07B1`. The initial import MUST preserve their actual mixed stock as a documented one-time exception; ordinary operations MUST NOT add stock to a still-violating cell, while outward movements remain possible. A violation MUST remain visible until the recorded stock contains at most one nonzero SKU. A newly discovered mixed Storage or Reserve cell outside the reviewed list MUST block confirmation until its policy is resolved; Shipping and Receiving cells may hold several SKUs.
- **FR-009**: Topology confirmation MUST be atomic. Matching existing cells MAY be reused; conflicting address, physical parent, purpose or activation MUST block confirmation rather than silently reclassify or move a cell.
- **FR-010**: Opening-balance preview MUST require a loaded target cell whose functional purpose matches the interpreted source kind, and a catalog SKU matching each accepted source GUID. The source unit MUST match the SKU's canonical unit; WMS MUST NOT infer package conversion from a product name or silently skip a missing SKU.
- **FR-011**: The opening stock stage MUST preserve accepted signed quantities and provided dates, including negative shipping stock and past dates. A missing date MUST remain unknown. Accepted source rows with `Требуется указания даты = Да` MUST enable the WMS expiration-date requirement for their SKU without fabricating missing dates or turning off a previously enabled WMS flag.
- **FR-012**: Opening stock MUST be recorded through WMS stock history, not by silently replacing current balances. Each confirmed snapshot MUST have identifiable source, warehouse, declared snapshot time, actor and confirmation time. The stage MUST commit all accepted stock effects and SKU expiration-requirement changes together or none.
- **FR-013**: Retrying the same confirmed stage MUST not create duplicate cells, movements, or balances. The opening stage MUST reject a warehouse that already has operational stock or work. A different opening snapshot MUST NOT replace or add to a warehouse's already confirmed opening stock; another rehearsal uses a cleaned target database.
- **FR-014**: Each preview and result MUST account for all source rows as accepted, corrected, excluded or blocking, and show counts of cells, empty cells, stock rows, signed stock exceptions, missing dates, and prohibited mixed cells. The result MUST support review without a separate 1C reconciliation report.
- **FR-015**: The former XLS warehouse-map import entry point MUST be replaced by the new two-stage export workflow so Administrators do not accidentally use the obsolete map rules.

### Key Entities

- **Import snapshot**: One source export selected for a warehouse, with its identity, interpretation decisions and confirmation records.
- **Operational cell**: A unique warehouse address in a physical hierarchy with a functional purpose and SKU-mixing permission.
- **Opening balance**: Signed opening stock by warehouse, cell, SKU and optional expiration date, linked to the confirmed snapshot and WMS stock history.
- **Initial mixing discrepancy**: A prohibited cell containing more than one nonzero SKU from the accepted opening snapshot; it disappears when stock is physically resolved and recorded.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The current 12,597-row sample can be previewed without splitting the file; every row has an explicit accepted, corrected, excluded or blocking outcome.
- **SC-002**: A topology-only rehearsal creates every accepted distinct address, including all accepted empty cells, and creates no balance.
- **SC-003**: After stock confirmation, 100% of accepted cell/SKU/date opening quantities match the interpreted source snapshot; 0 excluded rows create stock.
- **SC-004**: Exactly the 15 reviewed cells permit multiple SKUs; the eight prohibited mixed cells retain their opening contents and are visibly identified as violations.
- **SC-005**: A failed stock stage leaves 0 partial stock effects, and repeated confirmation leaves 0 duplicated stock effects.
- **SC-006**: A receiving worker can choose either a picking or reserve cell for putaway, while a direct shipping pick cannot originate in reserve.

## Assumptions

- The full export is the authoritative list of operational cells at cutover. Missing cells can be added manually through existing topology administration. Older map files are not imported in this workflow.
- Rehearsals are performed on a cleaned WMS database. The application import does not provide database-cleanup controls.
- Before the actual cutover snapshot, open work in the previous warehouse system is completed or separately resolved. Operations do not run concurrently in both warehouse systems; the export does not migrate in-progress orders, LPNs or transfers.
- The 1C SKU catalog is synchronized before opening stock is loaded. Opening stock does not create catalog SKUs or convert package quantities.
- The accepted signed contents of shipping cells are opening WMS stock without an order-level assignment. Their later correction requires the planned inventory-count replacement.
- The 1C stock-reconciliation report, replacement picking, replenishment, transfers and inventory counting are separate features. This feature establishes reserve purpose and accepts putaway into it; operational go-live requires the later workflows to support that stock.
