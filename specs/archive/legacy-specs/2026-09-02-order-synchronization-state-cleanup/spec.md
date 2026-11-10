# Order synchronization state cleanup

Status: frozen reference.

## Outcome

Keep only synchronization attributes that have an explicit current business
or audit use, so the order state remains understandable and maintainable.

## Scope

- Remove the persisted acknowledged fingerprint from receiving and shipping
  orders.
- Keep the current synchronization fingerprint used to identify the exact
  source state and protect acknowledgement.
- Keep the acknowledgement user and time as the human-readable audit record.
- Remove the redundant `External` prefix from the remaining synchronization
  property and database-column names.

## Acceptance criteria

- Acknowledgement still performs the existing fresh fingerprint comparison.
- Acknowledgement still records who accepted the differences and when.
- Receiving and shipping orders no longer contain or persist a separate
  acknowledged fingerprint.
- Remaining synchronization properties have concise `Synchronization...`
  names without changing their meaning.
- The EF Core model and database migration agree.
- The solution builds without errors.

## Open questions

None.
