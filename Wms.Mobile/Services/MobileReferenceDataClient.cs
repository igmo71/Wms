using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileReferenceDataClient
{
    private readonly MobileApiTransport _transport;

    internal MobileReferenceDataClient(MobileApiTransport transport)
    {
        _transport = transport;
    }

    public async Task<MobileStorageLocationResponse> ResolveStorageLocationAsync(
        string barcode,
        Guid? expectedWarehouseId = null,
        MobileStorageLocationContext context = MobileStorageLocationContext.AnyOperational,
        CancellationToken ct = default)
    {
        using HttpResponseMessage response = await _transport.PostAsync(
            MobileApiRoutes.ResolveStorageLocation,
            new MobileResolveStorageLocationRequest(barcode, expectedWarehouseId, context),
            ct);
        return await response.Content.ReadFromJsonAsync<MobileStorageLocationResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_storage_location_response",
                "Сервер вернул некорректные сведения о ячейке.");
    }

    public async Task<MobileStorageLocationSearchResponse> FindReceivingLocationsAsync(
        Guid warehouseId,
        string? search = null,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.ReceivingStorageLocations}?warehouseId={warehouseId:D}";
        if (!string.IsNullOrWhiteSpace(search))
            route += $"&search={Uri.EscapeDataString(search.Trim())}";
        using HttpResponseMessage response = await _transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<MobileStorageLocationSearchResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_receiving_locations_response",
                "Сервер вернул некорректный список ячеек приёмки.");
    }

    public async Task<MobileSkuResponse> ResolveSkuAsync(
        string barcode,
        CancellationToken ct = default)
    {
        using HttpResponseMessage response = await _transport.PostAsync(
            MobileApiRoutes.ResolveSku,
            new MobileResolveSkuRequest(barcode),
            ct);
        return await response.Content.ReadFromJsonAsync<MobileSkuResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_sku_response",
                "Сервер вернул некорректные сведения о товаре.");
    }

    public async Task<IReadOnlyList<MobileWarehouseResponse>> GetWarehousesAsync(
        CancellationToken ct = default)
    {
        using HttpResponseMessage response = await _transport.GetAsync(MobileApiRoutes.Warehouses, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileWarehouseResponse>>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_warehouses_response",
                "Сервер вернул некорректный список складов.");
    }
}
