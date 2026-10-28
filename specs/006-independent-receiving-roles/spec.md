# Feature Specification: Independent receiving roles

**Feature Branch**: `codex/006-independent-roles`
**Issue**: [#24](https://github.com/igmo71/Wms/issues/24)
**Created**: 2026-10-28
**Status**: In progress
**Input**: Separate Operator and Manager permissions in receiving and putaway while retaining Manager-only Web control actions.

## User Scenarios & Testing

### User Story 1 - Warehouse work requires Operator (Priority: P1)

An Operator performs ordinary Mobile receiving and putaway. Manager alone cannot perform those operations; a user assigned both roles can both execute work and approve exceptions.

**Why this priority**: Role membership must match responsibility for physical stock.

**Independent Test**: Try Mobile receiving and putaway with Operator-only, Manager-only, both-role, and Administrator-only users.

**Acceptance Scenarios**:

1. **Given** Operator only, **when** receiving an LPN within plan or putting it away, **then** the action follows existing business rules.
2. **Given** Manager only or Administrator only, **when** using Mobile receiving or putaway, **then** access is denied before warehouse work.
3. **Given** Operator and Manager, **when** closing an LPN above plan or without a known plan in Mobile, **then** the exception may be approved and posted.
4. **Given** Operator only, **when** the same exception is needed, **then** closing is rejected without posting stock.

### User Story 2 - Manager controls receiving in Web (Priority: P1)

A Manager reviews receiving work and makes control decisions in Web without an Operator grant. Operators retain read access but cannot perform Manager-only actions.

**Why this priority**: Control decisions and physical work are separate responsibilities.

**Independent Test**: Use Manager-only, Operator-only, and Administrator-only accounts for the Web control actions.

**Acceptance Scenarios**:

1. **Given** Manager only and a valid preview, **when** changing linked sources in Web, **then** existing source and concurrency rules apply.
2. **Given** Manager only and no open LPN, **when** completing the current cycle in Web, **then** the cycle completes without posting stock.
3. **Given** Manager only and an already filled open LPN, **when** confirming its saved content and current revision in Web, **then** the existing close operation posts it.
4. **Given** Operator only or Administrator only, **when** attempting these Manager-only Web actions, **then** the action is denied.

### Edge Cases

- Removing Operator membership prevents new Mobile work even if Manager remains; active sessions retain their current expiry behavior.
- Web close still checks LPN revision, plan, quantity, and expiration date.
- Replayed commands retain existing receipt behavior and never create another posting path.

## Requirements

### Functional Requirements

- **FR-001**: Operator, Manager, and Administrator MUST be independent grants in new receiving and putaway workflows.
- **FR-002**: Mobile receiving and putaway MUST require Operator for read and write access.
- **FR-003**: A Mobile manager exception MUST require both Operator and Manager for the acting user.
- **FR-004**: Web source changes, cycle completion, and closing an already filled LPN MUST accept Manager without Operator and preserve existing business checks.
- **FR-005**: Web receiving pages MUST remain readable to Operator or Manager; Manager-only controls MUST require Manager.
- **FR-006**: A denied role check MUST leave stock, receiving facts, and operational history untouched for the attempted action.
- **FR-007**: LPN issuance, catalogs, user administration, and older warehouse workflows MUST retain current permissions until separately revised.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All four role combinations in the acceptance scenarios produce their specified allow or deny result.
- **SC-002**: A Manager-only user can complete all three Web control actions without Operator.
- **SC-003**: Rejected Mobile manager-only attempts produce zero warehouse postings.
- **SC-004**: Operator and Manager users can each reach the receiving information and actions relevant to their responsibilities without an unrelated role.

## Assumptions

- There are no real users yet, so no membership migration or compatibility alias is needed.
- Existing Web receiving read access for both roles remains useful for supervision and lookup.
- Manager-only users do not need the Mobile receiving or putaway workspace; assign both roles for combined Mobile work.
- Inventory counts and other older workflows will receive separate role decisions when redesigned.
