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
picking and shipping button on the Mobile home screen still opens the legacy
scenario; replacement picking pages and API routes are registered but have not
replaced that entry point. The legacy `Wms`
project is being eliminated as its scenarios are replaced in the new projects.
The new outbound fact and per-document exchange tables are prepared. New
application commands can scan picking stock, hold and return a direct-pick
pallet, reserve and correct picked quantities, and report and resolve shortages.
A Manager can revise a started WMS plan against a reviewed 1C snapshot; the
allocation preview separates automatic from unresolved shared-SKU splits.
The replacement closing command can post picked stock and prepare one exchange
target per 1C document. The replacement Mobile picking screens execute this work;
the Web Manager can review WMS plan, physical fact, current 1C projections and
audit, change a started plan, distribute a shared-SKU shortage, resolve physical
shortage reports, request cancellation and approve a warehouse close. The actual
1C closing exchange still awaits implementation.
Operator SKU information in the replacement clients reads available stock by
warehouse, cell and expiration group without changing balances.
The replacement cancellation command can stop unclosed work after physical
returns, cancel unstarted replenishments and retain linked 1C documents while
their status restoration is pending.
Replenishment can now report and separately block source shortages, transfer
the found stock, and finish a source with a return or empty-cell scan. Closing
a picking assembly cancels orders without a source scan while started tasks
continue; completing the last started task can finish a pending cancellation
once the linked 1C sources have been released.
A Manager can also correct a posted replenishment step in Web. The correction
posts a separate dated movement linked to that step and updates task progress;
the original transfer remains in history.
Mobile V1 now has replacement replenishment routes and a client for proposals,
task queue and detail, ordering and physical commands. The replenishment
screens and main navigation switch still await implementation.
