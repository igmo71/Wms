# WMS roadmap

This file contains unfinished accepted work. Current behavior belongs in
[`PROJECT_CONTEXT.md`](PROJECT_CONTEXT.md); engineering conventions belong in
[`ARCHITECTURE.md`](ARCHITECTURE.md); detailed decision history belongs in
`specs/`. Only **Next delivery** is recommended immediate work.

## Next delivery

Discuss the implementation survey and unified
[LPN development plan](DEVELOPMENT_PLAN.md). Its implementation stages remain
proposed until accepted; the active scope is planning only.
The existing pilot prerequisites below remain outstanding. Receiving's proposed
one-way integration may narrow operator recovery, but does not remove shipping's
WMS-to-1C recovery requirement.

## Pilot prerequisites

### 1C access

Authentication of 1C callers is deferred. Until it is implemented, unverified
1C endpoints must remain inside the trusted network.

### Operational confidence

- Manually validate WebApp and Mobile flows against a representative database:
  receiving, putaway, picking, shipping, count, and both transfer routes.
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
- Make multi-step Identity role updates atomic and protect the last active
  administrator under concurrent administration before relying on that
  invariant in production.
