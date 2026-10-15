# Immediate client-visible correctness fixes

Status: frozen reference. Completed 2026-09-07.

## Outcome

Bring two isolated paths into conformance with existing WMS rules:

- Web transfer creation returns the classified active-transit-location
  conflict instead of leaking an EF exception;
- Mobile receiving and shipping open operations return order details queried
  after a successful synchronization, so the details and assessment describe
  the same persisted state.

## Scope

- Route the final save in `InventoryTransferCommandService.CreateAsync`
  through the existing `ApplicationPersistence` conflict classifier.
- After a successful receiving or shipping synchronization in both Mobile
  document-resolution and detail endpoints, query current details again before
  mapping the response.
- Preserve the current terminal-receiving shortcut and current behavior when
  synchronization itself fails.
- Keep endpoint routes, response contracts, business messages, persistence
  model, and 1C protocol behavior unchanged.

## Out of scope

- Application-owned 1C synchronization ports or host-boundary refactoring.
- Mobile receipt-boundary changes.
- Operational-location policy or topology concurrency changes.
- Moving or splitting endpoint, client, contract, or command-service files.
- New automated tests.

## Acceptance criteria

- Concurrent Web creation using an already claimed transit location reaches
  the existing classified `OperationResult.Conflict` path.
- A successful receiving synchronization that updates an unstarted plan is
  followed by a new receiving-details query before response mapping.
- A successful shipping synchronization that updates an unstarted plan is
  followed by a new shipping-details query before response mapping.
- Both resolve-document and direct-details Mobile endpoints follow this order.
- A failed synchronization still returns the already resolved details with a
  verification error, preserving current degraded-open behavior.
- Relevant projects build and `git diff --check` passes; no tests are created.

## Verification

- Inspect all four Mobile endpoint paths for query/synchronization ordering.
- Inspect transfer creation for use of the named conflict classifier.
- Build `Wms` and `Wms.WebApi`.
- Run `git diff --check` and review the focused diff.

## Result

- Web transfer creation now uses `ApplicationPersistence.SaveChangesAsync` and
  returns its classified conflict.
- Both receiving endpoints and both shipping endpoints requery current details
  after a successful synchronization before mapping the response.
- Synchronization failures retain the previously resolved details and the
  verification-error response.
- `Wms` and `Wms.WebApi` build successfully. WebApi retains the previously
  known `NU1903` warning for `Microsoft.OpenApi 2.0.0`; no new warning was
  introduced.
- `git diff --check` passes and no automated tests were created.
