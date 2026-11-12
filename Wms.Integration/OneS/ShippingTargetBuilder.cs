using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Integration.OneS;

public sealed record ShippingTargetPatch(Guid ShippingOrderId, string PatchJson);

public sealed class ShippingTargetBuilder
{
    private static readonly string[] ShippingFields =
    [
        "Ref_Key", "LineNumber", "Номенклатура_Key", "Количество",
        "КоличествоУпаковок", "Действие", "Характеристика_Key",
        "Назначение_Key", "Серия_Key", "СтатусУказанияСерий",
        "ЭтоУпаковочныйЛист", "Упаковка_Key", "УпаковочныйЛист_Key",
        "УпаковочныйЛистРодитель_Key",
        "ЭтоСлужебнаяСтрокаПустогоУпаковочногоЛиста"
    ];
    private static readonly string[] BasisFields =
    [
        "Ref_Key", "LineNumber", "Номенклатура_Key", "Количество",
        "Распоряжение", "Распоряжение_Type", "Характеристика_Key",
        "Назначение_Key", "Серия_Key", "СтатусУказанияСерий"
    ];
    private readonly ShippingStoredSnapshotParser parser = new();

    public OperationResult<ShippingTargetPatch> Build(
        ShippingExchangeTarget target, string planSourceSnapshotJson)
    {
        if (target is null || string.IsNullOrWhiteSpace(planSourceSnapshotJson))
            return OperationError.Invalid("Для обмена нужны цель и сохранённый снимок РО 1С.");
        var original = parser.Parse(target.ShippingOrderId, planSourceSnapshotJson);
        if (!original.IsSuccess) return original.Error!;
        var reviewed = parser.Parse(target.ShippingOrderId, target.ReviewedSourceJson);
        if (!reviewed.IsSuccess) return reviewed.Error!;

        Decision? decision;
        try { decision = JsonSerializer.Deserialize<Decision>(target.TargetDocumentJson); }
        catch (JsonException)
        { return OperationError.Conflict("Сохранённая цель обмена РО 1С нечитаема."); }
        if (decision is null || decision.Version != 1
            || decision.Status != ShippingOrderStatus.Shipped.ToString()
            || decision.ApprovedPlan is null || decision.Allocation is null
            || decision.ApprovedPlan.Any(x => x.SkuId == Guid.Empty
                || !WarehouseQuantity.IsNonNegative(x.Quantity))
            || decision.Allocation.Any(x => x.SkuId == Guid.Empty
                || !WarehouseQuantity.IsNonNegative(x.Quantity))
            || decision.ApprovedPlan.Select(x => x.SkuId).Distinct().Count()
                != decision.ApprovedPlan.Count
            || decision.Allocation.Select(x => x.SkuId).Distinct().Count()
                != decision.Allocation.Count)
            return OperationError.Conflict("Сохранённая цель обмена РО 1С некорректна.");

        var approved = decision.ApprovedPlan.ToDictionary(x => x.SkuId, x => x.Quantity);
        var allocated = decision.Allocation.ToDictionary(x => x.SkuId, x => x.Quantity);
        if (allocated.Any(x => !approved.TryGetValue(x.Key, out var plan)
            || x.Value > plan))
            return OperationError.Conflict("Распределение РО 1С превышает утверждённый план.");

        var items = BuildShippingItems(original.Value!, reviewed.Value!, approved, allocated);
        if (!items.IsSuccess) return items.Error!;
        var basis = BuildBasisItems(original.Value!, reviewed.Value!, allocated);
        if (!basis.IsSuccess) return basis.Error!;
        var patch = new JsonObject
        {
            ["Статус"] = "Отгружен",
            ["ТоварыПоРаспоряжениям"] = basis.Value!,
            ["ОтгружаемыеТовары"] = items.Value!
        };
        return new ShippingTargetPatch(target.ShippingOrderId, patch.ToJsonString());
    }

    public bool MatchesCurrent(ShippingTargetPatch patch,
        ShippingSourceSnapshot current)
    {
        if (patch.ShippingOrderId != current.RefKey
            || current.Status != ShippingOrderStatus.Shipped)
            return false;
        using var json = JsonDocument.Parse(patch.PatchJson);
        var document = json.RootElement;
        return RowsMatch(document.GetProperty("ОтгружаемыеТовары"),
                current.Items.Select(x => x.RawLineJson), ShippingFields)
            && RowsMatch(document.GetProperty("ТоварыПоРаспоряжениям"),
                current.BasisItems.Select(x => x.RawLineJson), BasisFields);
    }

