using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Data;

namespace Wms.Application.Inventory.Transfers;

public sealed class MobileInventoryTransferCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
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
        ExecuteIdempotentAsync(
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
        double quantity,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default) =>
        ExecuteIdempotentAsync(
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
        double quantity,
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
        double quantity,
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
        ExecuteIdempotentAsync(
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
        double quantity,
        Guid clientRequestId,
        string userId,
        bool isPick,
        CancellationToken ct) =>
        ExecuteIdempotentAsync(
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

    private async Task<OperationResult<Guid>> ExecuteIdempotentAsync(
        string commandType,
        Guid clientRequestId,
        string requestHash,
        string userId,
        Func<ApplicationDbContext, CancellationToken, Task<OperationResult<Guid>>> stageAction,
        CancellationToken ct)
    {
        if (clientRequestId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор запроса обязателен.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return OperationError.Invalid("Пользователь команды не определён.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var existingReceipt = await FindReceiptAsync(
            dbContext,
            userId,
            commandType,
            clientRequestId,
            ct);
        if (existingReceipt is not null)
        {
            return ResolveReceipt(existingReceipt, requestHash);
        }

        var result = await stageAction(dbContext, ct);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        dbContext.MobileCommandReceipts.Add(new MobileCommandReceipt
        {
            UserId = userId,
            CommandType = commandType,
            ClientRequestId = clientRequestId,
            RequestHash = requestHash,
            ResultResourceId = result.Value,
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return result.Value;
        }
        catch (DbUpdateException exception)
        {
            await using var retryContext = await dbContextFactory.CreateDbContextAsync(ct);
            var winningReceipt = await FindReceiptAsync(
                retryContext,
                userId,
                commandType,
                clientRequestId,
                ct);
            if (winningReceipt is not null)
            {
                return ResolveReceipt(winningReceipt, requestHash);
            }

            if (InventoryPersistenceConflictClassifier.TryClassify(exception, out var error))
            {
                return error;
            }

            throw;
        }
    }

    private static Task<MobileCommandReceipt?> FindReceiptAsync(
        ApplicationDbContext dbContext,
        string userId,
        string commandType,
        Guid clientRequestId,
        CancellationToken ct) =>
        dbContext.MobileCommandReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId
                && x.CommandType == commandType
                && x.ClientRequestId == clientRequestId,
                ct);

    private static OperationResult<Guid> ResolveReceipt(
        MobileCommandReceipt receipt,
        string requestHash) =>
        receipt.RequestHash == requestHash
            ? receipt.ResultResourceId
            : OperationError.Conflict(
                "Этот идентификатор запроса уже использован с другими данными.");

    private static string ComputeCreateDraftHash(
        Guid warehouseId,
        Guid? transitStorageLocationId)
    {
        var value = transitStorageLocationId is Guid locationId
            ? $"{warehouseId:N}|{locationId:N}"
            : warehouseId.ToString("N");
        return ComputeHash(value);
    }

    private static string ComputeMoveDirectHash(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity) =>
        ComputeHash(string.Join(
            '|',
            transferId.ToString("N"),
            sourceStorageLocationId.ToString("N"),
            destinationStorageLocationId.ToString("N"),
            stockKeepingUnitId.ToString("N"),
            quantity.ToString("R", CultureInfo.InvariantCulture)));

    private static string ComputeTransitMovementHash(
        Guid transferId,
        Guid enteredStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity) =>
        ComputeHash(string.Join(
            '|',
            transferId.ToString("N"),
            enteredStorageLocationId.ToString("N"),
            stockKeepingUnitId.ToString("N"),
            quantity.ToString("R", CultureInfo.InvariantCulture)));

    private static string ComputeCompleteHash(Guid transferId) =>
        ComputeHash(transferId.ToString("N"));

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
