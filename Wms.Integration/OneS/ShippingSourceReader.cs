using Wms.Application.Picking;
using Wms.Common;

namespace Wms.Integration.OneS;

public sealed class ShippingSourceReader(HttpClient httpClient) : IShippingSourceReader
{
    private const string SelectedFields =
        "Ref_Key,DeletionMark,Posted,Number,Date,Склад_Key,Статус,СкладскаяОперация,Получатель,Получатель_Type,Комментарий," +
        "Доброга_ТипОчереди,Доброга_НаправлениеДоставки_Key,Доброга_ПланируемаяДатаОтгрузки,ВсегоМест,ТоварыПоРаспоряжениям,ОтгружаемыеТовары";

    public async Task<OperationResult<ShippingSourceSnapshot>> ReadAsync(
        Guid refKey, CancellationToken ct)
    {
        if (refKey == Guid.Empty)
            return OperationError.Invalid("Ref_Key расходного документа 1С обязателен.");

        var uri = "Document_РасходныйОрдерНаТовары?$format=json"
            + $"&$select={SelectedFields}&$filter=Ref_Key eq guid'{refKey:D}'";
        try
        {
            using var response = await httpClient.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
                return OperationError.Failure(
                    $"Не удалось прочитать расходный документ 1С: HTTP {(int)response.StatusCode}.");

            var json = await response.Content.ReadAsStringAsync(ct);
            return ShippingSourceParser.Parse(refKey, json);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return OperationError.Failure($"Не удалось прочитать расходный документ 1С: {exception.Message}");
        }
        catch (TaskCanceledException)
        {
            return OperationError.Failure("Превышено время ожидания расходного документа 1С.");
        }
    }
}
