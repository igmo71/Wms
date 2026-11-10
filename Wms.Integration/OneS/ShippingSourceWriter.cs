using System.Net.Http.Json;
using System.Text.Json;
using Wms.Application.Picking;
using Wms.Common;

namespace Wms.Integration.OneS;

public sealed class ShippingSourceWriter(HttpClient httpClient) : IShippingSourceWriter
{
    public async Task<OperationResult> SetReadyForPickingAsync(Guid sourceId, CancellationToken ct)
    {
        var uri = DocumentUri(sourceId) + "?$format=json";
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri)
        {
            Content = JsonContent.Create(new { Статус = "КОтбору" })
        };
        return await SendAsync(request, expectedStatus: "КОтбору", ct);
    }

    public async Task<OperationResult> PostAsync(Guid sourceId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            DocumentUri(sourceId) + "/Post?$format=json");
        return await SendAsync(request, expectedStatus: null, ct);
    }

    private async Task<OperationResult> SendAsync(HttpRequestMessage request,
        string? expectedStatus, CancellationToken ct)
    {
        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return OperationError.Failure(
                    $"1С отклонила {request.Method}: HTTP {(int)response.StatusCode}. {ErrorText(body)}");
            if (expectedStatus is null)
                return OperationResult.Success();

            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("Статус", out var status)
                || status.ValueKind != JsonValueKind.String
                || status.GetString() != expectedStatus)
                return OperationError.Conflict(
                    "1С не подтвердила переход расходного документа в статус КОтбору.");
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return OperationError.Failure($"Обмен с 1С не выполнен: {exception.Message}");
        }
        catch (TaskCanceledException)
        {
            return OperationError.Failure("Превышено время ожидания ответа 1С.");
        }
        catch (JsonException)
        {
            return OperationError.Failure("1С вернула нечитаемый ответ на PATCH.");
        }
    }

    private static string DocumentUri(Guid sourceId) =>
        $"Document_РасходныйОрдерНаТовары(guid'{sourceId:D}')";

    private static string ErrorText(string body) =>
        body.Length <= 1000 ? body : body[..1000];
}
