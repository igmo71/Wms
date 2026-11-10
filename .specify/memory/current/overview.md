# Current WMS state

This directory describes implemented behavior. The code and database schema
remain the source of truth for implementation details. Completed feature
specifications preserve decisions made during delivery, not the current rules.

Read only the relevant area: [identity](identity.md), [inventory](inventory.md), [receiving](receiving.md), [other warehouse workflows](other-workflows.md), [1C integration](integration.md), and [clients](clients.md). Current technical structure and conventions are in [architecture](../architecture.md). Deferred work is tracked in [GitHub Issues](https://github.com/igmo71/Wms/issues).

## Product boundary

WMS is a warehouse-management MVP. 1C owns catalogs and source receiving and
shipping documents. WMS imports them, executes warehouse work, and owns
operational inventory by storage location. Outbound exchange with 1C depends on
the workflow, as described in [integration](integration.md).

Receiving and putaway use replacement workflows. Older picking and
shipping, direct and transit intra-warehouse transfers, and location inventory
counts still exist but await replacement for dated and signed stock; they are
not established as production-ready with the new receiving inventory. Inventory
history, employee-performance reporting, topology administration, synchronized
catalogs, and WMS user administration also exist.

The supported interactive clients are `Wms.WebApp` and Android `Wms.Mobile`.
`Wms.WebApi` exposes authenticated Mobile V1 plus separate 1C integration
endpoints.

The [receiving rules](receiving.md) post stock on LPN close and partial
putaway. Completing an order only records the cycle decision. The project map
and implementation boundaries are in [architecture](../architecture.md).

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

The replacement outbound workflow currently has the WMS picking assembly model
and schema, reservations, shortages and replenishment, shared availability
queries, and dated inventory posting primitives. Its application services can
list and read assemblies, and explicitly transfer an already started assembly
to another Operator. The owner and revision change together; source plans,
reservations and the actors of earlier physical records are retained. The
picking and shipping screens still use the legacy scenario. The legacy `Wms`
project is being eliminated as its scenarios are replaced in the new projects.
