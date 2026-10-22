using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Authorization;
using MudBlazor;
using Wms.Application.StockKeepingUnits;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.WebApp.Components.Pages.StockKeepingUnitPages;

public partial class Index
{
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private WarehousePermission PermissionService { get; set; } = null!;

    [Inject] private StockKeepingUnitService StockKeepingUnitService { get; set; } = null!;
    [Inject] private SetExpirationTracking ExpirationTracking { get; set; } = null!;
    [Inject] private ILogger<Index> Logger { get; set; } = null!;
    [Inject] private Catalog_Номенклатура_Service CatalogImportService { get; set; } = null!;
    private MudDataGrid<StockKeepingUnit> _dataGrid = null!;
    private string? _searchString;
    private bool _includeDeleted;
    private bool _isImporting;
    private bool _importFailed;
    private bool _importSucceeded;
    private bool _importWarning;
    private string? _importMessage;
    private PendingTracking? _pendingTracking;
    private bool _isChangingTracking;
    private bool _trackingError;
    private string? _trackingMessage;
    private Guid? _historySkuId;
    private string? _historySkuName;
    private IReadOnlyList<ExpirationTrackingChange> _trackingHistory = [];

    private async Task ChangeTrackingAsync(StockKeepingUnit sku, bool enabled)
    {
        if (_pendingTracking is not null || _isChangingTracking
            || sku.TracksExpirationDate == enabled) return;
        _pendingTracking = new PendingTracking(Guid.NewGuid(), sku.Id, enabled);
        await ExecuteTrackingAsync();
    }

    private Task RetryTrackingAsync() => ExecuteTrackingAsync();

    private async Task ExecuteTrackingAsync()
    {
        if (_pendingTracking is not { } pending || _isChangingTracking) return;
        _isChangingTracking = true;
        _trackingMessage = null;
        try
        {
            var userId = (await AuthenticationStateProvider.GetAuthenticationStateAsync())
                .User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var result = await ExpirationTracking.ExecuteAsync(new SetExpirationTrackingRequest(
                pending.RequestId, pending.SkuId, pending.Enabled, userId));
            if (!result.IsSuccess)
            {
                _trackingError = true;
                _trackingMessage = result.Error!.Message;
                _pendingTracking = null;
            }
            else
            {
                _trackingError = false;
                _trackingMessage = pending.Enabled
                    ? "Учёт срока годности включён." : "Учёт срока годности выключен.";
                _pendingTracking = null;
                try
                {
                    await _dataGrid.ReloadServerData();
                    if (_historySkuId == pending.SkuId)
                        _trackingHistory = await ExpirationTracking.ListHistoryAsync(pending.SkuId);
                }
                catch (Exception refreshException)
                {
                    Logger.LogWarning(refreshException,
                        "Признак срока SKU {SkuId} изменён, но список не обновился", pending.SkuId);
                    _trackingMessage = "Признак изменён. Обновите список для просмотра результата.";
                }
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Не удалось подтвердить изменение признака срока SKU {SkuId}", pending.SkuId);
            _trackingError = true;
            _trackingMessage = "Результат не подтверждён. Повторите то же изменение.";
        }
        finally
        {
            _isChangingTracking = false;
        }
    }

    private async Task ShowTrackingHistoryAsync(StockKeepingUnit sku)
    {
        _historySkuId = sku.Id;
        _historySkuName = sku.Name ?? sku.Code ?? sku.Id.ToString();
        _trackingHistory = [];
        try
        {
            _trackingHistory = await ExpirationTracking.ListHistoryAsync(sku.Id);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Не удалось загрузить историю признака срока SKU {SkuId}", sku.Id);
            _trackingError = true;
            _trackingMessage = "Не удалось загрузить историю учёта срока годности.";
        }
    }

    private sealed record PendingTracking(Guid RequestId, Guid SkuId, bool Enabled);

    private async Task<GridData<StockKeepingUnit>> LoadServerDataAsync(GridState<StockKeepingUnit> state, CancellationToken ct)
    {
        var sort = state.SortDefinitions.FirstOrDefault();
        var result = await StockKeepingUnitService.ListAsync(new ListQuery { SearchString = _searchString, ExcludeDeleted = !_includeDeleted, SortBy = sort?.SortBy, SortDescending = sort?.Descending ?? false, Skip = state.Page * state.PageSize, Take = state.PageSize }, ct);
        return new GridData<StockKeepingUnit> { Items = result.Items, TotalItems = result.TotalItems };
    }

    private Task OnSearchChangedAsync(string? value) { _searchString = value; return _dataGrid.ReloadServerData(); }
    private Task OnIncludeDeletedChangedAsync(bool value) { _includeDeleted = value; return _dataGrid.ReloadServerData(); }

    private async Task RefreshFromOneCAsync()
    {
        var actorId = (await AuthenticationStateProvider.GetAuthenticationStateAsync())
            .User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var permission = await PermissionService.RequireManagerAsync(actorId);
        if (!permission.IsSuccess)
        {
            _importFailed = true;
            _importSucceeded = false;
            _importMessage = permission.Error!.Message;
            return;
        }

        _importWarning = false;

        _isImporting = true;
        _importFailed = false;
        _importSucceeded = false;
        _importWarning = false;
        _importMessage = null;
        try
        {
            var result = await CatalogImportService.ImportListAsync();
            if (!result.IsSuccess)
            {
                _importFailed = true;
                _importMessage = result.Error?.Message ?? "Не удалось обновить SKU из 1С.";
                return;
            }

            await _dataGrid.ReloadServerData();
            var summary = result.Value!;
            if (summary.HasWarnings)
            {
                _importWarning = true;
                _importMessage = $"SKU обновлены, но часть физических свойств не импортирована: " +
                    $"вес — {summary.InvalidWeightCount}, объём — {summary.InvalidVolumeCount}. " +
                    "Подробности записаны в журнал.";
            }
            else
            {
                _importSucceeded = true;
                _importMessage = "SKU и единицы измерения успешно обновлены из 1С.";
            }
        }
        catch
        {
            _importFailed = true;
            _importMessage = "Не удалось обновить SKU из 1С.";
        }
        finally
        {
            _isImporting = false;
        }
    }
}
