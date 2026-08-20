using Wms.Application.Partners;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class Catalog_Партнеры_Service(
    OneCClient oneCClient,
    PartnerService partnerService)
{
    public async Task<OperationResult> ImportAsync(string refKey, CancellationToken ct = default)
    {
        var uri = Catalog_Партнеры.GetUri(refKey);
        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_Партнеры>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItem = serviceResult.Value?.Value?[0];

        if (fetchedItem is null)
            return OperationError.Failure("1С вернула некорректный ответ: партнёр отсутствует.");

        await partnerService.CreateOrUpdateAsync(MapToPartner(fetchedItem), ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> ImportListAsync(CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("Catalog_Партнеры Import List", nameof(Catalog_Партнеры_Service));

        var totalResult = await oneCClient.GetValueAsync<int?>(Catalog_Партнеры.TotalUri, ct);

        if (!totalResult.IsSuccess)
            return totalResult;

        if (totalResult.Value is not int totalItems)
            return OperationError.Failure("1С вернула некорректный ответ: количество партнёров отсутствует.");

        var totalBatches = (totalItems + Catalog_Партнеры.BatchSize - 1) / Catalog_Партнеры.BatchSize;
        List<Task<OperationResult>> tasks = [];

        using var semaphore = new SemaphoreSlim(10);

        for (var i = 0; i < totalBatches; i++)
        {
            var batchIndex = i;

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);

                try
                {
                    var uri = Catalog_Партнеры.GetListUri(batchIndex);
                    var batchResult = await oneCClient.GetValueAsync<RootObject<Catalog_Партнеры>>(uri, ct);

                    if (!batchResult.IsSuccess)
                        return OperationResult.Failure(batchResult.Error!);

                    var fetchedItems = batchResult.Value?.Value;

                    if (fetchedItems is null)
                        return OperationResult.Failure(
                            OperationError.Failure("1С вернула некорректный ответ: пакет партнёров отсутствует."));

                    if (fetchedItems.Count == 0)
                        return OperationResult.Success();

                    var partners = fetchedItems
                        .Select(MapToPartner)
                        .ToList();

                    await partnerService.CreateOrUpdateBatchAsync(partners, ct);

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
                $"Не удалось полностью обновить партнёров. Часть данных могла быть обновлена. {failedResult.Error?.Message}");
    }

    private static Partner MapToPartner(Catalog_Партнеры fetchedItem)
    {
        return new Partner
        {
            Id = fetchedItem.Ref_Key,
            Name = fetchedItem.Description,
            Code = fetchedItem.Code,
            DeletionMark = fetchedItem.DeletionMark,
            ParentId = fetchedItem.Parent_Key is null || fetchedItem.Parent_Key == Guid.Empty
                ? null
                : fetchedItem.Parent_Key
        };
    }
}
