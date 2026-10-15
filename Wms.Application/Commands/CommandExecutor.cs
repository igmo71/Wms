using System.Security.Cryptography;
using System.Text;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.CommandExecution;

public sealed record CommandReceiptSnapshot(
    string UserId,
    string CommandType,
    Guid RequestId,
    string RequestHash,
    Guid ResultResourceId);

public sealed record CommandExecutionResult(
    Guid ResourceId,
    IReadOnlyCollection<WarehouseAuditEvent> AuditEvents);

public interface ICommandExecutionContext
{
    void Stage<TEntity>(TEntity entity) where TEntity : class;
}

public interface ICommandExecutionSession : ICommandExecutionContext, IAsyncDisposable
{
    Task CommitAsync(
        CommandReceiptSnapshot receipt,
        IReadOnlyCollection<WarehouseAuditEvent> auditEvents,
        CancellationToken ct);
}

public interface ICommandExecutionStore
{
    Task<CommandReceiptSnapshot?> FindReceiptAsync(
        string userId, string commandType, Guid requestId, CancellationToken ct);

    Task<ICommandExecutionSession> OpenSessionAsync(CancellationToken ct);
}

public sealed class CommandExecutor(ICommandExecutionStore store)
{
    public async Task<OperationResult<Guid>> ExecuteAsync(
        string commandType,
        Guid requestId,
        string requestHash,
        string userId,
        Func<ICommandExecutionContext, CancellationToken, Task<OperationResult<CommandExecutionResult>>> operation,
        CancellationToken ct)
    {
        if (requestId == Guid.Empty)
            return OperationError.Invalid("Идентификатор запроса обязателен.");
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 36)
            return OperationError.Invalid("Пользователь команды не определён.");
        if (string.IsNullOrWhiteSpace(commandType) || commandType.Length > 100)
            return OperationError.Invalid("Тип команды не определён.");
        if (string.IsNullOrWhiteSpace(requestHash) || requestHash.Length != 64)
            return OperationError.Invalid("Хеш команды должен содержать 64 символа.");

        var existing = await store.FindReceiptAsync(userId, commandType, requestId, ct);
        if (existing is not null)
            return ResolveReceipt(existing, requestHash);

        await using var session = await store.OpenSessionAsync(ct);
        var result = await operation(session, ct);
        if (!result.IsSuccess)
            return result.Error!;

        var effect = result.Value!;
        if (effect.AuditEvents is null || effect.AuditEvents.Count == 0
            || effect.AuditEvents.Any(x => x.CommandUserId != userId
                || x.CommandType != commandType || x.RequestId != requestId))
            throw new InvalidOperationException("Успешная команда должна содержать события аудита с её ключом.");

        var receipt = new CommandReceiptSnapshot(
            userId, commandType, requestId, requestHash, effect.ResourceId);
        try
        {
            await session.CommitAsync(receipt, effect.AuditEvents, ct);
            return effect.ResourceId;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A competing request may have committed the same key while this one was saving.
            var winner = await store.FindReceiptAsync(userId, commandType, requestId, ct);
            if (winner is not null)
                return ResolveReceipt(winner, requestHash);
            throw;
        }
    }

    public static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static OperationResult<Guid> ResolveReceipt(CommandReceiptSnapshot receipt, string requestHash) =>
        receipt.RequestHash == requestHash
            ? receipt.ResultResourceId
            : OperationError.Conflict("Этот идентификатор запроса уже использован с другими данными.");
}
