using System.Globalization;
using Wms.Application.MobileCommands;
using Wms.Common;
using Wms.Data;

namespace Wms.Application.Inventory.Transfers;

public sealed class MobileInventoryTransferCommandService(
    MobileCommandExecutor mobileCommandExecutor,
    InventoryTransferCommandService transferCommandService)
{
    private const string CreateDraftCommand = "inventory-transfer.create-draft";
    private const string MoveDirectCommand = "inventory-transfer.move-direct";
    private const string PickToTransitCommand = "inventory-transfer.pick-to-transit";
    private const string PutFromTransitCommand = "inventory-transfer.put-from-transit";
    private const string CompleteCommand = "inventory-transfer.complete";

    public Task<OperationResult<Guid>> CreateDraftAsync(
        Guid warehouseId,
        Guid? transitStorageLocationId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        mobileCommandExecutor.ExecuteAsync(
            CreateDraftCommand,
            clientRequestId,
            ComputeCreateDraftHash(warehouseId, transitStorageLocationId),
            userId,
            async (dbContext, token) =>
            {
                var result = await transferCommandService.StageCreateAsync(
                    dbContext,
                    warehouseId,
                    transitStorageLocationId,
                    userId,
                    token);
                return result.IsSuccess ? result.Value!.Id : result.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> MoveDirectAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        decimal quantity,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        mobileCommandExecutor.ExecuteAsync(
            MoveDirectCommand,
            clientRequestId,
            ComputeMoveDirectHash(
                transferId,
                sourceStorageLocationId,
                destinationStorageLocationId,
                stockKeepingUnitId,
                quantity),
            userId,
            async (dbContext, token) =>
            {
                var result = await transferCommandService.StageDirectMovementAsync(
                    dbContext,
                    transferId,
                    sourceStorageLocationId,
                    destinationStorageLocationId,
                    stockKeepingUnitId,
                    quantity,
                    userId,
                    token);
                return result.IsSuccess ? result.Value!.Id : result.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> PickToTransitAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid stockKeepingUnitId,
        decimal quantity,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        MoveTransitAsync(
            PickToTransitCommand,
            transferId,
            sourceStorageLocationId,
            stockKeepingUnitId,
            quantity,
            clientRequestId,
            userId,
            isPick: true,
            ct);

    public Task<OperationResult<Guid>> PutFromTransitAsync(
        Guid transferId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        decimal quantity,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        MoveTransitAsync(
            PutFromTransitCommand,
            transferId,
            destinationStorageLocationId,
            stockKeepingUnitId,
            quantity,
            clientRequestId,
            userId,
            isPick: false,
            ct);

    public Task<OperationResult<Guid>> CompleteAsync(
        Guid transferId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        mobileCommandExecutor.ExecuteAsync(
            CompleteCommand,
            clientRequestId,
            ComputeCompleteHash(transferId),
            userId,
            async (dbContext, token) =>
            {
                var result = await transferCommandService.StageCompleteAsync(
                    dbContext,
                    transferId,
                    userId,
                    token);
                return result.IsSuccess ? transferId : result.Error!;
            },
            ct);

    private Task<OperationResult<Guid>> MoveTransitAsync(
        string commandType,
        Guid transferId,
        Guid enteredStorageLocationId,
        Guid stockKeepingUnitId,
        decimal quantity,
        Guid clientRequestId,
        string userId,
        bool isPick,
        CancellationToken ct) =>
        mobileCommandExecutor.ExecuteAsync(
            commandType,
            clientRequestId,
            ComputeTransitMovementHash(
                transferId,
                enteredStorageLocationId,
                stockKeepingUnitId,
                quantity),
            userId,
            async (dbContext, token) =>
            {
                var result = isPick
                    ? await transferCommandService.StagePickMovementAsync(
                        dbContext,
                        transferId,
                        enteredStorageLocationId,
                        stockKeepingUnitId,
                        quantity,
                        userId,
                        token)
                    : await transferCommandService.StagePutMovementAsync(
                        dbContext,
                        transferId,
                        enteredStorageLocationId,
                        stockKeepingUnitId,
                        quantity,
                        userId,
                        token);
                return result.IsSuccess ? result.Value!.Id : result.Error!;
            },
            ct);

    private static string ComputeCreateDraftHash(
        Guid warehouseId,
        Guid? transitStorageLocationId)
    {
        var value = transitStorageLocationId is Guid locationId
            ? $"{warehouseId:N}|{locationId:N}"
            : warehouseId.ToString("N");
        return MobileCommandExecutor.ComputeHash(value);
    }

    private static string ComputeMoveDirectHash(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        decimal quantity) =>
        MobileCommandExecutor.ComputeHash(string.Join(
            '|',
            transferId.ToString("N"),
            sourceStorageLocationId.ToString("N"),
            destinationStorageLocationId.ToString("N"),
            stockKeepingUnitId.ToString("N"),
            quantity.ToString("G29", CultureInfo.InvariantCulture)));

    private static string ComputeTransitMovementHash(
        Guid transferId,
        Guid enteredStorageLocationId,
        Guid stockKeepingUnitId,
        decimal quantity) =>
        MobileCommandExecutor.ComputeHash(string.Join(
            '|',
            transferId.ToString("N"),
            enteredStorageLocationId.ToString("N"),
            stockKeepingUnitId.ToString("N"),
            quantity.ToString("G29", CultureInfo.InvariantCulture)));

    private static string ComputeCompleteHash(Guid transferId) =>
        MobileCommandExecutor.ComputeHash(transferId.ToString("N"));
}
