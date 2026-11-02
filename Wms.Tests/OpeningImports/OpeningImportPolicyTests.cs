using Wms.Application.OpeningImports;
using Wms.Domain.Enums;

namespace Wms.Tests.OpeningImports;

public sealed class OpeningImportPolicyTests
{
    [Fact]
    public void Exclusions_keep_approved_loss_visible_and_block_new_stock_in_empty_exception()
    {
        var zeroAddress = new OpeningExportRow
        {
            RowNumber = 3278,
            Area = "(1) Фронтальные стеллажи",
            CellGroup = "Помещение 1",
            Address = "0A00A1",
            CellKind = "Хранение",
            SourceQuantity = "-220",
            Quantity = -220m
        };
        var zeroDecision = OpeningImportPolicy.Interpret(zeroAddress);
        Assert.Equal(OpeningImportRowOutcome.Excluded, zeroDecision.Outcome);
        Assert.Equal<decimal?>(-220m, zeroDecision.Source.Quantity);

        var emptyMismatchedRoom = new OpeningExportRow
        {
            RowNumber = 11451,
            Area = "(3) Мезонин 1 этаж 2",
            CellGroup = "Помещение 3",
            Address = "4A10A1",
            CellKind = "Хранение"
        };
        Assert.Equal(OpeningImportRowOutcome.Excluded,
            OpeningImportPolicy.Interpret(emptyMismatchedRoom).Outcome);
        Assert.Equal(OpeningImportRowOutcome.Blocking,
            OpeningImportPolicy.Interpret(emptyMismatchedRoom with
            {
                SourceQuantity = "1",
                Quantity = 1m
            }).Outcome);
    }

    [Fact]
    public void Chain_correction_moves_only_the_reviewed_row_to_shipping()
    {
        var row = new OpeningExportRow
        {
            RowNumber = 3301,
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

        var decision = OpeningImportPolicy.Interpret(row);
        Assert.Equal(OpeningImportRowOutcome.Corrected, decision.Outcome);
        Assert.Equal("Ворота", decision.Area);
        Assert.Equal("Ворота", decision.CellGroup);
        Assert.Equal<ZoneType?>(ZoneType.Shipping, decision.Purpose);
        Assert.Equal<decimal?>(1m, decision.Source.Quantity);

        Assert.Equal(OpeningImportRowOutcome.Blocking,
            OpeningImportPolicy.Interpret(row with { SourceQuantity = "2", Quantity = 2m }).Outcome);
    }
}
