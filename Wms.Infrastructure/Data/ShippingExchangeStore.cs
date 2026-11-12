using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data;

public sealed class ShippingExchangeStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IShippingExchangeStore
{
    public async Task<OperationResult<IShippingExchangeLease>> AcquireAsync(
        Guid shippingOrderId, CancellationToken ct)
    {
        if (shippingOrderId == Guid.Empty)
            return OperationError.Invalid("Укажите РО 1С для обмена.");
        var db = await contextFactory.CreateDbContextAsync(ct);
        var resource = $"wms:shipping:{shippingOrderId:N}";
        try
        {
            await db.Database.OpenConnectionAsync(ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "DECLARE @result int; "
                + "EXEC @result = sp_getapplock @Resource = @resource, "
                + "@LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; "
                + "SELECT @result;";
            command.Parameters.Add(new SqlParameter("@resource", resource));
            var result = (int)(await command.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("1С-блокировка не вернула результат."));
            if (result < 0)
            {
                await db.DisposeAsync();
                return OperationError.Conflict(
                    "Обмен этого РО 1С уже выполняется. Повторите позже.");
            }
            return OperationResult<IShippingExchangeLease>.Success(
                new Lease(db, resource));
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    public async Task<ShippingExchangeState> ReadAsync(Guid pickingAssemblyId,
        Guid shippingOrderId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var assembly = await db.PickingAssemblies.AsNoTracking()
            .Where(x => x.Id == pickingAssemblyId)
            .Select(x => (int?)x.Status).SingleOrDefaultAsync(ct);
        var target = await db.ShippingExchangeTargets.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PickingAssemblyId == pickingAssemblyId
                && x.ShippingOrderId == shippingOrderId, ct);
        var snapshot = await db.PickingAssemblySourceLinks.AsNoTracking()
            .Where(x => x.PickingAssemblyId == pickingAssemblyId
                && x.ShippingOrderId == shippingOrderId
                && x.DetachedAtUtc == null)
            .Select(x => x.PlanSourceSnapshotJson).SingleOrDefaultAsync(ct);
        return new ShippingExchangeState(target, snapshot,
            assembly is int status ? (Wms.Domain.Enums.PickingAssemblyStatus)status : null);
    }

    public async Task<ShippingExchangeTarget?> LoadTargetAsync(
        ICommandExecutionContext context, Guid pickingAssemblyId,
        Guid shippingOrderId, CancellationToken ct)
    {
        var session = context as CommandExecutionStore.Session
            ?? throw new InvalidOperationException("Обмен требует складскую сессию команды.");
        await session.BeginSerializableAsync(ct);
        return await session.Db.ShippingExchangeTargets.SingleOrDefaultAsync(
            x => x.PickingAssemblyId == pickingAssemblyId
                && x.ShippingOrderId == shippingOrderId, ct);
    }

    private sealed class Lease(ApplicationDbContext db, string resource)
        : IShippingExchangeLease
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "EXEC sp_releaseapplock "
                    + "@Resource = @resource, @LockOwner = 'Session';";
                command.Parameters.Add(new SqlParameter("@resource", resource));
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                await db.DisposeAsync();
            }
        }
    }
}
