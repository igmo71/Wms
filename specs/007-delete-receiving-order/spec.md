# Feature Specification: Delete a receiving order

**Feature Branch**: `codex/007-delete-receiving-order`
**Issue**: [#27](https://github.com/igmo71/Wms/issues/27)
**Created**: 2026-10-29
**Status**: Completed
**Input**: A Manager can delete a WMS receiving order before any of its LPNs has been put away, preserving history and compensating inventory.

## User Scenarios & Testing

### User Story 1 - Manager deletes an unplaced order (Priority: P1)

A Manager reviews the whole order, its LPNs, and the stock change that deletion would cause. After confirmation, the order and its LPNs become deleted, their received stock is compensated, and the linked 1C documents may be used in a new WMS order.

**Why this priority**: An erroneous receiving order must be retired without erasing warehouse history or leaving its stock behind.

**Independent Test**: Delete an order containing open, closed, previously cancelled, and previously deleted LPNs across multiple receiving cycles, with receipts and quantity corrections but no putaway. Check the order, LPNs, documents, balances, movement history, and audit afterward.

**Acceptance Scenarios**:

1. **Given** a ready order with no LPNs, **when** a Manager confirms deletion, **then** it is archived as deleted, its active 1C links are released, and no stock changes.
2. **Given** an order with closed LPNs and quantity corrections but no LPN putaway, **when** a Manager confirms its current preview, **then** every outstanding receipt and correction is compensated exactly once, and all of its LPNs are deleted.
3. **Given** an open or previously cancelled LPN in the order, **when** the order is deleted, **then** the label cannot be used for new receiving, and its earlier work and cancellation history remain available.
4. **Given** an order with any historical putaway belonging to any of its LPNs, including an already deleted LPN, **when** deletion is attempted, **then** the whole order remains unchanged.
5. **Given** a user without Manager, including an Administrator without Manager, **when** deletion is attempted, **then** access is denied without effects.

### User Story 2 - Deleted order remains traceable (Priority: P2)

A Manager can find and read a deleted order and see who deleted it, when, what happened to its LPNs, and which compensating movements were made. An Operator cannot resume the archived work.

**Why this priority**: Stock and source-document decisions must remain explainable after deletion.

**Independent Test**: Open the deleted order from Web history and from a former direct link; check its deletion information and history, then attempt receiving, source edits, completion, reopening, and putaway through stale clients.

**Acceptance Scenarios**:

1. **Given** a deleted order, **when** Web history is opened, **then** its number, deleted status, actor, time, LPNs, source-link history, cycles, and compensating movement history remain readable.
2. **Given** a deleted order, **when** Mobile loads its active queue, **then** it is absent; a stale Mobile command cannot change it or its LPNs.
3. **Given** a document formerly linked only to that deleted order, **when** a new WMS order is created from it, **then** the historical link remains and the new active link is allowed.

### Edge Cases

- No putaway means no putaway record exists for **any** LPN ever assigned to the order, regardless of current LPN status or cycle.
- The preview covers all LPNs and all affected warehouse, location, SKU, and expiration-date balance groups together. An unexpected negative forecast requires a separate explicit Manager confirmation. A stale preview is rejected.
- A repeated request with the same identity and content returns its original result without duplicating compensation; another deletion attempt is rejected.
- Concurrent closing, correction, LPN deletion, source edit, putaway, or balance change must either be included in a fresh preview or make the deletion fail without partial effects.
- Earlier completed-cycle snapshots and prior movement, turnover, audit, and source versions remain unchanged.
- A receiving order may have no active sources when deletion begins; deleting it still works if the other conditions hold.

## Requirements

### Functional Requirements

- **FR-001**: Only a user with an explicit Manager role may preview and confirm deletion in Web. No Mobile deletion control is required.
- **FR-002**: Deletion MUST be rejected if any LPN ever assigned to the order has a putaway record; this check MUST include already deleted LPNs.
- **FR-003**: A successful deletion MUST mark the order deleted with the acting user's identity and time, keep its ID and number, and retain its prior cycle and source history.
- **FR-004**: A successful deletion MUST mark every LPN assigned to the order deleted without reissuing its label. Prior LPN work, cancellation, receipt, correction, and deletion history MUST remain traceable.
- **FR-005**: For each not-yet-compensated receipt or quantity correction of the order's LPNs, deletion MUST add an opposing movement and turnover and update the affected stock balance. It MUST preserve original records and unrelated stock operations.
- **FR-006**: The order's active 1C source links MUST be detached, freeing their documents for a new WMS order. No write to 1C is made. Current plan and received fact MUST reflect that no active sources or received LPNs remain; earlier cycle snapshots remain immutable.
- **FR-007**: Before confirmation, Web MUST show affected LPNs, compensating movements, and before/after balances. A negative forecast MUST require a separate explicit acknowledgement. A changed decision input MUST invalidate the preview.
- **FR-008**: The status change, LPN changes, source detachment, compensation, audit, and command result MUST succeed or fail together. Retrying the same command MUST not duplicate any effect.
- **FR-009**: Deleted orders MUST remain readable in Web history but MUST be absent from active Mobile work. No receiving, source-composition, completion, reopening, correction, LPN deletion, or putaway command may change a deleted order.
- **FR-010**: A deleted order's number MUST remain reserved, and its deletion actor/time and compensating history MUST be visible in its Web details.

### Key Entities

- **Receiving order**: WMS document with a fixed number, lifecycle, cycles, source links, LPNs, and deletion actor/time.
- **LPN**: Issued label assigned to an order; deletion preserves its code, items, prior status history, and stock provenance.
- **1C source link**: Historical association whose active state ends when the order is deleted.
- **Inventory movement and turnover**: Immutable original and compensating stock events with LPN and order provenance.
- **Deletion preview**: A temporary combined forecast and identity of the state the Manager confirmed.

## Success Criteria

### Measurable Outcomes

- **SC-001**: For every affected balance group, the final quantity equals the pre-deletion quantity plus the sum of compensating movement effects; unrelated movement and turnover records remain unchanged.
- **SC-002**: One confirmed deletion yields one deleted order, all assigned LPNs deleted, and at most one reversal per original movement, even after repeated requests.
- **SC-003**: Every forbidden or stale attempt leaves the order, LPNs, links, balances, movements, turnovers, and audit unchanged.
- **SC-004**: A Manager can identify the deletion actor, time, affected LPNs, and stock effects from the Web order history without database access.
- **SC-005**: A formerly linked 1C document can be selected for a new order after deletion, while the deleted order never reappears in active Mobile work.

## Assumptions

- The existing closed-LPN deletion behavior remains available separately, including after putaway; this feature adds a stricter order-level rule.
- The rare negative forecast safeguard follows the existing LPN deletion interaction. Ordinary supported workflows are not expected to produce a negative receiving-location forecast for an unplaced order.
- Web lists may show deleted orders as historical rows; the deleted order card is read-only.
- Previously cancelled or deleted LPNs retain their historical actor/time fields and audit; order deletion records its own event rather than rewriting earlier events.
- Existing database contents need no backfill or compatibility migration.
