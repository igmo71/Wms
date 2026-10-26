using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wms.Application.CommandExecution;
using Wms.Application.Receiving;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data;

public sealed class ReceivingOrderStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IReceivingOrderStore
{
    public async Task<CommandReceiptSnapshot?> FindReceiptAsync(
        string userId, string commandType, Guid requestId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.CommandReceipts.AsNoTracking()
            .Where(x => x.UserId == userId && x.CommandType == commandType
                && x.RequestId == requestId)
            .Select(x => new CommandReceiptSnapshot(x.UserId, x.CommandType,
                x.RequestId, x.RequestHash, x.ResultResourceId))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ReceivingSourceState>> LoadSourcesAsync(
        IReadOnlyCollection<Guid> refKeys, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var sources = await context.ReceivingSourceDocuments.AsNoTracking()
            .Where(x => refKeys.Contains(x.RefKey)).ToListAsync(ct);
        return await WithVersionsAsync(context, sources, ct);
    }

    public async Task<IReadOnlyList<ReceivingSourceHeader>> LoadSourceHeadersAsync(
        IReadOnlyCollection<Guid> refKeys, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await (from source in context.ReceivingSourceDocuments.AsNoTracking()
            join version in context.ReceivingSourceVersions.AsNoTracking()
                on source.LastSuccessfulVersionId equals (Guid?)version.Id into versions
            from version in versions.DefaultIfEmpty()
            where refKeys.Contains(source.RefKey)
            select new ReceivingSourceHeader(source.RefKey,
                version == null ? null : version.SnapshotJson,
                source.LastAttemptError)).ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ReceivingSourceState>> ListUnlinkedSourcesAsync(
        CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var sources = await context.ReceivingSourceDocuments.AsNoTracking()
            .Where(source => !context.ReceivingOrderSourceLinks.Any(link =>
                link.ReceivingSourceDocumentId == source.Id && link.DetachedAtUtc == null))
            .OrderByDescending(x => x.LastSuccessfulCheckAtUtc)
            .ThenBy(x => x.RefKey)
            .ToListAsync(ct);
        return await WithVersionsAsync(context, sources, ct);
    }

    public async Task<IReadOnlyList<ReceivingSourceState>> ListAllSourcesAsync(
        CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var sources = await context.ReceivingSourceDocuments.AsNoTracking()
            .OrderByDescending(x => x.LastSuccessfulCheckAtUtc)
            .ThenBy(x => x.RefKey)
            .ToListAsync(ct);
        var activeLinks = await context.ReceivingOrderSourceLinks.AsNoTracking()
            .Where(x => x.DetachedAtUtc == null)
            .Select(x => new
            {
                x.ReceivingSourceDocumentId,
                x.ReceivingOrderId,
                OrderNumber = x.ReceivingOrder!.Number
            })
            .ToDictionaryAsync(x => x.ReceivingSourceDocumentId, ct);
        var states = await WithVersionsAsync(context, sources, ct);
        return states.Select(state => activeLinks.TryGetValue(state.Source.Id, out var link)
            ? state with
            {
                LinkedOrderId = link.ReceivingOrderId,
                LinkedOrderNumber = link.OrderNumber
            }
            : state).ToArray();
    }

    public async Task<OperationResult<Guid>> CreateAsync(
        ReceivingOrder order, IReadOnlyList<ReceivingOrderSourceLink> links,
        IReadOnlyList<ReceivingOrderItem> items,
        IReadOnlyList<ReceivingSourceExpectation> expectedSources,
        CommandReceiptSnapshot receipt, WarehouseAuditEvent createdAudit,
        CancellationToken ct)
    {
        try
        {
            return await CreateCoreAsync(order, links, items, expectedSources,
                receipt, createdAudit, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            && (exception is SqlException { Number: 1205 }
                || exception is DbUpdateException
                {
                    InnerException: SqlException { Number: 2601 or 2627 or 1205 }
                }))
        {
            var winner = await FindReceiptAsync(
                receipt.UserId, receipt.CommandType, receipt.RequestId, ct);
            if (winner is not null)
                return winner.RequestHash == receipt.RequestHash
                    ? winner.ResultResourceId
                    : OperationError.Conflict(
                        "Этот идентификатор запроса уже использован с другими данными.");
            return OperationError.Conflict(
                "Источник уже был привязан или строка ордера создана конкурентно.");
        }
    }

    private async Task<OperationResult<Guid>> CreateCoreAsync(
        ReceivingOrder order, IReadOnlyList<ReceivingOrderSourceLink> links,
        IReadOnlyList<ReceivingOrderItem> items,
        IReadOnlyList<ReceivingSourceExpectation> expectedSources,
        CommandReceiptSnapshot receipt, WarehouseAuditEvent createdAudit,
        CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        var numberLock = await AcquireNumberLockAsync(context, transaction, ct);
        if (!numberLock.IsSuccess)
            return numberLock.Error!;

        var existingReceipt = await context.CommandReceipts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == receipt.UserId
                && x.CommandType == receipt.CommandType
                && x.RequestId == receipt.RequestId, ct);
        if (existingReceipt is not null)
            return existingReceipt.RequestHash == receipt.RequestHash
                ? existingReceipt.ResultResourceId
                : OperationError.Conflict("Этот идентификатор запроса уже использован с другими данными.");

        var sourceIds = expectedSources.Select(x => x.SourceId).ToArray();
        var activeLinks = await context.ReceivingOrderSourceLinks.AsNoTracking()
            .Where(x => sourceIds.Contains(x.ReceivingSourceDocumentId)
                && x.DetachedAtUtc == null)
            .ToListAsync(ct);
        if (activeLinks.Count > 0)
        {
            var existingOrderId = activeLinks[0].ReceivingOrderId;
            if (activeLinks.Count == sourceIds.Length
                && activeLinks.All(x => x.ReceivingOrderId == existingOrderId))
            {
                var existingSourceIds = await context.ReceivingOrderSourceLinks.AsNoTracking()
                    .Where(x => x.ReceivingOrderId == existingOrderId && x.DetachedAtUtc == null)
                    .Select(x => x.ReceivingSourceDocumentId).ToListAsync(ct);
                if (existingSourceIds.Count == sourceIds.Length
                    && existingSourceIds.All(sourceIds.Contains))
                {
                    context.CommandReceipts.Add(NewReceipt(receipt, existingOrderId));
                    await context.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return existingOrderId;
                }
            }
            return OperationError.Conflict("Один из источников уже привязан к другому ордеру.");
        }

        var sources = await context.ReceivingSourceDocuments.AsNoTracking()
            .Where(x => sourceIds.Contains(x.Id)).ToListAsync(ct);
        if (sources.Count != expectedSources.Count
            || expectedSources.Any(expected => !sources.Any(source =>
                source.Id == expected.SourceId
                && source.LastSuccessfulVersionId == expected.VersionId
                && source.LastSuccessfulCheckAtUtc == expected.CheckedAtUtc)))
            return OperationError.Conflict("Источник изменился после просмотра; повторите создание ордера.");

        if (!await context.Warehouses.AnyAsync(x => x.Id == order.WarehouseId, ct))
            return OperationError.NotFound("Склад источников не найден в WMS.");

        var assignedNumber = await AssignNextNumberAsync(context, order, ct);
        if (!assignedNumber.IsSuccess)
            return assignedNumber.Error!;

        context.ReceivingOrders.Add(order);
        context.ReceivingOrderSourceLinks.AddRange(links);
        context.ReceivingOrderItems.AddRange(items);
        context.CommandReceipts.Add(NewReceipt(receipt, order.Id));
        context.WarehouseAuditEvents.Add(createdAudit);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return order.Id;
    }

    private static async Task<OperationResult> AcquireNumberLockAsync(
        ApplicationDbContext context, IDbContextTransaction transaction,
        CancellationToken ct)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'receiving-order-number',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 10000;
            SELECT @result;
            """;
        var lockResult = Convert.ToInt32(await command.ExecuteScalarAsync(ct),
            CultureInfo.InvariantCulture);
        return lockResult < 0
            ? OperationError.Conflict("Не удалось выделить номер ордера; повторите создание.")
            : OperationResult.Success();
    }

    private static async Task<OperationResult> AssignNextNumberAsync(
        ApplicationDbContext context, ReceivingOrder order, CancellationToken ct)
    {
        var candidate = order.CreatedAtUtc.LocalDateTime;
        while (true)
        {
            var number = candidate.ToString("yyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (!await context.ReceivingOrders.AnyAsync(x => x.Number == number, ct))
                return order.AssignNumber(number);
            try
            {
                candidate = candidate.AddSeconds(1);
            }
            catch (ArgumentOutOfRangeException)
            {
                return OperationError.Conflict("Свободный номер приходного ордера не найден.");
            }
        }
    }

    private static CommandReceipt NewReceipt(
        CommandReceiptSnapshot receipt, Guid resultResourceId) => new()
    {
        UserId = receipt.UserId,
        CommandType = receipt.CommandType,
        RequestId = receipt.RequestId,
        RequestHash = receipt.RequestHash,
        ResultResourceId = resultResourceId,
        CompletedAtUtc = DateTimeOffset.UtcNow
    };

    private static async Task<IReadOnlyList<ReceivingSourceState>> WithVersionsAsync(
        ApplicationDbContext context,
        IReadOnlyList<ReceivingSourceDocument> sources, CancellationToken ct)
    {
        var versionIds = sources.Select(x => x.LastSuccessfulVersionId)
            .OfType<Guid>().ToArray();
        var versions = await context.ReceivingSourceVersions.AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => versionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        return sources.Select(source => new ReceivingSourceState(source,
            source.LastSuccessfulVersionId is Guid versionId
                ? versions.GetValueOrDefault(versionId) : null)).ToArray();
    }
}
