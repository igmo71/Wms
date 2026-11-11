using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class PickingAssemblyProcess(
    MobilePickingClient client, MobileReplenishmentClient replenishments)
{
    private PendingCommand? _pending;
    private bool _executing;

    public bool HasPendingCommand => _pending is not null;
    public string? PendingLabel => _pending?.Label;
    public MobilePickingStartResponse? LastStartResult { get; private set; }

    public Task<MobilePickingAssemblyResponse> CreateAsync(Guid warehouseId, string barcode) =>
        Begin("Создать сборку", warehouseId, async id =>
            (await client.CreateAsync(warehouseId, barcode, id)).AssemblyId);

    public Task<MobilePickingAssemblyResponse> AttachAsync(
        Guid assemblyId, Guid warehouseId, string barcode) =>
        Begin("Добавить РО 1С", warehouseId, async id =>
            (await client.AttachAsync(assemblyId, warehouseId, barcode, id)).AssemblyId);

    public Task<MobilePickingAssemblyResponse> DetachAsync(
        Guid assemblyId, Guid sourceRefKey, Guid warehouseId) =>
        Begin("Открепить РО 1С", warehouseId, async id =>
            (await client.DetachAsync(assemblyId, sourceRefKey, warehouseId, id)).AssemblyId);

    public Task<MobilePickingAssemblyResponse> StartAsync(Guid assemblyId, Guid warehouseId) =>
        Begin("Начать Отбор", warehouseId, async id =>
        {
            LastStartResult = await client.StartAsync(assemblyId, warehouseId, id);
            return LastStartResult.PickingAssemblyId;
        });

    public Task<MobilePickingAssemblyResponse> TakeOverAsync(
        Guid assemblyId, Guid warehouseId, long revision) =>
        Begin("Принять сборку", warehouseId, async id =>
            (await client.TakeOverAsync(assemblyId, warehouseId, revision, id)).AssemblyId);

    public Task<MobilePickingAssemblyResponse> HoldPalletAsync(
        Guid assemblyId, Guid warehouseId, long revision,
        string locationCode, Guid skuId) =>
        Begin("Снять поддон", warehouseId, async id =>
        {
            await client.HoldPalletAsync(assemblyId,
                new(id, warehouseId, revision, locationCode, skuId));
            return assemblyId;
        });

    public Task<MobilePickingAssemblyResponse> ReturnPalletAsync(
        Guid assemblyId, Guid warehouseId, long revision,
        string locationCode) =>
        Begin("Вернуть поддон", warehouseId, async id =>
        {
            await client.ReturnPalletAsync(assemblyId,
                new(id, warehouseId, revision, locationCode));
            return assemblyId;
        });

    public Task<MobilePickingAssemblyResponse> ConfirmPickAsync(
        Guid assemblyId, Guid warehouseId, long revision,
        string locationCode, Guid skuId, DateOnly? expirationDate,
        decimal quantity) =>
        Begin("Подтвердить Отбор", warehouseId, async id =>
        {
            await client.ConfirmPickAsync(assemblyId,
                new(id, warehouseId, revision, locationCode, skuId,
                    expirationDate, quantity));
            return assemblyId;
        });

    public Task<MobilePickingAssemblyResponse> ReducePickAsync(
        Guid assemblyId, Guid warehouseId, Guid entryId, long revision,
        string locationCode, decimal quantity, string? explanation) =>
        Begin("Вернуть товар", warehouseId, async id =>
        {
            await client.ReducePickAsync(assemblyId, entryId,
                new(id, warehouseId, revision, locationCode, quantity,
                    PhysicalReturnConfirmed: true, Explanation: explanation));
            return assemblyId;
        });

    public Task<MobilePickingAssemblyResponse> ReportShortageAsync(
        Guid assemblyId, Guid warehouseId, long revision,
        string locationCode, Guid skuId, DateOnly? expirationDate,
        decimal expectedQuantity, decimal foundQuantity, string? comment) =>
        Begin("Сообщить о недостаче", warehouseId, async id =>
        {
            await client.ReportShortageAsync(assemblyId,
                new(id, warehouseId, revision, locationCode, skuId,
                    expirationDate, expectedQuantity, foundQuantity,
                    Comment: comment));
            return assemblyId;
        });

    public Task<MobilePickingAssemblyResponse> CloseAsync(
        Guid assemblyId, Guid warehouseId, long revision) =>
        Begin("Закрыть и отгрузить", warehouseId, async id =>
            (await client.CloseAsync(assemblyId,
                new(id, warehouseId, revision))).AssemblyId);

    public Task<MobilePickingAssemblyResponse> OrderReplenishmentAsync(
        Guid assemblyId, Guid warehouseId, long revision,
        Guid skuId, Guid sourceId, Guid? destinationId, decimal quantity) =>
        Begin("Заказать Подпитку", warehouseId, async id =>
        {
            await replenishments.OrderAsync(assemblyId,
                new(id, warehouseId, revision, skuId, sourceId,
                    destinationId, quantity));
            return assemblyId;
        });

    private Task<MobilePickingAssemblyResponse> Begin(
        string label, Guid warehouseId, Func<Guid, Task<Guid>> execute)
    {
        if (_pending is not null)
            throw new InvalidOperationException("Сначала повторите незавершённую команду.");
        LastStartResult = null;
        _pending = new PendingCommand(Guid.NewGuid(), label, warehouseId, execute);
        return RetryAsync();
    }

    public async Task<MobilePickingAssemblyResponse> RetryAsync()
    {
        if (_executing)
            throw new InvalidOperationException("Дождитесь результата текущей команды.");
        var pending = _pending ?? throw new InvalidOperationException("Нет команды для повтора.");
        _executing = true;
        try
        {
            var assemblyId = await pending.Execute(pending.RequestId);
            var assembly = await client.GetAsync(assemblyId, pending.WarehouseId);
            _pending = null;
            return assembly;
        }
        catch (MobileApiException)
        {
            _pending = null;
            throw;
        }
        finally { _executing = false; }
    }

    private sealed record PendingCommand(Guid RequestId, string Label,
        Guid WarehouseId, Func<Guid, Task<Guid>> Execute);
}
