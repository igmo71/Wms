# Storage-location topology

## Business outcome

WMS must describe both small and industrial warehouses without imposing fixed
levels such as aisle, rack, level, and bin. Storage locations form an
arbitrary-depth tree inside an existing typed warehouse zone.

## Scope

- add a required zone code, unique inside its warehouse;
- add a self-referencing hierarchy to `StorageLocation`;
- distinguish structural folders from operational inventory locations with
  `IsFolder`;
- store a materialized numeric path code inside the zone;
- store optional dimensions, capacity, coordinates, and picking order;
- show the complete tree for one selected zone;
- create or edit one node and batch-create its immediate children;
- generate child codes, coordinates, and picking order in one transaction;
- reject folders in every inventory operation and operational selector.

## Rules

- A location and its parent always belong to the same warehouse and zone.
- `IsFolder` is independent of whether the node has children. Only a node with
  `IsFolder == false` may be referenced by inventory balances, movements, and
  warehouse documents.
- `StorageLocation.Code` is the materialized numeric path inside a zone, for
  example `01-03-04`. The displayed full address is
  `{Zone.Code}-{StorageLocation.Code}`.
- A zone code is required and unique inside a warehouse. A location code is
  required and unique inside a zone.
- The number and parent of an existing node cannot be changed in the MVP.
  Moving a subtree is not supported.
- A used operational location cannot be converted to a folder.
- A node with active children cannot be deactivated. A node cannot be activated
  while its parent is inactive.
- Length, width, and height are meters; volume is cubic meters; maximum weight
  is kilograms. Values cannot be negative. `VolumeFactor`, when specified, is
  greater than zero and at most one. Missing factor means the complete known
  volume is usable.
- X, Y, and Z are nullable absolute meter coordinates in the warehouse's local
  coordinate system. Relative coordinates are generator input only.
- `PickSequence` is nullable and ordered inside a zone. Duplicate values are
  permitted; code is the stable secondary sort.
- The technical barcode value is generated from the identifier as
  `WMSL:{Id:N}` and is not persisted separately.

## Batch generation

The operator selects a zone and optionally a parent, then specifies count,
starting number, number step, segment width, name prefix, node type, and optional
coordinate and picking-sequence generation parameters. Only immediate children
are created. The whole batch succeeds or fails in one transaction.

The generation request validates its own ranges and mutually dependent input.
The application service remains responsible for zone and parent state and for
code conflicts because those checks require persisted data.

Single-location creation reuses one domain `StorageLocationDetails` value for
the editable properties. Updating accepts that value directly rather than a
duplicate update request. UI form state is not used as an application request.

## Non-goals

- moving nodes or subtrees;
- fixed hierarchy levels;
- route graphs and pathfinding;
- relative coordinate storage;
- automatic capacity enforcement;
- a general-purpose naming or code-template engine.

## Acceptance criteria

- an operator can build trees of different depths in every zone;
- generated codes are deterministic and conflicts are rejected;
- folders never appear as valid locations in warehouse operations;
- dimensions and coordinates may be partially populated;
- creating a batch cannot leave a partially created structure;
- tree configuration remains understandable without specialized warehouse
  topology terminology.
