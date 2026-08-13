# Intra-warehouse transfers

## Business outcome

Allow an operator to record the actual movement of inventory between storage
locations of one warehouse. A transfer may be performed directly between two
locations or through a transit location such as a trolley. The inventory shown
by WMS must follow the physical inventory throughout an interrupted or
interleaved workflow.

## Core model

`InventoryTransfer` is a local WMS document that groups a chronological
sequence of completed inventory movements. It is an execution journal, not a
plan: it has no planned item lines and is not imported from or synchronized
with 1C.

Each operator-confirmed action creates and immediately posts one
`InventoryMovement` through the common balance-and-turnover posting service.
Confirmation means that the operator has physically completed the action and
explicitly confirms it in WMS. Data entered into an unfinished form is not an
inventory fact and does not affect balances.

Posted movements are immutable. A physical return is recorded as an ordinary
new movement in the opposite direction; the original movement is not edited or
deleted.

All movements and locations of an inventory transfer belong to its warehouse.
Inter-warehouse transfers are outside this process.

## Supported movement modes

The operator may freely alternate the following actions in one transfer. There is
no mandatory pick phase followed by a put phase.

### Pick to transit location

The operator enters the source location, SKU, and quantity. WMS supplies the
transfer's transit location as the destination and posts:

```text
source storage location -> transit location
```

### Put from transit location

The operator enters the destination location, SKU, and quantity. WMS supplies
the transfer's transit location as the source and posts:

```text
transit location -> destination storage location
```

Putting inventory back into a location from which it was previously picked is
an ordinary put action. No special reversal workflow is required.

### Direct movement

The operator enters the source location, destination location, SKU, and
quantity. WMS posts one movement:

```text
source storage location -> destination storage location
```

A direct movement must not be expanded into artificial movements through a
transit location. Direct and transit movements may be mixed in one transfer.

## Transit locations and zone types

A zone has an explicit type. The MVP supports:

- `Storage` for ordinary warehouse storage;
- `Transit` for temporary inventory locations such as trolleys;
- `Receiving` for inbound receiving locations;
- `Shipping` for outbound shipping locations.

Zones are created with an explicit type. The active receiving and shipping
workflows filter locations by their respective zone types and validate the type
on the server. An inventory transfer's transit location must
belong to a `Transit` zone in the transfer warehouse. Ordinary pick and put
locations used by the transfer flow must belong to `Storage` zones. Direct
movement into or out of a transit location is not allowed; transit inventory is
handled only by the pick and put actions of the active transfer that owns it.

The transit location is optional because a transfer may contain direct movements
only. If it is needed, it is selected once and becomes transfer context:

- the operator does not select it again for every pick or put;
- it must have no positive inventory balance when assigned;
- it can belong to only one non-completed inventory transfer at a time;
- a transfer can use at most one transit location;
- after the first movement through it, it cannot be changed or removed.

These restrictions make the physical inventory in a transit location
unambiguously attributable to one active inventory transfer. Multiple trolleys
per transfer, preloaded trolleys, and sharing a trolley between active transfers
are not supported by the MVP.

## Transfer lifecycle

The statuses are:

```text
Draft -> InProgress -> Completed
```

- `Draft`: the transfer exists but has no posted movements.
- `InProgress`: set automatically when the first movement is posted.
- `Completed`: set explicitly by the operator after the work is finished.

A draft with no movements may be physically deleted. There is no cancelled
status. Once the first movement has been posted, the transfer can never be deleted.
Pausing a work session does not change status: the transfer remains `InProgress`,
the inventory remains recorded in its current locations, and any authorized
operator may continue it later.

Completing a transfer requires:

- at least one posted movement;
- if a transit location was used, no positive inventory balance of any SKU in
  that location.

Completion is always explicit. An empty transit location does not automatically
complete the transfer because the operator may intend to pick more inventory.
After completion, the transfer is read-only and its transit location is
available for another transfer.

## Validation and consistency

For every action WMS validates at the time of posting that:

- the transfer is not completed;
- the quantity is greater than zero;
- source and destination are different;
- all locations belong to the transfer warehouse and have the zone types required
  by the selected action;
- the source has sufficient current physical balance for the SKU;
- for a put, the transit location has sufficient current physical balance for
  the SKU;
- the transit location still belongs exclusively to this active transfer.

