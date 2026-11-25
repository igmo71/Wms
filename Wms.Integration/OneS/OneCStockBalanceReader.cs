using System.Text.Json;
using Wms.Application.Reports;
using Wms.Common;

namespace Wms.Integration.OneS;

public sealed class OneCStockBalanceReader(HttpClient httpClient) : IOneCStockBalanceReader
{
    private const string BalancePath = "AccumulationRegister_ТоварыНаСкладах/Balance()"
        + "?$format=json&$select=Номенклатура_Key,Склад_Key,ВНаличииBalance";

    public async Task<OperationResult<StockReconciliationQuantity[]>> ReadAsync(CancellationToken ct)
    {
        try
        {
            using var response = await httpClient.GetAsync(BalancePath, ct);
            if (!response.IsSuccessStatusCode)
                return OperationError.Failure(
                    $"Не удалось получить остатки из 1С. Код HTTP {(int)response.StatusCode}.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return ReadBalances(document.RootElement, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return OperationError.Failure("Превышено время ожидания остатков из 1С.");
        }
        catch (HttpRequestException)
        {
            return OperationError.Failure("Не удалось получить остатки из 1С.");
        }
        catch (JsonException)
        {
            return OperationError.Failure("1С вернула повреждённый ответ с остатками.");
        }
    }

    private static OperationResult<StockReconciliationQuantity[]> ReadBalances(
        JsonElement root, CancellationToken ct)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("value", out var values)
            || values.ValueKind != JsonValueKind.Array)
            return OperationError.Failure("В ответе 1С отсутствует список остатков.");

        if (HasContinuation(root, "odata.nextLink") || HasContinuation(root, "@odata.nextLink"))
            return OperationError.Failure("1С вернула неполный список остатков.");

        var balances = new StockReconciliationQuantity[values.GetArrayLength()];
        var index = 0;
        foreach (var value in values.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            if (value.ValueKind != JsonValueKind.Object
                || !TryReadKey(value, "Склад_Key", out var warehouseId)
                || !TryReadKey(value, "Номенклатура_Key", out var skuId)
                || !value.TryGetProperty("ВНаличииBalance", out var amount)
                || amount.ValueKind != JsonValueKind.Number
                || !amount.TryGetDecimal(out var quantity)
                || !WarehouseQuantity.IsSupported(quantity))
                return OperationError.Failure($"Некорректные данные в строке остатков 1С № {index + 1}.");

            balances[index++] = new StockReconciliationQuantity(warehouseId, skuId, quantity);
        }

        return balances;
    }

    private static bool HasContinuation(JsonElement root, string name) =>
        root.TryGetProperty(name, out var continuation) && continuation.ValueKind != JsonValueKind.Null;

    private static bool TryReadKey(JsonElement row, string name, out Guid key)
    {
        key = default;
        return row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.TryGetGuid(out key) && key != Guid.Empty;
    }
}
