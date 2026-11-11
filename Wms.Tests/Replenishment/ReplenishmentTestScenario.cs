using Wms.Application.Authorization;
using Wms.Domain;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Replenishment;

internal static class ReplenishmentTestScenario
{
    public const string Worker = "replenisher";
    public const string Manager = "manager";

    public static (PickingAssembly Assembly, ShippingOrder Source) ActiveAssembly(
        Guid warehouseId, Guid skuId, decimal quantity)
    {
        var sourceId = Guid.NewGuid();
        var source = Value(ShippingOrder.Create(new ShippingOrderImportSnapshot(
            sourceId, false, true, "РО Подпитки", DateTime.UtcNow,
            warehouseId, null, ShippingOrderStatus.Prepared,
            ShippingOrderQueue.LiveQueue, null, null,
            WarehouseOperation.CustomerShipment, Guid.NewGuid(),
            PartyType.Partner,
            [new ShippingOrderItemImportSnapshot(1, skuId, quantity,
                quantity, ShippingOrderAction.PickUp)], []),
            DateTimeOffset.UtcNow));
        var assembly = PickingAssembly(warehouseId, Worker);
        Value(assembly.AttachSource(sourceId, "{}",
            new Dictionary<Guid, decimal> { [skuId] = quantity }, Worker,
            DateTimeOffset.UtcNow));
        var now = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid();
        Success(assembly.FreezeForStart(Worker, now));
        Success(assembly.ClaimStart(leaseId, now, now.AddMinutes(10)));
        Success(assembly.RecordSourceStartResult(leaseId, sourceId, now, null));
        Success(assembly.CompleteStart(leaseId, now));
        Success(assembly.ReleaseStart(leaseId));
        return (assembly, source);
    }

    public static WarehousePermission Permission() => new(new BothRoles());

    private sealed class BothRoles : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<bool> HasOperatorRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);
    }
}
