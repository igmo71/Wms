using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Data;

namespace Wms.Application.MobileCommands;

public sealed class MobileCommandExecutor(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<OperationResult<Guid>> ExecuteAsync(
        string commandType,
        Guid clientRequestId,
        string requestHash,
        string userId,
        Func<ApplicationDbContext, CancellationToken, Task<OperationResult<Guid>>> stageAction,
        CancellationToken ct)
    {
        if (clientRequestId == Guid.Empty)
            return OperationError.Invalid("Идентификатор запроса обязателен.");
        if (string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Пользователь команды не определён.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var existingReceipt = await FindReceiptAsync(
            dbContext,
            userId,
            commandType,
            clientRequestId,
            ct);
        if (existingReceipt is not null)
            return ResolveReceipt(existingReceipt, requestHash);

        var result = await stageAction(dbContext, ct);
        if (!result.IsSuccess)
            return result.Error!;

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
                return ResolveReceipt(winningReceipt, requestHash);
            if (InventoryPersistenceConflictClassifier.TryClassify(exception, out var error))
                return error;
            throw;
        }
    }

    public static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
            : OperationError.Conflict("Этот идентификатор запроса уже использован с другими данными.");
}
