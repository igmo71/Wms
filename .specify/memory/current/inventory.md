# Current storage and inventory

## Storage and inventory

A warehouse contains storage, transit, receiving, and shipping zones. Locations
form an arbitrary-depth physical tree within one warehouse. A folder may have no
functional zone; an operational location belongs to one active zone of its
warehouse.

- Folder locations are structural and cannot participate in inventory
  operations.
- `Zone.Code` is unique within a warehouse. `StorageLocation.Code` is the stable
  compact address, unique within the warehouse, independent of zone and parent.
  Manual addresses accept Latin letters and digits, with common visual
  separators ignored. This distribution center uses room, rack row, section,
  cell and level in that order, for example `1S02C3`. The physical tree groups
  by room, row, section, level, then cell; level and cell have reversed order
  relative to the address. Other warehouses may use a different tree.
- Existing locations cannot be moved or readdressed. Activation follows the
  parent chain, and a node with active children cannot be deactivated.
- `PickSequence` is optional; duplicates use the location code as stable
  secondary ordering.
- A location barcode carries the compact address, looked up within the selected
  warehouse. Printed Code 39 labels encode that value; scanner input must not
  add an unknown prefix or suffix.
- Administrators can preview and import the existing 1C XLS warehouse maps into
  a selected warehouse. Tier maps enumerate operational locations; overview
  maps only check tier coverage and never generate additional addresses. The
  imported physical hierarchy is room, row, section, level, then location.
  Each nonempty area requires a chosen functional zone; racks and mezzanine
  require a storage zone. Repeated map addresses create one location and appear
  in the preview. Existing matching locations are reused; conflicting zone,
  parent, type, or deactivation blocks the import. Preview warnings require an
  explicit acknowledgement. Room-4 address `4A10A1` is deferred. Gate addresses
  are deferred until their individual functional zones are known, and empty
  maps create no locations.
- Administrators can generate up to 1,000 five-segment location addresses into
  a selected active folder. The preview expands room, row, section, cell, and
  level ranges, checks warehouse-wide code conflicts, and blocks creation when
  an imported folder's physical path disagrees with the generated address.
  Creation saves the accepted batch atomically and does not make nested folders.
- The A4 location-label preview prints active operational addresses above a
  Code 39 barcode carrying the compact address. Five-segment addresses use
  middle-dot separators in the printed text; other addresses print as stored.
  One label or up to 120 direct child locations of a folder can be printed; labels
  are approximately 40 mm high. Exact adhesive-stock dimensions remain open.

An active operational location may have one active lock. A lock keeps the
location visible but excludes it from operational selection and final posting.
A locked location cannot be deactivated or converted to a folder. Administrators
own manual locks; an inventory-count draft owns its lock until posting or
explicit deletion. Lock release removes the active record; separate lock
history is not retained.

Inventory uses one canonical unit per SKU. Operational quantities are C#
`decimal` persisted as SQL Server `decimal(15,3)`, matching the current 1C
`Number(15,3)` boundary; unsupported scale or range is rejected, not rounded.
Physical dimensions remain `double`. Imported weight is kilograms per canonical
unit and volume is cubic meters per canonical unit. Invalid or unavailable
physical data is `null`, never numeric zero, and reports mark affected totals as
incomplete. Physical properties are current catalog values rather than
historical snapshots.

Dimensions use meters, maximum weight uses kilograms, and coordinates use the
warehouse-local meter system. Usable volume is
`Volume * (VolumeFactor ?? 1)`. Capacity values are stored but not enforced.
The current integration requires document quantity and package quantity to
match 1:1. The canonical unit comes from the SKU's base unit of measure:
for example, a SKU named "(500 шт)" can be counted as five `упак` when its
base unit is `упак`; the number in the name does not multiply its quantity.
Refilling into another pack size is represented by a different SKU. Package
keys and characteristics do not participate in SKU identity.

`InventoryBalance` is current quantity at a location. Posted
`InventoryMovement` records warehouse movement, and `InventoryTurnover` is its
immutable before/delta/after history. Draft picking movements do not change
inventory; LPN putaway posts each confirmed movement immediately. Zero balance
rows are retained and support historical-location suggestions.

Posting validates active topology, folder status, locks, the workflow-specific
zone route, and physical balance in the caller's save. Posting and
eligibility-changing topology or lock updates advance
`StorageLocation.OperationalRevision`. Transfers additionally use transfer and
balance concurrency tokens plus named database constraints. Only recognized
inventory concurrency failures become business conflicts.
