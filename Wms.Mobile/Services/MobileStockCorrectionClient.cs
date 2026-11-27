using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileStockCorrectionClient
{
    private readonly MobileApiTransport _transport;

    internal MobileStockCorrectionClient(MobileApiTransport transport) => _transport = transport;

    public async Task<MobileStockCorrectionCellResponse> ReadCellAsync(Guid warehouseId, string barcode)
    {
        var route = $"{MobileApiRoutes.StockCorrections}/cell?warehouseId={warehouseId:D}"
            + $"&barcode={Uri.EscapeDataString(barcode)}";
        using var response = await _transport.GetAsync(route);
        return await MobileApiTransport.ReadCommandResponseAsync<MobileStockCorrectionCellResponse>(
            response, "Сервер вернул некорректные сведения об остатках ячейки.", default);
    }

    public async Task<MobileCorrectCellStockResponse> PostAsync(MobileCorrectCellStockRequest request)
    {
        using var response = await _transport.PostAsync(MobileApiRoutes.StockCorrections, request, default);
        return await MobileApiTransport.ReadCommandResponseAsync<MobileCorrectCellStockResponse>(
            response, "Сервер не подтвердил результат корректировки.", default);
    }
}
