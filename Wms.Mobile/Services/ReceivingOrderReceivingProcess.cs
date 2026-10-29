using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class ReceivingOrderReceivingProcess(MobileReceivingOrderClient client)
{
    private PendingCommand? _pending;
    private bool _executing;

    public bool HasPendingCommand => _pending is not null;
    public string? PendingLabel => _pending?.Label;
    public Guid? LastResourceId { get; private set; }
    public MobileReceivingCloseResultResponse? LastCloseResult { get; private set; }

    public Task<MobileReceivingWorkOrderResponse> JoinAsync(Guid orderId, Guid? locationId) =>
        StartAsync("Присоединиться к приёмке", orderId, async id =>
            (await client.JoinAsync(orderId, locationId, id)).ResourceId);

    public Task<MobileReceivingWorkOrderResponse> CompleteOrderAsync(Guid orderId) =>
        StartAsync("Завершить ордер", orderId, async id =>
            (await client.CompleteOrderAsync(orderId, id)).ResourceId);

    public Task<MobileReceivingWorkOrderResponse> OpenLpnAsync(Guid orderId, string code) =>
        StartAsync("Открыть LPN", orderId, async id =>
            (await client.OpenLpnAsync(orderId, code, id)).ResourceId);

    public Task<MobileReceivingWorkOrderResponse> AddSkuAsync(Guid orderId, Guid skuId) =>
        StartAsync("Добавить SKU", orderId, async id =>
            (await client.AddSkuAsync(orderId, skuId, id)).ResourceId);

    public Task<MobileReceivingWorkOrderResponse> ConfirmZeroAsync(Guid orderId, Guid skuId) =>
        StartAsync("Проверено: 0", orderId, async id =>
            (await client.ConfirmZeroAsync(orderId, skuId, id)).ResourceId);

    public Task<MobileReceivingWorkOrderResponse> AttachSourceAsync(
        Guid orderId, string barcode) =>
        StartAsync("Добавить источник", orderId, async id =>
            (await client.AttachSourceAsync(orderId, barcode, id)).ResourceId);

    public Task<MobileReceivingWorkOrderResponse> DetachSourceAsync(
        Guid orderId, Guid sourceRefKey) =>
        StartAsync("Открепить источник", orderId, async id =>
            (await client.DetachSourceAsync(orderId, sourceRefKey, id)).ResourceId);

    public Task<MobileReceivingWorkOrderResponse> EditLpnAsync(
        Guid orderId, Guid lpnId, Guid skuId, decimal quantity, DateOnly? expirationDate) =>
        StartAsync("Сохранить LPN", orderId, async id =>
            (await client.EditLpnAsync(orderId, lpnId, skuId, quantity, expirationDate, id)).ResourceId);

    public Task<MobileReceivingWorkOrderResponse> CancelLpnAsync(Guid orderId, Guid lpnId) =>
        StartAsync("Отменить LPN", orderId, async id =>
            (await client.CancelLpnAsync(orderId, lpnId, id)).ResourceId);

    public Task<MobileReceivingWorkOrderResponse> ClearLpnItemAsync(
        Guid orderId, Guid lpnId, long expectedRevision) =>
        StartAsync("Убрать SKU", orderId, async id =>
            (await client.ClearLpnItemAsync(orderId, lpnId, expectedRevision, id)).ResourceId);

    public Task<MobileReceivingWorkOrderResponse> CloseLpnAsync(
        Guid orderId, Guid lpnId, string? managerReason) =>
        StartAsync("Закрыть LPN", orderId, async id =>
        {
            LastCloseResult = await client.CloseLpnAsync(
                orderId, lpnId, managerReason, id);
            return LastCloseResult.LpnId;
        });

    private Task<MobileReceivingWorkOrderResponse> StartAsync(
        string label, Guid orderId, Func<Guid, Task<Guid>> execute)
    {
        if (_pending is not null)
            throw new InvalidOperationException("Сначала повторите незавершённую команду.");
        LastResourceId = null;
        LastCloseResult = null;
        _pending = new PendingCommand(Guid.NewGuid(), label, orderId, execute);
        return RetryAsync();
    }

    public async Task<MobileReceivingWorkOrderResponse> RetryAsync()
    {
        if (_executing)
            throw new InvalidOperationException("Дождитесь результата текущей команды.");
        var pending = _pending ?? throw new InvalidOperationException("Нет команды для повтора.");
        _executing = true;
        try
        {
            LastResourceId = await pending.Execute(pending.RequestId);
            var order = await client.GetAsync(pending.OrderId);
            _pending = null;
            return order;
        }
        catch (MobileApiException)
        {
            _pending = null;
            throw;
        }
        finally { _executing = false; }
    }

    private sealed record PendingCommand(Guid RequestId, string Label, Guid OrderId,
        Func<Guid, Task<Guid>> Execute);
}