    private static bool RowsMatch(JsonElement expected,
        IEnumerable<string> currentRows, string[] fields)
    {
        var left = expected.EnumerateArray()
            .Select(x => Normalize(CopyFields(x.GetRawText(), fields)))
            .OrderBy(x => x["LineNumber"]!.GetValue<int>()).ToArray();
        var right = currentRows
            .Select(x => Normalize(CopyFields(x, fields)))
            .OrderBy(x => x["LineNumber"]!.GetValue<int>()).ToArray();
        return left.Length == right.Length
            && left.Zip(right).All(pair =>
                JsonNode.DeepEquals(pair.First, pair.Second));
    }

    private static JsonObject Normalize(JsonObject line)
    {
        foreach (var field in new[] { "Количество", "КоличествоУпаковок" })
            if (line[field] is JsonNode value
                && decimal.TryParse(value.ToString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var quantity))
                line[field] = quantity;
        if (line["LineNumber"] is JsonNode number
            && int.TryParse(number.ToString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var lineNumber))
            line["LineNumber"] = lineNumber;
        return line;
    }

    private static OperationResult<JsonArray> BuildShippingItems(
        ShippingSourceSnapshot original, ShippingSourceSnapshot reviewed,
        IReadOnlyDictionary<Guid, decimal> approved,
        IReadOnlyDictionary<Guid, decimal> allocated)
    {
        var result = new JsonArray();
        var used = new HashSet<int>();
        var next = original.Items.Concat(reviewed.Items)
            .Select(x => x.LineNumber).DefaultIfEmpty().Max() + 1;
        foreach (var line in reviewed.Items.Where(x => !x.IsRegular)
            .OrderBy(x => x.LineNumber))
        {
            result.Add(CopyFields(line.RawLineJson, ShippingFields));
            used.Add(line.LineNumber);
        }

        var templates = MergeItems(original.Items.Where(x => x.IsRegular),
            reviewed.Items.Where(x => x.IsRegular));
        foreach (var group in templates.GroupBy(x => x.SkuId).OrderBy(x => x.Min(y => y.LineNumber)))
        {
            var planRemaining = approved.GetValueOrDefault(group.Key);
            var shipRemaining = allocated.GetValueOrDefault(group.Key);
            var allowReviewedGrowth = planRemaining > group.Sum(x => x.OriginalCapacity);
            foreach (var template in group.OrderBy(x => x.LineNumber))
            {
                // A SKU not in the WMS plan remains in the reviewed document as
                // a non-shipped line; it cannot contribute to the warehouse fact.
                var capacity = allowReviewedGrowth
                    ? Math.Max(template.OriginalCapacity, template.ReviewedQuantity)
                    : template.OriginalCapacity;
                var plan = approved.ContainsKey(group.Key)
                    ? planRemaining == 0 && approved[group.Key] == 0
                        ? Math.Max(template.OriginalCapacity, template.ReviewedQuantity)
                        : Math.Min(planRemaining, capacity)
                    : template.ReviewedQuantity;
                if (plan <= 0) continue;
                if (approved.TryGetValue(group.Key, out var approvedQuantity)
                    && approvedQuantity > 0) planRemaining -= plan;
                var shipped = Math.Min(shipRemaining, plan);
                shipRemaining -= shipped;
                var firstNumber = Reserve(template.LineNumber, used, ref next);
                result.Add(ShippingLine(template.RawJson, firstNumber,
                    shipped > 0 ? shipped : plan,
                    shipped > 0 ? "Отгрузить" : "НеОтгружать"));
                if (shipped > 0 && plan > shipped)
                    result.Add(ShippingLine(template.RawJson,
                        Reserve(0, used, ref next), plan - shipped, "НеОтгружать"));
            }
            if (planRemaining != 0 || shipRemaining != 0)
                return OperationError.Conflict(
                    "Утверждённый план или факт РО 1С нельзя распределить по сохранённым строкам SKU.");
        }
        if (approved.Any(x => x.Value > 0 && !templates.Any(y => y.SkuId == x.Key)))
            return OperationError.Conflict("Для SKU утверждённого плана нет сохранённой строки РО 1С.");
        return result;
    }

