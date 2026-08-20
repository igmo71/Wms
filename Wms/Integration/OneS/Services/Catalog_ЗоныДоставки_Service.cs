using Wms.Application.DeliveryDirections;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

public class Catalog_ЗоныДоставки_Service(
    OneCClient oneCClient,
    DeliveryDirectionService deliveryDirectionService)
{
    public async Task<OperationResult> ImportAsync(string refKey, CancellationToken ct = default)
    {
        var uri = Catalog_ЗоныДоставки.GetUri(refKey);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_ЗоныДоставки>>(uri, ct);

        if (!serviceResult.IsSuccess)
        {
            return serviceResult;
        }

        var fetchedItem = serviceResult.Value?.Value?[0];

        if (fetchedItem is null)
        {
            return OperationError.Failure("1С вернула некорректный ответ: направление доставки отсутствует.");
        }

        var deliveryDirection = MapToDeliveryDirection(fetchedItem);

        await deliveryDirectionService.CreateOrUpdateAsync(deliveryDirection, ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> ImportListAsync(CancellationToken ct = default)
    {
        var uri = Catalog_ЗоныДоставки.GetListUri;

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_ЗоныДоставки>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return OperationError.Failure("1С вернула некорректный ответ: список направлений доставки отсутствует.");

        foreach (var fetchedItem in fetchedItems)
        {
            var warehouse = MapToDeliveryDirection(fetchedItem);

            await deliveryDirectionService.CreateOrUpdateAsync(warehouse, ct);
        }

        return OperationResult.Success();
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
