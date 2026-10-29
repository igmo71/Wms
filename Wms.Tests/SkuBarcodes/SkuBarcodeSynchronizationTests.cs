using Microsoft.EntityFrameworkCore;
using Wms.Application.SkuBarcodes;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.SkuBarcodes;

[Collection(SqlDatabaseCollection.Name)]
public sealed class SkuBarcodeSynchronizationTests(SqlDatabase database)
{
    [Fact]
    public async Task Imports_preserve_local_pairs_and_roll_back_a_failed_snapshot()
    {
        var firstSkuId = Guid.NewGuid();
        var secondSkuId = Guid.NewGuid();
        await using (var seed = database.CreateDbContext())
        {
            seed.StockKeepingUnits.AddRange(
                new StockKeepingUnit { Id = firstSkuId, Name = "First" },
                new StockKeepingUnit { Id = secondSkuId, Name = "Second" });
            seed.SkuBarcodes.AddRange(
                new SkuBarcode
                {
                    SkuId = firstSkuId, Value = "LOCAL-1",
                    Source = SkuBarcodeSource.Wms
                },
                new SkuBarcode
                {
                    SkuId = secondSkuId, Value = "IMPORTED-1",
                    Source = SkuBarcodeSource.OneC
                });
            await seed.SaveChangesAsync();
        }

        var store = new SkuBarcodeStore(database);
        var synchronize = new SynchronizeSkuBarcodes(store, store);

        Success(await synchronize.ExecuteAsync(firstSkuId, Snapshot()));
        Success(await synchronize.ExecuteAsync(null,
            Snapshot(new SkuBarcodeSourceItem(secondSkuId, "IMPORTED-1"))));
        var rows = await LoadRowsAsync();
        Assert.Equal(2, rows.Length);
        Assert.Equal(SkuBarcodeSource.Wms,
            rows.Single(x => x.SkuId == firstSkuId).Source);

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await synchronize.ExecuteAsync(null,
                Snapshot(new SkuBarcodeSourceItem(Guid.NewGuid(), "UNKNOWN-SKU"))));
        rows = await LoadRowsAsync();
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, x => x.SkuId == secondSkuId && x.Value == "IMPORTED-1");

        Success(await synchronize.ExecuteAsync(firstSkuId,
            Snapshot(new SkuBarcodeSourceItem(firstSkuId, "LOCAL-1"))));
        rows = await LoadRowsAsync();
        Assert.Equal(SkuBarcodeSource.OneC,
            rows.Single(x => x.SkuId == firstSkuId).Source);

        Success(await synchronize.ExecuteAsync(firstSkuId, Snapshot()));
        rows = await LoadRowsAsync();
        Assert.DoesNotContain(rows, x => x.SkuId == firstSkuId);

        Success(await synchronize.ExecuteAsync(null,
            Snapshot(new SkuBarcodeSourceItem(firstSkuId, "DUPLICATE"),
                new SkuBarcodeSourceItem(secondSkuId, "DUPLICATE"))));
        rows = await LoadRowsAsync();
        Assert.Equal(2, rows.Count(x => x.Value == "DUPLICATE"));
        Assert.All(rows, x => Assert.Equal(SkuBarcodeSource.OneC, x.Source));

        var list = new ListSkuBarcodes(store);
        var firstPage = await list.ExecuteAsync(new SkuBarcodeListQuery(
            null, false, true, "Value", false, 0, 1));
        var secondPage = await list.ExecuteAsync(new SkuBarcodeListQuery(
            null, false, true, "Value", false, 1, 1));
        Assert.Equal(2, firstPage.TotalItems);
        Assert.Equal(2, secondPage.TotalItems);
        Assert.True(Assert.Single(firstPage.Items).IsDuplicate);
        Assert.True(Assert.Single(secondPage.Items).IsDuplicate);
    }

    private async Task<SkuBarcode[]> LoadRowsAsync()
    {
        await using var db = database.CreateDbContext();
        return await db.SkuBarcodes.AsNoTracking().ToArrayAsync();
    }

    private static Func<CancellationToken,
        Task<OperationResult<IReadOnlyList<SkuBarcodeSourceItem>>>> Snapshot(
        params SkuBarcodeSourceItem[] items) =>
        _ => Task.FromResult(OperationResult<IReadOnlyList<SkuBarcodeSourceItem>>
            .Success(items));
}
