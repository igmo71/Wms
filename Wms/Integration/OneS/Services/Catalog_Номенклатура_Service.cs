using Microsoft.Extensions.Logging;
using Wms.Application.StockKeepingUnits;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

public class Catalog_Номенклатура_Service(
    OneCClient oneCClient,
    StockKeepingUnitService stockKeepingUnitService,
    Catalog_УпаковкиЕдиницыИзмерения_Service unitOfMeasureImportService,
    ILogger<Catalog_Номенклатура_Service> logger)
{
    public async Task<OperationResult> ImportAsync(string Ref_Key, CancellationToken ct = default)
    {
        var unitResult = await unitOfMeasureImportService.ImportListAndGetAsync(ct);
        if (!unitResult.IsSuccess)
        {
            return unitResult.Error!;
        }

        var uri = Catalog_Номенклатура.GetUri(Ref_Key);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItem = serviceResult.Value?.Value?[0];

        if (fetchedItem is null)
            return OperationError.Failure("1С вернула некорректный ответ: позиция номенклатуры отсутствует.");

        var mapResult = MapToStockKeepingUnit(fetchedItem, unitResult.Value!);

        LogPhysicalPropertyIssue(fetchedItem, "веса", mapResult.WeightIssue);
        LogPhysicalPropertyIssue(fetchedItem, "объёма", mapResult.VolumeIssue);

        await stockKeepingUnitService.CreateOrUpdateAsync(mapResult.StockKeepingUnit, ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult<StockKeepingUnitImportSummary>> ImportListAsync(CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("Catalog_Номенклатура Import List", nameof(Catalog_Номенклатура_Service));

        var unitResult = await unitOfMeasureImportService.ImportListAndGetAsync(ct);
        if (!unitResult.IsSuccess)
        {
            return unitResult.Error!;
        }

        var units = unitResult.Value!;

        var totalUri = Catalog_Номенклатура.TotalUri;

        var serviceResult = await oneCClient.GetValueAsync<int?>(totalUri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult.Error!;

        if (serviceResult.Value is not int totalItems)
            return OperationError.Failure("1С вернула некорректный ответ: количество позиций номенклатуры отсутствует.");

        int batchSize = Catalog_Номенклатура.BatchSize;

        int totalBatches = (totalItems + batchSize - 1) / batchSize;

        List<Task<OperationResult<StockKeepingUnitImportSummary>>> tasks = [];

        using var semaphore = new SemaphoreSlim(10);

        for (int i = 0; i < totalBatches; i++)
        {
            int batchIndex = i;

            tasks.Add(Task.Run<OperationResult<StockKeepingUnitImportSummary>>(async () =>
            {
                await semaphore.WaitAsync(ct);

                try
                {
                    var uri = Catalog_Номенклатура.GetListUri(batchIndex);

                    var batchResult = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);

                    if (!batchResult.IsSuccess)
                        return batchResult.Error!;

                    var fetchedItems = batchResult.Value?.Value;

                    if (fetchedItems is null)
                        return OperationError.Failure("1С вернула некорректный ответ: пакет номенклатуры отсутствует.");

                    if (fetchedItems.Count == 0)
                        return new StockKeepingUnitImportSummary(0, 0);

                    var mapResults = fetchedItems
                        .Select(x => MapToStockKeepingUnit(x, units))
                        .ToList();

                    foreach (var item in fetchedItems.Zip(mapResults))
                    {
                        LogPhysicalPropertyIssue(item.First, "веса", item.Second.WeightIssue);
                        LogPhysicalPropertyIssue(item.First, "объёма", item.Second.VolumeIssue);
                    }

                    List<StockKeepingUnit> stockKeepingUnits = mapResults
                        .Select(x => x.StockKeepingUnit)
                        .ToList();

                    await stockKeepingUnitService.CreateOrUpdateBatchAsync(stockKeepingUnits, ct);

                    return new StockKeepingUnitImportSummary(
                        mapResults.Count(x => x.WeightIssue is not null),
                        mapResults.Count(x => x.VolumeIssue is not null));
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
            ? new StockKeepingUnitImportSummary(
                batchResults.Sum(x => x.Value!.InvalidWeightCount),
                batchResults.Sum(x => x.Value!.InvalidVolumeCount))
            : OperationError.Failure(
                $"Не удалось полностью обновить номенклатуру. Часть данных могла быть обновлена. {failedResult.Error?.Message}");
    }

    private static StockKeepingUnitMapResult MapToStockKeepingUnit(
        Catalog_Номенклатура fetchedItem,
        IReadOnlyDictionary<Guid, UnitOfMeasure> units)
    {
        var weight = NormalizePhysicalProperty(
            fetchedItem.ВесИспользовать,
            fetchedItem.ВесЧислитель,
            fetchedItem.ВесЗнаменатель,
            fetchedItem.ВесЕдиницаИзмерения_Key,
            "Вес",
            units);
        var volume = NormalizePhysicalProperty(
            fetchedItem.ОбъемИспользовать,
            fetchedItem.ОбъемЧислитель,
            fetchedItem.ОбъемЗнаменатель,
            fetchedItem.ОбъемЕдиницаИзмерения_Key,
            "Объем",
            units);

        var sku = new StockKeepingUnit
        {
            Id = fetchedItem.Ref_Key,
            BaseUnitOfMeasureId = fetchedItem.ЕдиницаИзмерения_Key == Guid.Empty ? null : fetchedItem.ЕдиницаИзмерения_Key,
            Code = fetchedItem.Code,
            DeletionMark = fetchedItem.DeletionMark,
            Name = fetchedItem.Description,
            WeightKg = weight.Value,
            VolumeM3 = volume.Value
        };

        return new StockKeepingUnitMapResult(sku, weight.Issue, volume.Issue);
    }

    private static PhysicalPropertyResult NormalizePhysicalProperty(
        bool useProperty,
        double? propertyNumerator,
        double? propertyDenominator,
        Guid? unitId,
        string expectedMeasurementType,
        IReadOnlyDictionary<Guid, UnitOfMeasure> units)
    {
        if (!useProperty)
        {
            return new PhysicalPropertyResult(null, null);
        }

        if (propertyNumerator is not double numerator ||
            propertyDenominator is not double denominator ||
            !double.IsFinite(numerator) ||
            !double.IsFinite(denominator) ||
            numerator < 0 ||
            denominator <= 0)
        {
            return new PhysicalPropertyResult(null, "некорректные числитель или знаменатель свойства");
        }

        if (unitId is not Guid physicalUnitId || physicalUnitId == Guid.Empty ||
            !units.TryGetValue(physicalUnitId, out var unit))
        {
            return new PhysicalPropertyResult(null, "единица измерения не найдена");
        }

        if (unit.DeletionMark)
        {
            return new PhysicalPropertyResult(null, "единица измерения деактивирована");
        }

        if (!string.Equals(unit.MeasurementType, expectedMeasurementType, StringComparison.Ordinal))
        {
            return new PhysicalPropertyResult(
                null,
                $"ожидался тип единицы '{expectedMeasurementType}', получен '{unit.MeasurementType ?? "не указан"}'");
        }

        if (unit.Numerator is not double unitNumerator ||
            unit.Denominator is not double unitDenominator ||
            !double.IsFinite(unitNumerator) ||
            !double.IsFinite(unitDenominator) ||
            unitNumerator <= 0 ||
            unitDenominator <= 0)
        {
            return new PhysicalPropertyResult(null, "некорректные числитель или знаменатель единицы измерения");
        }

        var normalizedValue = numerator / denominator * unitNumerator / unitDenominator;
        return double.IsFinite(normalizedValue)
            ? new PhysicalPropertyResult(normalizedValue, null)
            : new PhysicalPropertyResult(null, "результат пересчёта не является конечным числом");
    }

    private void LogPhysicalPropertyIssue(
        Catalog_Номенклатура item,
        string propertyName,
        string? issue)
    {
        if (issue is null)
        {
            return;
        }

        logger.LogWarning("Для номенклатуры {SkuId} ({SkuCode}) не импортировано значение {PropertyName}: {Issue}",
            item.Ref_Key, item.Code, propertyName, issue);
    }

    private sealed record StockKeepingUnitMapResult(
        StockKeepingUnit StockKeepingUnit,
        string? WeightIssue,
        string? VolumeIssue);

    private sealed record PhysicalPropertyResult(double? Value, string? Issue);
}

public sealed record StockKeepingUnitImportSummary(
    int InvalidWeightCount,
    int InvalidVolumeCount)
{
    public bool HasWarnings => InvalidWeightCount > 0 || InvalidVolumeCount > 0;
}
