# Finish receiving and putaway model cleanup

Status: **Frozen** — accepted on 2026-10-07 after developer build, focused tests, and TSD smoke check.

## Outcome

The receiving model and 1C integration contain only the supported LPN receiving and independent putaway paths. Historical data and completion evidence remain available.

## Scope

- Remove the unused receiving-order outbound 1C interface, implementation, registration, and write-only URL helpers. Receiving remains inbound-only; shipping outbound integration is unchanged.
- Remove the disabled legacy start-receiving command and service method. Joining an order remains the supported start path.
- Remove computed receiving-line/order flags that have no consumer. Keep fact, quantity, weight, synchronization and completion data used by current screens, posting, or audit.
- Remove the unused `Available` part of the Mobile receiving queue; load only the current user's orders. Opening another order by scanned barcode remains available.
- Stop requesting an unused 1C receiving-document field.
- No database schema or endpoint route change. The Mobile queue response loses the unused `Available` field.

## Acceptance

- No receiving-order status or item update path to 1C remains in the executable code.
- Current order joining, LPN quantity aggregation, completion and putaway behavior remain unchanged.
- Persisted completion snapshots and historical line comments remain intact.
- The TSD still shows personal orders and opens other eligible orders by scan.

## Verification

The developer reported a successful WebApi build, focused receiving tests, and TSD smoke check on 2026-10-07. The agent performed only static checks.

## Open questions

None.
