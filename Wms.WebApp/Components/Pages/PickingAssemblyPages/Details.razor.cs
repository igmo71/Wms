using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Picking;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.PickingAssemblyPages;

public partial class Details
{
    [Parameter] public Guid Id { get; set; }
    [Inject] private IPickingAssemblyQueryStore Assemblies { get; set; } = null!;
    [Inject] private CheckPickingClosure Checker { get; set; } = null!;
    [Inject] private ClosePickingAssembly Closer { get; set; } = null!;
    [Inject] private CancelPickingAssembly Canceller { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;

    private PickingAssemblyView? _assembly;
    private PickingWorkView? _work;
    private PickingClosureCheck? _check;
    private IReadOnlyList<PickingAuditView> _audit = [];
    private IReadOnlyList<PickingAllocationView> _postedAllocations = [];
    private IReadOnlyList<PickingShortageView> _shortages = [];
    private IReadOnlyCollection<PickingAllocationInput>? _manualAllocations;
    private ClosePickingAssemblyRequest? _pendingCloseRequest;
    private CancelPickingAssemblyRequest? _pendingCancelRequest;
    private string? _userId;
    private string? _closeReason;
    private string? _cancelReason;
    private string? _error;
    private string? _message;
    private bool _busy;

    private sealed record SourceComparisonRow(string Name, decimal Plan,
        decimal? Current, decimal? Allocated);

    protected override async Task OnParametersSetAsync()
    {
        _userId = (await Authentication.GetAuthenticationStateAsync()).User
            .FindFirstValue(ClaimTypes.NameIdentifier);
        _check = null;
        _manualAllocations = null;
        _pendingCloseRequest = null;
        _pendingCancelRequest = null;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _busy = true;
        _error = null;
        _check = null;
        if (_pendingCloseRequest is null) _manualAllocations = null;
        try { await ReloadViewsAsync(); }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task ReloadViewsAsync()
    {
        _assembly = await Assemblies.GetAsync(Id, CancellationToken.None);
        _work = _assembly is null ? null : await Assemblies.GetWorkAsync(Id, CancellationToken.None);
        _audit = _assembly is null ? [] : await Assemblies.GetAuditAsync(Id, CancellationToken.None);
        _postedAllocations = _assembly is null ? [] : await Assemblies.GetPostedAllocationsAsync(Id, CancellationToken.None);
        _shortages = _assembly is null ? [] : await Assemblies.GetShortagesAsync(Id, CancellationToken.None);
    }

    private async Task CheckAsync()
    {
        if (_userId is null || _pendingCancelRequest is not null
            || _assembly?.Status != PickingAssemblyStatus.InProgress) return;
        _busy = true;
        _error = null;
        _message = null;
        _check = null;
        try
        {
            var result = await Checker.ExecuteAsync(Id, _userId, _manualAllocations);
            if (!result.IsSuccess)
            {
                _error = result.Error!.Message;
                return;
            }
            _check = result.Value;
            await ReloadViewsAsync();
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task OpenPlanAsync(Guid sourceId)
    {
        if (_userId is null || _pendingCloseRequest is not null || _pendingCancelRequest is not null
            || _assembly?.Status != PickingAssemblyStatus.InProgress) return;
        await CheckAsync();
        var source = _check?.Sources.SingleOrDefault(x => x.ShippingOrderId == sourceId);
        if (source?.CurrentSourceJson is null || _assembly is null) return;
        var parameters = new DialogParameters<PickingPlanDialog>
        {
            { x => x.Assembly, _assembly }, { x => x.Source, source }, { x => x.UserId, _userId }
        };
        var dialog = await DialogService.ShowAsync<PickingPlanDialog>(
            "Изменить план WMS", parameters,
            new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true,
                BackdropClick = false, CloseOnEscapeKey = false });
        var result = await dialog.Result;
        if (result is { Canceled: false })
        {
            _manualAllocations = null;
            await ReloadAsync();
            _message = "План WMS изменён. Перед закрытием выполните новую сверку с 1С.";
        }
    }

    private async Task OpenAllocationAsync()
    {
        if (_pendingCloseRequest is not null || _pendingCancelRequest is not null
            || _assembly is null || _work is null
            || _check?.Allocation?.RequiresManagerDecision.Count is not > 0) return;
        var parameters = new DialogParameters<PickingAllocationDialog>
        {
            { x => x.Assembly, _assembly }, { x => x.Work, _work },
            { x => x.UnresolvedSkuIds, _check.Allocation.RequiresManagerDecision }
        };
        var dialog = await DialogService.ShowAsync<PickingAllocationDialog>(
            "Распределение общего SKU", parameters,
            new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true });
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: PickingAllocationInput[] allocations })
        {
            _manualAllocations = allocations;
            await CheckAsync();
        }
    }

