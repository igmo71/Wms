# Specification registry

This registry selects the specification that is active work. The number of
files under `specs/` must not cause every historical specification to be loaded
or updated for a new issue.

## Naming

New specifications use a creation date and one problem slug:

```text
specs/YYYY-MM-DD-<problem-slug>/spec.md
```

The date is the stable chronological decision identifier. Different problems
created on one day use different slugs. Existing legacy directories are not
renamed merely to adopt this convention.

## Statuses

- **Active** — the single issue currently being implemented. Read it before
  making changes for that issue.
- **Queued** — accepted future work. Do not load it by default while another
  specification is active.
- **Frozen reference** — prior issue or decision history. Do not update or read
  it unless the active specification explicitly depends on it, the user names
  it, or the affected business rule cannot be established from current context
  and code.
- **Superseded** — replaced by a newer dated decision. The registry identifies
  the replacement.
- **Removed** — its lasting rules were distilled into current documentation and
  the body was removed from the working tree. Git remains the full archive.

Only one specification is normally **Active**. If several would be active, the
scope must be split or the user must choose their order.

## Current work

| Created | Status | Problem | Path |
| --- | --- | --- | --- |
| 2026-08-29 | Active | Mobile receiving and putaway | [`2026-08-29-mobile-receiving-putaway/spec.md`](2026-08-29-mobile-receiving-putaway/spec.md) |
| 2026-08-27 | Frozen reference | Location-based inventory counting shared by mobile and web | [`2026-08-27-location-inventory-count/spec.md`](2026-08-27-location-inventory-count/spec.md) |
| 2026-08-27 | Frozen reference | Temporary storage-location locking across inventory processes | [`2026-08-27-storage-location-locking/spec.md`](2026-08-27-storage-location-locking/spec.md) |
| 2026-08-19 | Frozen reference | Android mobile WMS foundation and first vertical | [`mobile-wms/spec.md`](mobile-wms/spec.md) |
| 2026-08-25 | Frozen reference | Atomic idempotency for mobile inventory-transfer commands | [`2026-08-25-mobile-command-idempotency/spec.md`](2026-08-25-mobile-command-idempotency/spec.md) |
| 2026-08-21 | Frozen reference | Concurrent `InventoryTransfer` commands and balance conflicts | [`2026-08-21-inventory-transfer-concurrency/spec.md`](2026-08-21-inventory-transfer-concurrency/spec.md) |
| 2026-08-10 | Superseded by `2026-08-27-location-inventory-count` | Legacy multi-location inventory-count backend | [`inventory-count-backend/spec.md`](inventory-count-backend/spec.md) |

Mobile receiving and putaway is the active issue under discussion.
Location-based inventory counting, storage-location locking, the mobile
foundation, and the intra-warehouse transfer vertical are accepted frozen
references.

## Legacy reference ledger

These entries are not active by default. Their dates come from the first Git
addition of their specification directories.

| Created | Specification |
| --- | --- |
| 2026-08-08 | `order-workflow-consistency` |
| 2026-08-08 | `shipping-order-workflow` |
| 2026-08-09 | `strict-order-workflows` |
| 2026-08-10 | `operational-hardening` |
| 2026-08-10 | `picking-draft-movements` |
| 2026-08-10 | `shipping-order-ui` |
| 2026-08-11 | `inventory-balances` |
| 2026-08-11 | `shipping-order-rollback` |
| 2026-08-12 | `inventory-turnovers` |
| 2026-08-12 | `shipping-order-items-odata-update` |
| 2026-08-12 | `synchronized-catalogs-ui` |
| 2026-08-13 | `intra-warehouse-transfers` |
| 2026-08-14 | `document-information-headers` |
| 2026-08-14 | `document-list-layout` |
| 2026-08-14 | `employee-performance-report` |
| 2026-08-14 | `receiving-putaway` |
| 2026-08-14 | `weight-in-wms` |
| 2026-08-17 | `partner-import` |
| 2026-08-18 | `individual-import` |
| 2026-08-18 | `order-parties` |
| 2026-08-18 | `organizational-unit-import` |
| 2026-08-19 | `architecture-alignment` |
| 2026-08-19 | `storage-location-topology` |
| 2026-08-20 | `identity-roles-and-user-management` |
| 2026-08-20 | `sku-physical-properties` |

## Completion and deletion

When an active issue is accepted:

1. Move lasting product rules to `docs/PROJECT_CONTEXT.md` and lasting
   engineering conventions to `docs/ARCHITECTURE.md`.
2. Move unfinished follow-up work to `docs/ROADMAP.md`; do not keep it hidden in
   a completed specification.
3. Mark the registry entry **Frozen reference** and stop propagating unrelated
   future changes into its body.
4. Remove its `implementation-plan.md` when it no longer helps current work;
   Git retains the implementation history.
5. If historical specs become numerous, remove the frozen `spec.md` only after
   the lasting rules have been distilled. Keep its dated registry row, change
   the status to **Removed**, and record the replacement/current documentation.

Deleting a frozen body is a repository-maintenance decision, not part of an
unrelated feature change.
