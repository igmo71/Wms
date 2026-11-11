using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class ReplenishmentTaskProcess(MobileReplenishmentClient client)
{
    private PendingCommand? _pending;
    private bool _executing;

    public bool HasPendingCommand => _pending is not null;
    public string? PendingLabel => _pending?.Label;

    public Task<MobileReplenishmentTaskResponse> StartSourceAsync(
        Guid taskId, Guid warehouseId, string sourceCode) =>
        Begin("Снять источник", taskId, warehouseId, id =>
            client.StartSourceAsync(taskId, new(id, warehouseId, sourceCode)));

    public Task<MobileReplenishmentTaskResponse> TransferAsync(
        Guid taskId, Guid warehouseId, Guid cycleId, string destinationCode,
        Guid skuId, DateOnly? expirationDate, decimal quantity) =>
        Begin("Провести Подпитку", taskId, warehouseId, id =>
            client.TransferAsync(taskId, new(id, warehouseId, cycleId,
                destinationCode, skuId, expirationDate, quantity)));

    public Task<MobileReplenishmentTaskResponse> ReportShortageAsync(
        Guid taskId, Guid warehouseId, Guid cycleId, Guid skuId,
        DateOnly? expirationDate, decimal expected, decimal found, string? comment) =>
        Begin("Сообщить о недостаче", taskId, warehouseId, id =>
            client.ReportShortageAsync(taskId, new(id, warehouseId, cycleId,
                skuId, expirationDate, expected, found, comment)));

    public Task<MobileReplenishmentTaskResponse> CompleteSourceAsync(
        Guid taskId, Guid warehouseId, Guid cycleId, string sourceCode,
        bool empty) =>
        Begin(empty ? "Подтвердить пустую ячейку" : "Вернуть поддон",
            taskId, warehouseId, id => client.CompleteSourceAsync(taskId,
                new(id, warehouseId, cycleId, sourceCode, empty)));

    private Task<MobileReplenishmentTaskResponse> Begin(string label,
        Guid taskId, Guid warehouseId,
        Func<Guid, Task<MobileReplenishmentResourceIdResponse>> execute)
    {
        if (_pending is not null)
            throw new InvalidOperationException("Сначала повторите незавершённую команду.");
        _pending = new PendingCommand(Guid.NewGuid(), label, taskId,
            warehouseId, execute);
        return RetryAsync();
    }

    public async Task<MobileReplenishmentTaskResponse> RetryAsync()
    {
        if (_executing)
            throw new InvalidOperationException("Дождитесь результата текущей команды.");
        var pending = _pending ?? throw new InvalidOperationException("Нет команды для повтора.");
        _executing = true;
        try
        {
            await pending.Execute(pending.RequestId);
            var detail = await client.TaskAsync(pending.TaskId, pending.WarehouseId);
            _pending = null;
            return detail;
        }
        catch (MobileApiException)
        {
            _pending = null;
            throw;
        }
        finally { _executing = false; }
    }

    private sealed record PendingCommand(Guid RequestId, string Label,
        Guid TaskId, Guid WarehouseId,
        Func<Guid, Task<MobileReplenishmentResourceIdResponse>> Execute);
}
