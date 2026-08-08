using Wms.Application.Services;
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

    public async Task ImportListAsync(CancellationToken ct = default)
    {
        var uri = Catalog_ЗоныДоставки.GetListUri;

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_ЗоныДоставки>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return;

        foreach (var fetchedItem in fetchedItems)
        {
            var warehouse = MapToDeliveryDirection(fetchedItem);

            await deliveryDirectionService.CreateOrUpdateAsync(warehouse, ct);
        }
    }

    private static DeliveryDirection MapToDeliveryDirection(Catalog_ЗоныДоставки fetchedItem)
    {
        return new DeliveryDirection
        {
            Id = fetchedItem.Ref_Key,
            DeletionMark = fetchedItem.DeletionMark,
            Description = fetchedItem.Description,
            Comment = fetchedItem.Описание
        };
    }
}
