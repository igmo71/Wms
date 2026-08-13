# Intra-warehouse transfers implementation plan

Implementation is split into independently verifiable stages. Each stage must
leave the solution buildable and must not introduce later-stage behavior
prematurely.

## Stage 1: typed zones

### Changes

- Add the `ZoneType` enum with `Storage`, `Transit`, `Receiving`, and `Shipping`
  values.
- Add `Zone.Type`, its EF configuration, and a schema migration. The development
  database is recreated and zones are configured explicitly with their types.
- Extend zone list, filtering where useful, and zone create/edit UI to display
  and edit the type.
- Ensure storage-location selection can be limited by zone type without adding
  a second location hierarchy.
- Filter and validate receiving and shipping locations by their respective zone
  types, and keep picking and inventory-count source locations in `Storage`
  zones.

### Verification

- New zones require an explicit type and all four types can be configured.
- A transit zone and locations in it can be configured.
- Existing receiving, shipping, inventory, balance, and turnover screens retain
  their current behavior.
- The solution builds and the migration model is consistent.

## Stage 2: inventory-transfer persistence

### Changes

- Add `InventoryTransfer` and `InventoryTransferStatus` (`Draft`, `InProgress`,
  `Completed`) following existing document conventions for identifiers,
  numbering, timestamps, and users.
- Store one warehouse and one optional transit storage location on the transfer.
- Add `RecorderType.InventoryTransfer` and expose the recorder in inventory movement
  and turnover history.
- Add EF configurations, `DbSet`, relationships, indexes, and migration.
- Enforce one non-completed inventory transfer per transit location. Use a database
  constraint or index where the provider supports the rule, with application
  validation retained for useful error messages.
- Add basic queries for a transfer and a paged transfer list.

### Verification

- A draft can be persisted and read with or without a transit location.
- Existing movement recorders continue to resolve correctly.
- Competing attempts to assign the same transit location cannot both succeed.
- The solution builds and the migration model is consistent.

## Stage 3: transfer command logic and inventory consistency

### Changes

- Implement commands to create a draft after warehouse confirmation, delete an
  empty draft, assign or change a transit location while allowed, pick to
  transit, put from transit, move directly, and complete a transfer.
- Validate warehouse ownership, zone types, positive quantity, distinct source
  and destination, current source balance, transfer status, and exclusive transit
  ownership.
- Post every confirmed action immediately through the common balance-and-turnover
  service.
- Persist movement creation, posting, history sequence allocation, and the
  first `Draft -> InProgress` transition with one `SaveChangesAsync` call.
- Make completion explicit and reject it when the transfer has no movements or
  its transit location has positive inventory.
- Keep posted movements immutable and prohibit deletion after the first posted
  movement.
- Record the confirming operator on every movement using the application's
  established identity representation.
- Follow the existing MVP persistence approach without introducing explicit
  transactions or special concurrent-command handling before a real case
  requires it.
- Automated transfer tests are intentionally deferred; do not introduce a test
  project during this stage. Verify the implementation through compilation,
  EF model checks, migration inspection, and later manual process scenarios.

### Verification scenarios

- Direct movement posts one source-to-destination movement.
- Picks and puts can alternate freely.
- The same SKU can be collected from several locations and distributed in
  partial quantities among several destinations.
- Putting inventory back into an earlier source is an ordinary put.
- Insufficient source or transit balance fails without partial changes.
- Invalid warehouse or zone use fails without partial changes.
- The first movement starts the transfer atomically.
- A used transit location cannot be changed.
- A non-empty transit location prevents completion; emptying it does not complete
  the transfer automatically.
- A completed transfer rejects all further mutations.

## Stage 4: operator web UI (completed)

### Pages

- Add a filtered, paged inventory-transfer list following current operator UI
  conventions.
- Use one full transfer work page for both initialization and subsequent work.
  Do not add a creation dialog or a separate warehouse-only page.

### Work-page states

Before a warehouse is confirmed, the page shows the complete recognizable work
layout. Warehouse selection and the primary `Start` action are enabled; transit
selection, movement actions, completion, transit contents, and history are
visible but disabled or empty.

Confirming the warehouse on that page creates the draft, navigates the page to
  the persisted transfer address if necessary, and locks warehouse selection. An
accidental warehouse choice therefore does not create a document until the
operator explicitly starts the transfer.

After warehouse confirmation:

- transit-location selection and direct movement are enabled;
- pick and put remain disabled until a transit location is assigned;
- the transit location is displayed as persistent transfer context and is never
  entered on individual pick or put forms;
- pick, put, and direct movement are three explicit, visually distinct actions;
- put is disabled when the transit location has no available inventory;
- completion is enabled only after at least one movement and while the transit
  location is empty;
- completed transfers are displayed read-only;
- an empty draft can be deleted from the work page.

The page shows current transit contents aggregated by SKU and immutable movement
history in actual execution order. Its interaction vocabulary should remain
recognizable for a future mobile client without copying a mobile layout into the
web application.

### Verification

- Exercise direct-only, transit-only, and mixed workflows in the browser.
- Verify disabled states before warehouse and transit confirmation.
- Verify that server-side validation remains authoritative when stale UI state
  submits an invalid action.
- Verify pause-and-resume by reopening an in-progress order.
- Verify responsive usability at narrow viewport widths without designing the
  future scanning client.

## Stage 5: end-to-end stabilization

- Run the complete build and any automated tests that exist at that time.
- Apply the migration to a representative development database.
- Recheck inventory balances, turnovers, and movement-history links for every
  transfer action.
- Review Russian labels, validation messages, loading states, and duplicate
  submission protection.
- Update `docs/PROJECT_CONTEXT.md`, `docs/ROADMAP.md`, and this specification if
  implementation reveals a lasting rule or an intentional MVP limitation.

## Delivery boundaries

The preferred review and delivery boundaries are:

1. typed zones;
2. transfer persistence and recorder integration;
3. command/query logic with concurrency coverage;
4. operator UI;
5. stabilization and documentation reconciliation.

Stages 2 and 3 may share a development branch when convenient, but their changes
and verification should remain separable. The UI must consume the command and
query services and must not become the only place where transfer rules are
enforced.