Movement creation, balance updates, turnover creation, the automatic first
status transition, and persistence are performed by one EF Core
`SaveChangesAsync` call. Preliminary UI checks are advisory only; the command
service repeats the business validations before saving.

The transit balance is aggregated by SKU. Inventory of the same SKU picked from
different source locations loses its source attribution while on the trolley
and may be put into one or several destination locations. Future lot, serial,
expiry, pallet, or other inventory dimensions must remain distinct if those
dimensions are introduced.

## Operator experience

The initial web UI uses one full transfer work page for both initialization and
subsequent work. Before the warehouse is confirmed, the page already shows the
recognizable work layout, but only warehouse selection and the explicit start
action are enabled. Confirming the warehouse creates the draft and locks the
warehouse; merely changing the selected value does not create a document. A
separate creation dialog or warehouse-only page is not used.

After warehouse confirmation, the web UI exposes separate, explicit commands:

- pick to trolley;
- put from trolley;
- move directly.

When a transit location is assigned, it remains visible as transfer context and is
filled automatically for pick and put actions. The work page shows the current
transit inventory by SKU and the immutable movement history in execution order.
It permits pick and put actions to be freely interleaved.

Without a transit location, direct movement and transit selection are available,
while pick and put are disabled. The layout and process vocabulary should remain
recognizable for a future mobile client without copying a mobile layout into the
web application.

A future mobile client may scan a transit location to open its active transfer or
offer to create a new transfer when the location is free. That shortcut is not
required for the initial web UI.

## Audit

Each movement is recorded with the inventory transfer as recorder, its chronological
transfer line or sequence number, posting time, and the operator who confirmed the
action. The transfer records its number, warehouse, status, creation time,
completion time, creator, and completing user where the application's available
identity model permits it.

The transfer history must show the real movement route and must not synthesize or
collapse posted movements.

## Non-goals

- Planned transfer tasks or comparison of plan and fact.
- Integration or synchronization with 1C.
- Inter-warehouse transfers.
- Reservations or long-lived inventory locks before confirmation.
- Multiple transit locations in one transfer.
- Shared, preloaded, or concurrently used transit locations.
- Lot, serial, expiry-date, pallet, or container tracking.
- Editing or deleting posted movements.
- A special administrative correction workflow. A registered-versus-physical
  discrepancy is handled through inventory counting until such a workflow is
  designed.
- Mobile scanning workflows.

## Acceptance criteria

1. An operator can create an inventory transfer for one warehouse without
   selecting a transit location.
2. A movement-free draft can be deleted; a transfer with a posted movement cannot.
3. The first successfully posted action changes the transfer from `Draft` to
   `InProgress`.
4. An operator can directly move an available SKU quantity between two ordinary
   locations, and balances and turnovers change immediately.
5. An operator can assign an empty, free location from a transit zone once, pick
   inventory to it, and put inventory from it without entering that location on
   each action.
6. Pick, put, and direct actions can be interleaved in any chronological order.
7. The same SKU can be picked from multiple locations, put in partial quantities,
   and distributed among multiple locations without exceeding its current
   transit balance.
8. A second active transfer cannot use the same transit location.
9. Insufficient source balance, cross-warehouse movement, invalid zone usage,
   and same-source-and-destination movement are rejected without partial balance
   or turnover changes.
10. A transfer using transit inventory cannot be completed until the transit
    location is empty; an empty location does not complete it automatically.
11. A completed transfer and all its posted movements are read-only.
12. The movement history identifies the actual source, destination, SKU,
    quantity, time, sequence, and confirming operator.

## Technical decisions

- Transfer commands follow the existing MVP application-service style: read
  the required data, validate the business rules, change tracked entities, and
  call `SaveChangesAsync` once. Explicit transactions and special concurrency
  handling are deferred until an observed case requires them.
- Transfer numbers use the local creation time in `yyMMdd-HHmmss` format. A
  dedicated numbering subsystem is outside the MVP until numbering gains
  additional business requirements.
- Application identity continues to use the existing string user identifier.
  Each movement stores it in `ConfirmedBy`; transfer creation, start, and
  completion retain their corresponding audit users and timestamps.
- The exact web page component layout remains a Stage 4 decision within the
  operator-experience rules above.

The staged implementation plan is defined in `implementation-plan.md` in this
specification directory.
