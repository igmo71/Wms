using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;

namespace Wms.Application.Inventory.Transfers;

public sealed class MobileInventoryTransferCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    InventoryTransferCommandService transferCommandService)
{
    private const string CreateDraftCommand = "inventory-transfer.create-draft";
    private const string ReceiptPrimaryKey = "PK_MobileCommandReceipts";

    public async Task<OperationResult<Guid>> CreateDraftAsync(
        Guid warehouseId,
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

        var requestHash = ComputeCreateDraftHash(warehouseId);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingReceipt = await FindReceiptAsync(
            dbContext,
            userId,
            clientRequestId,
            ct);
        if (existingReceipt is not null)
        {
            return ResolveReceipt(existingReceipt, requestHash);
        }

        var transferResult = await transferCommandService.StageCreateAsync(
            dbContext,
            warehouseId,
            transitStorageLocationId: null,
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
        catch (DbUpdateException exception) when (IsReceiptDuplicate(exception))
        {
            await using var retryContext = await dbContextFactory.CreateDbContextAsync(ct);
            var winningReceipt = await FindReceiptAsync(
                retryContext,
                userId,
                clientRequestId,
                ct);

            if (winningReceipt is null)
            {
                throw;
            }

            return ResolveReceipt(winningReceipt, requestHash);
        }
    }

    private static Task<MobileCommandReceipt?> FindReceiptAsync(
        ApplicationDbContext dbContext,
        string userId,
        Guid clientRequestId,
        CancellationToken ct) =>
        dbContext.MobileCommandReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId
                && x.CommandType == CreateDraftCommand
                && x.ClientRequestId == clientRequestId,
                ct);

    private static OperationResult<Guid> ResolveReceipt(
        MobileCommandReceipt receipt,
        string requestHash) =>
        receipt.RequestHash == requestHash
            ? receipt.ResultResourceId
            : OperationError.Conflict(
                "Этот идентификатор запроса уже использован с другими данными.");

    private static string ComputeCreateDraftHash(Guid warehouseId)
    {
        var payload = Encoding.UTF8.GetBytes(warehouseId.ToString("N"));
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static bool IsReceiptDuplicate(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 } sqlException
                && sqlException.Message.Contains(ReceiptPrimaryKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
