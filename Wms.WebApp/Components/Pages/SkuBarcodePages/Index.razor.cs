using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Authorization;
using MudBlazor;
using Wms.Application.SkuBarcodes;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Services;

namespace Wms.WebApp.Components.Pages.SkuBarcodePages;

public partial class Index
{
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private WarehousePermission PermissionService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;

    [Inject] private ListSkuBarcodes BarcodeList { get; set; } = null!;
    [Inject] private RemoveLocalSkuBarcode RemoveBarcode { get; set; } = null!;
    [Inject] private InformationRegister_ШтрихкодыНоменклатуры_Service CatalogImportService { get; set; } = null!;
    private MudDataGrid<SkuBarcodeListRow> _dataGrid = null!;
    private string? _searchString;
    private bool _includeDeleted;
    private bool _onlyDuplicates;
    private bool _isImporting;
    private bool _importFailed;
    private bool _importSucceeded;
    private string? _importMessage;
    private bool _isRemoving;
    private bool _removalError;
    private string? _removalMessage;
    private PendingRemoval? _pendingRemoval;
    private async Task<GridData<SkuBarcodeListRow>> LoadServerDataAsync(GridState<SkuBarcodeListRow> state, CancellationToken ct)
    {
        var sort = state.SortDefinitions.FirstOrDefault();
        var result = await BarcodeList.ExecuteAsync(new SkuBarcodeListQuery(
            _searchString, _includeDeleted, _onlyDuplicates,
            sort?.SortBy, sort?.Descending ?? false,
            state.Page * state.PageSize, state.PageSize), ct);
        return new GridData<SkuBarcodeListRow> { Items = result.Items, TotalItems = result.TotalItems };
    }
    private Task OnSearchChangedAsync(string? value) { _searchString = value; return _dataGrid.ReloadServerData(); }
    private Task OnIncludeDeletedChangedAsync(bool value) { _includeDeleted = value; return _dataGrid.ReloadServerData(); }
    private Task OnOnlyDuplicatesChangedAsync(bool value) { _onlyDuplicates = value; return _dataGrid.ReloadServerData(); }

    private static string SkuDisplay(SkuBarcodeListRow row)
    {
        var name = !string.IsNullOrWhiteSpace(row.SkuName)
            ? row.SkuName : row.SkuCode ?? row.SkuId.ToString();
        return string.IsNullOrWhiteSpace(row.SkuCode) || row.SkuCode == name
            ? name : $"{name} ({row.SkuCode})";
    }

    private async Task ConfirmRemovalAsync(SkuBarcodeListRow row)
    {
        if (row.Source != SkuBarcodeSource.Wms || _isRemoving
            || _pendingRemoval is not null || _isImporting) return;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Удалить локальный штрихкод",
            $"Удалить штрихкод {row.Value} у SKU {SkuDisplay(row)}? Источник: WMS.",
            yesText: "Удалить", cancelText: "Отмена");
        if (confirmed != true) return;

        _pendingRemoval = new PendingRemoval(Guid.NewGuid(), row.SkuId, row.Value);
        await ExecuteRemovalAsync();
    }

    private Task RetryRemovalAsync() => ExecuteRemovalAsync();

    private async Task ExecuteRemovalAsync()
    {
        if (_pendingRemoval is not { } pending || _isRemoving) return;
        _isRemoving = true;
        _removalMessage = null;
        try
        {
            var userId = (await AuthenticationStateProvider.GetAuthenticationStateAsync())
                .User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var result = await RemoveBarcode.ExecuteAsync(new RemoveLocalSkuBarcodeRequest(
                pending.RequestId, pending.SkuId, pending.Value, userId));
            if (!result.IsSuccess)
            {
                _pendingRemoval = null;
                _removalError = true;
                _removalMessage = result.Error!.Message;
                await _dataGrid.ReloadServerData();
                return;
            }

            await _dataGrid.ReloadServerData();
            _pendingRemoval = null;
            _removalError = false;
            _removalMessage = "Локальный штрихкод удалён.";
        }
        catch
        {
            _removalError = true;
            _removalMessage = _pendingRemoval is null
                ? "Не удалось обновить список. Обновите страницу."
                : "Результат удаления не подтверждён. Повторите ту же команду.";
        }
        finally
        {
            _isRemoving = false;
        }
    }

    private async Task RefreshFromOneCAsync()
    {
        if (_isRemoving || _pendingRemoval is not null) return;
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

        _isImporting = true;
        _importFailed = false;
        _importSucceeded = false;
        _importMessage = null;
        try
        {
            var result = await CatalogImportService.ImportListAsync();
            if (!result.IsSuccess)
            {
                _importFailed = true;
                _importMessage = result.Error?.Message ?? "Не удалось обновить штрихкоды из 1С.";
                return;
            }

            await _dataGrid.ReloadServerData();
            _importSucceeded = true;
            _importMessage = "Штрихкоды успешно обновлены из 1С.";
        }
        catch
        {
            _importFailed = true;
            _importMessage = "Не удалось обновить штрихкоды из 1С.";
        }
        finally
        {
            _isImporting = false;
        }
    }

    private sealed record PendingRemoval(Guid RequestId, Guid SkuId, string Value);
}
