# Operational topology integrity

Status: frozen reference. Completed 2026-09-07.

## Outcome

Prevent inventory posting from committing against inactive or concurrently
changed warehouse topology while keeping route-role rules in their owning
business workflows.

## Scope

- Final inventory posting requires an active warehouse, active location and
  active zone, a non-folder location in the movement warehouse, and an
  admissible location lock.
- Receiving, putaway, picking, shipping, count, and transfer completion retain
  or add final feature-owned zone-role checks where their route requires them.
- Eligibility-changing location writes advance that location's
  `OperationalRevision` in the same classified save.
- Zone activity and type changes advance all affected child location revisions
  in the same classified save.
- A warehouse activity transition imported from 1C advances all affected
  location revisions in the same classified save.
- Pilot a narrow operational-location policy in one workflow only when it
  makes the primary command path easier to read.

## Out of scope

- A general validation framework, `IValidator<T>`, discovery, pipelines, or a
  flag-based validation context.
- A generic movement-route policy inferred from recorder type.
- Topology schema changes or a new warehouse/zone concurrency token.
- Capacity, reservations, warehouse assignments, or new workflows.
- Refactoring Mobile, synchronization, or command receipts.
- New automated tests.

## Acceptance criteria

- Posting rejects a missing/inactive warehouse, location, or zone, a folder,
  a warehouse mismatch, and a foreign lock before changing balances.
- Inventory-count posting still accepts only its own count lock.
- Final route roles are checked by the completing feature rather than by the
  generic posting algorithm.
- Location activation/deactivation and eligibility-changing edits advance its
  operational revision and use classified persistence.
- Zone activation/deactivation/type changes advance child location revisions
  atomically with the zone change and use classified persistence.
- A warehouse deletion-mark transition advances child location revisions
  atomically with the imported warehouse update and uses classified
  persistence; unchanged imports do not churn revisions.
- A posting loaded before a competing topology change conflicts through the
  existing location operational-revision token.
- The selected policy pilot has a business name, owns no save, and contains no
  independent rule flags; it is retained only if the caller becomes clearer.
- Affected projects build, `git diff --check` passes, and no tests are created.

## Verification

- Trace each final posting call and its feature-owned route validation.
- Inspect every command/import path that changes location eligibility.
- Build affected projects and hosts.
- Review the focused diff and run `git diff --check`.

## Result

- Generic inventory posting now rejects inactive warehouses, zones, and
  locations in addition to folders, warehouse mismatch, and foreign locks.
- Receiving, putaway, picking, shipping, rollback, transfer, and inventory
  count retain explicit feature-owned route or role validation. Inventory
  count also validates its location when a zero difference produces no
  movement.
- `StorageLocation` advances its revision when activity or folder eligibility
  changes. Location, zone, and warehouse eligibility saves use the classified
  persistence boundary; zone and warehouse changes advance affected child
  revisions in the same save.
- `ReceivingOrderLocationPolicy`, `ShippingOrderLocationPolicy`, and the small
  `InventoryCountLocationPolicy` keep database-backed route rules out of the
  primary command sequence. They are internal, own no save, and use no rule
  flags or validator infrastructure.
- No schema or transport contract changed. `Wms`, `Wms.WebApi`, and
  `Wms.WebApp` build successfully. WebApi retains the known `NU1903` warning
  for `Microsoft.OpenApi 2.0.0`.
- `git diff --check` passes and no automated tests were created.
