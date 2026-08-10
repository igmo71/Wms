# Draft picking movements

## Business outcome

Allow an operator-facing picking flow to record where each shipping-order item
was picked from before inventory is posted.

## Scope

- Add, update, and delete unposted source-to-shipping-location movements.
- Allow changes only during picking or verification statuses.
- Recalculate each shipping line's actual quantity from its unposted draft
  movements.
- Post the draft movements when the order is set ready for shipment.

## Non-goals

- Picking endpoints or UI.
- Any other ready-for-shipment workflow behavior beyond posting its existing draft movements.
