using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileLicensePlateNumberPutawayClient
{
    private readonly MobileApiTransport transport;
    internal MobileLicensePlateNumberPutawayClient(MobileApiTransport transport) => this.transport = transport;
    private static string Route => MobileApiRoutes.LicensePlateNumberPutaway;

    public Task<MobileReceivingWorkLpnResponse> ResolveAsync(string code,
        CancellationToken ct = default) =>
        GetAsync<MobileReceivingWorkLpnResponse>(
            $"{Route}/resolve?code={Uri.EscapeDataString(code.Trim())}", ct);

    public Task<MobileReceivingWorkLpnResponse> GetAsync(Guid lpnId,
        CancellationToken ct = default) =>
        GetAsync<MobileReceivingWorkLpnResponse>($"{Route}/{lpnId:D}", ct);

    public Task<MobileReceivingPutawaySelectionResponse> GetSelectionAsync(
        Guid lpnId, Guid itemId, CancellationToken ct = default) =>
        GetAsync<MobileReceivingPutawaySelectionResponse>(
            $"{Route}/{lpnId:D}/items/{itemId:D}/selection", ct);

    public async Task<MobileReceivingPutawaySelectionResponse> ResolveSkuAsync(
        Guid lpnId, string barcode, CancellationToken ct = default)
    {
        using var response = await transport.PostAsync(
            $"{Route}/{lpnId:D}/resolve-sku", new MobileReceivingBarcodeRequest(barcode), ct);
        return await MobileApiTransport.ReadCommandResponseAsync<MobileReceivingPutawaySelectionResponse>(
            response, "Сервер вернул некорректный SKU LPN.", ct);
    }

    public async Task<MobileReceivingCommandIdResponse> PutawayAsync(
        Guid lpnId, Guid itemId, decimal quantity, Guid locationId,
        Guid requestId, CancellationToken ct = default)
    {
        using var response = await transport.PostAsync($"{Route}/{lpnId:D}/putaways",
            new MobileReceivingPutawayRequest(requestId, itemId, locationId, quantity), ct);
        return await MobileApiTransport.ReadCommandResponseAsync<MobileReceivingCommandIdResponse>(
            response, "Сервер вернул некорректный результат размещения.", ct);
    }

    private async Task<T> GetAsync<T>(string route, CancellationToken ct)
    {
        using var response = await transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<T>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response, "Сервер вернул некорректный LPN.");
    }
}
