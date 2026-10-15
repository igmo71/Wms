using System.Globalization;
using System.Text.Json;
using Wms.Application.Receiving;
using Wms.Common;

namespace Wms.Integration.OneS;

public sealed class ReceivingSourceParser : IReceivingSourceParser
{
    public OperationResult<ParsedReceivingSource> Parse(Guid refKey, string rawResponseJson)
    {
        if (string.IsNullOrWhiteSpace(rawResponseJson))
            return OperationError.Failure("1С вернула пустой приходный документ.");

        try
        {
            using var json = JsonDocument.Parse(rawResponseJson);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("value", out var values)
                || values.ValueKind != JsonValueKind.Array
                || values.GetArrayLength() != 1)
                return OperationError.Failure("1С вернула некорректный список приходных документов.");

            var document = values[0];
            if (document.ValueKind != JsonValueKind.Object
                || !document.TryGetProperty("Ref_Key", out var sourceKey)
                || !TryGuid(sourceKey, out var actualRefKey)
                || actualRefKey != refKey
                || !document.TryGetProperty("Товары", out var items)
                || items.ValueKind != JsonValueKind.Array)
                return OperationError.Failure("1С вернула некорректный приходный документ.");

            var lines = new List<ReceivingSourceLine>();
            var ordinal = 0;
            foreach (var item in items.EnumerateArray())
            {
                ordinal++;
                var lineNumber = ordinal;
                Guid? skuRefKey = null;
                string? rawQuantity = null;
                decimal? quantity = null;
                string? error = null;
                string? quantityError = null;

                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("LineNumber", out var sourceLineNumber)
                        && TryPositiveInt(sourceLineNumber, out var parsedLineNumber))
                        lineNumber = parsedLineNumber;

                    if (item.TryGetProperty("Номенклатура_Key", out var sourceSku)
                        && TryGuid(sourceSku, out var parsedSku) && parsedSku != Guid.Empty)
                        skuRefKey = parsedSku;
                    else
                        error = "SKU 1С не указан или некорректен.";

                    if (item.TryGetProperty("Количество", out var sourceQuantity))
                    {
                        rawQuantity = sourceQuantity.ValueKind == JsonValueKind.String
                            ? sourceQuantity.GetString() : sourceQuantity.GetRawText();
                        if ((sourceQuantity.ValueKind is JsonValueKind.Number or JsonValueKind.String)
                            && decimal.TryParse(rawQuantity, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var parsedQuantity)
                            && WarehouseQuantity.IsNonNegative(parsedQuantity))
                            quantity = parsedQuantity;
                    }

                    if (quantity is not null
                        && item.TryGetProperty("КоличествоУпаковок", out var packages))
                    {
                        var packageText = packages.ValueKind switch
                        {
                            JsonValueKind.Number => packages.GetRawText(),
                            JsonValueKind.String => packages.GetString(),
                            _ => null
                        };
                        if (packageText is null
                            || !decimal.TryParse(packageText, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var packageQuantity)
                            || packageQuantity != quantity)
                        {
                            quantity = null;
                            quantityError = "Количество и количество упаковок 1С неоднозначны.";
                        }
                    }
                }
                else
                    error = "Строка 1С имеет некорректный формат.";

                if (quantity is null)
                {
                    quantityError ??= "Количество 1С не поддерживается.";
                    error = error is null ? quantityError : $"{error} {quantityError}";
                }

                lines.Add(new ReceivingSourceLine(lineNumber, skuRefKey,
                    rawQuantity, quantity, error));
            }

            Guid? warehouseId = null;
            if (document.TryGetProperty("Склад_Key", out var warehouse)
                && TryGuid(warehouse, out var warehouseKey)
                && warehouseKey != Guid.Empty)
                warehouseId = warehouseKey;
            bool? posted = document.TryGetProperty("Posted", out var sourcePosted)
                && sourcePosted.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? sourcePosted.GetBoolean() : null;
            bool? deletionMark = document.TryGetProperty("DeletionMark", out var sourceDeletion)
                && sourceDeletion.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? sourceDeletion.GetBoolean() : null;
            var number = document.TryGetProperty("Number", out var sourceNumber)
                && sourceNumber.ValueKind == JsonValueKind.String
                ? sourceNumber.GetString() : null;

            return new ParsedReceivingSource(
                refKey, warehouseId, posted, deletionMark, number, lines);
        }
        catch (JsonException)
        {
            return OperationError.Failure("1С вернула нечитаемый JSON приходного документа.");
        }
    }

    private static bool TryGuid(JsonElement value, out Guid result)
    {
        if (value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out result))
            return true;
        result = default;
        return false;
    }

    private static bool TryPositiveInt(JsonElement value, out int result)
    {
        result = default;
        var parsed = value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out result),
            JsonValueKind.String => int.TryParse(value.GetString(),
                NumberStyles.None, CultureInfo.InvariantCulture, out result),
            _ => false
        };
        if (parsed && result > 0)
            return true;
        result = default;
        return false;
    }
}
