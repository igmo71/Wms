using Wms.Application.Individuals;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class Catalog_ФизическиеЛица_Service(
    OneCClient oneCClient,
    IndividualService individualService)
{
    public async Task<OperationResult> ImportAsync(string refKey, CancellationToken ct = default)
    {
        var uri = Catalog_ФизическиеЛица.GetUri(refKey);
        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_ФизическиеЛица>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItem = serviceResult.Value?.Value?[0];

        if (fetchedItem is null)
            return OperationError.Failure("1С вернула некорректный ответ: физическое лицо отсутствует.");

        await individualService.CreateOrUpdateAsync(MapToIndividual(fetchedItem), ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> ImportListAsync(CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity(
            "Catalog_ФизическиеЛица Import List",
            nameof(Catalog_ФизическиеЛица_Service));

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_ФизическиеЛица>>(
            Catalog_ФизическиеЛица.GetListUri,
            ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return OperationError.Failure("1С вернула некорректный ответ: список физических лиц отсутствует.");

        var individuals = fetchedItems
            .Select(MapToIndividual)
            .ToList();

        await individualService.CreateOrUpdateBatchAsync(individuals, ct);

        return OperationResult.Success();
    }

    private static Individual MapToIndividual(Catalog_ФизическиеЛица fetchedItem)
    {
        return new Individual
        {
            Id = fetchedItem.Ref_Key,
            Name = fetchedItem.Description,
            DeletionMark = fetchedItem.DeletionMark,
            ParentId = fetchedItem.Parent_Key is null || fetchedItem.Parent_Key == Guid.Empty
                ? null
                : fetchedItem.Parent_Key,
            IsFolder = fetchedItem.IsFolder
        };
    }
}
