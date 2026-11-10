using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Integration.OneS;

public static class ShippingSourceParser
{
    public static OperationResult<ShippingSourceSnapshot> Parse(Guid refKey, string rawResponseJson)
    {
        if (refKey == Guid.Empty)
            return OperationError.Invalid("Ref_Key расходного документа 1С обязателен.");
        if (string.IsNullOrWhiteSpace(rawResponseJson))
            return OperationError.Failure("1С вернула пустой расходный документ.");

        try
        {
            using var json = JsonDocument.Parse(rawResponseJson);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("value", out var values)
                || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 1)
                return OperationError.Failure("1С вернула некорректный список расходных документов.");

            var document = values[0];
            if (document.ValueKind != JsonValueKind.Object
                || ReadGuid(document, "Ref_Key") != refKey
                || !TryArray(document, "ОтгружаемыеТовары", out var items)
                || !TryArray(document, "ТоварыПоРаспоряжениям", out var basisItems))
                return OperationError.Failure("1С вернула некорректный расходный документ.");

            var parsedItems = ParseItems(items);
            if (!parsedItems.IsSuccess)
                return parsedItems.Error!;
            var parsedBasis = ParseBasisItems(basisItems);
            if (!parsedBasis.IsSuccess)
                return parsedBasis.Error!;

            var rawStatus = ReadScalar(document, "Статус");
            var rawOperation = ReadScalar(document, "СкладскаяОперация");
            var rawReceiverType = ReadScalar(document, "Получатель_Type");
            var rawQueue = ReadScalar(document, "Доброга_ТипОчереди");
            var receiverType = ParseEnum<PartyType>(rawReceiverType);
            var directionId = ReadGuid(document, "Доброга_НаправлениеДоставки_Key");
            return new ShippingSourceSnapshot(
                refKey, document.GetRawText(),
                ReadBool(document, "Posted"), ReadBool(document, "DeletionMark"),
                ReadScalar(document, "Number"), ReadDate(document, "Date"),
                ReadGuid(document, "Склад_Key"), ReadScalar(document, "Комментарий"),
                rawStatus, ParseEnum<ShippingOrderStatus>(rawStatus),
                rawOperation, ParseEnum<WarehouseOperation>(rawOperation),
                ReadGuid(document, "Получатель"), rawReceiverType,
                receiverType == default ? null : receiverType,
                rawQueue, ParseEnum<ShippingOrderQueue>(rawQueue),
                ReadDate(document, "Доброга_ПланируемаяДатаОтгрузки") is DateTime planned
                    && planned.Date != DateTime.MinValue.Date ? planned : null,
                directionId == Guid.Empty ? null : directionId,
                parsedItems.Value!, parsedBasis.Value!);
        }
        catch (JsonException)
        {
            return OperationError.Failure("1С вернула нечитаемый JSON расходного документа.");
        }
    }

    private static OperationResult<IReadOnlyList<ShippingSourceItem>> ParseItems(JsonElement items)
    {
        var result = new List<ShippingSourceItem>();
        var lineNumbers = new HashSet<int>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || ReadPositiveLineNumber(item) is not int lineNumber
                || !lineNumbers.Add(lineNumber))
                return OperationError.Failure("Строки отгружаемых товаров 1С имеют неверные или повторные номера.");

            var rawAction = ReadScalar(item, "Действие");
            var serviceNumber = ReadInt(item, "ЭтоСлужебнаяСтрокаПустогоУпаковочногоЛиста");
            result.Add(new ShippingSourceItem(
                lineNumber, ReadGuid(item, "Номенклатура_Key"),
                ReadScalar(item, "Количество"), ReadQuantity(item, "Количество"),
                ReadScalar(item, "КоличествоУпаковок"), ReadQuantity(item, "КоличествоУпаковок"),
                rawAction, ParseEnum<ShippingOrderAction>(rawAction),
                ReadBool(item, "ЭтоУпаковочныйЛист"),
                serviceNumber is null ? null : serviceNumber != 0,
                item.GetRawText()));
        }
        return OperationResult<IReadOnlyList<ShippingSourceItem>>.Success(result);
    }

    private static OperationResult<IReadOnlyList<ShippingSourceBasisItem>> ParseBasisItems(JsonElement items)
    {
        var result = new List<ShippingSourceBasisItem>();
        var lineNumbers = new HashSet<int>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || ReadPositiveLineNumber(item) is not int lineNumber
                || !lineNumbers.Add(lineNumber))
                return OperationError.Failure("Строки товаров по распоряжениям 1С имеют неверные или повторные номера.");

            result.Add(new ShippingSourceBasisItem(
                lineNumber, ReadGuid(item, "Номенклатура_Key"),
                ReadScalar(item, "Количество"), ReadQuantity(item, "Количество"),
                ReadGuid(item, "Распоряжение"), ReadScalar(item, "Распоряжение_Type"),
                item.GetRawText()));
        }
        return OperationResult<IReadOnlyList<ShippingSourceBasisItem>>.Success(result);
    }

    private static bool TryArray(JsonElement document, string name, out JsonElement value) =>
        document.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array;

    private static int? ReadPositiveLineNumber(JsonElement item) =>
        ReadInt(item, "LineNumber") is int number && number > 0 ? number : null;

    private static int? ReadInt(JsonElement item, string name)
    {
        var text = ReadScalar(item, name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var number) ? number : null;
    }

    private static Guid? ReadGuid(JsonElement item, string name)
    {
        var text = ReadScalar(item, name);
        return Guid.TryParse(text, out var value) ? value : null;
    }

    private static bool? ReadBool(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static DateTime? ReadDate(JsonElement item, string name) =>
        DateTime.TryParse(ReadScalar(item, name), CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value) ? value : null;

    private static decimal? ReadQuantity(JsonElement item, string name)
    {
        var text = ReadScalar(item, name);
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var value) && WarehouseQuantity.IsNonNegative(value) ? value : null;
    }

    private static string? ReadScalar(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null
        };
    }

    private static TEnum ParseEnum<TEnum>(string? value) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return default;
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var member = field.GetCustomAttribute<EnumMemberAttribute>();
            if (string.Equals(member?.Value, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field.Name, value, StringComparison.OrdinalIgnoreCase))
                return (TEnum)field.GetValue(null)!;
        }
        return default;
    }
}
