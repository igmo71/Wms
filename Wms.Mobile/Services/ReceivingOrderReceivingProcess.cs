using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class ReceivingOrderReceivingProcess
{
    private readonly MobileReceivingOrderClient _orderClient;
    private Guid? _startRequestId;
    private Guid? _openLpnRequestId;
    private Guid? _cancelLpnRequestId;
    private Guid? _lpnMutationRequestId;
    private string? _openLpnCode;
    private Guid? _cancelLpnId;
    private string? _lpnMutationKey;

    internal ReceivingOrderReceivingProcess(MobileReceivingOrderClient orderClient)
    {
        _orderClient = orderClient;
    }

    public bool HasPendingCommand => IsStartPending
        || IsOpenLpnPending
        || IsCancelLpnPending
        || IsLpnMutationPending;

    public bool IsStartPending => _startRequestId is not null;
    public bool IsOpenLpnPending => _openLpnRequestId is not null;
    public bool IsCancelLpnPending => _cancelLpnRequestId is not null;
    public bool IsLpnMutationPending => _lpnMutationRequestId is not null;
    public async Task<MobileReceivingOrderCommandResponse> StartAsync(
        Guid orderId,
        string receivingLocationBarcode)
    {
        _startRequestId ??= Guid.NewGuid();
        try
        {
            var response = await _orderClient.JoinAsync(
                orderId,
                receivingLocationBarcode,
                _startRequestId.Value);
            _startRequestId = null;
            return response;
        }
        catch (MobileApiException)
        {
            _startRequestId = null;
            throw;
        }
    }

    public async Task<MobileReceivingOrderCommandResponse> OpenLicensePlateNumberAsync(
        Guid orderId,
        string code)
    {
        if (_openLpnCode is not null && !string.Equals(_openLpnCode, code, StringComparison.Ordinal))
            throw new InvalidOperationException("Повторите предыдущий штрихкод LPN.");

        _openLpnCode ??= code;
        _openLpnRequestId ??= Guid.NewGuid();
        try
        {
            var response = await _orderClient.OpenLicensePlateNumberAsync(
                orderId, _openLpnCode, _openLpnRequestId.Value);
            _openLpnCode = null;
            _openLpnRequestId = null;
            return response;
        }
        catch (MobileApiException)
        {
            _openLpnCode = null;
            _openLpnRequestId = null;
            throw;
        }
    }

    public async Task<MobileReceivingOrderCommandResponse> CancelLicensePlateNumberAsync(
        Guid orderId,
        Guid lpnId)
    {
        if (_cancelLpnId is Guid pendingId && pendingId != lpnId)
            throw new InvalidOperationException("Сначала повторите отмену предыдущего LPN.");

        _cancelLpnId ??= lpnId;
        _cancelLpnRequestId ??= Guid.NewGuid();
        try
        {
            var response = await _orderClient.CancelLicensePlateNumberAsync(
                orderId, _cancelLpnId.Value, _cancelLpnRequestId.Value);
            _cancelLpnId = null;
            _cancelLpnRequestId = null;
            return response;
        }
        catch (MobileApiException)
        {
            _cancelLpnId = null;
            _cancelLpnRequestId = null;
            throw;
        }
    }

    public Task<MobileReceivingOrderCommandResponse> SetLicensePlateNumberItemQuantityAsync(
        Guid orderId,
        Guid lpnId,
        Guid skuId,
        decimal quantity) =>
        ExecuteLpnMutationAsync(
            $"set|{lpnId:N}|{skuId:N}|{quantity}",
            requestId => _orderClient.SetLicensePlateNumberItemQuantityAsync(
                orderId, lpnId, skuId, quantity, requestId));

    public Task<MobileReceivingOrderCommandResponse> RemoveLicensePlateNumberItemAsync(
        Guid orderId,
        Guid lpnId,
        Guid skuId) =>
        ExecuteLpnMutationAsync(
            $"remove|{lpnId:N}|{skuId:N}",
            requestId => _orderClient.RemoveLicensePlateNumberItemAsync(
                orderId, lpnId, skuId, requestId));

    public Task<MobileReceivingOrderCommandResponse> CloseLicensePlateNumberAsync(
        Guid orderId,
        Guid lpnId) =>
        ExecuteLpnMutationAsync(
            $"close|{lpnId:N}",
            requestId => _orderClient.CloseLicensePlateNumberAsync(orderId, lpnId, requestId));

    public Task<MobileReceivingOrderCommandResponse> ReopenLicensePlateNumberAsync(
        Guid orderId,
        Guid lpnId) =>
        ExecuteLpnMutationAsync(
            $"reopen|{lpnId:N}",
            requestId => _orderClient.ReopenLicensePlateNumberAsync(orderId, lpnId, requestId));

    private async Task<MobileReceivingOrderCommandResponse> ExecuteLpnMutationAsync(
        string key,
        Func<Guid, Task<MobileReceivingOrderCommandResponse>> execute)
    {
        if (_lpnMutationKey is not null
            && !string.Equals(_lpnMutationKey, key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Сначала повторите предыдущее изменение LPN.");
        }

        _lpnMutationKey ??= key;
        _lpnMutationRequestId ??= Guid.NewGuid();
        try
        {
            var response = await execute(_lpnMutationRequestId.Value);
            _lpnMutationKey = null;
            _lpnMutationRequestId = null;
            return response;
        }
        catch (MobileApiException)
        {
            _lpnMutationKey = null;
            _lpnMutationRequestId = null;
            throw;
        }
    }

}
