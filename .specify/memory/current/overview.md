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

WMS supports receiving, putaway, picking with shipment at assembly closure,
replenishment and location inventory counts. The current direct and transit
[transfer workflows](other-workflows.md#intra-warehouse-transfer) use undated
stock. WMS also provides stock history, employee-performance reports, topology
administration, synchronized catalogs, and user administration.

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
TSD receiving shows progress by line count. TSD picking shows plan, reserved
route availability and confirmed fact, with separate instructions and
warnings for the route.
See [picking clients](clients.md#mobile-picking).

LPN tracks receiving provenance and initial putaway, not stored inventory.

Outbound work uses WMS picking assemblies linked to 1C shipping documents.
Mobile opens separate picking and replenishment workplaces; Web supports
Manager review and decisions. The warehouse ships as part of closing the
assembly with «Закрыть и отгрузить». Picking records physical fact and
reservations for the single stock posting at closure. Replenishment posts
dated transfers when their destinations are confirmed. The close action starts
or retries per-document 1C exchange. Cancellation keeps started replenishment
and unresolved source restoration visible until they finish. `ShippingOrder`
is the imported 1C source projection.

The detailed stock rules are in [inventory](inventory.md), source and exchange
rules in [integration](integration.md), and workplaces in [clients](clients.md).
The legacy `Wms` project still hosts unrelated processes while they are being
replaced; its remaining boundary is described in [architecture](../architecture.md).
