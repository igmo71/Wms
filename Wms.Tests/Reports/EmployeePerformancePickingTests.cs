using Microsoft.EntityFrameworkCore;
using Wms.Application.Reports.EmployeePerformance;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Reports;

[Collection(SqlDatabaseCollection.Name)]
public sealed class EmployeePerformancePickingTests(SqlDatabase database)
{
    [Fact]
    public async Task Closed_assembly_attributes_positive_fact_to_each_confirming_worker()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Report warehouse" };
        var sku = new StockKeepingUnit
        {
            Id = Guid.NewGuid(), Name = "Report SKU", WeightKg = 1.5
        };
        var zone = Zone(warehouse.Id, "REPORT", ZoneType.Storage);
        var location = Location(warehouse.Id, zone, 1);
        var sourceId = Guid.NewGuid();
        var source = Value(ShippingOrder.Create(new ShippingOrderImportSnapshot(
            sourceId, false, true, "REPORT-1", DateTime.UtcNow,
            warehouse.Id, null, ShippingOrderStatus.Prepared,
            ShippingOrderQueue.Unknown, null, null,
            WarehouseOperation.CustomerShipment, Guid.NewGuid(),
            PartyType.Partner,
            [new ShippingOrderItemImportSnapshot(1, sku.Id, 5, 5,
                ShippingOrderAction.PickUp)], []), DateTimeOffset.UtcNow));
        var assembly = PickingAssembly(warehouse.Id, "worker-a");
        Value(assembly.AttachSource(sourceId, "{\"source\":1}",
            new Dictionary<Guid, decimal> { [sku.Id] = 5 },
            "worker-a", DateTimeOffset.UtcNow));
        var started = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid();
        Success(assembly.FreezeForStart("worker-a", started));
        Success(assembly.ClaimStart(leaseId, started, started.AddMinutes(10)));
        Success(assembly.RecordSourceStartResult(leaseId, sourceId, started, null));
        Success(assembly.CompleteStart(leaseId, started));
        var first = Value(PickingEntry.Create(assembly.Id, warehouse.Id,
            location.Id, sku.Id, null, 2, "worker-a",
            started.AddMinutes(1)));
        var second = Value(PickingEntry.Create(assembly.Id, warehouse.Id,
            location.Id, sku.Id, null, 3, "worker-b",
            started.AddMinutes(2)));
        Success(assembly.Close("worker-a", assembly.OperationalRevision,
            started.AddMinutes(3)));

        await using (var db = database.CreateDbContext())
        {
            db.AddRange(warehouse, sku, zone, location, source, assembly,
                first, second);
            await db.SaveChangesAsync();
        }

        var report = new EmployeePerformanceReportService(database);
        var summary = await report.GetSummaryAsync(new EmployeePerformanceSummaryQuery
        {
            WarehouseId = warehouse.Id, Take = 10
        });
        Assert.Equal(2, summary.TotalItems);
        Assert.Equal(2, summary.Totals.DocumentCount);
        Assert.Equal(2, summary.Totals.PositiveLineCount);
        Assert.Equal(7.5, summary.Totals.KnownFactWeightKg);
        Assert.All(summary.Items, x => Assert.Equal(1, x.DocumentCount));

        var details = await report.GetDetailsAsync(new EmployeePerformanceDetailsQuery
        {
            WarehouseId = warehouse.Id, UserId = "worker-b", Take = 10
        });
        var item = Assert.Single(details.Items);
        Assert.Equal(assembly.Id, item.DocumentId);
        Assert.Equal(4.5, item.KnownFactWeightKg);
        Assert.Equal(started.AddMinutes(2), item.StartedAtUtc);
        Assert.Equal(started.AddMinutes(2), item.CompletedAtUtc);
    }
}
