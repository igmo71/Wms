using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileLicensePlateNumberPutawayClient
{
    private readonly MobileApiTransport _transport;

    internal MobileLicensePlateNumberPutawayClient(MobileApiTransport transport) =>
        _transport = transport;

    public async Task<MobileLicensePlateNumberPutawayDetailsResponse> ResolveAsync(
        string code,
        CancellationToken ct = default)
    {
        using var response = await _transport.PostAsync(
            $"{MobileApiRoutes.LicensePlateNumberPutaway}/resolve",
            new MobileResolveLicensePlateNumberPutawayRequest(code),
            ct);
        return await response.Content.ReadFromJsonAsync<MobileLicensePlateNumberPutawayDetailsResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response, "Сервер вернул некорректный LPN.");
    }

    public async Task<MobileLicensePlateNumberPutawayDetailsResponse> GetAsync(
        Guid lpnId,
        CancellationToken ct = default)
    {
        using var response = await _transport.GetAsync(
            $"{MobileApiRoutes.LicensePlateNumberPutaway}/{lpnId:D}", ct);
        return await response.Content.ReadFromJsonAsync<MobileLicensePlateNumberPutawayDetailsResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response, "Сервер вернул некорректный LPN.");
    }

    public async Task<MobileLicensePlateNumberPutawaySelectionResponse> ResolveSkuAsync(
        Guid lpnId,
        string barcode,
        CancellationToken ct = default)
    {
        using var response = await _transport.PostAsync(
            $"{MobileApiRoutes.LicensePlateNumberPutaway}/{lpnId:D}/resolve-sku",
            new MobileResolveLicensePlateNumberPutawaySkuRequest(barcode),
            ct);
        return await response.Content.ReadFromJsonAsync<MobileLicensePlateNumberPutawaySelectionResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response, "Сервер вернул некорректный товар LPN.");
    }

    public async Task<MobileLicensePlateNumberPutawaySelectionResponse> GetSelectionAsync(
        Guid lpnId,
        Guid lpnItemId,
        CancellationToken ct = default)
    {
        using var response = await _transport.GetAsync(
            $"{MobileApiRoutes.LicensePlateNumberPutaway}/{lpnId:D}/items/{lpnItemId:D}/selection",
            ct);
        return await response.Content.ReadFromJsonAsync<MobileLicensePlateNumberPutawaySelectionResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response, "Сервер вернул некорректные рекомендации размещения.");
    }

    public async Task<MobileLicensePlateNumberPutawayCommandResponse> PutawayAsync(
        Guid lpnId,
        Guid itemId,
        decimal quantity,
        string destinationBarcode,
        Guid clientRequestId,
        CancellationToken ct = default)
    {
        using var response = await _transport.PostAsync(
            $"{MobileApiRoutes.LicensePlateNumberPutaway}/{lpnId:D}/putaway",
            new MobilePutawayLicensePlateNumberRequest(
                clientRequestId, itemId, quantity, destinationBarcode),
            ct);
        return await MobileApiTransport.ReadCommandResponseAsync<MobileLicensePlateNumberPutawayCommandResponse>(
            response,
            "Сервер вернул некорректный результат размещения LPN.",
            ct);
    }
}
