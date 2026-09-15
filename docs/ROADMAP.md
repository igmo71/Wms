# WMS roadmap

This file contains unfinished accepted work. Current behavior belongs in
[`PROJECT_CONTEXT.md`](PROJECT_CONTEXT.md); engineering conventions belong in
[`ARCHITECTURE.md`](ARCHITECTURE.md); detailed decision history belongs in
`specs/`. Only **Next delivery** is recommended immediate work.

## Next delivery

Prioritize an end-to-end receiving and putaway demo through LPN, requested
on 2026-09-15 for approximately September 17–18. See the scope and checkpoints
in [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md), section 6. Use existing topology;
LPN tracks receiving and initial putaway, not stored inventory dimensions.
Addressing, topology editing, advanced recommendations and replenishment follow later.
Planning is complete; implement the active
[receiving and putaway specification](../specs/2026-09-15-lpn-receiving-putaway/spec.md).
It includes Manager reconciliation and simple location recommendations;
SKU storage groups and capacity ranking remain later work. Quality and usability
take priority over the requested demo date. Implementation has not started.
The active specification splits delivery into I01–I08; begin with I01 label
issuance and A4 printing, including its UI and checks.
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
