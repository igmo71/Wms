using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileStockAvailabilityClient
{
    private readonly MobileApiTransport transport;

    internal MobileStockAvailabilityClient(MobileApiTransport transport) => this.transport = transport;

    public async Task<MobileStockAvailabilityResponse> GetAsync(
        Guid warehouseId, Guid skuId, CancellationToken ct = default)
    {
        using var response = await transport.GetAsync(
            $"{MobileApiRoutes.StockAvailability}/{warehouseId:D}/skus/{skuId:D}", ct);
        return await response.Content.ReadFromJsonAsync<MobileStockAvailabilityResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response,
                "Сервер вернул некорректные остатки SKU.");
    }
}
