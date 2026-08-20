# WMS roadmap

This roadmap records intentional MVP limitations and the conditions for moving
from a development stand to a real operational pilot. It is not a commitment to
implement every item before completing the current warehouse processes.

## Current MVP focus

- Finish and manually validate receiving, shipping/picking, and inventory count.
- Keep the direct application-service structure while the processes are still
  evolving.
- Use `ExternalChangeDetected` and logs to identify inbound 1C conflicts.
- Establish the mobile foundation and validate the first scanning-oriented
  vertical through intra-warehouse direct movement.

## Before an operational pilot

### External boundaries and operator identity

- Protect WMS operator commands with authentication and pass the authenticated
  user id to command services instead of the temporary fixed id in Web API.
- Reuse ASP.NET Core Identity for mobile token authentication, keep tokens in
  Android secure storage, and do not expose mobile self-registration.
- Define how pilot operator accounts are created and confirmed. The current web
  Identity setup requires confirmed accounts while its email sender is a no-op.
- Decide whether pilot authorization needs roles and warehouse assignments or
  whether every authenticated account is initially an operator for every
  warehouse.
- Define session lifetime and forced sign-out for a lost or retired device.
- Authenticate or otherwise verify 1C webhook/import callers before exposing
  those endpoints outside a trusted development network.
- Disable sensitive EF data logging outside development.

### 1C failure recovery

- Define an operator-visible recovery procedure for a partial WMS-to-1C
  transition: an external PATCH may succeed while Post or local SaveChanges
  fails.
- Decide whether a later stage needs persistent retry/outbox processing. Do not
  introduce it while a documented manual recovery flow remains sufficient.
- Define expected delivery semantics for 1C notifications. The current
  in-memory channel can lose queued notifications on process restart and has no
  retry queue.

### Inventory confidence

- Add focused integration tests for receiving, picking, shipping, and inventory
  count posting: balance deltas, turnover records, invalid transitions, and
  insufficient balance must be covered.
- Establish a clean, reproducible build path independent of files locked by a
  running IDE or local WebApp process.

## Mobile WMS delivery path

The accepted architecture and staged implementation are specified under
`specs/mobile-wms/`.

### Foundation

- Keep shared mobile API contracts in `Wms.Contracts`; reference them from
  `Wms.WebApi` and `Wms.Mobile` without exposing domain or persistence types.
- Add authenticated `/api/mobile/v1` endpoints, token refresh, `/me`, stable
  problem responses, and server-side current-user resolution.
- Add client request ids and atomic idempotency before the first mobile command
  that changes inventory.
- Keep the first mobile release online-only and distinguish unconfirmed network
  state from a server-confirmed physical action.

### Scanning and labels

- Validate Intent/Broadcast and keyboard-wedge behavior on the available Urovo
  DT50 without introducing Urovo-specific workflow or public names.
- Add a vendor-neutral scanning abstraction, source discovery/fallback, a
  diagnostic screen, and camera scanning with runtime permission.
- Implement contextual server resolvers for WMS storage-location QR and existing
  1C SKU barcodes.
- Prototype and physically verify a storage-location label containing
  `wms:location:v1:<guid>` plus readable warehouse, zone, and location names.
- Obtain the 1C document-barcode algorithm and control samples before mobile
  receiving or shipping workflows depend on document scanning.

### First vertical and expansion

- Deliver direct intra-warehouse movement from source-location scan through one
  idempotent confirmed movement and completion.
- Extend the same mobile workflow to transit-location pick and put after the
  direct-movement pilot.
- Specify receiving, putaway, picking/shipping, and inventory-count mobile
  workflows in the order justified by pilot feedback.

### Deferred mobile capabilities

- Design badge login separately. A badge containing only `ApplicationUser.Id`
  may identify an account but must not authenticate it without a PIN, revocable
  credential, or equivalent proof.
- Add roles and per-warehouse authorization when the pilot access model requires
  them.
- Add mass label generation/printing after the physical label prototype is
  accepted.
- Add offline command synchronization only after defining conflict and audit
  semantics for inventory changes.
- Expand and certify scanner profiles as additional TSD models enter the fleet.

## Process and integration backlog

- Implement manual batch import of receiving and shipping documents when its
  operator workflow is defined. The corresponding endpoints remain unexposed
  until then.
- Confirm 1C quantity semantics: `Количество` versus `КоличествоУпаковок`.
- Implement storage-location capacity display and enforcement using normalized
  SKU weight and volume after the missing-data policy is confirmed for the
  operational pilot.
- Confirm characteristic and packaging identity using real 1C catalog,
  document-line, and barcode-register examples before changing inventory keys.
- Confirm shipping table-section behavior for `Отгружать` / `НеОтгружать`, then
  implement line splitting only after that business decision.
- Implement recounts, reservations, and inventory tasks when their business
  rules are agreed.
- Continue validating and hardening the implemented intra-warehouse transfer
  backend according to `specs/intra-warehouse-transfers/spec.md`; expose it as
  the first mobile vertical according to `specs/mobile-wms/inventory-transfer.md`.

## Technical maintenance

- Update or replace the vulnerable `Microsoft.OpenApi` dependency in
  `Wms.WebApi`.
- Review the .NET preview SDK and package versions before production rollout.
