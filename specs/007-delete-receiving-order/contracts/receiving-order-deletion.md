# Web contract: receiving order deletion

The authenticated Web order card is the only deletion entry point. Its Manager-only action opens a preview dialog. Mobile receives no deletion endpoint or control.

## Preview

Input: order ID and actor. Output: order identity, affected LPN codes/statuses, ordered compensating movements, affected dated balance groups (`Before`, `After`), `RequiresNegativeConfirmation` and opaque `SnapshotToken`. Historical putaway or inconsistent movement history returns a conflict; non-Manager receives an access error. Preview has no stock effect.

## Confirm

Input: request ID, order ID, token, explicit negative acknowledgement and actor. Success returns order ID and reloads the archived card. Stale token or missing acknowledgement returns conflict without partial effect. Repeating the exact request ID/data returns the original result; a new deletion request against the deleted order is rejected.

## Read surfaces

Web list/direct card retain deleted orders, show status and actor/time, and hide mutation controls. The card shows LPNs, source/cycle history and compensating movements. Mobile active queue and direct receiving-order lookup omit deleted orders; stale writes fail at command execution.
