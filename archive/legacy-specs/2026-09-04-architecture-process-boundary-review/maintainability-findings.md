# Maintainability and boundary findings

## F-01 — Concrete OneS types cross the application boundary

**Priority:** P1 boundary risk.

Receiving and shipping command services depend on concrete OneS outbound
services. Concrete OneS synchronization services depend back on those command
services, and WebApp/Mobile endpoints request synchronization implementations
directly. Extraction would create a circular project dependency. Existing
`IReceivingOrderSource` and `IShippingOrderSource` demonstrate the correct port
direction.

Recommendation: add application-owned execution-sink ports and
application-facing synchronization operations. Keep OData, statuses, DTOs,
notification parsing, and delay in the adapter.

## F-04 — Hosts repeat synchronization orchestration

**Priority:** P2.

Web pages directly check/acknowledge through concrete OneS services. Mobile
endpoints repeat local query, conditional check, and response merge. Critical
commands correctly retain their own authoritative fresh checks.

Recommendation: application synchronization owns check/acknowledge and hosts
query details only after that operation. Do not move critical guards to UI.

## F-05 — Shipping rollback is Web-only

**Priority:** open.

Two WebApp pages call rollback. Mobile V1 has no route or receipt. This may be
an intentional supervisory boundary. Confirm pilot ownership before proposing
Mobile support.

## F-06 — Large services mix different work

**Priority:** P2.

Command services reach 609 lines for shipping, 590 receiving, 532 transfers,
438 inventory counts, and 422 putaway. Repeated location loading/checking is
mixed with orchestration, while receiving/shipping additionally combine
synchronization, editing, workflow, 1C, and persistence. Mobile query-service
size mostly comes from query and projection composition; inventory posting is
a comparatively cohesive algorithm.

Recommendation:

- keep value rules in value objects and state invariants in domain methods;
- extract repeated database-backed location rules into a narrow policy;
- keep feature-specific availability/route rules in named feature policies
  only when they form a sizeable cohesive cluster;
- split receiving/shipping synchronization from workflow at the 1C boundary;
- do not add `IValidator<T>`, pipelines, discovery, per-command validators,
  flag bags, partial classes, or generic helpers.

## F-11 — Mobile technical containers cross features

**Priority:** P2.

`MobileApiClient.cs` is 983 lines and contains every feature. Every page depends
on the whole client. `MobileApiContracts.cs` is 522 lines. Large pages combine
process state, scanner/camera lifecycle, cancellable search, quantity editing,
API calls, busy state, and Android focus workarounds. Receiving is 874 lines,
picking 835, inventory count 681, and transit movement 584.

Recommendation:

1. use small concrete feature clients over one internal HTTP/session transport;
2. split independent contract records into feature files without changing
   namespace or wire shape;
3. extract the Android focus suppression helper, which is byte-for-byte
   technical behavior repeated across pages;
4. pilot one concrete scanning-session helper only after the feature-client
   split; it may own subscription and camera start/stop, but receives the
   page's `isScanExpected` decision and owns no process modes;
5. move retry request ids and their associated API sequences into a concrete,
   page-specific process object when decomposing one largest page;
6. add no MVVM framework, universal base page, or workflow engine.

### Largest-page boundary decision

The visible repetition is not one common workflow. `SetBusy`,
`ReturnToScanning`, action availability, camera eligibility, search results,
and error recovery all depend on the page's modes and pending commands. They
stay page- or feature-owned. The small `_searchVersion` idiom also stays local
for now: a generic latest-request coordinator would save few lines while
hiding the condition that makes a result current.

For a first page pilot, use two concrete responsibilities:

- the page owns controls, navigation, focus, dialogs, rendering, and the
  explicit mode-to-UI mapping;
- a feature-named process object owns Mobile API command sequences, stable
  request ids across uncertain responses, retry compatibility, and returned
  current details.

The event handler should remain a short readable sequence: reject an invalid
UI action, invoke the feature process, then render its result. Do not create a
generic controller contract. Start with receiving or picking, measure whether
the main path became easier to read, and only then repeat the shape.

## F-12 — Rich order aggregates combine substantial concerns

**Priority:** P2 pending invariant review.

`ReceivingOrder.cs` is 912 lines and `ShippingOrder.cs` 990. Both combine source
import/reconciliation with operational lifecycle. Their operational revisions
and invariants are real aggregate responsibilities, so size alone is not a
reason to split.

Recommendation: source-document reconciliation is the only plausible separate
domain concern. Map all private mutation it needs before extracting anything.
Prefer a large safe aggregate over exposed setters or scattered invariants.

## F-13 — Confirmed safe cleanup

**Priority:** P3.

- unused template `WeatherForecast.cs` and stale `.http` request;
- commented serializer field in `OneCClient`;
- transfer private save pass-through with no policy;
- receiving outbound telemetry labeled with `ShippingOrderCommandService`.

Apply these as an isolated cleanup batch, not mixed with boundary changes.

## Large-file treatment matrix

| Group | Main source of size | Likely treatment |
| --- | --- | --- |
| Order command services | synchronization, workflow, integration, editing | responsibility split after boundary acceptance |
| Putaway/picking/transfer/count commands | save wrappers, stages, eligibility, domain calls | location policy and narrow feature policies |
| Mobile order query services | query/search/queue plus projection | cohesive query/projection collaborators |
| Inventory posting | one balance/turnover algorithm | keep cohesive unless independent boundary emerges |
| Order aggregates | workflow plus source reconciliation | cautious reconciliation review only |
| Mobile API/contracts | all features grouped technically | feature clients and feature files |
| Mobile pages | process state plus repeated UI mechanics | repeated mechanics first; controller only if needed |
| Mobile endpoints | routes, auth, composition, mapping | extract cohesive response mapping where useful |

An extraction must have a business name, reduce the context needed for the main
path, and leave control flow and side-effect ownership visible.
