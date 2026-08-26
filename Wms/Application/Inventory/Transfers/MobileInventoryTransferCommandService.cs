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

    public async Task<OperationResult<Guid>> CreateDraftAsync(
        Guid warehouseId,
        Guid? transitStorageLocationId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default)
    {
        if (clientRequestId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор запроса обязателен.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return OperationError.Invalid("Пользователь команды не определён.");
        }

        var requestHash = ComputeCreateDraftHash(warehouseId, transitStorageLocationId);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingReceipt = await FindReceiptAsync(
            dbContext,
            userId,
            CreateDraftCommand,
            clientRequestId,
            ct);
        if (existingReceipt is not null)
        {
            return ResolveReceipt(existingReceipt, requestHash);
        }

        var transferResult = await transferCommandService.StageCreateAsync(
            dbContext,
            warehouseId,
            transitStorageLocationId,
            userId,
            ct);
        if (!transferResult.IsSuccess)
        {
            return transferResult.Error!;
        }

        var transfer = transferResult.Value!;
        dbContext.MobileCommandReceipts.Add(new MobileCommandReceipt
        {
            UserId = userId,
            CommandType = CreateDraftCommand,
            ClientRequestId = clientRequestId,
            RequestHash = requestHash,
            ResultResourceId = transfer.Id,
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return transfer.Id;
        }
        catch (DbUpdateException exception)
        {
            await using var retryContext = await dbContextFactory.CreateDbContextAsync(ct);
            var winningReceipt = await FindReceiptAsync(
                retryContext,
                userId,
                CreateDraftCommand,
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

    public async Task<OperationResult<Guid>> MoveDirectAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default)
    {
        if (clientRequestId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор запроса обязателен.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return OperationError.Invalid("Пользователь команды не определён.");
        }

        var requestHash = ComputeMoveDirectHash(
            transferId,
            sourceStorageLocationId,
            destinationStorageLocationId,
            stockKeepingUnitId,
            quantity);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingReceipt = await FindReceiptAsync(
            dbContext,
            userId,
            MoveDirectCommand,
            clientRequestId,
            ct);
        if (existingReceipt is not null)
        {
            return ResolveReceipt(existingReceipt, requestHash);
        }

        var movementResult = await transferCommandService.StageDirectMovementAsync(
            dbContext,
            transferId,
            sourceStorageLocationId,
            destinationStorageLocationId,
            stockKeepingUnitId,
            quantity,
            userId,
            ct);
        if (!movementResult.IsSuccess)
        {
            return movementResult.Error!;
        }

        var movement = movementResult.Value!;
        dbContext.MobileCommandReceipts.Add(new MobileCommandReceipt
        {
            UserId = userId,
            CommandType = MoveDirectCommand,
            ClientRequestId = clientRequestId,
            RequestHash = requestHash,
            ResultResourceId = movement.Id,
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return movement.Id;
        }
        catch (DbUpdateException exception)
        {
            await using var retryContext = await dbContextFactory.CreateDbContextAsync(ct);
            var winningReceipt = await FindReceiptAsync(
                retryContext,
                userId,
                MoveDirectCommand,
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

    private async Task<OperationResult<Guid>> MoveTransitAsync(
        string commandType,
        Guid transferId,
        Guid enteredStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        Guid clientRequestId,
        string userId,
        bool isPick,
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

        var requestHash = ComputeTransitMovementHash(
            transferId,
            enteredStorageLocationId,
            stockKeepingUnitId,
            quantity);
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

        var movementResult = isPick
            ? await transferCommandService.StagePickMovementAsync(
                dbContext,
                transferId,
                enteredStorageLocationId,
                stockKeepingUnitId,
                quantity,
                userId,
                ct)
            : await transferCommandService.StagePutMovementAsync(
                dbContext,
                transferId,
                enteredStorageLocationId,
                stockKeepingUnitId,
                quantity,
                userId,
                ct);
        if (!movementResult.IsSuccess)
        {
            return movementResult.Error!;
        }

        var movement = movementResult.Value!;
        dbContext.MobileCommandReceipts.Add(new MobileCommandReceipt
        {
            UserId = userId,
            CommandType = commandType,
            ClientRequestId = clientRequestId,
            RequestHash = requestHash,
            ResultResourceId = movement.Id,
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return movement.Id;
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

    public async Task<OperationResult<Guid>> CompleteAsync(
        Guid transferId,
        Guid clientRequestId,
        string userId,
        CancellationToken ct = default)
    {
        if (clientRequestId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор запроса обязателен.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return OperationError.Invalid("Пользователь команды не определён.");
        }

        var requestHash = ComputeCompleteHash(transferId);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingReceipt = await FindReceiptAsync(
            dbContext,
            userId,
            CompleteCommand,
            clientRequestId,
            ct);
        if (existingReceipt is not null)
        {
            return ResolveReceipt(existingReceipt, requestHash);
        }

        var completionResult = await transferCommandService.StageCompleteAsync(
            dbContext,
            transferId,
            userId,
            ct);
        if (!completionResult.IsSuccess)
        {
            return completionResult.Error!;
        }

        dbContext.MobileCommandReceipts.Add(new MobileCommandReceipt
        {
            UserId = userId,
            CommandType = CompleteCommand,
            ClientRequestId = clientRequestId,
            RequestHash = requestHash,
            ResultResourceId = transferId,
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return transferId;
        }
        catch (DbUpdateException exception)
        {
            await using var retryContext = await dbContextFactory.CreateDbContextAsync(ct);
            var winningReceipt = await FindReceiptAsync(
                retryContext,
                userId,
                CompleteCommand,
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
        var payload = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static string ComputeMoveDirectHash(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity)
    {
        var canonicalRequest = string.Join(
            '|',
            transferId.ToString("N"),
            sourceStorageLocationId.ToString("N"),
            destinationStorageLocationId.ToString("N"),
            stockKeepingUnitId.ToString("N"),
            quantity.ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));
    }

    private static string ComputeCompleteHash(Guid transferId)
    {
        var payload = Encoding.UTF8.GetBytes(transferId.ToString("N"));
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static string ComputeTransitMovementHash(
        Guid transferId,
        Guid enteredStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity)
    {
        var canonicalRequest = string.Join(
            '|',
            transferId.ToString("N"),
            enteredStorageLocationId.ToString("N"),
            stockKeepingUnitId.ToString("N"),
            quantity.ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));
    }

}
