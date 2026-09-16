using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class ReceivingOrderReceivingProcess
{
    private readonly MobileReceivingOrderClient _orderClient;
    private Guid? _startRequestId;
    private Guid? _scanRequestId;
    private Guid? _quantityRequestId;
    private Guid? _completionRequestId;
    private Guid? _openLpnRequestId;
    private Guid? _cancelLpnRequestId;
    private Guid? _lpnMutationRequestId;
    private string? _scanBarcode;
    private int? _scanLineNumber;
    private int? _quantityLineNumber;
    private string? _openLpnCode;
    private Guid? _cancelLpnId;
    private string? _lpnMutationKey;

    internal ReceivingOrderReceivingProcess(MobileReceivingOrderClient orderClient)
    {
        _orderClient = orderClient;
    }

    public bool HasPendingCommand => IsStartPending
        || IsScanPending
        || IsQuantityPending
        || IsCompletionPending
        || IsOpenLpnPending
        || IsCancelLpnPending
        || IsLpnMutationPending;

    public bool IsStartPending => _startRequestId is not null;
    public bool IsScanPending => _scanRequestId is not null;
    public bool IsQuantityPending => _quantityRequestId is not null;
    public bool IsCompletionPending => _completionRequestId is not null;
    public bool IsOpenLpnPending => _openLpnRequestId is not null;
    public bool IsCancelLpnPending => _cancelLpnRequestId is not null;
    public bool IsLpnMutationPending => _lpnMutationRequestId is not null;
    public int? PendingQuantityLineNumber => _quantityLineNumber;

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

    public async Task<ReceivingOrderScanResult> ScanAsync(Guid orderId, string barcode)
    {
        if (IsScanPending)
        {
            if (_scanBarcode != barcode || _scanLineNumber is not int retryLine)
            {
                return ReceivingOrderScanResult.Failed(
                    "Повторите предыдущий штрихкод: ответ сервера не был получен.");
            }

            return await IncrementAsync(orderId, retryLine);
        }

        var candidates = await _orderClient.ResolveSkuAsync(orderId, barcode);
        _scanBarcode = barcode;
        if (candidates.Count != 1)
        {
            return ReceivingOrderScanResult.SelectFrom(candidates);
        }

        _scanLineNumber = candidates[0].LineNumber;
        _scanRequestId = Guid.NewGuid();
        return await IncrementAsync(orderId, candidates[0].LineNumber);
    }

    public Task<ReceivingOrderScanResult> SelectCandidateAsync(Guid orderId, int lineNumber)
    {
        if (_scanLineNumber is int pendingLine && pendingLine != lineNumber)
        {
            return Task.FromResult(ReceivingOrderScanResult.Failed(
                "Повторите предыдущую строку: ответ сервера не был получен."));
        }

        _scanLineNumber = lineNumber;
        _scanRequestId ??= Guid.NewGuid();
        return IncrementAsync(orderId, lineNumber);
    }

    public async Task<ReceivingOrderQuantityResult> SetQuantityAsync(
        Guid orderId,
        int lineNumber,
        decimal quantity)
    {
        if (_quantityLineNumber is int pendingLine && pendingLine != lineNumber)
        {
            return ReceivingOrderQuantityResult.Failed(
                "Сначала повторите сохранение предыдущей строки.");
        }

        _quantityLineNumber = lineNumber;
        _quantityRequestId ??= Guid.NewGuid();
        try
        {
            var response = await _orderClient.SetLineQuantityAsync(
                orderId,
                lineNumber,
                quantity,
                _quantityRequestId.Value);
            _quantityRequestId = null;
            _quantityLineNumber = null;
            return ReceivingOrderQuantityResult.Succeeded(response);
        }
        catch (MobileApiException)
        {
            _quantityRequestId = null;
            _quantityLineNumber = null;
            throw;
        }
    }

    public async Task<MobileReceivingOrderCommandResponse> CompleteAsync(Guid orderId)
    {
        _completionRequestId ??= Guid.NewGuid();
        try
        {
            var response = await _orderClient.CompleteAsync(orderId, _completionRequestId.Value);
            _completionRequestId = null;
            return response;
        }
        catch (MobileApiException)
        {
            _completionRequestId = null;
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

    private async Task<ReceivingOrderScanResult> IncrementAsync(Guid orderId, int lineNumber)
    {
        try
        {
            var response = await _orderClient.IncrementLineAsync(
                orderId,
                lineNumber,
                _scanRequestId!.Value);
            _scanRequestId = null;
            _scanLineNumber = null;
            _scanBarcode = null;
            return ReceivingOrderScanResult.Succeeded(response, lineNumber);
        }
        catch (MobileApiException)
        {
            _scanRequestId = null;
            _scanLineNumber = null;
            throw;
        }
    }
}

public sealed record ReceivingOrderScanResult(
    MobileReceivingOrderCommandResponse? Response,
    IReadOnlyList<MobileReceivingOrderLineCandidateResponse>? Candidates,
    int? ChangedLineNumber,
    string? ErrorMessage)
{
    internal static ReceivingOrderScanResult Succeeded(
        MobileReceivingOrderCommandResponse response,
        int lineNumber) => new(response, null, lineNumber, null);

    internal static ReceivingOrderScanResult SelectFrom(
        IReadOnlyList<MobileReceivingOrderLineCandidateResponse> candidates) =>
        new(null, candidates, null, null);

    internal static ReceivingOrderScanResult Failed(string errorMessage) =>
        new(null, null, null, errorMessage);
}

public sealed record ReceivingOrderQuantityResult(
    MobileReceivingOrderCommandResponse? Response,
    string? ErrorMessage)
{
    internal static ReceivingOrderQuantityResult Succeeded(
        MobileReceivingOrderCommandResponse response) => new(response, null);

    internal static ReceivingOrderQuantityResult Failed(string errorMessage) =>
        new(null, errorMessage);
}
