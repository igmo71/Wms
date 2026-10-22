# Current 1C integration

## Catalogs and receiving sources

Manual catalog imports are available from the WebApp catalog lists and the
`GET /api/1c/.../import` endpoints. Their role rule is owned by
[identity.md](identity.md); notification endpoints and background imports
remain the automatic integration path. SKU imports preserve the WMS-owned
expiration-tracking flag described in [receiving.md](receiving.md).

The receiving reader fetches the current 1C document by `Ref_Key` and returns
its JSON response. A successful check stores a new immutable source version
only when content changes. An unchanged response updates the last successful
check time and rebuilds line projections after catalog changes. Source lines
remain separate: unsupported quantities and unknown SKUs retain reasons
without hiding valid quantities of other SKUs. A failed read or unreadable
response records the attempt error without replacing the last successful
version. For linked WMS orders, successful checks recalculate only the current
plan as described in [receiving.md](receiving.md).
Linked-source checks also enforce the order's stored sender GUID. A changed or
invalid sender is recorded as a failed check without replacing the accepted
version or plan.
The 1C sender type `StandardODATA.Catalog_Партнеры` denotes a reference to the
partner catalog. Receiving views resolve its reference to the locally imported
partner name when available; the source snapshot retains the original ID and
type.

The 1C receiving notification checks and stores the source document. It does
not create a WMS receiving order; a Web Manager or Mobile receiving worker
creates that order explicitly from selected sources. Web Managers may check all
sources of one WMS order manually.
Manual checks record their outcome in audit.

New receiving and putaway do not write status, item tables, or comments to 1C.
WMS owns its receiving location, facts, cycles, LPNs, movements, users, and
timestamps. Current receiving has no direct line-comment command. Historical
receiving comments remain WMS-owned annotations, not warehouse facts.

## Remaining older processes

The older picking and shipping screens support pull-to-refresh of their source
assessment. Picking completion and final shipping fetch a fresh checkpoint
before effects. An unresolved decision, blocking assessment, or technical
failure stops the affected transition. Shipping accepts an exact source state
or repeat-safe outbound target. Its item-table target, status, and posting
calls are repeatable.

Notification delivery uses an in-memory channel and can be lost on restart.
WMS-to-1C multi-step transitions are not atomic across systems; the shipping
recovery procedure remains in [the backlog](../backlog.md). Replacement of
picking, shipping, transfers, and expanded inventory is also deferred there;
their old flows are not established as ready for production with dated stock.