    private async Task CloseAsync()
    {
        if (_pendingCancelRequest is not null || _userId is null
            || (_assembly?.Status != PickingAssemblyStatus.InProgress
            && _pendingCloseRequest is null))
            return;
        if (_pendingCloseRequest is null)
        {
            if (string.IsNullOrWhiteSpace(_closeReason))
            {
                _error = "Укажите причину решения руководителя.";
                return;
            }
            await CheckAsync();
            if (_check?.CanManagerClose != true) return;
            _pendingCloseRequest = new ClosePickingAssemblyRequest(Guid.NewGuid(), Id,
                _check.Revision, _userId, ManagerOverride: true,
                DecisionReason: _closeReason.Trim(), ManagerAllocations: _manualAllocations,
                ReviewedSourcesById: _check.Sources.ToDictionary(
                    x => x.ShippingOrderId, x => x.CurrentSourceJson!));
        }
        _busy = true;
        _error = null;
        try
        {
            var result = await Closer.ExecuteAsync(_pendingCloseRequest);
            if (!result.IsSuccess)
            {
                _pendingCloseRequest = null;
                _error = result.Error!.Message;
                return;
            }
            await ReloadViewsAsync();
            _pendingCloseRequest = null;
            _check = null;
            _message = "Складской факт проведён. Цели обмена с 1С сохранены по каждому документу.";
        }
        catch (Exception exception)
        {
            _error = $"Ответ на закрытие не получен: {exception.Message} Повторите той же кнопкой.";
        }
        finally { _busy = false; }
    }

    private async Task OpenShortageAsync(PickingShortageView shortage)
    {
        if (_userId is null || _busy || shortage.ResolvedAtUtc is not null) return;
        var parameters = new DialogParameters<ShortageResolutionDialog>
        {
            { x => x.Shortage, shortage }, { x => x.UserId, _userId }
        };
        var dialog = await DialogService.ShowAsync<ShortageResolutionDialog>(
            "Разобрать недостачу", parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true,
                BackdropClick = false, CloseOnEscapeKey = false });
        var result = await dialog.Result;
        if (result is { Canceled: false })
        {
            _check = null;
            await ReloadViewsAsync();
            _message = "Решение по недостаче сохранено. Перед закрытием выполните новую сверку.";
        }
    }

    private async Task CancelAsync()
    {
        if (_userId is null || _pendingCloseRequest is not null
            || (_pendingCancelRequest is null && _assembly?.Status is not
                (PickingAssemblyStatus.Draft or PickingAssemblyStatus.Preparing
                    or PickingAssemblyStatus.InProgress))) return;
        if (_pendingCancelRequest is null)
        {
            if (_work?.Entries.Any(x => x.Quantity > 0) == true)
            {
                _error = "Верните весь отобранный товар в исходные ячейки через ТСД.";
                return;
            }
            if (string.IsNullOrWhiteSpace(_cancelReason))
            {
                _error = "Укажите причину отмены.";
                return;
            }
            _pendingCancelRequest = new CancelPickingAssemblyRequest(
                Guid.NewGuid(), Id, _assembly!.Revision, _userId, _cancelReason.Trim());
        }
        _busy = true;
        _error = null;
        try
        {
            var result = await Canceller.ExecuteAsync(_pendingCancelRequest);
            if (!result.IsSuccess)
            {
                _pendingCancelRequest = null;
                _error = result.Error!.Message;
                return;
            }
            _check = null;
            await ReloadViewsAsync();
            _pendingCancelRequest = null;
            _message = _assembly?.Status == PickingAssemblyStatus.Cancelled
                ? "Сборка отменена." : "Отмена запрошена. Ожидаются незавершённые операции и возврат статусов РО 1С.";
        }
        catch (Exception exception)
        {
            _error = $"Ответ на отмену не получен: {exception.Message} Повторите той же кнопкой.";
        }
        finally { _busy = false; }
    }

    private string SkuName(Guid skuId) =>
        _work?.Skus.FirstOrDefault(x => x.SkuId == skuId) is { } sku
            ? sku.Name ?? sku.Code ?? skuId.ToString() : skuId.ToString();

    private static string AuditText(string eventType) => eventType switch
    {
        "picking.plan.changed" => "Изменён план WMS",
        "picking.plan.line.changed" => "Изменена строка плана",
        "picking.assembly.closed" => "Сборка закрыта",
        "picking.pick.confirmed" => "Подтверждён отбор",
        "picking.pick.reduced" => "Исправлен отбор",
        "picking.shortage.reported" => "Сообщено о недостаче",
        "inventory.shortage.resolved" => "Недостача разобрана",
        "picking.assembly.cancellation-requested" => "Запрошена отмена сборки",
        _ => eventType
    };

    private IReadOnlyList<SourceComparisonRow> SourceRows(Guid sourceId)
    {
        if (_assembly is null) return [];
        var plan = _assembly.PlanLines.Where(x => x.SourceRefKey == sourceId)
            .ToDictionary(x => x.SkuId);
        var current = _assembly.Sources.First(x => x.SourceRefKey == sourceId)
            .CurrentLines.GroupBy(x => x.SkuId)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
        var allocationPreview = _check?.Allocation?.Allocations;
        var allocated = allocationPreview is null
            ? _postedAllocations.Where(x => x.ShippingOrderId == sourceId)
                .ToDictionary(x => x.SkuId, x => x.Quantity)
            : allocationPreview.Where(x => x.ShippingOrderId == sourceId)
                .ToDictionary(x => x.StockKeepingUnitId, x => x.Quantity);
        return plan.Keys.Union(current.Keys).Union(allocated.Keys)
            .Select(id => new SourceComparisonRow(
                plan.TryGetValue(id, out var row)
                    ? row.SkuName ?? row.SkuCode ?? id.ToString() : SkuName(id),
                row?.Quantity ?? 0,
                _assembly.Sources.First(x => x.SourceRefKey == sourceId).CurrentSourceJson is null
                    ? null : current.GetValueOrDefault(id),
                allocated.TryGetValue(id, out var quantity) ? quantity : null))
            .OrderBy(x => x.Name).ToArray();
    }
}
