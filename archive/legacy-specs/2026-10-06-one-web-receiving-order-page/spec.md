# One Web receiving order page

Status: **Frozen** — accepted on 2026-10-06 after the developer confirmed the
WebApp build and application startup following the final list-indicator change.

## Outcome

The Web list opens every receiving order at `/receiving-orders/{id}`. That page
shows the appropriate actions and audit for its current lifecycle state without
sending the user to a separate in-process address. Completion updates the same
page to the received view.

## Scope

- Keep one canonical Web URL for ready, in-progress and received orders.
- Keep the route component focused on loading the order and choosing the
  in-progress or ready/received view. Each view composes the shared header,
  discrepancies, confirmation and order contents.
- Reuse the existing in-progress completion and read-only order-content UI so
  source reconciliation, explicit Manager approval and repeat-safe retries keep
  their behavior. Do not introduce a second synchronization call when opening
  an in-progress order.
- After successful Web completion, show received details on the same URL.
- Remove the separate `/in-process` route and all new links to it.
- Preserve the current read-only nomenclature/pallet layout and audits.
- Show the number of open pallets on the "Палеты" tab when nonzero, instead
  of repeating their codes in a separate warning above the tabs.
- In the list's "Сверка" column, show an orange warning for a received order
  with Manager-approved quantity differences and explain that the 1C document
  is synchronized. Other rows retain their source-synchronization indicator.
- In receiving and received views, show one "План" column when all order-line
  quantities in WMS and 1C match. If any line differs or a compared 1C value
  is unavailable, show "План WMS" and "План 1С" columns for the whole order.
  The latter is the fresh 1C plan while receiving and the saved completion
  snapshot after acceptance. A ready order shows its stored plan only.
- Use the same order title and summary header across statuses, with status and
  receiving participants in the header below the number and date. Remove the
  redundant informational banners and disabled Web start action.
- Show one shared "Расхождения" block above the order content. Put the Manager
  confirmation in a separate block at the same position when its reason is being
  entered and after it has been saved. Omit generic explanatory text under the
  discrepancy title and omit putaway action timestamps from the summary header.
  Preserve the source-change acknowledgement
  action for a ready order and Manager-only quantity approval during receiving.

Completion guidance, quantity presentation and SKU-to-LPN visibility changes
are separate future increments. Mobile and server business rules are unchanged.

## Acceptance

1. The Web list opens ready, in-progress and received orders at one URL.
2. An in-progress order still offers the authorized completion action and
   shows the current synchronization check, source plan, LPNs and audit.
3. Successful completion changes the page to the received view at the same URL;
   Manager-only differences, reason and replay behavior are preserved.
4. `/in-process` is no longer a Web route; the order list links only to the
   canonical URL.
5. The in-progress route does not make a duplicate fresh 1C check.
6. Ready, receiving and received views share the title/header, discrepancy
   presentation and order-content components. The reason appears separately
   only where a Manager decision is needed or was recorded.

## Verification

Developer builds WebApp and manually opens all three states, completes an exact
order, checks a Manager difference when one is available, and follows an old
bookmark. Existing receiving safety tests protect posting/replay; no new test
is needed for this navigation change.

## Open questions

None.
