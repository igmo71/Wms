using Wms.Application.OpeningImports;
using Wms.Domain.Enums;

namespace Wms.Tests.OpeningImports;

public sealed class OpeningTopologyPlanTests
{
    [Fact]
    public void Distinct_purposes_share_physical_folders_and_repeated_stock_address_is_one_cell()
    {
        var rows = new[]
        {
            Row(2, "1A01A1", "Комплектация"),
            Row(3, "1A01B1", "Хранение"),
            Row(4, "1A01A1", "Комплектация") with
            {
                SourceSkuGuid = Guid.NewGuid().ToString(),
                SourceQuantity = "2",
                Quantity = 2m
            },
            new OpeningExportRow
            {
                RowNumber = 5, Area = "Ворота", CellGroup = "Ворота",
                Address = "9Z99A1", CellKind = "Отгрузка"
            }
        };
        var plan = Build(rows, EmptyState());

        Assert.True(plan.CanConfirm);
        Assert.Equal(3, plan.Cells.Count);
        Assert.Equal(1, plan.Counts.RepeatedAddressRows);
        Assert.Equal(1, plan.Counts.StockRows);
        Assert.Equal(2, plan.Counts.EmptyCells);
        Assert.Equal(3, plan.Zones.Count);
        Assert.Equal(plan.Cells[0].ParentCode, plan.Cells[1].ParentCode);
        Assert.NotEqual(plan.Cells[0].ZoneCode, plan.Cells[1].ZoneCode);
        Assert.Equal("FGATES", plan.Cells.Single(x => x.Address == "9Z99A1").ParentCode);
        Assert.Contains(plan.Folders, x => x.Code == "FGATES" && x.ParentCode is null);
        Assert.Equal(2, plan.Page(1, 2).Cells.Items.Count);
        Assert.Equal(1, plan.Page(2, 2).Cells.Items.Count);
    }

    [Fact]
    public void Conflicting_repeated_address_blocks_and_saved_state_changes_signature()
    {
        var rows = new[]
        {
            Row(2, "1A01A1", "Комплектация"),
            Row(3, "1A01A1", "Хранение")
        };
        var clean = Build(rows, EmptyState());
        var changed = Build(rows, EmptyState() with
        {
            Zones = [new OpeningTopologyExistingZone(
                Guid.NewGuid(), "UNRELATED", "Другая зона", ZoneType.Storage, false)]
        });

        Assert.False(clean.CanConfirm);
        Assert.Contains(clean.Issues, x => x.BlocksConfirmation
            && x.Address == "1A01A1" && x.RowNumber == 3);
        Assert.NotEqual(clean.Signature, changed.Signature);
    }

    [Fact]
    public void Repeating_the_one_approved_chain_correction_blocks_topology()
    {
        var chain = new OpeningExportRow
        {
            RowNumber = 2,
            Area = "(1) Фронтальные стеллажи",
            CellGroup = "Помещение 1",
            Address = "9Z99A1",
            CellKind = "Комплектация",
            SourceSkuGuid = "ea64f133-2d92-11ec-baa2-00155d01d102",
            SkuId = Guid.Parse("ea64f133-2d92-11ec-baa2-00155d01d102"),
            UnitOfMeasure = "шт",
            SourceQuantity = "1",
            Quantity = 1m,
            RequiresExpirationDate = false
        };
        var plan = Build([chain, chain with { RowNumber = 3 }], EmptyState());

        Assert.False(plan.CanConfirm);
        Assert.Equal(2, plan.Counts.CorrectedRows);
        Assert.Contains(plan.Issues, x => x.BlocksConfirmation && x.Address == "9Z99A1");
    }

    private static OpeningExportRow Row(int row, string address, string kind) => new()
    {
        RowNumber = row,
        Area = "(1) Фронтальные стеллажи",
        CellGroup = "Помещение 1",
        Address = address,
        CellKind = kind
    };

    private static OpeningTopologyImportState EmptyState() =>
        new(true, false, null, [], []);

    private static OpeningTopologyPlan Build(
        IReadOnlyList<OpeningExportRow> rows, OpeningTopologyImportState state) =>
        OpeningTopologyPlanBuilder.Build(Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new string('A', 64), new OpeningExportReadResult(rows, []), state);
}
