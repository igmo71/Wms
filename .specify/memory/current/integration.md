# Current 1C integration

## 1C synchronization

Manual catalog imports are available from the WebApp catalog lists and the
`GET /api/1c/.../import` endpoints. Their role rule is owned by
[identity.md](identity.md); notification endpoints and background imports remain
the automatic integration path.
SKU imports preserve the WMS-owned expiration-tracking flag; its behavior is
described in [receiving.md](receiving.md).

The receiving source reader fetches the current 1C document by `Ref_Key` and
returns the unchanged JSON response. The new synchronization use case stores a
successful response as a source version only when its content changes. It also
updates the last successful check time on an unchanged response and rebuilds the
derived line projection after catalog changes. Source lines remain separate;
unsupported quantities and unknown SKU retain reasons without hiding valid
quantities of other SKU. A failed read or unreadable response records the last
attempt error without replacing the successful version. The notification path
uses this synchronization before the legacy receiving adapter maps the response.

Before warehouse work starts, an admissible source document remains owned by
1C. While WMS and 1C remain in matching initial states, synchronization replaces
source-owned metadata and the complete plan. Deletion marks, posting changes,
unexpected source status, malformed plans, and unsupported line semantics are
blocking. After work starts, synchronization never replaces the protected WMS
plan or warehouse fact. A successful shipping rollback returns the order to the
initial synchronization mode.

A document notification may create a missing source order. An explicit
synchronization check requires that the local order already exist.

Synchronization assessments are `Synchronized`,
`RequiresOperatorDecision`, or `Blocking`. WebApp may acknowledge only an
operator-decision assessment after a fresh fingerprint check; acknowledgement
copies source-owned metadata only. Mobile shows the latest assessment but
cannot acknowledge it.

Mobile receiving, picking, and shipping screens support pull-to-refresh for a
fresh synchronization check. A rejected completion conflict also refreshes the
assessment; unresolved or failed verification disables completion. Refresh
does not acknowledge source changes or replay pending commands.

Mobile receiving omits the technical changed-field list from synchronization
messages, retaining verification errors, blocking guidance and required actions.

Starting work rejects a known unresolved assessment. Receiving completion,
picking completion, and final shipping fetch and persist a fresh checkpoint
before final effects. Receiving applies the LPN plan/fact and Manager-decision
rules above and never writes to 1C. Shipping may continue on an exact source
state or the exact repeat-safe outbound target; an unacknowledged decision
stops that transition. Technical failure or a blocking assessment stops both.
Repeated identical assessments do not advance the order revision.

Notification delivery uses an in-memory channel and can be lost on restart.
WMS-to-1C multi-step transitions are not atomic across both systems; pilot
operation therefore requires the recovery procedure tracked in the roadmap.
Shipping completion treats an already-applied exact 1C item-table target as
success, and its target status and posting calls are repeatable.

Historical receiving line comments are retained as WMS-owned annotations, not
warehouse facts. Current receiving has no direct line-comment command and does
not send status, item updates or comments to 1C. Comment differences
do not affect synchronization assessments or fingerprints and never block work.
