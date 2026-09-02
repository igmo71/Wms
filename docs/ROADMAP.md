# WMS roadmap

This roadmap contains unfinished work only. Completed behavior belongs in
[`PROJECT_CONTEXT.md`](PROJECT_CONTEXT.md), and detailed accepted decisions
belong in `specs/`.

Only **Next delivery** is recommended immediate work. Later sections are ordered
by dependency, not by a promised release date.

## Delivery sequence

1. **Order synchronization decisions:** show meaningful differences between
   WMS and fresh 1C orders and separate operator decisions from blocking
   conflicts.
2. **Standalone deployment baseline:** reproducible launch outside Visual
   Studio, trusted HTTPS, migration validation, and Android connectivity.
3. **Pilot prerequisites:** security boundaries, inventory confidence, and an
   operator recovery procedure for partial 1C failures.
4. **Pilot rehearsal:** one documented end-to-end run after its prerequisites
   exist.
5. **Product increments:** capacity and optional processes whose inputs and
   business need are confirmed.
6. **Production maintenance:** dependency, diagnostics, and administrative
   concurrency work that does not block staging.

## Next delivery — order synchronization decisions

### Outcome

An operator can understand every detected difference between the local WMS
order and its current 1C document. Safe continuation always requires an
explicit, audited decision; changes capable of corrupting warehouse facts
remain blocked.

### Work

1. Replace the current undifferentiated conflict result with a structured list
   of changed fields and lines.
2. Use two levels:
   - **Requires operator decision** for changed comments, queues, dates,
     directions, non-final compatible statuses, and other differences that do
     not change warehouse quantities or identity;
   - **Blocking** for changed quantities, SKU or line composition, warehouse,
     deletion, incompatible warehouse operation, posting, or final status.
3. Fetch the fresh 1C document when details are opened and immediately before
   an allowed final operation. Do not persist a second full 1C snapshot.
4. Show detailed differences and the audited decision in WebApp. Show a short
   severity summary in Mobile and direct blocking cases to WebApp or external
   resolution.
5. Store only the marker, level, detection time, and decision audit or checked
   state fingerprint required to invalidate an acknowledgement after another
   1C change.
6. Clear `ExternalChangeDetected` when a fresh 1C document matches WMS again;
   the current early `Unchanged` result leaves a previously set marker stale.
7. Treat an already posted or final 1C order as blocking unless existing
   repeat-safe recovery logic proves that it is the exact result of the WMS
   operation being resumed.

## Standalone deployment baseline

### Outcome

The current solution can be published and launched independently of Visual
Studio, apply migrations, expose WebApp and Mobile V1 through trusted HTTPS,
and connect the Android client without certificate-validation workarounds.
Connection addresses, credentials, logging detail, and sensitive-data logging
may remain the same as in the current development environment for this stage.

### Work

1. Record the target host, DNS names, hosting or reverse-proxy model, database,
   configuration source, and Android network path.
2. Define certificate issuance, trust chain, installation, renewal, and the
   exact WebApp and WebApi URLs.
3. Establish reproducible restore, build, publish, deployment, and migration
   commands independent of IDE or running-process file locks.
4. Validate required zone and storage-location code migrations against a safe
   copy of an existing database.
5. Keep unauthenticated 1C endpoints inside a trusted network boundary or
   disable external access to them until caller verification is implemented.
6. Deploy the applications and manually exercise login plus one short happy
   path for transfer, inventory count, receiving/putaway, and picking/shipping.

### Done when

- WebApp and Mobile V1 are reachable through trusted HTTPS;
- Android connects without bypassing certificate validation;
- fresh and repeated standalone deployments are documented and reproducible;
- database migration has a verified backup and restore procedure;
- all four accepted mobile processes reach the staging API;
- unverified 1C endpoints are not publicly exposed.

## Pilot prerequisites

### Security and device access

- Authenticate or otherwise verify 1C webhook and import callers before their
  endpoints leave a trusted network.
- Define session lifetime, refresh, and operational revocation for a lost or
  retired device.
- Keep fine-grained operation permissions and per-warehouse assignments
  deferred until the pilot demonstrates a concrete need.

### Inventory and authorization confidence

- Manually validate the implemented WebApp workflows after the completed
  domain, authorization, topology, and catalog refactoring.
- Add focused integration tests for receiving, putaway, picking, shipping,
  inventory count, and transfer posting: balance deltas, turnovers, invalid
  transitions, folder rejection, and insufficient balance.
- Add authorization-boundary tests for web roles and authenticated Mobile V1.

### Operator recovery for partial 1C failures

- Define what an operator does when a WMS-to-1C PATCH or post succeeds but a
  later external or local save step fails.
- Record the evidence needed to distinguish safe repeat, already-applied
  success, and manual escalation.
- Exercise the procedure under representative staging failures.

## Pilot rehearsal

Run one documented end-to-end rehearsal only after the staging baseline and
pilot prerequisites are complete. It must cover backup and restore, migration,
authentication, the four warehouse processes, 1C exchange, an interrupted
command, and the operator recovery procedure.

## 1C resilience after synchronization visibility

- Persist the latest conflicting 1C revision of an active receiving order.
- Show header and line differences in WebApp and Mobile and block work while a
  material conflict is unresolved.
- Add audited resolution commands. Automatically rebase only changes that
  preserve recorded WMS facts; route changed worked lines and post-putaway
  conflicts to a responsible user.
- Define notification delivery semantics. The current in-memory channel can
  lose queued notifications on restart and has no retry queue.
- Decide whether persistent retry or an outbox is justified from evidence
  gathered during recovery-procedure exercises.

## Product increments

### Source-data fidelity

- Before supporting packaging, capture real line and catalog examples and
  define the relationship between `Количество`, `КоличествоУпаковок`, and the
  packaging coefficient.
- Confirm characteristic identity from real catalog, document-line, and
  barcode-register examples before changing the SKU or inventory key.

### Capacity

- Display occupied and free location weight and volume.
- Show incomplete capacity separately from numeric zero.
- Block known excesses during putaway and direct movement after the
  missing-data policy and operational exceptions are accepted.

### Optional operator processes

- Define the operator workflow before implementing manual batch import of
  receiving and shipping documents.
- Add recounts, reservations, inventory tasks, or assignment queues only after
  their business rules and pilot need are agreed.
- Finalize label geometry after printer and label constraints are available.
- Revisit badge login only after a safe identity and revocation decision.
- Treat offline commands, mass label printing, fleet management, and broader
  device certification as separate epics.

### Mobile operator notifications

- Define which warehouse events require an immediate notification and which
  operator or role receives each event.
- Add server push delivery, device-token lifecycle, notification privacy, and
  deep links into the corresponding Mobile order or operation only after the
  operational event model and recipient rules are stable.

## Production maintenance

- Disable sensitive EF data logging and detailed errors outside approved
  non-production environments before production rollout.
- Update or replace the transitive vulnerable `Microsoft.OpenApi 2.0.0`
  dependency reported by `NU1903` before production rollout.
- Review .NET SDK and package versions before production rollout.
- Review non-atomic multi-step Identity role updates and concurrent protection
  of the last active administrator before relying on them under multiple
  administrators.

## Inputs needed for later work

- standalone host, DNS, certificate, database, and configuration constraints;
- a safe copy or representative snapshot of an existing database;
- printer model, label size, and print-path constraints;
- real 1C packaging, characteristic, and shipping-flag examples;
- a business decision on badge login and any new warehouse process.
