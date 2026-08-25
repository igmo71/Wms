# Atomic idempotency for mobile inventory-transfer commands

Status: **Active**
Created: **2026-08-25**

## Business outcome

Retrying one confirmed mobile command, including a concurrent retry, never
creates a second warehouse action. The server returns the result of the first
successful execution and rejects reuse of the same request id for different
input.

## Scope

- introduce a persisted mobile command receipt;
- identify a command by authenticated user, command type and `ClientRequestId`;
- store a deterministic request hash and result resource id;
- save the receipt atomically with the affected WMS data;
- apply the mechanism first to creation of a direct inventory-transfer draft;
- reuse the same mechanism for direct movement and completion in subsequent
  increments.

## Decisions

`MobileCommandReceipt` contains the authenticated user id, stable command type,
client request id, SHA-256 request hash, result resource id and completion time.
Its composite primary key is the idempotency boundary.

The application service stages the business change and receipt in one
`ApplicationDbContext` and one `SaveChangesAsync`. A concurrent loser is rolled
back by the database primary-key violation, then reads and returns the winning
receipt. The same key with another request hash is a conflict.

Only successful commands create receipts. Validation and business failures can
be retried with the same id after the underlying input or state is corrected.
No endpoint middleware, distributed lock, retry queue or cleanup process is
introduced for the MVP.

## Acceptance criteria

1. One create request produces one draft and one receipt.
2. Sequential and concurrent repetitions return the same transfer id.
3. Reusing the id with another warehouse returns conflict.
4. Receipt and draft either commit together or neither commits.
5. The authenticated user, not a request field, scopes the key.
6. Unrelated database failures remain infrastructure exceptions.

## Non-goals

- offline command queues;
- automatic retry of business conflicts;
- generic idempotency middleware;
- receipt expiration and cleanup before pilot measurements;
- idempotency for non-mobile web commands.
