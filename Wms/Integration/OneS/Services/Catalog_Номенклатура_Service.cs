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
    public async Task Import(string Ref_Key, CancellationToken ct)
    {
        var fetchedItems = await Get(Ref_Key, ct);

        if (fetchedItems is null || fetchedItems.Count == 0)
            return;

        var fetchedItem = fetchedItems[0];
        StockKeepingUnit newItem = CreateNew(fetchedItem);

        await stockKeepingUnitService.CreateOrUpdateAsync(newItem, ct);
    }

    private async Task<List<Catalog_Номенклатура>?> Get(string Ref_Key, CancellationToken ct)
    {
        var uri = Catalog_Номенклатура.GetUri(Ref_Key);
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);
        return rootObject?.Value;
    }

    public async Task ImportList(CancellationToken ct)
    {
        var startedAt = Stopwatch.GetTimestamp();

        int totalItems = await GetTotal(ct);

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
                    var fetchedItems = await GetList(batchIndex, ct);

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
            logger.LogDebug("{Source} {Elapsed}", nameof(ImportList), Stopwatch.GetElapsedTime(startedAt));
    }

    private async Task<int> GetTotal(CancellationToken ct)
    {
        var uri = Catalog_Номенклатура.TotalUri;
        var result = await oneCClient.GetValueAsync<int>(uri, ct);
        return result;
    }

    private async Task<List<Catalog_Номенклатура>?> GetList(int page, CancellationToken ct)
    {
        var uri = Catalog_Номенклатура.GetListUri(page);
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);
        return rootObject?.Value;
    }

    private async Task ProcessBatchAsync(List<Catalog_Номенклатура> fetchedItems, CancellationToken ct)
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
            BaseUnitOfMeasureId = fetchedItem.ЕдиницаИзмерения_Key == Guid.Empty ? null : fetchedItem.ЕдиницаИзмерения_Key,
            Code = fetchedItem.Code,
            DeletionMark = fetchedItem.DeletionMark,
            Id = fetchedItem.Ref_Key,
            Name = fetchedItem.Description,
            IsFolder = fetchedItem.IsFolder,
            ParentId = fetchedItem.Parent_Key,
            WeightKg = fetchedItem.ВесИспользовать.HasValue && fetchedItem.ВесИспользовать.Value
                ? fetchedItem.ВесЧислитель / fetchedItem.ВесЗнаменатель
                : null
        };
    }
}