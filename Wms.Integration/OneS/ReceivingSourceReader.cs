using Wms.Application.Receiving;
using Wms.Common;

namespace Wms.Integration.OneS;

public sealed class ReceivingSourceReader(HttpClient httpClient) : IReceivingSourceReader
{
    private const string DocumentPath = "Document_ПриходныйОрдерНаТовары";
    private const string SelectedFields =
        "Ref_Key,DeletionMark,Posted,Number,Date,Склад_Key,Статус,СкладскаяОперация,Отправитель,Отправитель_Type,Комментарий," +
        "Доброга_ТипОчереди,Распоряжение,Распоряжение_Type,ХозяйственнаяОперация,Товары";

    public async Task<OperationResult<ReceivingSourceRead>> ReadAsync(Guid refKey, CancellationToken ct)
    {
        if (refKey == Guid.Empty)
            return OperationError.Invalid("Ref_Key приходного документа 1С обязателен.");

        var uri = $"{DocumentPath}?$format=json&$select={SelectedFields}&$filter=Ref_Key eq guid'{refKey:D}'";
        try
        {
            using var response = await httpClient.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
                return OperationError.Failure($"Не удалось прочитать приходный документ 1С: HTTP {(int)response.StatusCode}.");

            var rawResponseJson = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(rawResponseJson))
                return OperationError.Failure("1С вернула пустой ответ для приходного документа.");

            return new ReceivingSourceRead(refKey, rawResponseJson);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return OperationError.Failure($"Не удалось прочитать приходный документ 1С: {exception.Message}");
        }
        catch (TaskCanceledException)
        {
            return OperationError.Failure("Превышено время ожидания приходного документа 1С.");
        }
    }
}
