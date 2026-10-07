# WMS roadmap

This file contains unfinished accepted work. Current behavior belongs in
[`PROJECT_CONTEXT.md`](PROJECT_CONTEXT.md); engineering conventions belong in
[`ARCHITECTURE.md`](ARCHITECTURE.md); detailed decision history belongs in
`specs/`. **Next task** identifies the agreed immediate work; future delivery
areas are not authorization to begin implementation.

## Next task

Not yet selected. Agree one small increment before implementation.

## Future delivery areas

Verify the accepted safe putaway quantity flow with the camera fallback when
camera use becomes relevant; TSD behavior was accepted on 2026-10-06.

E1 addressing, E5 storage restrictions/capacity, E6 replenishment, E7 release
rehearsal and E3/E4 Web/reporting extensions remain future work. Their decisions,
dependencies and boundaries belong in DEVELOPMENT_PLAN; order and implementation
scope require a separate task. The pilot prerequisites below govern E7.

Receiving already uses inbound-only 1C integration. Shipping still requires
the WMS-to-1C recovery procedure below.

## Pilot prerequisites

### 1C access

Authentication of 1C callers is deferred. Until it is implemented, unverified
1C endpoints must remain inside the trusted network.

### Operational confidence

- For the release rehearsal, manually validate WebApp and Mobile flows against a representative database:
  receiving, putaway, picking, shipping, count, and both transfer routes.
  The accepted I01–I08 Web/Mobile checks are completed; this is the broader
  release-environment checkpoint.
- Confirm backup/restore and migration application on the staging topology.
- Resolve the operator-recovery procedure described below.

### Operator recovery

- Map each multi-step WMS-to-1C transition and its observable checkpoints.
- Distinguish safe retry, already-applied success, and manual escalation.
- Specify the evidence and operator action for each outcome.
- Exercise representative failures on staging without changing integration
  semantics.

## Pilot rehearsal

After the prerequisites, run one documented end-to-end rehearsal covering
backup and restore, migration, authentication, all four Mobile workflows, 1C
exchange, an interrupted command, and operator recovery.

## Production maintenance

- Upgrade the legacy SQL Server and then remove the weakened
  `openssl-legacy.cnf` compatibility profile from application containers.
- Disable sensitive EF logging and detailed errors outside approved
  non-production environments.
- Update or replace the vulnerable `Microsoft.OpenApi 2.0.0` dependency reported
  by `NU1903`.
