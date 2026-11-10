using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wms.Application.CommandExecution;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data;

public sealed class PickingAssemblyStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IPickingAssemblyStore
{
    public async Task<PickingAssembly?> ReadAssemblyAsync(Guid assemblyId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.PickingAssemblies.AsNoTracking()
            .Include(x => x.SourceLinks).Include(x => x.PlanLines)
            .SingleOrDefaultAsync(x => x.Id == assemblyId, ct);
    }

    public async Task<ShippingOrder?> ReadSourceAsync(Guid sourceId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.ShippingOrders.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == sourceId, ct);
    }

    public async Task<PickingAssembly?> LoadAssemblyAsync(
        ICommandExecutionContext context, Guid assemblyId, CancellationToken ct)
    {
        var session = Session(context);
        await session.BeginSerializableAsync(ct);
        return await session.Db.PickingAssemblies
            .Include(x => x.SourceLinks).Include(x => x.PlanLines)
            .SingleOrDefaultAsync(x => x.Id == assemblyId, ct);
    }
    public async Task<Guid?> FindLinkedAssemblyIdAsync(Guid sourceId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.PickingAssemblySourceLinks.AsNoTracking()
            .Where(x => x.ShippingOrderId == sourceId && x.DetachedAtUtc == null)
            .Select(x => (Guid?)x.PickingAssemblyId)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<PickingAssemblyChangeState> LoadAsync(
        ICommandExecutionContext context, Guid? assemblyId, Guid sourceId,
        CancellationToken ct)
    {
        var session = Session(context);
        await session.BeginSerializableAsync(ct);
        var db = session.Db;
        var source = await db.ShippingOrders.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == sourceId, ct);
        var linkedId = await db.PickingAssemblySourceLinks.AsNoTracking()
            .Where(x => x.ShippingOrderId == sourceId && x.DetachedAtUtc == null)
            .Select(x => (Guid?)x.PickingAssemblyId)
            .SingleOrDefaultAsync(ct);
        var selectedId = assemblyId ?? linkedId;
        var assembly = selectedId is Guid id
            ? await db.PickingAssemblies.Include(x => x.SourceLinks)
                .Include(x => x.PlanLines).SingleOrDefaultAsync(x => x.Id == id, ct)
            : null;
        return new PickingAssemblyChangeState(source, assembly, linkedId);
    }

    public async Task<OperationResult> AssignNumberAsync(
        ICommandExecutionContext context, PickingAssembly assembly, CancellationToken ct)
    {
        var db = Session(context).Db;
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Номер сборки выделяется внутри транзакции команды.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'picking-assembly-number',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 10000;
            SELECT @result;
            """;
        var lockResult = Convert.ToInt32(await command.ExecuteScalarAsync(ct),
            CultureInfo.InvariantCulture);
        if (lockResult < 0)
            return OperationError.Conflict("Не удалось выделить номер сборки; повторите создание.");

        var candidate = assembly.CreatedAtUtc.LocalDateTime;
        while (true)
        {
            var number = candidate.ToString("yyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (!await db.PickingAssemblies.AnyAsync(x => x.Number == number, ct))
                return assembly.AssignNumber(number);
            try
            {
                candidate = candidate.AddSeconds(1);
            }
            catch (ArgumentOutOfRangeException)
            {
                return OperationError.Conflict("Свободный номер сборки не найден.");
            }
        }
    }

    private static CommandExecutionStore.Session Session(ICommandExecutionContext context) =>
        context as CommandExecutionStore.Session
        ?? throw new InvalidOperationException("Для сборки нужен сеанс складской команды.");
}
