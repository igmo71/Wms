# Validation guide: topology and opening balances

The developer runs builds, migrations, tests and application launches under [AGENTS.md](../../AGENTS.md). Use an isolated disposable database and the agreed `Топология и Начальные остатки.xlsx` export. The [staging runbook](../../.specify/runbooks/staging.md#cutover-rehearsal-database) owns rehearsal cleanup; never run it against a database containing work to preserve. The two stages use the same unchanged workbook. Synchronize the 1C SKU and unit catalogs before the stock stage.

After the feature is implemented and its migration is generated, the developer runs these repository-root checks and reports the result:

```powershell
dotnet build Wms.slnx -nologo -v:minimal
dotnet test Wms.Tests/Wms.Tests.csproj -nologo -v:minimal
```

Inspect the generated migration before applying it to the disposable database. Start the existing WebApp and WebApi using the project's normal local setup, then complete the scenarios below.

## Transition order

For the final cutover, finish open work in the old WMS or resolve it separately. Stop old WMS warehouse operations and record the stop time; unfinished orders, LPNs and transfers are not imported. Export the complete topology and balances after the stop, record the export/snapshot time in Moscow time, and keep the original XLSX unchanged. Do not run warehouse operations in both systems at once.

In the new WMS, preview and confirm topology, synchronize the 1C SKU and unit catalogs, then preview and confirm opening stock from that same XLSX with the recorded snapshot time. Reopen the import dialog to inspect the saved protocol before allowing operations in the new WMS. A rehearsal may use a copy of an earlier export in an isolated database, but only the final stopped-warehouse export can establish live opening stock.

## Topology rehearsal

1. Sign in to WebApp as an Administrator and select the target warehouse.
2. Preview the full workbook. The current `Топология и Начальные остатки.xlsx` has 12,597 data rows; the known topology preview reported 11,365 distinct cells and 4,348 empty cells. The preview must show the two excluded zero-prefix addresses, two excluded virtual transit addresses, two excluded non-address labels, the empty `4A10A1` exclusion, and the `9Z99A1` correction. If the newly exported workbook differs, use its own preview counts.
3. Confirm topology. No opening balance exists yet. Check that the eight gate addresses sit under `Ворота`, that rack/mezzanine cells retain their room hierarchy, that 15 reviewed cells allow multiple SKUs and eight reviewed cells do not.
4. Reopen the dialog and inspect the saved topology protocol without uploading the file. It must show source name/hash, actor/time, counts, exclusions and the `9Z99A1` correction. A new confirmation request for this warehouse is rejected; an exact retry of the original request key returns its recorded result. Check existing-cell reuse and address/purpose conflicts in a separate clean disposable rehearsal, before confirming topology.

## Opening-stock rehearsal

1. Verify the effective WebApp `OneCClient:BaseAddress` points to the 1C UT database that owns the workbook's SKU GUIDs; a rehearsal export from production may not match a `dobroga2025_dev2` catalog. With explicit Manager membership, use «Обновить из 1С» on «Единицы измерения», then on «SKU». Upload the same unedited workbook, supply its Moscow-time snapshot moment, and preview stock. Resolve reported missing SKU or unit mismatch in the catalog, then retry the stock preview with the same workbook. If the workbook itself changes, start a new rehearsal in a cleaned database. When all stock rows fail catalog validation, accepted quantity, negative-row and missing-date counters remain zero until catalog issues are resolved.
2. Check the accepted, corrected, excluded, negative and undated row counts shown by this workbook's stock preview. Stock on `0A00A0` and `0A00A1` is intentionally excluded; the chain's one unit from the `9Z99A1` picking-labelled row belongs to shipping. The mixed shipping cells `8F88A1`, `9Z99A1`, `9Z99A2`, and `9Z99A3` are not storage mixing violations. Negative shipping stock keeps its sign, and missing required dates remain unknown.
3. Confirm stock, then reopen the dialog without uploading a file. Inspect the saved status, source hash, snapshot/confirmation times, actor, accepted/corrected/excluded counts, zero-prefix stock loss, `9Z99A1` correction, signed and undated exceptions, and the eight current prohibited mixed-cell violations. Check WMS balances by cell, SKU and optional date; new placement into a still mixed prohibited cell is rejected. Confirm that the 1C-required-date SKUs have WMS tracking enabled.
4. The focused LocalDB tests cover exact request replay, cross-user concurrent confirmation, and rejection of another workbook without a second stock effect. In a separate disposable rehearsal, force a blocking stock issue and verify that topology remains but movements, balances, turnovers and SKU-flag changes from that attempt do not.

## Operational boundary check

On an otherwise valid receiving LPN, scan one picking cell and one reserve cell as putaway destinations. Both must be accepted by the new receiving route. An older picking scan must still reject a reserve source. Full picking, replenishment, shipping and count readiness is outside this feature; current legacy flows remain subject to their documented limits.

Before resuming work in the new WMS, compare the saved protocol and selected WMS balances against the export. A separate 1C reconciliation report is outside this feature.
