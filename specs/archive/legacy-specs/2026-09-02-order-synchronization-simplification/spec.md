# Order synchronization simplification

Status: **Frozen reference**

## Outcome

Exact repeated checks do not create operational concurrency changes, and
Mobile keeps an order visible when a fresh 1C check fails while preventing
critical transitions.

## Scope

- remove the persisted last-check time from receiving and shipping orders;
- retain level, fingerprint, current-issue detection time, and acknowledgement
  audit;
- return Mobile order details with the last known synchronization state and a
  technical verification error when 1C cannot be checked;
- keep start and completion unavailable until a successful fresh check;
- preserve the last fresh assessment across Mobile draft-command responses;
- do not add a start fingerprint token or change the agreed completion checks;
- do not optimize the second shipping GET before staging measurements.

## Acceptance criteria

- an exact repeated check does not advance `OperationalRevision`;
- a changed level or fingerprint still advances `OperationalRevision`;
- WebApp and Mobile both retain visible order details after a technical check
  failure;
- Mobile clearly distinguishes a technical verification failure from a
  business difference and blocks critical transitions;
- draft scans and edits do not erase the opening verification result;
- no tests are created or run.
