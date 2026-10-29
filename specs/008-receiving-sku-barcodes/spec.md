# Feature Specification: Add SKU barcodes during receiving

**Feature Branch**: `codex/008-receiving-sku-barcodes`
**Issue**: [#30](https://github.com/igmo71/Wms/issues/30)
**Created**: 2026-10-29
**Status**: Completed
**Input**: Receiving workers need to register a missing SKU barcode while receiving; WMS must preserve local additions across 1C synchronization and provide a way to find and resolve duplicate barcode values.

## User Scenarios & Testing

### User Story 1 - Register a barcode at receiving (Priority: P1)

An Operator participating in the current receiving cycle opens the barcode action on a SKU card, scans the product barcode, checks the displayed SKU and code, and saves it. The barcode can immediately identify that SKU in WMS. The action remains available whether the SKU already has barcodes or not.

**Why this priority**: A missing barcode should not interrupt physical receiving or require a catalog administrator to leave the warehouse workflow.

**Independent Test**: On an active order, add a previously unknown barcode to a current SKU, scan it again, and confirm it resolves to that SKU. Repeat on a SKU that already has another barcode.

**Acceptance Scenarios**:

1. **Given** a current SKU without barcodes and a participating Operator, **when** the Operator opens the pale barcode action, scans a new code and saves it, **then** the panel closes, the action becomes prominent, and a subsequent scan resolves the SKU.
2. **Given** a current SKU with one or more barcodes, **when** the Operator opens the prominent action and saves another new code, **then** the new code is associated with the same SKU and the earlier codes remain available.
3. **Given** a scanned code assigned to another SKU, **when** the Operator tries to save it, **then** no new association is saved and the Operator sees which SKU already uses it.
4. **Given** a code already assigned to the selected SKU, **when** the Operator tries to save it again, **then** no duplicate association is created and the Operator sees that it is already registered.
5. **Given** an Operator who is not participating in the active cycle, or an order that is no longer active, **when** they view the order, **then** they cannot add a barcode through it.

---

### User Story 2 - Reconcile local and 1C barcodes (Priority: P1)

A barcode entered during receiving remains usable when 1C synchronizes its barcode register. When 1C later supplies the same SKU and barcode, that pair becomes 1C-owned without a duplicate and the earlier manual addition remains in the operational audit. 1C may supply the same barcode value for different SKUs; WMS retains those source associations and requires an explicit SKU choice when a scan is ambiguous.

**Why this priority**: Routine source updates must neither erase warehouse additions nor silently discard authoritative 1C data.

**Independent Test**: Add a local code, run both a single-SKU and full barcode synchronization, then add the same pair in 1C and synchronize again. Verify ownership, continued resolution, source deletion behavior and preservation of the manual audit.

**Acceptance Scenarios**:

1. **Given** a locally added code absent from 1C, **when** either single-SKU or full synchronization succeeds, **then** the local association remains usable.
2. **Given** a local association and the identical SKU/code pair now present in 1C, **when** synchronization succeeds, **then** one association remains, 1C owns its current state, and the original manual-addition audit remains available.
3. **Given** a 1C-owned association later removed from 1C, **when** its scope synchronizes successfully, **then** WMS removes that association; a still-local association absent from 1C remains.
4. **Given** that 1C supplies one code for two SKUs, **when** synchronization succeeds, **then** both source associations are present and scanning that code does not silently select either SKU.
5. **Given** a failed or unreadable 1C response, **when** synchronization is attempted, **then** existing barcode associations remain unchanged.

---

### User Story 3 - Find and resolve barcode duplicates (Priority: P2)

A Manager opens the Web barcode list, filters to barcode values associated with multiple SKUs, and sees each SKU and whether its association is local or imported. The Manager may remove a local association with confirmation and an audit record. Imported associations must be corrected in 1C.

**Why this priority**: 1C can legitimately supply a value for several SKUs, while WMS scanning needs a visible way to investigate ambiguity and remove an incorrect warehouse addition.

**Independent Test**: Import or seed two associations with the same value for different SKUs, filter the list, remove a local row, and verify the remaining association resolves and the deletion is audited.

**Acceptance Scenarios**:

1. **Given** a value associated with at least two distinct SKUs, **when** the Manager selects the duplicate filter, **then** every association for that value is displayed with its SKU and source.
2. **Given** a local association, **when** a Manager confirms its removal, **then** that association is no longer used for scanning and the actor, time and removed pair are retained in audit.
3. **Given** an imported association, **when** a Manager views it, **then** WMS offers no local removal action for that association.
4. **Given** an ambiguous scanned code, **when** a receiving worker encounters it, **then** WMS reports the ambiguity and manual SKU search remains available.

### Edge Cases

- A scan arriving while the add-barcode panel is open must be handled by that panel, not by the underlying order scan action.
- Saving must reject empty or otherwise unsupported barcode values without changing catalog data.
- Repeated save requests and simultaneous attempts to add the same value must not create duplicate associations for one SKU or new ambiguous manual mappings.
- Duplicate detection is based on one barcode value associated with more than one distinct SKU; repeated 1C records for the same SKU and value do not create extra WMS associations.
- Removing an association affects future barcode lookup only; it does not change prior receiving facts or stock movements.
- An imported association may be deleted by a later successful 1C synchronization even if its first appearance in WMS was a manual entry; its manual-addition audit remains.

## Requirements

### Functional Requirements

- **FR-001**: The Mobile receiving SKU card MUST show a barcode action beside the zero-confirmation action when the latter appears, and in the same action area when it does not. It MUST be visually prominent when the SKU has at least one barcode and pale otherwise; either appearance MUST allow an eligible worker to add a code.
- **FR-002**: A participating Operator MUST be able to scan a code for a current SKU in an active receiving cycle, review the selected SKU and scanned value, and explicitly save or cancel. Scanning in the panel MUST not trigger the underlying order's scan action.
- **FR-003**: WMS MUST reject manual addition of a code already associated with a different SKU, identify the existing SKU, and avoid duplicating an association already present for the selected SKU.
- **FR-004**: A successful manual addition MUST be immediately usable for WMS SKU lookup, update the card indicator, and record the acting user, time, SKU and code in operational audit.
- **FR-005**: Manual additions MUST remain usable through both single-SKU and full 1C barcode synchronization while the exact pair is absent from 1C. No receiving action in this feature writes to 1C.
- **FR-006**: A successful 1C synchronization MUST reconcile its source scope: add current 1C pairs, keep still-local pairs, and remove 1C-owned pairs no longer in the source scope. A failed source read MUST not change saved associations.
- **FR-007**: When 1C supplies a pair that was manually added, WMS MUST retain one association, treat 1C as its current owner, and preserve the earlier manual-addition audit. Subsequent source removal MUST remove the 1C-owned association.
- **FR-008**: WMS MUST accept distinct 1C associations with the same barcode value for different SKUs. Receiving barcode lookup MUST report ambiguity rather than choose a SKU automatically when more than one active SKU matches.
- **FR-009**: The Web barcode list MUST offer a quick filter for values associated with more than one distinct SKU and display the SKU and current source of each association.
- **FR-010**: A Manager MUST be able to remove a WMS-owned barcode association from the Web list after confirmation. WMS MUST audit the actor, time and removed pair. Imported associations MUST not be removable through this action.
- **FR-011**: Removing a barcode association MUST affect later lookup only and MUST NOT alter previously recorded receiving or inventory facts.

### Key Entities

- **SKU barcode association**: A barcode value linked to one SKU, with current ownership by WMS or 1C. The pair is unique; one value may appear for different SKUs when supplied by 1C.
- **Barcode source snapshot**: The current set of associations supplied by 1C for a SKU or the whole catalog, used to reconcile imported associations without deleting local ones.
- **Barcode audit event**: The actor, time, SKU, value and action for a manual addition or removal, retained after the live association changes or disappears.

## Success Criteria

### Measurable Outcomes

- **SC-001**: For a known SKU and a previously unknown barcode, an eligible receiving worker can register the code with one scan and one explicit save after opening the action; the next scan resolves that SKU.
- **SC-002**: In all checked single-SKU and full synchronization scenarios, locally owned codes absent from 1C remain usable and imported codes removed from 1C cease to resolve.
- **SC-003**: The duplicate filter returns every barcode value assigned to at least two distinct SKUs in the checked catalog and excludes values assigned to only one SKU.
- **SC-004**: No checked manual-add or ambiguous-scan scenario silently associates one barcode with an unintended SKU.
- **SC-005**: Every checked manual addition and removal retains an actor, time, SKU and value in audit after subsequent synchronization.

## Assumptions

- This feature is limited to Mobile receiving barcode creation and Web barcode investigation/removal; broader catalog editing is not introduced.
- The existing online-only Mobile scanner and camera fallback remain the input devices. Manual text entry is not required for the first release.
- The existing Manual SKU search is the fallback when a barcode supplied by 1C is ambiguous.
- Web barcode investigation follows existing catalog visibility rules; removal requires explicit Manager membership.
- 1C owns imported associations, while WMS owns local associations until the exact pair appears in 1C.