    private static OperationResult<JsonArray> BuildBasisItems(
        ShippingSourceSnapshot original, ShippingSourceSnapshot reviewed,
        IReadOnlyDictionary<Guid, decimal> allocated)
    {
        var result = new JsonArray();
        var used = new HashSet<int>();
        var next = original.BasisItems.Concat(reviewed.BasisItems)
            .Select(x => x.LineNumber).DefaultIfEmpty().Max() + 1;
        var templates = MergeBasis(original.BasisItems, reviewed.BasisItems);
        foreach (var (skuId, quantity) in allocated.Where(x => x.Value > 0))
        {
            var remaining = quantity;
            var allowReviewedGrowth = quantity > templates.Where(x => x.SkuId == skuId)
                .Sum(x => x.OriginalCapacity);
            foreach (var line in templates.Where(x => x.SkuId == skuId)
                .OrderBy(x => x.LineNumber))
            {
                var capacity = allowReviewedGrowth
                    ? Math.Max(line.OriginalCapacity, line.ReviewedQuantity)
                    : line.OriginalCapacity;
                var taken = Math.Min(remaining, capacity);
                if (taken <= 0) continue;
                var item = CopyFields(line.RawJson, BasisFields);
                item["LineNumber"] = Reserve(line.LineNumber, used, ref next);
                item["Количество"] = taken;
                result.Add(item);
                remaining -= taken;
                if (remaining == 0) break;
            }
            if (remaining != 0)
                return OperationError.Conflict(
                    "Распределённый факт РО 1С нельзя связать с сохранёнными распоряжениями.");
        }
        return result;
    }

    private static IReadOnlyList<LineTemplate> MergeItems(
        IEnumerable<ShippingSourceItem> original,
        IEnumerable<ShippingSourceItem> reviewed)
    {
        var current = reviewed.Where(x => x.StockKeepingUnitId is not null)
            .ToDictionary(x => (x.LineNumber, x.StockKeepingUnitId));
        var result = new List<LineTemplate>();
        foreach (var line in original.Where(x => x.StockKeepingUnitId is not null))
        {
            current.Remove((line.LineNumber, line.StockKeepingUnitId), out var fresh);
            result.Add(new LineTemplate(line.LineNumber, line.StockKeepingUnitId!.Value,
                line.PackageQuantity ?? 0,
                fresh?.PackageQuantity ?? 0, fresh?.RawLineJson ?? line.RawLineJson));
        }
        result.AddRange(current.Values.Select(x => new LineTemplate(x.LineNumber,
            x.StockKeepingUnitId!.Value, 0,
            x.PackageQuantity ?? 0, x.RawLineJson)));
        return result;
    }

    private static IReadOnlyList<LineTemplate> MergeBasis(
        IEnumerable<ShippingSourceBasisItem> original,
        IEnumerable<ShippingSourceBasisItem> reviewed)
    {
        var current = reviewed.Where(x => x.StockKeepingUnitId is not null)
            .ToDictionary(x => (x.LineNumber, x.StockKeepingUnitId));
        var result = new List<LineTemplate>();
        foreach (var line in original.Where(x => x.StockKeepingUnitId is not null))
        {
            current.Remove((line.LineNumber, line.StockKeepingUnitId), out var fresh);
            result.Add(new LineTemplate(line.LineNumber, line.StockKeepingUnitId!.Value,
                line.Quantity ?? 0,
                fresh?.Quantity ?? 0, fresh?.RawLineJson ?? line.RawLineJson));
        }
        result.AddRange(current.Values.Select(x => new LineTemplate(x.LineNumber,
            x.StockKeepingUnitId!.Value, 0,
            x.Quantity ?? 0, x.RawLineJson)));
        return result;
    }

    private static JsonObject ShippingLine(string rawJson, int number,
        decimal quantity, string action)
    {
        var line = CopyFields(rawJson, ShippingFields);
        line["LineNumber"] = number;
        line["Количество"] = quantity;
        line["КоличествоУпаковок"] = quantity;
        line["Действие"] = action;
        return line;
    }

    private static JsonObject CopyFields(string rawJson, IEnumerable<string> fields)
    {
        var source = JsonNode.Parse(rawJson)!.AsObject();
        var line = new JsonObject();
        foreach (var field in fields)
            if (source.TryGetPropertyValue(field, out var value))
                line[field] = value?.DeepClone();
        return line;
    }

    private static int Reserve(int preferred, HashSet<int> used, ref int next)
    {
        if (preferred > 0 && used.Add(preferred)) return preferred;
        while (!used.Add(next)) next++;
        return next++;
    }

    private sealed record LineTemplate(int LineNumber, Guid SkuId,
        decimal OriginalCapacity, decimal ReviewedQuantity, string RawJson);
    private sealed record SkuQuantity(Guid SkuId, decimal Quantity);
    private sealed record Decision(int Version, string Status,
        IReadOnlyList<SkuQuantity> ApprovedPlan,
        IReadOnlyList<SkuQuantity> Allocation);
}
