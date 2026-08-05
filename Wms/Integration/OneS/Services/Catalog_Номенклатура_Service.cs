using Microsoft.Extensions.Logging;
using Wms.Application;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class Catalog_Номенклатура_Service(
    OneCClient oneCClient,
    StockKeepingUnitService stockKeepingUnitService,
    ILogger<Catalog_Номенклатура_Service> logger)
{
    public async Task ImportAsync(string Ref_Key, CancellationToken ct = default)
    {
        //using var scope = logger.BeginScope("Catalog_Номенклатура Import {Ref_Key}", Ref_Key);
        using var activity = AppTracing.StartActivity("Catalog_Номенклатура Import", nameof(Catalog_Номенклатура_Service));

        var fetchedItem = await GetAsync(Ref_Key, ct);

        if (fetchedItem is null)
            return;

        var stockKeepingUnit = MapToStockKeepingUnit(fetchedItem);

        await stockKeepingUnitService.CreateOrUpdateAsync(stockKeepingUnit, ct);
    }

    public async Task ImportListAsync(CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("Catalog_Номенклатура Import List", nameof(Catalog_Номенклатура_Service));

        int totalItems = await GetTotalAsync(ct);

        int batchSize = Catalog_Номенклатура.BatchSize;

        int totalBatches = (totalItems + batchSize - 1) / batchSize;

        List<Task> tasks = [];

        using var semaphore = new SemaphoreSlim(10);

        for (int i = 0; i < totalBatches; i++)
        {
            int batchIndex = i;

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var fetchedItems = await GetListAsync(batchIndex, ct);

                    if (fetchedItems is null || fetchedItems.Count == 0)
                        return;

                    List<StockKeepingUnit> stockKeepingUnits = fetchedItems
                        .Select(MapToStockKeepingUnit)
                        .ToList();

                    await stockKeepingUnitService.CreateOrUpdateBatchAsync(stockKeepingUnits, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
    }

    private async Task<int> GetTotalAsync(CancellationToken ct = default)
    {
        var uri = Catalog_Номенклатура.TotalUri;

        var result = await oneCClient.GetValueAsync<int>(uri, ct);

        return result;
    }

    private async Task<Catalog_Номенклатура?> GetAsync(string Ref_Key, CancellationToken ct = default)
    {
        var uri = Catalog_Номенклатура.GetUri(Ref_Key);

        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);

        var result = rootObject?.Value?[0];

        return result;
    }

    private async Task<List<Catalog_Номенклатура>?> GetListAsync(int page, CancellationToken ct = default)
    {
        var uri = Catalog_Номенклатура.GetListUri(page);

        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);

        return rootObject?.Value;
    }

    private static StockKeepingUnit MapToStockKeepingUnit(Catalog_Номенклатура fetchedItem)
    {
        return new StockKeepingUnit
        {
            Id = fetchedItem.Ref_Key,
            BaseUnitOfMeasureId = fetchedItem.ЕдиницаИзмерения_Key == Guid.Empty ? null : fetchedItem.ЕдиницаИзмерения_Key,
            Code = fetchedItem.Code,
            DeletionMark = fetchedItem.DeletionMark,
            Name = fetchedItem.Description,
            WeightKg = fetchedItem.ВесИспользовать ? fetchedItem.ВесЧислитель / fetchedItem.ВесЗнаменатель : null
        };
    }
}