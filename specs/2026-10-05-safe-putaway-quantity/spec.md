# Safe quantity confirmation before LPN putaway

Status: **Frozen** — TSD behavior accepted on 2026-10-06. Camera fallback remains unverified.

Current rules belong to [Receiving and putaway](../../docs/PROJECT_CONTEXT.md#receiving-and-putaway).

## Outcome and scope

On the Mobile LPN putaway quantity screen, a destination scan posts only the
quantity explicitly confirmed by the employee. Keep recommendations and quantity
on one screen; remove the separate advance-to-destination button and step.
Web receiving improvements and server posting rules are outside this increment.

## Rules

- Selecting an SKU starts with an empty, unconfirmed amount and no keyboard focus.
- The employee enters an amount and finishes input (keyboard Done or leaving the
  field), or taps «Весь остаток». Both paths validate a positive amount within the
  displayed remainder and the existing decimal range/scale before confirmation.
- Focusing or changing the field revokes confirmation. A focused, unconfirmed or
  invalid amount cannot post. Scans received before confirmation must not become
  actionable later when queued on the UI thread.
- Confirmation keeps the same screen and shows the confirmed amount and destination
  scan instruction. A new destination scan posts immediately, without another button.
- Busy/uncertain commands block editing, remainder selection and new scans. Explicit
  retry retains the original amount, destination and request id.
- A definitive destination rejection retains a still-valid confirmed quantity.
  After conflict refresh, missing SKU, blocked LPN or an insufficient remainder
  revokes confirmation. A successful movement returns to SKU selection; the next
  selection starts unconfirmed, including the same SKU with a partial remainder.
- ScanWedge uses Intent only. Buttons suppress native Android focus; navigation
  and scanner activity do not open the keyboard.

## Acceptance and verification

Developer builds Mobile and checks on the target Urovo (and camera fallback):
1. Opening an SKU and scanning a destination posts nothing before confirmation.
2. Enter a partial amount: scanning while focused posts nothing; Done or leaving
   the field confirms it, hides the keyboard, and the next scan posts that amount.
3. «Весь остаток» explicitly confirms the displayed remainder. No advance button
   is needed; recommendations remain visible.
4. Editing revokes confirmation. Empty, zero, negative, excessive or overprecision
   amounts cannot post. A scan queued during editing cannot post after confirmation.
5. An invalid destination allows another scan with the same confirmed amount;
   a refreshed insufficient remainder requires another valid confirmation.
6. A lost response exposes explicit retry and blocks changes/scans. Retry sends
   the original request and creates at most one movement. After success, selecting
   another or the same SKU starts with an empty, unconfirmed amount.
7. Returning to the page does not execute a stale scanner callback or open the keyboard.

Existing server tests own atomic/repeat-safe putaway. The changed UI focus and
ScanWedge behavior require device verification; no new test infrastructure is added.

## Open questions

None. Camera fallback verification is tracked in the roadmap.
