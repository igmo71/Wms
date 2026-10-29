# Validation guide: Receiving SKU barcodes

The developer performs operational commands and interactive checks. Codex reviews the reported results and the generated migration before it is applied.

## Migration gate

After the ownership field and EF mapping are implemented, generate a migration using the existing WebApp startup project:

```powershell
dotnet ef migrations add AddSkuBarcodeSource --project Wms.Infrastructure/Wms.Infrastructure.csproj --startup-project Wms.WebApp/Wms.WebApp.csproj
```

Inspect the generated migration and snapshot: existing rows must become `OneC`, `(SkuId, Value)` stays the primary key, and the `Value` index is nonunique. Apply only after that review. If the WebApp's configured `DefaultConnection` points to the development database, use:

```powershell
dotnet ef database update --project Wms.Infrastructure/Wms.Infrastructure.csproj --startup-project Wms.WebApp/Wms.WebApp.csproj
```

For a different target, pass its actual connection string with `--connection`; never pass a placeholder literally.

## Focused automated verification

The focused SQL scenario covers local survival, promotion, failed-save atomicity,
and duplicate grouping before paging:

```powershell
dotnet test Wms.Tests/Wms.Tests.csproj --filter "FullyQualifiedName~Imports_preserve_local_pairs_and_roll_back_a_failed_snapshot" -nologo -v:minimal
```

Build the affected hosts and Android client:

```powershell
dotnet build Wms.WebApi/Wms.WebApi.csproj -nologo -v:minimal
dotnet build Wms.WebApp/Wms.WebApp.csproj -nologo -v:minimal
dotnet build Wms.Mobile/Wms.Mobile.csproj -nologo -v:minimal
```

Do not broaden automated testing without a concrete unresolved risk.

## Mobile and Web checks

1. On an active receiving order, join as Operator. Open a barcode action on a SKU without a code; scan, review and save a new code. Confirm the panel closes, the icon becomes prominent and a later scan resolves the SKU.
2. Add another code to an SKU that already has one. Try the same pair again and a value used by another SKU; verify one pair remains and the conflicting SKU is named.
3. Confirm the action cannot save when the worker is not a current-cycle participant or the order is inactive. Verify an uncertain response retains the exact scanned value and request ID for explicit retry.
4. Run per-SKU and full 1C imports with a still-local pair, then with that exact pair in 1C, then after deleting it in 1C. Confirm survival, promotion and removal in order, including retained manual audit.
5. Import the same value for two SKUs from 1C. Verify both rows appear under Web's duplicate filter with their source, and scanning reports ambiguity without selecting a SKU. Confirm manual SKU search still works.
6. As Manager, remove a `Wms` pair from Web and verify audit and later lookup. Confirm a `OneC` pair has no removal action and stale confirmation cannot delete a pair promoted by a fresh import.
