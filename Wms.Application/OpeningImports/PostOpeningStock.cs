using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.OpeningImports;

public sealed record OpeningStockPostingResult(int Movements, int Balances);

public sealed class PostOpeningStock(IOpeningInventoryPostingStore store)
{
    public async Task<OperationResult<OpeningStockPostingResult>> ExecuteAsync(
        OpeningStockPlan plan,
        OpeningStockConfirmationState confirmationState,
        Guid importId,
        string confirmedBy,
        DateTimeOffset postedAtUtc,
        ICommandExecutionContext session,
        CancellationToken ct)
    {
        if (plan is null || !plan.CanConfirm || confirmationState is null
            || importId == Guid.Empty || string.IsNullOrWhiteSpace(confirmedBy)
            || postedAtUtc == default)
            return OperationError.Invalid("Для проведения нужен проверенный снимок начальных остатков.");
        var import = confirmationState.Import;
        if (!confirmationState.Snapshot.WarehouseExists
            || confirmationState.Snapshot.WarehouseDeleted
            || confirmationState.Snapshot.HasOperationalStock
            || confirmationState.Snapshot.HasOperationalWork
            || import is null || import.Id != importId
            || import.WarehouseId != plan.WarehouseId
            || import.Status != WarehouseOpeningImportStatus.TopologyImported
            || !string.Equals(import.SourceSha256,
                plan.SourceSha256, StringComparison.OrdinalIgnoreCase))
            return OperationError.Conflict("Состояние склада не соответствует проверенному снимку.");

        var rows = plan.Rows
            .Where(x => (x.Outcome is OpeningImportRowOutcome.Accepted
                or OpeningImportRowOutcome.Corrected)
                && x.Quantity.GetValueOrDefault() != 0)
            .OrderBy(x => x.RowNumber).ToArray();
        if (rows.Length != plan.Counts.NonzeroRows
            || rows.Select(x => x.RowNumber).Distinct().Count() != rows.Length)
            return OperationError.Invalid("Строки плана начальных остатков некорректны.");

        var snapshotLocations = confirmationState.Snapshot.Locations
            .ToDictionary(x => x.Address, StringComparer.Ordinal);
        var locationsByRow = new Dictionary<int, Guid>(rows.Length);
        foreach (var row in rows)
        {
            if (row.RowNumber <= 1 || row.Address is null
                || row.Purpose is null || row.StockKeepingUnitId is not Guid skuId
                || !WarehouseQuantity.IsSupported(row.Quantity!.Value)
                || !snapshotLocations.TryGetValue(row.Address, out var location)
                || !confirmationState.Skus.ContainsKey(skuId))
                return OperationError.Invalid($"Строка {row.RowNumber} отсутствует в проверенном снимке.");

            locationsByRow.Add(row.RowNumber, location.Id);
        }

        var state = await store.LoadOpeningAsync(session, plan.WarehouseId,
            locationsByRow.Values.Distinct().ToArray(), ct);
        if (state.Balances.Count != 0)
            return OperationError.Conflict("На складе уже есть остатки; повторная загрузка запрещена.");

        foreach (var row in rows)
        {
            var locationId = locationsByRow[row.RowNumber];
            if (!state.Locations.TryGetValue(locationId, out var location)
                || location.WarehouseId != plan.WarehouseId
                || location.Code != row.Address
                || location.IsFolder || location.DeletionMark
                || location.ActiveLock is not null
                || location.Warehouse is null || location.Warehouse.DeletionMark
                || location.Zone is null || location.Zone.DeletionMark
                || location.Zone.Type != row.Purpose
                || location.AllowsMultipleSkus !=
                    (row.MixingPolicy == OpeningImportMixingPolicy.ApprovedMultipleSkus))
                return OperationError.Conflict(
                    $"Ячейка строки {row.RowNumber} изменилась после проверки снимка.");
        }

        var balanceCount = 0;
        foreach (var row in rows)
        {
            var locationId = locationsByRow[row.RowNumber];
            var skuId = row.StockKeepingUnitId!.Value;
            var delta = row.Quantity!.Value;
            var movement = InventoryMovement.Create(Guid.NewGuid(), plan.WarehouseId,
                delta < 0 ? locationId : null,
                delta > 0 ? locationId : null,
                skuId, Math.Abs(delta), postedAtUtc,
                RecorderType.OpeningBalance, importId, row.RowNumber,
                confirmedBy, row.ExpirationDate);
            if (!movement.IsSuccess)
                return movement.Error!;

            var post = movement.Value!.Post(postedAtUtc);
            if (!post.IsSuccess)
                return post.Error!;

            var key = new OpeningBalanceKey(plan.WarehouseId, locationId,
                skuId, row.ExpirationDate);
            InventoryBalanceChange change;
            if (state.Balances.TryGetValue(key, out var balance))
            {
                var adjusted = balance.Adjust(delta, postedAtUtc, allowNegative: true);
                if (!adjusted.IsSuccess)
                    return adjusted.Error!;
                change = adjusted.Value!;
            }
            else
            {
                var created = InventoryBalance.Create(Guid.NewGuid(),
                    plan.WarehouseId, locationId, skuId, delta,
                    postedAtUtc, row.ExpirationDate, allowNegative: true);
                if (!created.IsSuccess)
                    return created.Error!;
                balance = created.Value!;
                state.Balances.Add(key, balance);
                session.Stage(balance);
                change = new InventoryBalanceChange(0, delta, delta);
                balanceCount++;
            }

            var turnover = InventoryTurnover.Create(Guid.NewGuid(),
                plan.WarehouseId, locationId, skuId, change,
                postedAtUtc, movement.Value.Id, row.ExpirationDate);
            if (!turnover.IsSuccess)
                return turnover.Error!;

            session.Stage(movement.Value);
            session.Stage(turnover.Value!);
        }

        foreach (var locationId in locationsByRow.Values.Distinct())
            state.Locations[locationId].AdvanceOperationalRevision();

        return new OpeningStockPostingResult(rows.Length, balanceCount);
    }
}
