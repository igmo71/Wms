using Microsoft.Extensions.Logging;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class Catalog_Номенклатура_Service(
    OneCClient oneCClient,
    StockKeepingUnitService stockKeepingUnitService,
    ILogger<Catalog_Номенклатура_Service> logger)
{


    public async Task<OperationResult> ImportAsync(string Ref_Key, CancellationToken ct = default)
    {
        var uri = Catalog_Номенклатура.GetUri(Ref_Key);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItem = serviceResult.Value?.Value?[0];

        if (fetchedItem is null)
            return OperationError.Failure("Fetched item is null.");

        var sku = MapToStockKeepingUnit(fetchedItem);

        await stockKeepingUnitService.CreateOrUpdateAsync(sku, ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> ImportListAsync(CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("Catalog_Номенклатура Import List", nameof(Catalog_Номенклатура_Service));

        var totalUri = Catalog_Номенклатура.TotalUri;

        var serviceResult = await oneCClient.GetValueAsync<int?>(totalUri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        if (serviceResult.Value is not int totalItems)
            return OperationError.Failure("1С вернула некорректный ответ: количество позиций номенклатуры отсутствует.");

        int batchSize = Catalog_Номенклатура.BatchSize;

        int totalBatches = (totalItems + batchSize - 1) / batchSize;

        List<Task<OperationResult>> tasks = [];

        using var semaphore = new SemaphoreSlim(10);

        for (int i = 0; i < totalBatches; i++)
        {
            int batchIndex = i;

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);

                try
                {
                    var uri = Catalog_Номенклатура.GetListUri(batchIndex);

                    var batchResult = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);

                    if (!batchResult.IsSuccess)
                        return OperationResult.Failure(batchResult.Error!);

                    var fetchedItems = batchResult.Value?.Value;

                    if (fetchedItems is null)
                        return OperationResult.Failure(OperationError.Failure("1С вернула некорректный ответ: пакет номенклатуры отсутствует."));

                    if (fetchedItems.Count == 0)
                        return OperationResult.Success();

                    List<StockKeepingUnit> stockKeepingUnits = fetchedItems
                        .Select(MapToStockKeepingUnit)
                        .ToList();

                    await stockKeepingUnitService.CreateOrUpdateBatchAsync(stockKeepingUnits, ct);

                    return OperationResult.Success();
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        var batchResults = await Task.WhenAll(tasks);
        var failedResult = batchResults.FirstOrDefault(x => !x.IsSuccess);

        return failedResult is null
            ? OperationResult.Success()
            : OperationError.Failure(
                $"Не удалось полностью обновить номенклатуру. Часть данных могла быть обновлена. {failedResult.Error?.Message}");
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
            WeightKg = fetchedItem.ВесИспользовать && fetchedItem.ВесЗнаменатель != 0
                ? fetchedItem.ВесЧислитель / fetchedItem.ВесЗнаменатель
                : null
        };
    }
}
