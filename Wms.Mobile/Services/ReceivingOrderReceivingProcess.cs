using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class ReceivingOrderReceivingProcess
{
    private readonly MobileReceivingOrderClient _orderClient;
    private PendingCommand? _pending;
    private bool _executing;

    internal ReceivingOrderReceivingProcess(MobileReceivingOrderClient orderClient) => _orderClient = orderClient;
    public bool HasPendingCommand => _pending is not null;
    public bool IsOpenLpnPending => _pending?.Kind == "open";
    public bool IsCancelLpnPending => _pending?.Kind == "cancel";
    public bool IsCompletionPending => _pending?.Kind == "complete";
    public bool IsClosePending => _pending?.Kind == "close";
    public string? PendingLabel => _pending?.Label;

    public Task<MobileReceivingOrderCommandResponse> CompleteAsync(Guid orderId, string expectedFingerprint) =>
        StartAsync("complete", "Завершить приёмку", id => _orderClient.CompleteAsync(orderId, expectedFingerprint, id));

    public Task<MobileReceivingOrderCommandResponse> StartAsync(Guid orderId, string receivingLocationBarcode) =>
        StartAsync("start", "Начать приёмку", id => _orderClient.JoinAsync(orderId, receivingLocationBarcode, id));

    public Task<MobileReceivingOrderCommandResponse> OpenLicensePlateNumberAsync(Guid orderId, string code) =>
        StartAsync("open", "Открыть палету", id => _orderClient.OpenLicensePlateNumberAsync(orderId, code, id));

    public Task<MobileReceivingOrderCommandResponse> CancelLicensePlateNumberAsync(Guid orderId, Guid lpnId) =>
        StartAsync("cancel", "Отменить палету", id => _orderClient.CancelLicensePlateNumberAsync(orderId, lpnId, id));

    public Task<MobileReceivingOrderCommandResponse> SetLicensePlateNumberItemQuantityAsync(
        Guid orderId, Guid lpnId, Guid skuId, decimal quantity) =>
        StartAsync("quantity", "Сохранить количество",
            id => _orderClient.SetLicensePlateNumberItemQuantityAsync(orderId, lpnId, skuId, quantity, id));

    public Task<MobileReceivingOrderCommandResponse> RemoveLicensePlateNumberItemAsync(
        Guid orderId, Guid lpnId, Guid skuId) =>
        StartAsync("remove", "Удалить товар",
            id => _orderClient.RemoveLicensePlateNumberItemAsync(orderId, lpnId, skuId, id));

    public Task<MobileReceivingOrderCommandResponse> CloseLicensePlateNumberAsync(Guid orderId, Guid lpnId) =>
        StartAsync("close", "Закрыть палету", id => _orderClient.CloseLicensePlateNumberAsync(orderId, lpnId, id));

    public Task<MobileReceivingOrderCommandResponse> ReopenLicensePlateNumberAsync(Guid orderId, Guid lpnId) =>
        StartAsync("reopen", "Возобновить палету", id => _orderClient.ReopenLicensePlateNumberAsync(orderId, lpnId, id));

    private Task<MobileReceivingOrderCommandResponse> StartAsync(
        string kind, string label, Func<Guid, Task<MobileReceivingOrderCommandResponse>> execute)
    {
        if (HasPendingCommand)
            throw new InvalidOperationException("Сначала повторите незавершённую команду.");
        // Captured value inputs survive refresh; retry never reads current page controls.
        _pending = new(Guid.NewGuid(), kind, label, execute);
        return RetryAsync();
    }

    public async Task<MobileReceivingOrderCommandResponse> RetryAsync()
    {
        if (_executing)
            throw new InvalidOperationException("Дождитесь результата текущего запроса.");
        var pending = _pending ?? throw new InvalidOperationException("Нет команды для повтора.");
        _executing = true;
        try
        {
            var response = await pending.Execute(pending.RequestId);
            _pending = null;
            return response;
        }
        catch (MobileApiException)
        {
            _pending = null;
            throw;
        }
        finally { _executing = false; }
    }

    private sealed record PendingCommand(Guid RequestId, string Kind, string Label,
        Func<Guid, Task<MobileReceivingOrderCommandResponse>> Execute);
}
