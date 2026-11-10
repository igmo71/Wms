using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class PickingAssemblyProcess(MobilePickingClient client)
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
