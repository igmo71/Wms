# WMS roadmap

This roadmap records intentional MVP limitations and the conditions for moving
from a development stand to a real operational pilot. It is not a commitment to
implement every item before completing the current warehouse processes.

## Current MVP focus

- Finish and manually validate receiving, shipping/picking, and inventory count.
- Keep the direct application-service structure while the processes are still
  evolving.
- Use `ExternalChangeDetected` and logs to identify inbound 1C conflicts.

## Before an operational pilot

### External boundaries and operator identity

- Protect WMS operator commands with authentication and pass the authenticated
  user id to command services instead of the temporary fixed id in Web API.
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

## Process and integration backlog

- Implement manual batch import of receiving and shipping documents when its
  operator workflow is defined. The corresponding endpoints remain unexposed
  until then.
- Confirm 1C quantity semantics: `Количество` versus `КоличествоУпаковок`.
- Resolve the referenced 1C base, weight, and volume units before importing SKU
  volume or treating an existing weight ratio as kilograms. Then implement the
  staged SKU physical-property and capacity rules from
  `specs/sku-physical-properties/spec.md`.
- Confirm characteristic and packaging identity using real 1C catalog,
  document-line, and barcode-register examples before changing inventory keys.
- Confirm shipping table-section behavior for `Отгружать` / `НеОтгружать`, then
  implement line splitting only after that business decision.
- Implement recounts, reservations, and inventory tasks when their business
  rules are agreed.

## Technical maintenance

- Update or replace the vulnerable `Microsoft.OpenApi` dependency in
  `Wms.WebApi`.
- Review the .NET preview SDK and package versions before production rollout.
