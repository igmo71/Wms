using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.OpeningImports;

public enum OpeningImportRowOutcome
{
    Accepted,
    Corrected,
    Excluded,
    Blocking
}

public enum OpeningImportMixingPolicy
{
    Default,
    ApprovedMultipleSkus,
    ProhibitedMixedOpeningStock
}

public sealed record OpeningImportRowDecision(
    OpeningExportRow Source,
    OpeningImportRowOutcome Outcome,
    string? Area,
    string? CellGroup,
    string? Address,
    ZoneType? Purpose,
    OpeningImportMixingPolicy MixingPolicy,
    string? Reason);

public static class OpeningImportPolicy
{
    private const string RackArea = "(1) Фронтальные стеллажи";
    private const string FirstMezzanineArea = "(2) Мезонин 1 этаж 1";
    private const string SecondMezzanineArea = "(3) Мезонин 1 этаж 2";
    private const string GateArea = "Ворота";
    private const string FirstRoom = "Помещение 1";
    private const string SecondRoom = "Помещение 2";
    private const string ThirdRoom = "Помещение 3";
    private static readonly Guid CorrectedChainSkuId =
        Guid.Parse("ea64f133-2d92-11ec-baa2-00155d01d102");

    private static readonly HashSet<string> ApprovedMultipleSkuAddresses =
    [
        "1C09A1", "1G09A1", "1K09A1", "1M09A1", "1O09A1", "1Q09A1",
        "1U11A1", "2O01A1", "2O02A1", "2O03A1", "2O04A1", "2O05A1",
        "2O06A1", "2O07A1", "2O08A1"
    ];

    private static readonly HashSet<string> ProhibitedMixedAddresses =
    [
        "1H05A7", "1K03A5", "1O08C6", "1R04A5",
        "1R05B1", "1T03C3", "2A02F2", "3E07B1"
    ];

