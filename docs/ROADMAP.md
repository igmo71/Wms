# WMS roadmap

This roadmap contains unfinished work only. Completed behavior belongs in
[`PROJECT_CONTEXT.md`](PROJECT_CONTEXT.md), and detailed accepted rules belong
in `specs/`.

## Current MVP focus

- Resume mobile development from the contracts and authenticated API
  foundation, then deliver direct intra-warehouse movement as the first
  scanning-oriented vertical.
- Manually validate and harden the implemented web workflows after the
  rich-model, authorization, storage-topology, and SKU-import refactoring.
- Prove that existing databases can be migrated safely to required zone and
  storage-location codes before an operational pilot.
- Continue capacity work from stored geometry/weight limits and normalized SKU
  weight/volume after the missing-data policy is accepted.

## Before an operational pilot

### Security and external boundaries

- Finish the stable problem-code mapping for upcoming mobile business
  endpoints; identity failures already use mobile V1 wire errors.
- Authenticate or otherwise verify 1C webhook/import callers before exposing
  their endpoints outside a trusted network.
- Define session lifetime, refresh, and operational revocation for a lost or
  retired device.
- Disable sensitive EF data logging outside development.

Web roles, administrator-managed confirmed accounts, display names, and initial
administrator bootstrap are implemented. Fine-grained operation permissions
and per-warehouse assignments remain deferred until a pilot needs them.

### Database transition and inventory confidence

- Replace or explicitly validate the topology migration strategy that adds
  required unique zone/location codes to databases containing existing rows.
- Add focused integration tests for receiving, putaway, picking, shipping,
  inventory count, and transfer posting: balance deltas, turnover records,
  invalid transitions, folder rejection, and insufficient balance.
- Add authorization boundary tests for web roles and the authenticated mobile
  API.
- Establish a clean reproducible build and migration-check path independent of
  files locked by an IDE or running WebApp process.

### 1C failure recovery

- Define an operator-visible recovery procedure when a WMS-to-1C PATCH or post
  succeeds but a later external or local save step fails.
- Decide whether persistent retry/outbox processing is justified after the
  manual recovery procedure is exercised.
- Define expected notification delivery semantics. The current in-memory
  channel can lose queued notifications on restart and has no retry queue.

## Mobile WMS — active delivery path

The accepted architecture and detailed stages are under `specs/mobile-wms/`.
The mobile identity slice now shares V1 contracts, exposes bearer login,
refresh, and `/me`, stores tokens in Android secure storage, and derives server
users from claims. Scanner, resolver, idempotency store, and business workflow
are still absent; the packaged `mobile-api.json` address must be replaced with
the reachable trusted HTTPS endpoint for a physical device.

Delivery order:

1. Validate login, refresh, session restoration, and `/me` against a reachable
   trusted HTTPS endpoint on the emulator and pilot device.
2. Build vendor-neutral diagnostic scanning on the chosen TSD and a smartphone.
3. Add server resolvers for `WMSL:{storage-location-guid-N}` and imported 1C SKU
   barcodes, then physically verify a location label.
4. Add atomic request idempotency and deliver direct intra-warehouse movement,
   then the transit-location workflow.
5. Specify and deliver receiving, putaway, picking/shipping, and count flows in
   the order justified by pilot feedback.

External inputs still required: 1C document-barcode control examples, verified
scanner modes/action/extras for pilot devices, camera-library selection,
printer/label constraints, and a safe badge-login decision if badge login
remains desirable.

Offline inventory commands, mass label printing, fleet management, and broader
device certification remain separate later epics.

## Process and integration backlog

- Confirm real 1C quantity semantics for `Количество` and
  `КоличествоУпаковок`, then implement packaging conversion in one shared,
  directionally tested function.
- Confirm characteristic identity from real catalog, document-line, and
  barcode-register examples before changing the SKU/inventory key.
- Display occupied/free location weight and volume, show incomplete capacity
  separately from zero, and block known excesses during putaway and direct
  movement.
- Confirm shipping table-section behavior for `Отгружать` and
  `НеОтгружать`; add line splitting only after the business decision.
- Implement manual batch import of receiving and shipping documents when its
  operator workflow is defined.
- Add recounts, reservations, inventory tasks, or assignment queues only after
  their business rules and pilot need are agreed.

## Technical maintenance

- Update or replace the transitive vulnerable `Microsoft.OpenApi` 2.0.0
  dependency reported by NU1903 in `Wms.WebApi`.
- Review .NET SDK and package versions before production rollout.
- Review non-atomic multi-step Identity role updates and concurrent protection
  of the last active administrator before relying on them under multiple
  administrators.
