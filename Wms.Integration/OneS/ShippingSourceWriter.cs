using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain;

namespace Wms.Integration.OneS;

public sealed class ShippingSourceWriter(HttpClient httpClient) : IShippingSourceWriter
{
    public OperationResult ValidateTarget(ShippingExchangeTarget target,
        string planSourceSnapshotJson)
    {
        var built = new ShippingTargetBuilder().Build(target,
            planSourceSnapshotJson);
        return built.IsSuccess ? OperationResult.Success() : built.Error!;
    }

    public OperationResult<bool> TargetMatches(ShippingExchangeTarget target,
        string planSourceSnapshotJson, ShippingSourceSnapshot current)
    {
        if (target is null || current is null
            || target.ShippingOrderId != current.RefKey)
            return OperationError.Invalid("Цель обмена и прочитанный РО 1С не совпадают.");
        var builder = new ShippingTargetBuilder();
        var built = builder.Build(target, planSourceSnapshotJson);
        return built.IsSuccess
            ? builder.MatchesCurrent(built.Value!, current)
            : built.Error!;
    }

    public async Task<OperationResult> SetReadyForPickingAsync(Guid sourceId, CancellationToken ct)
    {
        var uri = DocumentUri(sourceId) + "?$format=json";
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri)
        {
            Content = JsonContent.Create(new { Статус = "КОтбору" })
        };
        return await SendAsync(request, expectedStatus: "КОтбору", ct);
    }

    public async Task<OperationResult> SetPreparedAsync(Guid sourceId,
        CancellationToken ct)
    {
        var uri = DocumentUri(sourceId) + "?$format=json";
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri)
        {
            Content = JsonContent.Create(new { Статус = "Подготовлен" })
        };
        return await SendAsync(request, expectedStatus: "Подготовлен", ct);
    }

    public async Task<OperationResult> ApplyTargetAsync(ShippingExchangeTarget target,
        string planSourceSnapshotJson, ShippingSourceSnapshot current,
        CancellationToken ct)
    {
        if (target is null || current is null
            || target.ShippingOrderId != current.RefKey)
            return OperationError.Invalid("Цель обмена и прочитанный РО 1С не совпадают.");
        var builder = new ShippingTargetBuilder();
        var built = builder.Build(target, planSourceSnapshotJson);
        if (!built.IsSuccess) return built.Error!;
        if (builder.MatchesCurrent(built.Value!, current))
            return OperationResult.Success();

        using var request = new HttpRequestMessage(HttpMethod.Patch,
            DocumentUri(target.ShippingOrderId) + "?$format=json")
        {
            Content = new StringContent(built.Value!.PatchJson,
                Encoding.UTF8, "application/json")
        };
        return await SendAsync(request, expectedStatus: null, ct);
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
                    $"1С не подтвердила переход расходного документа в статус {expectedStatus}.");
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
