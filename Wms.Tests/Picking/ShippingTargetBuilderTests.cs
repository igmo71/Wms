using System.Text.Json;
using System.Text.Json.Nodes;
using Wms.Domain;
using Wms.Integration.OneS;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Picking;

public sealed class ShippingTargetBuilderTests
{
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid SkuId = Guid.NewGuid();
    private static readonly Guid FirstBasis = Guid.NewGuid();
    private static readonly Guid SecondBasis = Guid.NewGuid();
    private static readonly Guid SecondSeries = Guid.NewGuid();

    [Fact]
    public void Allocated_fact_fills_source_lines_in_order_and_keeps_basis_links()
    {
        var source = Source(includeSecondLine: true);
        var target = Target(source, approved: 10, allocated: 8);

        var patch = Value(new ShippingTargetBuilder().Build(target, source));
        using var json = JsonDocument.Parse(patch.PatchJson);
        var items = json.RootElement.GetProperty("ОтгружаемыеТовары")
            .EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal(new[] { 3m, 5m, 2m }, items.Select(Quantity));
        Assert.Equal(new[] { "Отгрузить", "Отгрузить", "НеОтгружать" },
            items.Select(x => x.GetProperty("Действие").GetString()));
        Assert.Equal(SecondSeries.ToString(),
            items[2].GetProperty("Серия_Key").GetString());
        Assert.False(items[0].TryGetProperty("@odata.etag", out _));

        var basis = json.RootElement.GetProperty("ТоварыПоРаспоряжениям")
            .EnumerateArray().ToArray();
        Assert.Equal(new[] { 3m, 5m }, basis.Select(Quantity));
        Assert.Equal(new[] { FirstBasis.ToString(), SecondBasis.ToString() },
            basis.Select(x => x.GetProperty("Распоряжение").GetString()));
        Assert.Equal("Отгружен", json.RootElement.GetProperty("Статус").GetString());
    }

    [Fact]
    public void Zero_fact_restores_removed_line_as_not_shipped_and_clears_basis()
    {
        var original = Source(includeSecondLine: true);
        var reviewed = Source(includeSecondLine: false);
        foreach (var approved in new[] { 0m, 10m })
        {
            var target = Target(reviewed, approved, allocated: 0);
            var patch = Value(new ShippingTargetBuilder().Build(target, original));
            using var json = JsonDocument.Parse(patch.PatchJson);
            var items = json.RootElement.GetProperty("ОтгружаемыеТовары")
                .EnumerateArray().ToArray();
            Assert.Equal(2, items.Length);
            Assert.Equal(new[] { 3m, 7m }, items.Select(Quantity));
            Assert.All(items, x => Assert.Equal("НеОтгружать",
                x.GetProperty("Действие").GetString()));
            Assert.Equal(SecondSeries.ToString(),
                items[1].GetProperty("Серия_Key").GetString());
            Assert.Empty(json.RootElement.GetProperty("ТоварыПоРаспоряжениям")
                .EnumerateArray());
        }
    }

    [Fact]
    public void Changed_source_quantity_does_not_raise_original_line_limit()
    {
        var original = Source(includeSecondLine: true);
        var reviewed = Source(includeSecondLine: true,
            firstQuantity: 10, secondQuantity: 0);
        var target = Target(reviewed, approved: 10, allocated: 8);

        var patch = Value(new ShippingTargetBuilder().Build(target, original));
        using var json = JsonDocument.Parse(patch.PatchJson);
        var items = json.RootElement.GetProperty("ОтгружаемыеТовары")
            .EnumerateArray().ToArray();
        Assert.Equal(new[] { 3m, 5m, 2m }, items.Select(Quantity));
    }

    private static decimal Quantity(JsonElement line) =>
        line.GetProperty("Количество").GetDecimal();

    private static ShippingExchangeTarget Target(string reviewed,
        decimal approved, decimal allocated) =>
        Value(ShippingExchangeTarget.Create(Guid.NewGuid(), OrderId, 1,
            reviewed, JsonSerializer.Serialize(new
            {
                Version = 1, Status = "Shipped",
                ApprovedPlan = new[] { new { SkuId, Quantity = approved } },
                Allocation = new[] { new { SkuId, Quantity = allocated } }
            })));

    private static string Source(bool includeSecondLine,
        decimal firstQuantity = 3, decimal secondQuantity = 7)
    {
        var items = new JsonArray
        {
            ShippingLine(1, firstQuantity, Guid.NewGuid())
        };
        var basis = new JsonArray
        {
            BasisLine(1, firstQuantity, FirstBasis)
        };
        if (includeSecondLine)
        {
            items.Add(ShippingLine(2, secondQuantity, SecondSeries));
            basis.Add(BasisLine(2, secondQuantity, SecondBasis));
        }
        return new JsonObject
        {
            ["Ref_Key"] = OrderId.ToString(),
            ["ОтгружаемыеТовары"] = items,
            ["ТоварыПоРаспоряжениям"] = basis
        }.ToJsonString();
    }

    private static JsonObject ShippingLine(int number, decimal quantity,
        Guid series) => new()
    {
        ["LineNumber"] = number,
        ["Номенклатура_Key"] = SkuId.ToString(),
        ["Количество"] = quantity,
        ["КоличествоУпаковок"] = quantity,
        ["Действие"] = "Отгрузить",
        ["ЭтоУпаковочныйЛист"] = false,
        ["ЭтоСлужебнаяСтрокаПустогоУпаковочногоЛиста"] = 0,
        ["Серия_Key"] = series.ToString(),
        ["@odata.etag"] = "metadata"
    };

    private static JsonObject BasisLine(int number, decimal quantity,
        Guid basis) => new()
    {
        ["LineNumber"] = number,
        ["Номенклатура_Key"] = SkuId.ToString(),
        ["Количество"] = quantity,
        ["Распоряжение"] = basis.ToString(),
        ["Распоряжение_Type"] = "StandardODATA.Document_Распоряжение",
        ["Серия_Key"] = SecondSeries.ToString()
    };
}
