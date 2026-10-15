# Current identity and access

## Identity and access

WebApp uses ASP.NET Core Identity with three roles. `Operator` runs warehouse
operations and reports; `Manager` can also correct and reopen receiving LPNs and
approve quantity differences; `Administrator` manages configuration and users.
Legacy warehouse workflows still allow some operations and LPN corrections with
`Administrator`, pending their replacement. New receiving manager actions require
explicit `Manager` membership, including for an administrator. Imported catalog
lists are visible to `Manager` and `Administrator`; manual synchronization from
WebApp or the catalog import API requires `Manager` at execution. Zones and
storage locations remain administrator-only configuration. Public
self-registration is not offered; administrator-created accounts are confirmed
local accounts with one or more WMS roles whose rights combine. An administrator
may change their other roles but cannot block themselves or remove their own
`Administrator` membership.
The last active administrator cannot be blocked or lose that membership, including
through concurrent user-management requests.

User creation and updates save memberships and account state atomically. A changed
WMS role set or block state also updates the security stamp in that transaction;
reordering the same roles does not invalidate sessions. Web cookies and connected
circuits retain their existing 30-minute stamp-validation interval. Mobile rejects
an old stamp on refresh; an issued access token remains valid until expiration.
Signing in again loads all current role claims. Instant session revocation is not
provided.

An `ApplicationUser` may have an optional default warehouse. Administrators
assign only a non-deleted warehouse; this is a Mobile convenience preference,
not a warehouse access restriction. Mobile reads it from `/me` on sign-in or
session restoration and selects it across receiving, shipping, inventory count
and transfer workflows. A manual warehouse change carries to later workflows
only while that app process runs; logout, a new sign-in, or app restart resets
selection to the account default. Without a default, Mobile automatically
selects a sole available warehouse. If the assigned warehouse is unavailable,
Mobile requires an explicit choice. LPN putaway continues to use the LPN's
warehouse.

`ApplicationUser.DisplayName` is used for audit and reports, falling back to
`UserName`. Operational records retain the Identity user id, so renamed users
change historical display and deleted users display the stored id.

Startup initializes roles idempotently, assigns `Operator` to existing users
without a role, and creates a missing bootstrap administrator from
`IdentityBootstrap` configuration. Startup preserves additional roles explicitly
assigned to the bootstrap administrator; it does not automatically grant `Manager`.

Mobile uses the same confirmed accounts. A transient refresh failure
(transport, `408`, `5xx`, or an unreadable successful response) retains the
session and does not replay the request with a stale token. A definitive refresh
rejection (`400` or `401`), or an authenticated API `401`, clears the session.
