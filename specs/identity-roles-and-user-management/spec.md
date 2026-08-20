# Roles and user management

## Problem and outcome

WMS records application user identifiers in operational audit fields, but the
UI currently resolves them to Identity `UserName`, which is the user's email.
Public self-registration also doesn't provide an administrative boundary for
warehouse accounts.

WMS must use administrator-managed accounts, authorize access by role, and show
a human-readable current display name in audit and employee reports.

## Scope

- Add the fixed roles `Administrator` and `Operator`.
- Require either role for WMS operational and report pages, and require
  `Administrator` for configuration catalogs.
- Restrict user management to `Administrator`.
- Remove public local self-registration links and restrict its retained route
  to administrators; redirect administrators to the dedicated user page.
- Provide an administrator page to create users, edit their display name and
  role, and block or unblock sign-in.
- Add `ApplicationUser.DisplayName` and use it for current audit/report display,
  falling back to `UserName` for migrated users whose name is not yet filled.
- Initialize roles and the first administrator from secret-backed
  configuration.

## Decisions

- A user has one WMS role in the MVP: `Administrator` or `Operator`.
- `DisplayName` is one field rather than structured surname/name/patronymic.
- Audit entities continue storing only the Identity user identifier. Renaming a
  user therefore changes their name in historical UI.
- Accounts created by an administrator have a confirmed email because account
  ownership is established by the administrator workflow.
- Existing users without a WMS role become operators during initialization.
- The configured bootstrap administrator is assigned `Administrator`. If the
  account doesn't exist, an initial password must be supplied through secret
  configuration before it can be created.
- An administrator cannot block their own account or remove their own
  administrator role.

## Non-goals

- Fine-grained permissions per warehouse operation.
- Multiple simultaneous WMS roles per user.
- Linking application users to the 1C Individuals catalog.
- Snapshotting display names into operational audit records.
- Password reset and account deletion in the administrator UI.
- Role authorization for the separate 1C integration API.

## Acceptance criteria

- Anonymous users are redirected to login from WMS operational pages.
- Operators can use operational, catalog, and report pages but cannot open user
  management.
- Administrators can create an operator or administrator with email, display
  name, and initial password.
- Administrators can change another account's display name, WMS role, and
  blocked state.
- Public registration links are unavailable, and anonymous users cannot use
  the retained registration route.
- Audit headers, lists, user filters, and employee performance reports show the
  current `DisplayName`, with `UserName` as a migration fallback.
- Deleted users remain shown by their stored identifier.
- Roles and bootstrap initialization are idempotent.

## Configuration

The optional `IdentityBootstrap` section supports:

- `AdministratorEmail`;
- `AdministratorDisplayName` when a new account must be created;
- `AdministratorPassword` when a new account must be created.

The password must be supplied through user secrets or environment variables,
not committed configuration.
