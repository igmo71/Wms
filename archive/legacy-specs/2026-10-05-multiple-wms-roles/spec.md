# Multiple WMS roles per account

Status: **Frozen** — accepted on 2026-10-05.

Current rules belong to [Identity and access](../../../docs/PROJECT_CONTEXT.md#identity-and-access).

## Outcome

An account can explicitly hold `Administrator` and `Manager` together: it keeps
user/configuration administration and can approve a receiving quantity difference.
`Administrator` alone still cannot approve that difference.

## Scope

- Create and edit a nonempty set of known WMS roles; show every assigned role.
- Compare sets, add missing memberships and remove only obsolete WMS memberships.
  Preserve unrelated Identity roles. Reordering or repeating roles is not a change.
- Preserve self-block/self-demotion and last-active-administrator protection,
  including concurrent administration requests and accounts with multiple roles.
- Save account changes, memberships, lockout and security-stamp invalidation in
  one transaction. A failure must leave the original account and permissions intact.
- Preserve explicitly assigned bootstrap roles on restart. Do not grant Manager
  automatically to existing administrators. Existing single-role users need no migration.
- Keep existing authorization rules and session validation timing. A changed role
  set invalidates the security stamp; a new sign-in obtains all current roles.
  Web cookie/circuit validation uses the existing 30-minute interval; Mobile
  refresh rejects the old stamp, while issued access tokens last until expiry.

Receiving-screen improvements, new operational permissions, role hierarchy changes,
instant session revocation and Mobile UI changes are outside this increment.

## Acceptance

1. Create an Administrator + Manager account, reopen its editor and observe both
   roles; administration and the Manager quantity-difference action are available
   after sign-in. Removing only Manager preserves administration and removes that
   action after sign-in. Administrator alone cannot approve a difference.
2. At least one WMS role is required; unknown roles are rejected. All existing
   single-role accounts remain editable; saving the same set does not invalidate
   sessions or remove/reinsert memberships.
3. An administrator can add/remove their own Manager role while keeping
   Administrator, but cannot block themselves or remove their Administrator role.
4. Blocking or removing Administrator from the last active administrator is
   rejected regardless of other roles; concurrent changes cannot remove both
   remaining active administrators.
5. A failure during role/stamp persistence rolls back all changes. Successful
   addition/removal of a role changes the stamp; subsequent sign-in uses the new set.
6. Restart does not remove Manager from the configured bootstrap administrator.

## Verification result

On 2026-10-05 the developer confirmed that the compilation issue was resolved,
multiple roles could be assigned and survived restart, and the tests passed.
Existing receiving safety tests own discrepancy posting; this increment does not
change receiving authorization or posting. Separate manual verification of old
Mobile refresh-token rejection was not reported.

## Open questions

None.
