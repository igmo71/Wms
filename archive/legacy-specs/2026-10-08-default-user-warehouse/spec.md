# Default warehouse for a WMS user

Accepted on 2026-10-08.

## Outcome

An administrator may assign an optional default warehouse to an `ApplicationUser`.
After Mobile sign-in or session restoration, warehouse workflows start on that
warehouse without requiring the employee to choose it on each screen. An employee
may still switch warehouses; this is a convenience setting, not an access rule.

## Scope and rules

- Store nullable `DefaultWarehouseId` on `ApplicationUser` as a foreign key to
  `Warehouse`. Existing accounts remain unassigned after migration. Physical
  deletion of a referenced warehouse must not silently remove the preference.
- User creation and editing in WebApp offer an optional "Склад по умолчанию"
  selector. Only non-deleted warehouses may be newly assigned. Clearing the
  selector removes the preference. Saving it remains in the existing atomic user
  management operation. Show the assigned warehouse in the user list or edit
  dialog so an administrator can inspect and change it.
- Extend the authenticated Mobile `/me` response with nullable
  `DefaultWarehouseId`. Read it from the current user record; do not put the
  preference in access-token claims or alter the login and refresh contracts.
- Mobile keeps one selected warehouse in memory for the current signed-in user
  while the app process is running. On sign-in or session restoration after an
  app restart, initialize it from the default warehouse if it remains
  in the available warehouse list. Warehouse lookup must not mistake the former
  `/warehouses` response limit of 100 for a complete list. If no default is
  configured and exactly one warehouse is available, select that warehouse.
  In all other cases, require an explicit selection. Explain an unavailable
  assigned warehouse to the employee; do not silently select another one.
- The existing selectors on receiving, shipping, inventory count, and transfer
  screens reflect this current selection. A manual change applies to subsequent
  workflows during the current app run; it does not edit `ApplicationUser` or
  persist on the device. On logout or a new sign-in, clear the previous selected
  warehouse so another user cannot inherit it. Restarting the app with a saved
  authentication session restores the account default, not a manual override.
- LPN putaway derives its warehouse from the scanned LPN and continues to do so.
  No warehouse authorization filter or operational command semantics change.
  Admin changes to the default take effect when Mobile next loads `/me` during
  sign-in or session restoration.

## Acceptance criteria

- A new or existing user may have no default warehouse; existing accounts
  migrate with `NULL` and retain the current manual selection behavior.
- An administrator can assign, change, or clear an active default warehouse.
  Unknown or deleted warehouses cannot be newly assigned.
- A user with an available default can open each of the four workflows and work
  immediately in that warehouse. Switching it on one screen updates the choice
  used when entering another workflow during that session.
- A new sign-in, app restart with session restoration, or sign-in by a different
  user initializes warehouse selection from that user's own `/me` response.
  Logout clears the previous selection. A manual override does not survive an
  app restart.
- If the assigned warehouse becomes unavailable, Mobile explains the problem
  and waits for an explicit warehouse choice. It does not use a stale id for
  warehouse-scoped requests.
- Users can continue to choose and work in other warehouses. Server access
  policy remains role-based, without a warehouse restriction.

## Acceptance

The developer reported successful builds, automated tests, and manual checks of
the Web and Mobile behavior on 2026-10-08.
