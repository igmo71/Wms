using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Wms.Application;
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
        var fetchedItem = await GetAsync(Ref_Key, ct);

        if (fetchedItem is null)
            return;

        StockKeepingUnit newItem = CreateNew(fetchedItem);

        await stockKeepingUnitService.CreateOrUpdateAsync(newItem, ct);
    }

    private async Task<Catalog_Номенклатура?> GetAsync(string Ref_Key, CancellationToken ct = default)
    {
        var uri = Catalog_Номенклатура.GetUri(Ref_Key);

        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);

        var result = rootObject?.Value?[0];

        return result;
    }

    public async Task ImportListAsync(CancellationToken ct = default)
    {
        var startedAt = Stopwatch.GetTimestamp();

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

                    await ProcessBatchAsync(fetchedItems, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {Elapsed}", nameof(ImportListAsync), Stopwatch.GetElapsedTime(startedAt));
    }

    private async Task<int> GetTotalAsync(CancellationToken ct = default)
    {
        var uri = Catalog_Номенклатура.TotalUri;

        var result = await oneCClient.GetValueAsync<int>(uri, ct);

        return result;
    }

    private async Task<List<Catalog_Номенклатура>?> GetListAsync(int page, CancellationToken ct = default)
    {
        var uri = Catalog_Номенклатура.GetListUri(page);

        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);

        return rootObject?.Value;
    }

    private async Task ProcessBatchAsync(List<Catalog_Номенклатура> fetchedItems, CancellationToken ct = default)
    {
        foreach (var fetchedItem in fetchedItems)
        {
            StockKeepingUnit newItem = CreateNew(fetchedItem);

            await stockKeepingUnitService.CreateOrUpdateAsync(newItem, ct);
        }
    }

    private static StockKeepingUnit CreateNew(Catalog_Номенклатура fetchedItem)
    {
        return new StockKeepingUnit
        {
            Id = fetchedItem.Ref_Key,
            BaseUnitOfMeasureId = fetchedItem.ЕдиницаИзмерения_Key,
            Code = fetchedItem.Code,
            DeletionMark = fetchedItem.DeletionMark,
            Name = fetchedItem.Description,
            WeightKg = fetchedItem.ВесИспользовать ? fetchedItem.ВесЧислитель / fetchedItem.ВесЗнаменатель : null
        };
    }
}