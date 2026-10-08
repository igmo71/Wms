# Confirmed safe cleanup

Status: frozen reference. Accepted 2026-09-07.

Implementation is ready for review. The recorded receiving telemetry mismatch
was no longer present: both receiving outbound activities already use
`ReceivingOrderCommandService`, so that code was deliberately left unchanged.

## Outcome

Remove the confirmed template, comment, pass-through, and telemetry-category
debris identified by the accepted architecture review without changing runtime
behavior or broadening the cleanup into nearby code.

## Scope

- Remove the unused WebApi `WeatherForecast` template type and its stale HTTP
  request file.
- Remove the commented serializer-options field from `OneCClient`.
- Replace the private inventory-transfer save pass-through with direct calls to
  the existing application persistence helper.
- Verify the receiving outbound activity category and correct it only if the
  reviewed mismatch is still present.

## Out of scope

- General comment, dead-code, formatting, telemetry, or template cleanup.
- Changes to persistence conflict classification or save boundaries.
- Changes to 1C requests, serialization, activity names, or logging content.
- New automated tests.

## Acceptance criteria

- No application code references either removed WebApi template artifact.
- Inventory-transfer save behavior still uses `ApplicationPersistence` at the
  same three save points.
- `OneCClient` retains its current serializer behavior.
- Receiving outbound activities use the receiving command category; an already
  correct category is left unchanged.
- The solution builds and `git diff --check` passes; no tests are created.
