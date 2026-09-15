using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class ReceivingOrderReceivingProcess
{
    private readonly MobileReceivingOrderClient _orderClient;
    private Guid? _startRequestId;
    private Guid? _scanRequestId;
    private Guid? _quantityRequestId;
    private Guid? _completionRequestId;
    private string? _scanBarcode;
    private int? _scanLineNumber;
    private int? _quantityLineNumber;

    internal ReceivingOrderReceivingProcess(MobileReceivingOrderClient orderClient)
    {
        _orderClient = orderClient;
    }

    public bool HasPendingCommand => IsStartPending
        || IsScanPending
        || IsQuantityPending
        || IsCompletionPending;

    public bool IsStartPending => _startRequestId is not null;
    public bool IsScanPending => _scanRequestId is not null;
    public bool IsQuantityPending => _quantityRequestId is not null;
    public bool IsCompletionPending => _completionRequestId is not null;
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
