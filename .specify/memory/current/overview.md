# Current WMS state

This directory describes implemented behavior. The code and migrations remain the source of truth for implementation details. The planned receiving replacement is in [the active specification](../../../specs/001-receiving-stock-lifecycle/spec.md).

Read only the relevant area: [identity](identity.md), [inventory](inventory.md), [receiving](receiving.md), [other warehouse workflows](other-workflows.md), [1C integration](integration.md), and [clients](clients.md). Current technical structure and conventions are in [architecture](../architecture.md); deferred work is in [backlog](../backlog.md).

## Product boundary

WMS is a warehouse-management MVP. 1C owns catalogs and source receiving and
shipping documents. WMS imports them, executes warehouse work, returns results
to 1C, and owns operational inventory by storage location.

Implemented workflows are receiving and putaway, picking and shipping, direct
and transit intra-warehouse transfers, location inventory counts, inventory
history, employee-performance reporting, topology administration, synchronized
catalogs, and WMS user administration.

The supported interactive clients are `Wms.WebApp` and Android `Wms.Mobile`.
`Wms.WebApi` exposes authenticated Mobile V1 plus separate 1C integration
endpoints.

Domain types are extracted into `Wms.Domain`. The existing EF context,
configurations, Identity data types and migration history reside in
`Wms.Infrastructure`; existing application services and 1C integration still
run in the legacy `Wms` project. `Wms.Application` contains the new command
execution, inventory-posting and receiving-source/order foundation;
`Wms.Integration` contains the 1C receiving reader and parser. The currently implemented
[receiving rules](receiving.md) describe the current transition: the old order
completion no longer posts stock, and the new LPN close use case is not yet
connected to Web or Mobile.

Warehouse quantities retain at most three decimal places. Read-only Web
quantity tables show three places; compact TSD displays show up to three.
Each SKU quantity group shows its base unit once, with an unknown unit shown
explicitly. Quantity inputs retain their existing validation and compact
editing behavior. Counts of lines and pallets are integers, and weight is a
separate kg measure. Order progress does not total quantities across SKUs;
TSD receiving and shipping show progress by line count instead. TSD picking
requires discrepancy confirmation when any shipping line differs from its plan;
differences on separate SKUs cannot cancel each other.

LPN tracks receiving provenance and initial putaway, not stored inventory.
