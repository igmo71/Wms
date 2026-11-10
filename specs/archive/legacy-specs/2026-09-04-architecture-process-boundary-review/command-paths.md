# Command-path evidence

## Baseline

`Wms` currently contains domain, application, EF persistence, and concrete 1C
integration in one assembly. Both hosts reference it. WebApp calls public
application commands directly. Mobile V1 wraps the same internal `Stage...`
operations with `MobileCommandExecutor`, which saves a persisted receipt with
the final local business change.

```text
WebApp -> public command -> Stage... -> local save
Mobile endpoint -> Mobile command -> receipt lookup -> same Stage...
                -> business change + receipt local save
```

## Entry-path matrix

| Process | WebApp entry | Mobile V1 entry | Shared operation | 1C |
| --- | --- | --- | --- | --- |
| Start receiving | `ReceivingOrderPages/Details.razor.cs:203` | `MobileReceivingOrderEndpoints.cs:159` | `ReceivingOrderCommandService.StageStartReceivingAsync` | status PATCH and document POST |
| Complete receiving | `ReceivingOrderPages/InProcess.razor.cs:253-262` | `MobileReceivingOrderEndpoints.cs:272` | `StageSetReceivedAsync`, but Web first saves location separately | fresh read, optional item PATCH, status PATCH and POST |
| Putaway | `ReceivingOrderPages/Putaway.razor.cs:134-182` | `MobileReceivingOrderEndpoints.cs:287-378` | `PutawayCommandService.Stage...` | none |
| Start picking | `ShippingOrderPages/Details.razor.cs:197` | `MobileShippingOrderEndpoints.cs:144` | `ShippingOrderCommandService.StageStartPickingAsync` | status PATCH and POST |
| Complete picking | `ShippingOrderPages/Picking.razor.cs:297` | `MobileShippingOrderEndpoints.cs:227` | `StageSetReadyForShipmentAsync` | fresh read, item read/PATCH, status PATCH and POST |
| Ship | `ShippingOrderPages/Details.razor.cs:278` | `MobileShippingOrderEndpoints.cs:249` | `StageSetShippedAsync` | fresh read, status PATCH and POST |
| Shipping rollback | two shipping WebApp pages | absent | `ShippingOrderCommandService.RollbackAsync` | none |
| Inventory count | count index/details pages | `MobileInventoryCountEndpoints.cs:102-259` | `InventoryCountCommandService.Stage...` | none |
| Transfer | `InventoryTransferPages/Work.razor.cs:200-275` | `MobileInventoryTransferEndpoints.cs:128-483` | transfer `Stage...`, except Web deletion | none |

## Side-effect and save ordering

Each save is its own EF unit of work. No explicit transaction spans a 1C call
and database save or the synchronization checkpoint and final command save.

| Action | Confirmed order | Final local boundary |
| --- | --- | --- |
| Synchronize | fetch 1C snapshot; reconcile/create; save when state changed | one application save, no receipt |
| Acknowledge | fetch fresh snapshot; compare fingerprint; reconcile or acknowledge | one application save, no receipt |
| Start receiving/picking | load and validate local order/location; mutate; PATCH status; POST document | Web saves order; Mobile saves order and receipt |
| Edit receiving fact | load order/items; domain edit | one Web save or edit plus receipt |
| Complete receiving | Web preliminary location save; fresh 1C read; save assessment checkpoint; stage transition and inventory; update 1C items if needed; set/post 1C status | separate final Web save or completion plus receipt |
| Putaway | load order/drafts; validate; domain change; completion stages inventory posting | one Web save or change plus receipt |
| Picking drafts | load order/drafts; domain change; validate source and balance | one Web save; Mobile add/delete plus receipt |
| Complete picking | fresh 1C read; save assessment checkpoint; stage transition/posting; read 1C again; validate/PATCH items; set/post status | separate final Web save or completion plus receipt |
| Ship | fresh 1C read; save assessment checkpoint; validate; stage issue; set/post 1C status | separate final Web save or completion plus receipt |
| Rollback | load cycle movements; domain rollback; remove drafts; stage compensations | one Web save |
| Inventory count | stage aggregate, expected facts, lock, differences, inventory, or release | one Web save or change plus receipt |
| Transfer movement | derive/validate route; create movement; immediately stage posting | one Web save or movement plus receipt |
| Transfer lifecycle | validate/apply create, complete, or delete | one save; Web create uniquely uses raw EF save |

Critical order commands deliberately persist a fresh synchronization
assessment before continuing. This preserves a newly detected issue when the
transition is blocked, but the logical command is two-phase and a Mobile
receipt covers only its final phase.

## Retry ownership

- `OneCClient` performs one HTTP attempt and maps protocol, transport, and JSON
  failures to an operation failure; it has no automatic retry policy.
- Status changes are exact PATCH followed by document POST. Current governing
  decisions treat exact target status and posting as repeatable.
- Receiving and shipping comparers recognize their exact WMS-produced targets
  before repeating a completion after external success and local failure.
- Shipping item-table update explicitly reads and recognizes its exact target
  before patching; receiving repeats its exact item-table PATCH.
- Web recovery is an operator retry. Mobile retains its request id for an
  uncertain transport/server response and depends on the receipt or repeat-safe
  external target.
- There is no outbox or distributed transaction; operator recovery for partial
  1C success remains a separate pilot prerequisite.

## Concurrency coverage

| State | Mechanism | Consumers |
| --- | --- | --- |
| Receiving/shipping workflow | `OperationalRevision` token | reconciliation, facts, transitions, drafts, rollback |
| Location operational state | `OperationalRevision` token | posting and lock changes |
| Balances | row version plus unique warehouse/location/SKU key | all posting |
| Inventory count | aggregate row version, location revision, lock PK, unique count/SKU | create/edit/delete/post |
| Transfer | aggregate row version, active-transit unique index, movement-line unique index | create/move/complete |

`PersistenceConflictClassifier` translates recognized conflicts. Raw Web
transfer creation is the confirmed exception. Location and zone configuration
changes that affect eligibility do not yet participate consistently in the
location operational revision, as detailed in F-14.
