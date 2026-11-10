# Operational hardening before pilot

## Business outcome

Make WMS safe and recoverable enough for an operational pilot without replacing
the straightforward MVP architecture.

## Scope

- Authenticate operator commands and verify 1C callers.
- Remove development-only sensitive logging from non-development environments.
- Define recovery for partial WMS-to-1C transitions and notification failures.
- Add focused inventory workflow integration tests.
- Resolve the known 1C document import and table-section TODOs required by the
  selected pilot scope.

## Non-goals

- A generic enterprise workflow engine, event bus, or repository layer.
- Automatic retries or an outbox unless manual recovery is shown to be
  insufficient for the pilot.
- Roadmap warehouse processes whose business rules are not yet agreed.

## Open decisions

- Which authentication mechanism will the 1C environment support for webhook
  calls?
- What recovery time and amount of manual intervention are acceptable when 1C
  and WMS temporarily diverge?
- Which of receiving, shipping, and inventory count are included in the first
  operational pilot?
