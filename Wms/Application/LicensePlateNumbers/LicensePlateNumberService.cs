using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Commands;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.LicensePlateNumbers;

public sealed class LicensePlateNumberService(CommandExecutor executor, IDbContextFactory<ApplicationDbContext> factory)
{
    private const string OpenCommandType = "receiving-lpn.open";
    private const string CancelCommandType = "receiving-lpn.cancel-empty";

    public Task<OperationResult<Guid>> IssueAsync(int quantity, CommandContext context, CancellationToken ct = default) =>
        executor.ExecuteAsync("lpn-label.issue-batch", context.RequestId,
            CommandExecutor.ComputeHash(quantity.ToString(CultureInfo.InvariantCulture)), context.UserId,
            (db, _) =>
            {
                var result = LicensePlateNumberBatch.Issue(quantity, context.UserId, DateTimeOffset.UtcNow);
                if (!result.IsSuccess)
                    return Task.FromResult<OperationResult<Guid>>(result.Error!);
                db.LicensePlateNumberBatches.Add(result.Value!);
                return Task.FromResult<OperationResult<Guid>>(result.Value!.Id);
            }, ct);

    public async Task<(IReadOnlyList<LicensePlateNumber> Items, int Total)> ListAsync(string? search, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.LicensePlateNumbers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Code.Contains(search.Trim()));
        var total = await query.CountAsync(ct);
        var items = await query.Include(x => x.Batch).OrderByDescending(x => x.Code)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        return (items, total);
    }

    // Printing only reads issued values; it never allocates numbers or changes availability.
    public async Task<IReadOnlyList<LicensePlateNumber>> GetForPrintAsync(Guid id, bool batch, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LicensePlateNumbers.AsNoTracking().Include(x => x.Batch)
            .Where(x => batch ? x.BatchId == id : x.Id == id).OrderBy(x => x.Code).ToListAsync(ct);
    }

    public Task<OperationResult<Guid>> OpenForReceivingAsync(
        OpenReceivingLicensePlateNumberCommand command,
        CommandContext context,
        CancellationToken ct = default)
    {
        string code = command.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        return executor.ExecuteAsync(
            OpenCommandType,
            context.RequestId,
            CommandExecutor.ComputeHash($"{command.ReceivingOrderId:N}|{code}"),
            context.UserId,
            async (db, token) =>
            {
                if (code.Length != 15
                    || !code.StartsWith("LPN", StringComparison.Ordinal)
                    || code.AsSpan(3).IndexOfAnyExceptInRange('0', '9') >= 0)
                {
                    return OperationError.Invalid("Некорректный штрихкод LPN.");
                }

                var order = await db.ReceivingOrders
                    .Include(x => x.Participants)
                    .Include(x => x.LicensePlateNumbers)
                    .SingleOrDefaultAsync(x => x.Id == command.ReceivingOrderId, token);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");

                var lpn = await db.LicensePlateNumbers
                    .SingleOrDefaultAsync(x => x.Code == code, token);
                if (lpn is null)
                    return OperationError.NotFound("Этикетка LPN не выпускалась в WMS.");

                return order.OpenOrCreateLicensePlateNumber(
                    lpn,
                    DateTimeOffset.UtcNow,
                    context.UserId);
            },
            ct);
    }

    public Task<OperationResult<Guid>> CancelEmptyReceivingAsync(
        CancelReceivingLicensePlateNumberCommand command,
        CommandContext context,
        CancellationToken ct = default) =>
        executor.ExecuteAsync(
            CancelCommandType,
            context.RequestId,
            CommandExecutor.ComputeHash($"{command.ReceivingOrderId:N}|{command.LicensePlateNumberId:N}"),
            context.UserId,
            async (db, token) =>
            {
                var order = await db.ReceivingOrders
                    .Include(x => x.LicensePlateNumbers)
                    .SingleOrDefaultAsync(x => x.Id == command.ReceivingOrderId, token);
                if (order is null)
                    return OperationError.NotFound("Приходный ордер не найден.");

                var lpn = order.LicensePlateNumbers
                    .SingleOrDefault(x => x.Id == command.LicensePlateNumberId);
                if (lpn is null)
                    return OperationError.NotFound("LPN не найден в приходном ордере.");

                var result = order.CancelEmptyLicensePlateNumber(
                    lpn,
                    DateTimeOffset.UtcNow,
                    context.UserId);
                return result.IsSuccess ? lpn.Id : result.Error!;
            },
            ct);
}
