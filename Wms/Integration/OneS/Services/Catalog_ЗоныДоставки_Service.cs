using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class Catalog_ЗоныДоставки_Service(
    OneCClient oneCClient,
    DeliveryDirectionService deliveryDirectionService)
{
    public async Task ImportAsync(string Ref_Key, CancellationToken ct = default)
    {
        var uri = Catalog_ЗоныДоставки.GetUri(Ref_Key);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_ЗоныДоставки>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return;

        var fetchedItem = serviceResult.Value?.Value?[0];

        if (fetchedItem is null)
            return;

        var deliveryDirection = MapToDeliveryDirection(fetchedItem);

        await deliveryDirectionService.CreateOrUpdateAsync(deliveryDirection, ct);
    }

    public async Task<ServiceResult> ImportListAsync(CancellationToken ct = default)
    {
        var uri = Catalog_ЗоныДоставки.GetListUri;

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_ЗоныДоставки>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return ServiceError.Failure("1С вернула некорректный ответ: список направлений доставки отсутствует.");

        foreach (var fetchedItem in fetchedItems)
        {
            var warehouse = MapToDeliveryDirection(fetchedItem);

            await deliveryDirectionService.CreateOrUpdateAsync(warehouse, ct);
        }

        return ServiceResult.Success();
    }

    private static DeliveryDirection MapToDeliveryDirection(Catalog_ЗоныДоставки fetchedItem)
    {
        return new DeliveryDirection
        {
            Id = fetchedItem.Ref_Key,
            DeletionMark = fetchedItem.DeletionMark,
            ParentId = fetchedItem.Parent_Key == Guid.Empty ? null : fetchedItem.Parent_Key,
            IsFolder = fetchedItem.IsFolder,
            Description = fetchedItem.Description,
            Comment = fetchedItem.Описание
        };
    }
}
