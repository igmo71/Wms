# Correctness and consistency findings

## F-02 — `Stage...` can save

**Priority:** P1.

`MobileCommandExecutor` suggests one save containing staged business state and
its receipt. However, receiving completion saves a fresh synchronization
assessment through `ReceivingOrderCommandService.cs:356`; picking completion
and shipping do the same at `ShippingOrderCommandService.cs:426`.

The checkpoint is intentional because a failed critical transition must retain
the newest synchronization state. The problem is the contract: the receipt is
atomic with final WMS state, not every persistence effect of the command.

Recommendation: preserve checkpoint behavior but make the two phases explicit.
Reserve `Stage...` for methods that do not save, or use a higher-level operation
name that states its checkpoint and final-commit ownership.

## F-03 — Web transfer creation bypasses conflict mapping

**Priority:** P1.

`InventoryTransferCommandService.CreateAsync` calls raw EF
`SaveChangesAsync` at line 34. Other transfer paths use
`ApplicationPersistence`, and Mobile uses the same conflict classifier through
its executor. Concurrent Web assignment of one transit location can therefore
throw instead of returning the intended business conflict.

Recommendation: use the existing classified persistence boundary. This is an
isolated fix, not a redesign.

## F-07 — Mobile details can be stale after synchronization

**Priority:** P1.

Receiving and shipping `ResolveDocumentAsync`/`GetDetailsAsync` endpoints load
Mobile details, then run synchronization, then combine the new assessment with
the earlier detail object. Synchronization may replace and save an unstarted
order's plan and metadata. WebApp synchronizes before querying current state.

Operational consequence: Mobile can display an old plan while reporting the
new synchronization result until the next reload. This contradicts the
governing rule that opening returns the fresh assessment with its details.

Recommendation: perform application-facing synchronization first and query
current details afterwards. Avoid repeating this composition in four endpoint
methods.

## F-08 — Endpoint prechecks can bypass receipt replay

**Priority:** P1.

The executor checks a receipt before invoking its staged action. But receiving
start/add-putaway resolve current order/location state before the executor;
inventory-count start shortcuts through a current draft/location; SKU scan
resolves the current active SKU first. These facts may change after a command
committed but before a retry arrives.

An already successful request can therefore be rejected before its receipt is
read, or a reused id with different input can follow an endpoint shortcut
instead of conflicting.

Recommendation: keep authentication, deterministic parsing, and transport
shape checks outside. Move mutable resolution and business preconditions
inside the receipt-protected action. Hash original semantic input or its
deterministically parsed id. Make inventory-count open-existing a query or
ensure the combined action consults a receipt first.

## F-09 — Web receiving completion has a partial save

**Priority:** P1.

`ReceivingOrderPages/InProcess.razor.cs:253` saves the selected receiving
location, then calls completion at line 262. Completion can fail at fresh 1C
verification, domain transition, posting, 1C update/status, or final save.
Mobile completion has no equivalent preliminary write.

Operational consequence: the user sees failed completion while a location
change remains committed.

Recommendation: either accept and stage the selected location in the completion
operation, or make location change an explicit independently saved action when
the operator changes it. Remove the implicit preliminary save.

## F-10 — Live legacy receiving API diverges

**Priority:** P1 until consumers are ruled out.

`Wms.WebApi/Program.cs:84` maps `/api/ReceivingOrder` endpoints. The legacy
start route calls standalone `SetInReceivingAsync` without assigning and
validating location in the combined operation, and maps all expected failures
to an empty 400. No repository caller exists, but external use is unknown.

Recommendation: confirm consumers. If none exist, remove the route group and
now-unused command path. Otherwise migrate it to the common operation and an
explicit error contract before removal.

## F-14 — Final posting can use inactive topology

**Priority:** P1 inventory correctness.

`InventoryPostingService.ValidateLocationsAsync` rejects missing locations,
folders, warehouse mismatch, and foreign locks. It does not reject an inactive
location, missing/inactive zone, or inactive warehouse. Empty assigned
receiving/shipping locations can be deactivated before completion, after which
posting can create positive inventory in them.

Location deactivation/folder conversion also does not advance
`OperationalRevision`; zone activation/type changes do not advance child
location revisions. A configuration change can therefore race with posting
outside the current guard.

Recommendation:

1. require active warehouse, location, and zone in common final-posting
   validation;
2. retain feature route-role validation at completion;
3. advance location revision for location configuration changes affecting
   eligibility and use classified saves;
4. advance affected child location revisions in the same save when a zone's
   activity/type or a warehouse's activity changes.

Warehouse import currently updates a detached `Warehouse` directly, so it
cannot detect the activity transition or advance child revisions. This needs a
focused import/application change; it should not be hidden in the validator.
The full policy and the reason route roles remain feature-owned are recorded in
`operational-location-policy.md`.
