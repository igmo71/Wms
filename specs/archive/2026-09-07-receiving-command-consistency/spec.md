# Receiving command consistency

Status: frozen reference. Completed 2026-09-07.

## Outcome

Web completion of receiving treats the operator's selected receiving location
and the final receiving transition as one explicit final-save operation. The
unused legacy receiving API is removed because the supported interactive
clients are explicitly limited to `Wms.WebApp` and `Wms.Mobile`.

## Scope

- Add a receiving completion operation that accepts the selected receiving
  location.
- Perform the fresh 1C synchronization checkpoint before staging a changed
  location, then validate and stage the location before transition, posting,
  outbound completion, and the final classified save.
- Replace the Web page's preliminary location save plus completion call with
  the combined operation.
- Remove the legacy `/api/ReceivingOrder` route group and its application
  endpoint mapper.
- Remove command methods that become unreachable with the legacy routes and
  the Web preliminary location save gone.
- Preserve Mobile receipt behavior, routes, domain rules, and 1C protocol.

## Out of scope

- Refactoring application/1C ports or the synchronization checkpoint contract.
- Endpoint or page decomposition.
- New automated tests.

## Acceptance criteria

- A Web completion failure after fresh synchronization does not persist the
  newly selected receiving location.
- A successful Web completion persists the selected location, received state,
  inventory effects, and final local changes through the existing final save.
- Mobile completion continues using the location assigned at start and its
  receipt-protected staged operation.
- `/api/ReceivingOrder` is no longer mapped, and no legacy-only receiving
  command path remains.
- Relevant projects build and `git diff --check` passes; no tests are created.

## Result

- Web completion now calls `CompleteReceivingAsync` once with the selected
  location. The required synchronization checkpoint is saved before that
  location is staged, so a later failure does not commit the location alone.
- The standalone public location-save command and the location-free receiving
  transition were removed. Start receiving continues through the combined
  location validation, transition, outbound call, and classified save.
- The supported-client boundary was clarified as `Wms.WebApp` and
  `Wms.Mobile`; repository search confirms neither uses `/api/ReceivingOrder`.
  Its endpoint group, mapper, and legacy-only command paths were removed.
- Mobile start and completion keep their existing receipt-protected staged
  operations and transport contracts.
- `Wms`, `Wms.WebApi`, and `Wms.WebApp` build successfully. WebApi retains the
  known `NU1903` warning for `Microsoft.OpenApi 2.0.0`.
- `git diff --check` passes and no automated tests were created.
