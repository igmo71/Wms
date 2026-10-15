# Operational-location policy prototype

This is a paper API, not an accepted implementation.

The common checks have two genuinely different contexts. They do not require a
general validator or a boolean option bag.

```text
RequireForSelectionAsync(
    dbContext,
    locationId,
    warehouseId,
    OperationalLocationUse,
    cancellationToken)
    -> active warehouse + active location + active zone
       + non-folder + expected zone role + unlocked
       -> OperationResult<StorageLocation>

RequireForPostingAsync(
    dbContext,
    locationId,
    warehouseId,
    acceptedLockOwner,
    cancellationToken)
    -> active topology + non-folder + same warehouse
       + no lock except an explicitly accepted owner
       -> OperationResult<StorageLocation>
```

`OperationalLocationUse` is a small closed set: receiving, shipping, ordinary
storage, and transit. It carries the expected zone type and stable message
vocabulary. `acceptedLockOwner` is absent for ordinary movements and explicitly
identifies the inventory count when posting count differences.

## Call-site check

| Call site | Common policy | Rule retained by feature |
| --- | --- | --- |
| Start receiving | receiving selection | synchronization and transition |
| Add putaway | storage destination and receiving source | different locations, allocation, source balance |
| Complete putaway | posting for every route | completeness and storage route roles |
| Start picking | shipping selection | synchronization and transition |
| Add picking | storage source and shipping destination | allocation and source balance with drafts |
| Complete picking | posting for every route | storage-to-shipping roles and completeness |
| Ship | posting plus shipping role | status and issue creation |
| Create count | storage selection | expected snapshot and owned lock creation |
| Post count | posting accepting this count's lock | unchanged expectations and completeness |
| Create transit transfer | transit selection | empty and exclusive ownership |
| Transfer movement | selection according to mode | route, SKU, quantity, lifecycle |

Final route-role validation remains necessary because zone roles can change
after a draft movement is created.

## Final route decision

The common posting policy owns only facts that are invariant across every
inventory movement: active warehouse, active location and zone, non-folder,
same warehouse, and lock ownership. The feature completing the operation owns
the final route-role check.

This is intentional rather than temporary duplication. `ReceivingOrder`
movements cover both external-to-receiving and receiving-to-storage routes;
`ShippingOrder` covers storage-to-shipping, shipping-to-external, and rollback
from shipping to storage. `RecorderType` and movement direction therefore do
not identify the allowed route without importing workflow and compensation
knowledge into inventory posting.

Do not add a generic movement-route policy now. Extract a feature-named route
policy later only if one feature accumulates a sizeable repeated rule set.

## Configuration concurrency decision

Early selection and final validation are both required, but are insufficient
if topology changes between validation and commit. Eligibility-changing
configuration writes must advance the affected location revisions in the same
database save:

- a location activation, deactivation, folder conversion, zone assignment, or
  warehouse assignment advances that location's `OperationalRevision`;
- a zone activation, deactivation, type change, or warehouse move advances the
  revisions of its child locations;
- a warehouse activation/deactivation transition advances the revisions of
  its locations.

This makes an already loaded posting conflict with a concurrent configuration
change through the existing location concurrency token. It avoids a new
topology-wide token and keeps the posting service independent of configuration
commands. Bulk advancement for a zone or warehouse is acceptable because
these are rare administrative/import operations; it must use classified
persistence and remain in the same transaction as the topology change.

## Rejection criteria

Reject or narrow the policy if implementation requires:

- `MustBeActive`, `MustBeEmpty`, `AllowLocked`, or similar independent flags;
- knowledge of order or transfer statuses;
- feature-specific allocation or quantity calculations;
- ownership of `SaveChangesAsync`;
- exception-based expected business flow;
- a public interface without a real project boundary.
