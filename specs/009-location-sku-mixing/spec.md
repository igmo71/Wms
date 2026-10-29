# Feature Specification: Control SKU mixing by location

**Feature Branch**: `codex/009-location-sku-mixing`
**Created**: 2026-10-29
**Status**: Completed
**Input**: Warehouse staff need storage locations to reject a second SKU by default, with an administrator-controlled exception for intentional mixing.

## User Scenarios & Testing

### User Story 1 - Configure a location (Priority: P1)

An Administrator can see and edit whether a location permits several SKUs. Every new location starts with the permission off, including manually created, range-generated, and map-imported locations. Permission cannot be turned off while stock of several SKUs remains there.

**Why this priority**: The few locations intended for mixed storage need an explicit, visible exception.

**Independent Test**: Create locations by all three paths, inspect the default, enable one, place two SKUs there, and attempt to disable it.

**Acceptance Scenarios**:

1. **Given** a newly created operational location, **when** it is opened, **then** SKU mixing is off regardless of creation path.
2. **Given** an operational storage location, **when** an Administrator enables mixing, **then** the setting is saved and visible when reopened.
3. **Given** nonzero balances for two SKUs, **when** an Administrator tries to disable mixing, **then** the change is rejected without altering the setting.
4. **Given** several expiration dates of one SKU and zero rows of other SKUs, **when** mixing is disabled, **then** the change succeeds.

---

### User Story 2 - Place stock under the location rule (Priority: P1)

An Operator may add stock to an empty storage location, a location occupied only by that SKU, or a location with mixing enabled. Adding another SKU to a location with mixing off reports a conflict and posts nothing. Several expiration dates of one SKU remain allowed.

**Why this priority**: The setting must govern actual posting, including receiving putaway and warehouse movements.

**Independent Test**: Put A into a location with mixing off, try to add B by putaway and transfer, then enable mixing and repeat.

**Acceptance Scenarios**:

1. **Given** an empty storage location with mixing off, **when** A is placed, **then** its stock posts.
2. **Given** A alone in a location with mixing off, **when** more A or another date of A is placed, **then** its stock posts.
3. **Given** A in a location with mixing off, **when** B is sent there by receiving putaway, direct movement, or movement from transit, **then** the operation conflicts without partial stock effects.
4. **Given** A in a location with mixing on, **when** B is placed there, **then** both SKUs remain recorded.
5. **Given** two concurrent attempts to put different SKUs into an empty location with mixing off, **when** both finish, **then** at most one succeeds and the other reports a conflict.

---

### User Story 3 - Handle count correction and existing rollback (Priority: P2)

An inventory count may replace recorded A with B when its final state contains B alone. It cannot post a final mixed state into a location with mixing off. If rollback of the current picking workflow would restore A into a location now occupied by B, it reports a conflict without partial effects.

**Why this priority**: Other current paths that add stock to storage must respect the same permission.

**Independent Test**: Count A as zero and B as positive in one location; then count both positive. Pick all A, place B, and try to roll back the pick.

**Acceptance Scenarios**:

1. **Given** A in a location with mixing off, **when** a count posts A as zero and B as positive, **then** the final balance contains B alone.
2. **Given** mixing is off, **when** a count would leave both A and B nonzero, **then** posting conflicts without changing the count or stock.
3. **Given** A was fully picked and B was subsequently placed in that now-empty location, **when** current picking rollback tries to return A, **then** it conflicts without changing the order, balances, or history.
4. **Given** B is moved away after that conflict, **when** the rollback is retried, **then** it may proceed under the normal rollback rules.

### Edge Cases

- Retained zero balance rows do not occupy a location. A negative balance counts as nonzero for this permission.
- A batch of movements is judged by its final stock composition, so valid replacement of A with B is independent of movement order.
- A stale recommendation or a changed location setting cannot bypass validation at posting.
- Receiving, transit, and shipping positions may contain multiple SKUs as part of their operational flows; this restriction does not apply to them.
- Existing recommendations still prefer locations already holding the chosen SKU, then empty locations with zero-balance history for that SKU. A mixing-enabled location holding only other SKUs remains address-selectable but is not added to recommendations.

## Requirements

### Functional Requirements

- **FR-001**: Each operational location MUST have saved SKU-mixing permission, off by default for every supported creation path.
- **FR-002**: Administrators MUST be able to view and change the permission on a storage location. Structural folders MUST NOT present it as an operational choice.
- **FR-003**: Disabling permission MUST fail if nonzero balances of more than one SKU remain. Multiple dates of one SKU count as one SKU; zero rows do not count.
- **FR-004**: An operation adding stock to a storage location with permission off MUST fail if its final recorded state would contain nonzero balances of more than one SKU. This covers receiving putaway, direct and transit-backed movement, inventory count posting, and current picking rollback.
- **FR-005**: Permission on MUST allow a second SKU subject to existing route, quantity, lock, and availability rules.
- **FR-006**: A rejected operation MUST report a clear location-mixing conflict and MUST NOT leave partial movements, balances, history, order or count changes, or a successful command result.
- **FR-007**: Concurrent operations and administrator changes MUST preserve the permission decision at commit; stale operations MUST conflict instead of creating unapproved mixing.
- **FR-008**: The restriction MUST cover current storage destinations and future storage-purpose location types when introduced, while leaving current receiving, transit, and shipping flows unaffected.
- **FR-009**: Destination recommendation scope and ordering MUST stay unchanged. Any otherwise valid destination MAY still be selected by address and checked at posting.

### Key Entities

- **Storage location**: Physical address with administrator-controlled permission for several SKUs, independent of future picking eligibility.
- **Inventory balance**: Signed quantity by location, SKU, and optional expiration date; nonzero balances determine occupied SKUs.
- **Inventory operation**: Putaway, movement, count correction, or current picking rollback that can add stock to storage.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All three location-creation paths yield mixing permission off until an Administrator changes it.
- **SC-002**: In every current stock-adding workflow, adding a second SKU to a location with permission off leaves zero effects from the rejected operation.
- **SC-003**: A mixing-enabled location accepts two SKUs, while a location with mixing off accepts two dates of one SKU.
- **SC-004**: Conflicting administrator edits, concurrent placements, count posting, and current picking rollback never leave partially completed warehouse operations.
- **SC-005**: Operators see a specific mixing conflict at rejected posting and can choose another destination without re-entering an otherwise confirmed putaway quantity.

## Assumptions

- Warehouse data will be cleaned before deployment; existing locations may start with permission off without migration of mixed stock.
- Current picking rollback is covered because it exists now. Future picking and replenishment workflows are outside this feature.
- Old-system location kinds and duplicate displayed addresses are outside this feature. Current WMS address rules remain.
- Permission controls recorded stock composition; it cannot discover physically misplaced items that have not been counted or recorded by an operator.
