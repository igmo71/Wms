# Current 1C integration

## Warehouse cutover export

The cutover XLSX reader accepts one worksheet with the ten known 1C columns,
bounded to 20 MB and 100,000 data rows. It returns typed values, original row
numbers and parse issues without changing WMS data. WebApp uses this reader for
the topology preview and confirmation. After topology and SKU catalog loading,
the separate opening-stock stage requires the same workbook hash and a declared
snapshot time. It resolves source SKU GUIDs against the local catalog and
requires the source unit to match the SKU's canonical unit; it does not infer
package conversion or skip unknown stock. The exact source unit text may match
the base unit's 1C display description, full name or abbreviation; all three
labels belong to the same base-unit record. The application command rebuilds
the preview inside a serializable transaction and posts accepted rows through
WMS history. The cutover import reads 1C data and writes no stock result back
to 1C.

## Catalogs and receiving sources

Manual catalog imports are available from the WebApp catalog lists and the
`GET /api/1c/.../import` endpoints. Their role rule is owned by
[identity.md](identity.md); notification endpoints and background imports
remain the automatic integration path. SKU imports preserve the WMS-owned
expiration-tracking flag described in [receiving.md](receiving.md).

The SKU barcode register has a separate import rule. Both single-SKU
notifications and full imports read the complete requested 1C set before
changing stored barcodes, then reconcile that set in one database transaction.
Existing barcode rows are treated as 1C-owned. A 1C-owned pair absent from a
successful source response is removed; a WMS-owned pair absent from 1C remains.
When 1C supplies the same SKU and barcode as a WMS-owned pair, that pair becomes
1C-owned without a second row. The original manual-addition audit remains
after promotion or later source removal. The same barcode value may be
imported for different SKUs; barcode
lookup must not silently choose between them. A failed source read or save
retains the previously stored set. Barcode imports do not write to 1C.
The barcode catalog list can identify values linked to more than one distinct
visible SKU. A Manager may remove a current WMS-owned pair with an audit event;
the command rechecks ownership and rejects a pair already promoted to 1C.
Imported pairs remain under 1C control and cannot be removed locally.
Removing a local pair changes future barcode lookup only; receiving and
inventory history remain unchanged.

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

Receiving and putaway do not write status, item tables, or comments to 1C.
WMS owns its receiving location, facts, cycles, LPNs, movements, users, and
timestamps. Current receiving has no direct line-comment command. Historical
receiving comments remain WMS-owned annotations, not warehouse facts.

## Remaining older processes

Shipping notifications now use the replacement `Wms.Integration` reader and
`SynchronizeShippingSource` use case. They read both item tables and preserve
the current raw document JSON on the 1C source projection, including rows
needed for later reconciliation and PATCH construction. A failed read or
unsupported quantity records an error without turning it into a zero plan.
For a linked draft assembly, a valid same-warehouse source updates its plan;
after the first start attempt, source changes update only the 1C projection.
Starting a picking assembly freezes its source set and WMS plan before any 1C
write. The application then checks each source, PATCHes its status to `КОтбору`
and calls `/Post` outside the warehouse transaction. A failed document leaves the
assembly in preparation; confirmed documents are skipped on retry. Before PATCH,
WMS records an intent for that source. A retry after an uncertain response reads
1C again and can continue with POST when the status is already `КОтбору`;
without WMS intent, that status requires investigation. A per-assembly lease
prevents overlapping start attempts. The picking screens still use the legacy
workflow.

For a started assembly, a Manager may change the WMS plan of one linked 1C
document. The command checks the assembly revision and requires the source JSON
shown during review to match the current stored projection. It records the
reviewed source fingerprint, reason and each changed SKU quantity in audit;
the source projection itself is not edited. A later source check still applies
before closure. Allocation preview uses the approved per-document WMS plan and
the physically confirmed aggregate fact: a single document or a complete
multi-document SKU allocates automatically; a partial shared SKU needs an
explicit Manager split within every document's plan. The allocation becomes
warehouse history only when closure posts it.

The replacement closure check fetches every linked 1C document again, updates
its current projection, and compares it with the stored start snapshot. An
unreadable source, missing posting, changed warehouse or warehouse operation,
unexpected status or deletion mark blocks both roles. A changed receiver or
item plan is shown for Manager review; queue, planned date and direction do
not block closure. The check also compares the aggregate WMS plan with picked
fact, flags known expired groups and previews the document allocation. A
failed GET leaves the former projection visible as historical data, not as a
successful current check. Closure still has to recheck its database state
under the command revision before posting.

The replacement close command performs the fresh check before opening its
stock transaction, then verifies the same source JSON and assembly revision
inside that transaction. It stores an immutable per-document target decision:
the approved WMS plan, allocated SKU quantities, intended `Shipped` status and
reviewed source JSON. The Integration target builder will turn that decision
into concrete 1C item rows for PATCH. The stock commit does not call 1C.

Cancelling an unclosed picking assembly does not call 1C inside the warehouse
transaction. Source links with no confirmed or intended start PATCH are
released immediately. Sources with a confirmed start or an uncertain PATCH
intent remain linked to the assembly in `CancellationPending`, preventing a
new assembly from taking them. Their return to `Prepared` requires a separate
safe 1C exchange, which is not wired yet; until then, the links remain pending.
The cancellation request and its reason are audited.

The older picking and shipping screens support pull-to-refresh of their source
assessment. Picking completion and final shipping fetch a fresh checkpoint
before effects. An unresolved decision, blocking assessment, or technical
failure stops the affected transition. Shipping accepts an exact source state
or repeat-safe outbound target. Its item-table target, status, and posting
calls are repeatable.

Notification delivery uses an in-memory channel and can be lost on restart.
WMS-to-1C multi-step transitions are not atomic across systems. The operational
shipping adapter still belongs to the legacy workflow. Its warehouse boundaries
and date limitations are described in [other workflows](other-workflows.md).