    public static OpeningImportRowDecision Interpret(OpeningExportRow row)
    {
        var rawAddress = row.Address?.Trim();
        var area = row.Area?.Trim();
        var group = row.CellGroup?.Trim();
        var kind = row.CellKind?.Trim();

        if (string.Equals(rawAddress, "+", StringComparison.Ordinal))
            return area is null && group is null && kind is null && !HasStockPayload(row)
                ? Excluded(row, "Служебная строка «+» не является адресом.")
                : Blocked(row, "Строка «+» изменила согласованный служебный формат.");

        if (string.Equals(rawAddress, "Невозможно скомплектовать",
                StringComparison.OrdinalIgnoreCase))
            return area is null && group is null && kind == "Хранение" && !HasStockPayload(row)
                ? Excluded(row, "Конфликтная служебная ячейка не переносится в WMS.")
                : Blocked(row, "В служебной ячейке «Невозможно скомплектовать» есть данные или изменён её смысл.");

        var normalized = StorageLocationAddress.Normalize(rawAddress);
        if (!normalized.IsSuccess)
            return Blocked(row, normalized.Error!.Message);
        var address = normalized.Value!;

        if (address is "0A00A0" or "0A00A1")
        {
            var expectedArea = address == "0A00A0" ? FirstMezzanineArea : RackArea;
            var expectedGroup = address == "0A00A0" ? SecondRoom : FirstRoom;
            return area == expectedArea && group == expectedGroup
                ? Excluded(row, "Нулевой адрес исключён по согласованному решению; его остаток не переносится.")
                : Blocked(row, "Нулевой адрес встретился в другом помещении; требуется новое решение.");
        }

        if (address == "4A10A1")
            return area == SecondMezzanineArea && group == ThirdRoom && kind == "Хранение"
                   && !HasStockPayload(row)
                ? Excluded(row, "Пустой адрес 4A10A1 исключён: он не соответствует помещению 3.")
                : Blocked(row, "Адрес 4A10A1 изменился или содержит остаток; требуется новое решение.");

        if (address is "1W01W1" or "3W03W3")
        {
            var expectedArea = address == "1W01W1" ? RackArea : SecondMezzanineArea;
            var expectedGroup = address == "1W01W1" ? FirstRoom : ThirdRoom;
            var expectedKind = address == "1W01W1"
                ? "Перемещение на другой склад" : "Общая ячейка помещения";
            return area == expectedArea && group == expectedGroup && kind == expectedKind
                   && !HasStockPayload(row)
                ? Excluded(row, "Виртуальная транзитная ячейка старой WMS не переносится.")
                : Blocked(row, "У исключаемой виртуальной ячейки изменились назначение или остаток.");
        }

        if (address == "9Z99A1" && kind == "Комплектация")
        {
            if (area != RackArea || group != FirstRoom || row.SkuId != CorrectedChainSkuId
                || row.Quantity != 1m || row.UnitOfMeasure?.Trim() != "шт"
                || row.SourceDate is not null || row.ExpirationDate is not null
                || row.RequiresExpirationDate != false)
                return Blocked(row, "Исправление 9Z99A1 согласовано только для одной единицы указанной цепи.");
            return new OpeningImportRowDecision(row, OpeningImportRowOutcome.Corrected,
                GateArea, GateArea, address, ZoneType.Shipping,
                OpeningImportMixingPolicy.Default,
                "Ошибочно указанную комплектацию перенесли в отгрузку с сохранением товара.");
        }

        var purpose = kind switch
        {
            "Комплектация" => ZoneType.Storage,
            "Хранение" => ZoneType.Reserve,
            "Отгрузка" => ZoneType.Shipping,
            "Приемка" or "Приёмка" => ZoneType.Receiving,
            _ => (ZoneType?)null
        };
        if (purpose is null)
            return Blocked(row, "Неизвестный вид ячейки.");

        var expectedPrefix = (area, group) switch
        {
            (RackArea, FirstRoom) => '1',
            (FirstMezzanineArea, SecondRoom) => '2',
            (SecondMezzanineArea, ThirdRoom) => '3',
            (GateArea, GateArea) => 'G',
            _ => '\0'
        };
        if (expectedPrefix == '\0')
            return Blocked(row, "Неизвестная пара «Помещение» и «Группа ячеек».");
        if (!IsCompactAddress(address)
            || (expectedPrefix == 'G' ? address[0] is not ('8' or '9')
                : address[0] != expectedPrefix))
            return Blocked(row, "Адрес не соответствует формату или указанному помещению.");
        if (expectedPrefix == 'G' && purpose is not (ZoneType.Receiving or ZoneType.Shipping))
            return Blocked(row, "В воротах ожидается ячейка приёмки или отгрузки.");
        if (expectedPrefix != 'G' && purpose is not (ZoneType.Storage or ZoneType.Reserve))
            return Blocked(row, "В стеллажах и мезонине ожидается комплектация или хранение.");

        var mixingPolicy = ApprovedMultipleSkuAddresses.Contains(address)
            ? OpeningImportMixingPolicy.ApprovedMultipleSkus
            : ProhibitedMixedAddresses.Contains(address)
                ? OpeningImportMixingPolicy.ProhibitedMixedOpeningStock
                : OpeningImportMixingPolicy.Default;
        return new OpeningImportRowDecision(row, OpeningImportRowOutcome.Accepted,
            area, group, address, purpose, mixingPolicy, null);
    }

    private static bool HasStockPayload(OpeningExportRow row) =>
        row.SkuName is not null || row.SourceSkuGuid is not null
        || row.UnitOfMeasure is not null || row.SourceQuantity is not null
        || row.SourceDate is not null;

    private static bool IsCompactAddress(string value) =>
        value.Length == 6 && char.IsAsciiDigit(value[0])
        && value[1] is >= 'A' and <= 'Z'
        && char.IsAsciiDigit(value[2]) && char.IsAsciiDigit(value[3])
        && value[4] is >= 'A' and <= 'Z' && char.IsAsciiDigit(value[5]);

    private static OpeningImportRowDecision Excluded(OpeningExportRow row, string reason) =>
        new(row, OpeningImportRowOutcome.Excluded, null, null, null, null,
            OpeningImportMixingPolicy.Default, reason);

    private static OpeningImportRowDecision Blocked(OpeningExportRow row, string reason) =>
        new(row, OpeningImportRowOutcome.Blocking, null, null, null, null,
            OpeningImportMixingPolicy.Default, reason);
}
