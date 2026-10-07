# Remove direct receiving fact edits

## Outcome

Receiving facts can change only through the composition of LPNs. Obsolete direct edits of order lines no longer remain as callable application or domain methods.

## Scope

- Remove unused order-line increment, quantity replacement, and comment commands and their command-service/domain methods.
- Keep aggregate facts derived from active LPN items, receiving completion, inventory posting, and historical line comments.
- Adapt the synchronization test to construct its fact through an LPN.
- No database schema or Mobile/Web contract change.

## Acceptance

- No supported command path can directly edit a receiving order line fact.
- A change to an LPN item still recalculates the order-line fact.
- Source comment changes still leave the synchronization fingerprint unchanged, while source quantity changes remain blocking.

## Verification

The developer reported successful focused tests on 2026-10-07; UI behavior is unchanged.

## Open questions

None.
