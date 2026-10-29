# Research: Receiving SKU barcodes

## Source ownership

**Decision**: Keep the existing `(SkuId, Value)` association and add current ownership (`OneC` or `Wms`); retain manual history in `WarehouseAuditEvents`.

**Rationale**: The key already permits a value on more than one SKU and prevents duplicate identical pairs. Current 1C imports delete all rows or all rows of one SKU before inserting, so a WMS addition would otherwise disappear. Source ownership lets reconciliation remove only stale imported pairs. Existing rows came from 1C and should migrate as such. Audit remains after source promotion or row removal.

**Alternatives considered**: Separate local/imported tables would require every barcode lookup to union them and could produce two rows for one pair. Insert-only import would preserve local entries but also retain mistaken codes removed from 1C. A global value uniqueness constraint would reject source data under the agreed rule.

## 1C synchronization boundary

**Decision**: Read and validate the entire requested source scope before a single atomic reconciliation of that scope. A full import and per-SKU notification use the same merge rules.

**Rationale**: Current `DeleteAllAsync`/`DeleteRangeAsync` and `CreateListAsync` save separately. A failed insert after deletion loses previously usable barcodes. Reconciliation preserves local rows and makes successful 1C removal meaningful. Incoming duplicate `(SkuId, Value)` pairs can be collapsed because WMS stores one association per pair.

**Alternatives considered**: Keep delete/reinsert with a filter for local rows; this still creates a visible gap and risks losing imported rows on partial failure. Reject equal barcode values across different SKUs; this would discard authoritative 1C associations.

## Manual commands and concurrent changes

**Decision**: Reuse the application command executor for Mobile addition and Manager removal, and coordinate barcode writes across commands and imports before checking/committing associations.

**Rationale**: The composite key protects one pair but does not stop two users from concurrently assigning one new value to two different SKUs. A shared database coordination point protects the manual-add rule; 1C may still introduce same-value associations by design. The executor supplies repeat-safe command receipts and atomic user audit.

**Alternatives considered**: A global unique index contradicts 1C import behavior. A pre-save query alone is race-prone. A new catalog service layer is unnecessary.

## Client data and duplicate search

**Decision**: Add a barcode-presence flag to the compact Mobile order item response and a server-paged Web read model with source and duplicate filtering.

**Rationale**: Mobile only needs to know whether at least one code exists; it should not load every barcode for every order item. Web needs pair-level source information and a filter evaluated before paging, so duplicate groups are complete and discoverable. Existing receiving scans already report ambiguity instead of selecting an arbitrary SKU.

**Alternatives considered**: Load all barcode values in each Mobile order detail or calculate duplicates after Web paging; both do unnecessary work and the latter misses groups split across pages.
