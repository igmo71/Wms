# Implementation Plan: Add SKU barcodes during receiving

**Branch**: `codex/008-receiving-sku-barcodes` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

## Summary

Give current-cycle Mobile receiving workers a barcode scan/save action on each SKU card. Persist WMS-owned barcode pairs with command audit, replace destructive 1C barcode imports with atomic source reconciliation, and add a Web duplicate filter and Manager-only removal for WMS-owned pairs. Preserve the current composite SKU/value identity because 1C may supply one value for different SKUs; ambiguous scans never choose a SKU silently.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Dependencies**: ASP.NET Core, Blazor/MudBlazor, .NET MAUI, EF Core
**Storage**: SQL Server with compatibility level 120; existing `SkuBarcodes` and `WarehouseAuditEvents`
**Testing**: Existing xUnit SQL Server LocalDB integration suite for import data loss and concurrent manual mapping risks; developer executes tests
**Target Platform**: Authenticated Mobile V1 API, Android Mobile client, WebApp and 1C notification/manual-import adapters
**Project Type**: Existing Domain/Application/Infrastructure feature with legacy 1C adapter and two clients
**Performance Goals**: One server-paged Web duplicate lookup; no catalog-wide load on a Mobile order view
**Constraints**: No outbound 1C write; retain SQL Server 2014 compatibility; developer creates/applies migration and runs operational verification
**Scale/Scope**: Single-SKU and complete 1C barcode snapshots, individual receiving additions and Web removals

## Constitution Check

**Before design**: Pass. 1C owns imported catalog facts while WMS owns local operational additions. No stock facts change. Use an application use case for local mutation and source reconciliation, EF Core for persistence, and the existing 1C adapter for transport. Add only source ownership and a lookup index to the existing barcode table. Material data-loss and concurrency risks warrant focused integration tests. Developer owns builds, tests and migration commands.

**After design**: Pass. The legacy delete-then-insert path is replaced, not retained alongside the new reconciliation. Mobile and Web commands share one barcode mutation boundary; the existing command executor makes user actions repeat-safe with audit. Import snapshots are read before any database mutation, and each accepted scope commits once. No dependency cycle or outbound 1C path is added.

## Design

1. Extend the existing `SkuBarcode` pair with current source (`OneC` or `Wms`). Existing rows migrate as `OneC`; the existing `(SkuId, Value)` primary key remains. Add a **nonunique** value index for lookup and duplicate filtering. Manual-add and removal history lives in `WarehouseAuditEvents`, so it survives promotion and row deletion. Do not put a global unique constraint on `Value`.
2. Replace `InformationRegister_ШтрихкодыНоменклатуры_Service` calls to `SkuBarcodeService.DeleteRangeAsync/DeleteAllAsync/CreateListAsync` with a new application synchronization use case. Parse and validate the complete 1C response first. Within one coordinated database save for the requested scope, deduplicate identical incoming pairs, insert new `OneC` pairs, promote matching `Wms` pairs to `OneC`, remove stale `OneC` pairs, and retain unmatched `Wms` pairs. A failed read or save leaves the previous scope unchanged. A full import and a single-SKU notification use the same reconciliation rules.
3. Add a receiving barcode command using the existing command executor and audit receipt. Check Operator membership, current-cycle participation, active order, current SKU, active catalog SKU, valid value and existing associations before staging the new `Wms` pair. Existing same-SKU pair is a no-op result; an existing other-SKU pair is a business conflict with that SKU identified. Coordinate all barcode mutations, including imports, so concurrent manual requests cannot both create a new ambiguous mapping. Keep the source import allowed to introduce a duplicate value from 1C.
4. Add a Manager-only command to remove only a `Wms` pair, recording actor/time/value/SKU in the existing audit. A stale Web confirmation or a pair promoted to `OneC` must be rejected without deleting it. The command does not touch receiving or inventory facts.
5. Extend the Mobile order item response with `HasBarcode`; calculate it with a set-based query for current order SKUs. Add a barcode action alongside zero confirmation on `ReceivingOrderSkuPage`, with a pale/strong visual state, scanner/camera capture isolated from the underlying order scanner, explicit review/save, and the existing uncertain-command retry flow. Refresh order data after success so the icon reflects persisted state.
6. Replace the Web barcode list's raw entity binding with a read model containing SKU, value, source and duplicate status. Apply the `distinct SkuId > 1` duplicate filter server-side before paging; show source and a confirmed removal action only for `Wms` rows. Preserve existing search, sort, inactive-SKU switch and manual refresh.

## Project Structure

### Documentation

```text
specs/008-receiving-sku-barcodes/
  spec.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/sku-barcodes.md
  tasks.md
  checklists/requirements.md
```

### Source Code

```text
Wms.Domain/SkuBarcode.cs
Wms.Application/SkuBarcodes/                 # new application use cases/contracts
Wms.Infrastructure/Data/Configurations/SkuBarcodeConfiguration.cs
Wms.Infrastructure/Data/                    # barcode store, query and registration
Wms.Infrastructure/Data/Migrations/         # developer-generated migration
Wms/Integration/OneS/Services/InformationRegister_ШтрихкодыНоменклатуры_Service.cs
Wms/Application/SkuBarcodes/SkuBarcodeService.cs   # retire replaced mutations/query
Wms.Contracts/Mobile/V1/MobileReceivingOrderContracts.cs
Wms.WebApi/Mobile/MobileReceivingOrderEndpoints.cs
Wms.Mobile/Services/MobileReceivingOrderClient.cs
Wms.Mobile/Services/ReceivingOrderReceivingProcess.cs
Wms.Mobile/Pages/ReceivingOrders/ReceivingOrderSkuPage.xaml(.cs)
Wms.WebApp/Components/Pages/SkuBarcodePages/Index.razor(.cs)
Wms.Tests/                                     # focused integration risk scenarios
.specify/memory/current/{integration,receiving,clients,identity}.md
.specify/memory/architecture.md               # only if lasting structure changes
```

**Structure Decision**: Move barcode business operations to the existing new Application/Infrastructure pattern while keeping 1C HTTP/OData parsing in its existing adapter. Retire the legacy barcode mutation path. The developer creates the EF migration after the model and configuration change; inspect generated migration and snapshot before applying it.

## Delivery Gates

- After source-ownership mapping is implemented, ask the developer to generate the EF migration. Inspect its default/backfill of existing rows as `OneC`, the retained composite key and the nonunique value index before asking the developer to apply it.
- Protect source reconciliation from data loss and manual-add race ambiguity with the smallest focused SQL integration scenarios not covered by existing tests. Reuse existing command-executor replay coverage.
- Give the developer exact focused build/test and Mobile/Web interaction checks from [quickstart.md](quickstart.md). Review reported results before marking the feature complete, then distill implemented behavior into owning current-state documents.
