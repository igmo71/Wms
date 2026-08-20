using Wms.Application.OrganizationalUnits;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

public class Catalog_СтруктураПредприятия_Service(
    OneCClient oneCClient,
    OrganizationalUnitService organizationalUnitService)
{
    public async Task<OperationResult> ImportAsync(string refKey, CancellationToken ct = default)
    {
        var uri = Catalog_СтруктураПредприятия.GetUri(refKey);
        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_СтруктураПредприятия>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItem = serviceResult.Value?.Value?[0];

        if (fetchedItem is null)
            return OperationError.Failure("1С вернула некорректный ответ: подразделение отсутствует.");

        await organizationalUnitService.CreateOrUpdateAsync(MapToOrganizationalUnit(fetchedItem), ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> ImportListAsync(CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity(
            "Catalog_СтруктураПредприятия Import List",
            nameof(Catalog_СтруктураПредприятия_Service));

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_СтруктураПредприятия>>(
            Catalog_СтруктураПредприятия.GetListUri,
            ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return OperationError.Failure("1С вернула некорректный ответ: структура предприятия отсутствует.");

        var organizationalUnits = fetchedItems
            .Select(MapToOrganizationalUnit)
            .ToList();

        await organizationalUnitService.CreateOrUpdateBatchAsync(organizationalUnits, ct);

        return OperationResult.Success();
    }

    private static OrganizationalUnit MapToOrganizationalUnit(Catalog_СтруктураПредприятия fetchedItem)
    {
        return new OrganizationalUnit
        {
            Id = fetchedItem.Ref_Key,
            Code = fetchedItem.Code,
            Name = fetchedItem.Description,
            DeletionMark = fetchedItem.DeletionMark,
            ParentId = fetchedItem.Parent_Key is null || fetchedItem.Parent_Key == Guid.Empty
                ? null
                : fetchedItem.Parent_Key
        };
    }
}
