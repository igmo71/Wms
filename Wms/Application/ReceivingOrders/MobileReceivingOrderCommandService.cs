using System.Globalization;
using Wms.Application.MobileCommands;
using Wms.Common;

namespace Wms.Application.ReceivingOrders;

public sealed class MobileReceivingOrderCommandService(
    MobileCommandExecutor mobileCommandExecutor,
    ReceivingOrderCommandService receivingOrderCommandService,
    PutawayCommandService putawayCommandService)
{
    private const string StartReceivingCommand = "receiving-order.start-receiving";
    private const string IncrementFactCommand = "receiving-order.increment-fact";
    private const string SetFactCommand = "receiving-order.set-fact";
    private const string CompleteReceivingCommand = "receiving-order.complete-receiving";
    private const string StartPutawayCommand = "receiving-order.start-putaway";
    private const string AddPutawayMovementCommand = "receiving-order.add-putaway-movement";
    private const string DeletePutawayMovementCommand = "receiving-order.delete-putaway-movement";
    private const string CompletePutawayCommand = "receiving-order.complete-putaway";

    public Task<OperationResult<Guid>> StartReceivingAsync(
        Guid orderId,
        Guid receivingLocationId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        ExecuteOrderActionAsync(
            StartReceivingCommand,
            orderId,
            clientRequestId,
            userId,
            Hash(orderId, receivingLocationId),
            (dbContext, token) => receivingOrderCommandService.StageStartReceivingAsync(
                dbContext,
                orderId,
                receivingLocationId,
                userId,
                token),
            ct);

    public Task<OperationResult<Guid>> IncrementItemFactAsync(
        Guid orderId,
        int lineNumber,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        ExecuteOrderActionAsync(
            IncrementFactCommand,
            orderId,
            clientRequestId,
            userId,
            Hash(orderId, lineNumber),
            (dbContext, token) => receivingOrderCommandService.StageIncrementItemFactAsync(
                dbContext,
                orderId,
                lineNumber,
                token),
            ct);

    public Task<OperationResult<Guid>> SetItemFactQuantityAsync(
        Guid orderId,
        int lineNumber,
        double factQuantity,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        ExecuteOrderActionAsync(
            SetFactCommand,
            orderId,
            clientRequestId,
            userId,
            Hash(orderId, lineNumber, factQuantity),
            (dbContext, token) => receivingOrderCommandService.StageSetItemFactQuantityAsync(
                dbContext,
                orderId,
                lineNumber,
                factQuantity,
                token),
            ct);

    public Task<OperationResult<Guid>> CompleteReceivingAsync(
        Guid orderId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        ExecuteOrderActionAsync(
            CompleteReceivingCommand,
            orderId,
            clientRequestId,
            userId,
            Hash(orderId),
            (dbContext, token) => receivingOrderCommandService.StageSetReceivedAsync(
                dbContext,
                orderId,
                userId,
                token),
            ct);

    public Task<OperationResult<Guid>> StartPutawayAsync(
        Guid orderId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        ExecuteOrderActionAsync(
            StartPutawayCommand,
            orderId,
            clientRequestId,
            userId,
            Hash(orderId),
            (dbContext, token) => putawayCommandService.StageStartAsync(
                dbContext,
                orderId,
                userId,
                token),
            ct);

    public Task<OperationResult<Guid>> AddPutawayMovementAsync(
        Guid orderId,
        int lineNumber,
        Guid destinationStorageLocationId,
        double quantity,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        mobileCommandExecutor.ExecuteAsync(
            AddPutawayMovementCommand,
            clientRequestId,
            Hash(orderId, lineNumber, destinationStorageLocationId, quantity),
            userId,
            async (dbContext, token) =>
            {
                var result = await putawayCommandService.StageAddMovementAsync(
                    dbContext,
                    orderId,
                    lineNumber,
                    destinationStorageLocationId,
                    quantity,
                    token);
                return result.IsSuccess ? result.Value!.Id : result.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> DeletePutawayMovementAsync(
        Guid orderId,
        Guid movementId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        mobileCommandExecutor.ExecuteAsync(
            DeletePutawayMovementCommand,
            clientRequestId,
            Hash(orderId, movementId),
            userId,
            async (dbContext, token) =>
            {
                var result = await putawayCommandService.StageDeleteMovementAsync(
                    dbContext,
                    orderId,
                    movementId,
                    token);
                return result.IsSuccess ? movementId : result.Error!;
            },
            ct);

    public Task<OperationResult<Guid>> CompletePutawayAsync(
        Guid orderId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        ExecuteOrderActionAsync(
            CompletePutawayCommand,
            orderId,
            clientRequestId,
            userId,
            Hash(orderId),
            (dbContext, token) => putawayCommandService.StageCompleteAsync(
                dbContext,
                orderId,
                userId,
                token),
            ct);

    private Task<OperationResult<Guid>> ExecuteOrderActionAsync(
        string commandType,
        Guid orderId,
        Guid clientRequestId,
        string userId,
        string requestHash,
        Func<Data.ApplicationDbContext, CancellationToken, Task<OperationResult>> stageAction,
        CancellationToken ct) =>
        mobileCommandExecutor.ExecuteAsync(
            commandType,
            clientRequestId,
            requestHash,
            userId,
            async (dbContext, token) =>
            {
                var result = await stageAction(dbContext, token);
                return result.IsSuccess ? orderId : result.Error!;
            },
            ct);

    private static string Hash(params Guid[] ids) =>
        MobileCommandExecutor.ComputeHash(string.Join('|', ids.Select(x => x.ToString("N"))));

    private static string Hash(Guid orderId, int lineNumber) =>
        MobileCommandExecutor.ComputeHash(string.Join(
            '|',
            orderId.ToString("N"),
            lineNumber.ToString(CultureInfo.InvariantCulture)));

    private static string Hash(Guid orderId, int lineNumber, double quantity) =>
        MobileCommandExecutor.ComputeHash(string.Join(
            '|',
            orderId.ToString("N"),
            lineNumber.ToString(CultureInfo.InvariantCulture),
            quantity.ToString("R", CultureInfo.InvariantCulture)));

    private static string Hash(
        Guid orderId,
        int lineNumber,
        Guid destinationStorageLocationId,
        double quantity) =>
        MobileCommandExecutor.ComputeHash(string.Join(
            '|',
            orderId.ToString("N"),
            lineNumber.ToString(CultureInfo.InvariantCulture),
            destinationStorageLocationId.ToString("N"),
            quantity.ToString("R", CultureInfo.InvariantCulture)));
}
