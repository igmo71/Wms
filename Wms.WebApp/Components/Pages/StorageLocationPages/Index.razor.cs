using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Security.Claims;
using Wms.Application.StorageLocations;
using Wms.Application.Users;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.StorageLocationPages;

public partial class Index
{
    [Inject] private StorageLocationCommandService StorageLocationCommandService { get; set; } = null!;
    [Inject] private StorageLocationLockCommandService StorageLocationLockCommandService { get; set; } = null!;
    [Inject] private StorageLocationQueryService StorageLocationQueryService { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;

    private Warehouse? _warehouse;
    private StorageLocation? _selectedLocation;
    private IReadOnlyList<StorageLocation> _locations = [];
    private IReadOnlyList<StorageLocation> _rootLocations = [];
    private IReadOnlyDictionary<Guid, IReadOnlyList<StorageLocation>> _childrenByParent =
        new Dictionary<Guid, IReadOnlyList<StorageLocation>>();
    private bool _includeDeleted;
    private bool _isLoading;
    private bool _isChangingLock;
    private string _lockReason = string.Empty;
    private string? _errorMessage;
    private IReadOnlyDictionary<string, string> _lockUserNames = new Dictionary<string, string>();

    private string CoordinatesText => _selectedLocation is null
        ? "—"
        : $"X: {Format(_selectedLocation.Coordinates.X)}, Y: {Format(_selectedLocation.Coordinates.Y)}, Z: {Format(_selectedLocation.Coordinates.Z)}";

    private bool CanPrintChildren
    {
        get
        {
            if (_selectedLocation is not { IsFolder: true, DeletionMark: false } folder
                || !_childrenByParent.TryGetValue(folder.Id, out var children))
                return false;
            var printable = children.Where(x => !x.IsFolder && !x.DeletionMark).ToArray();
            return printable.Length is > 0 and <= 120
                && printable.All(x => x.Zone is { DeletionMark: false });
        }
    }

    private async Task<IEnumerable<Warehouse>> SearchWarehousesAsync(string? searchText, CancellationToken ct)
    {
        var result = await WarehouseService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            Take = 20
        }, ct);
        return result.Items;
    }

    private async Task OnWarehouseChangedAsync(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        _selectedLocation = null;
        _locations = [];
        await LoadTreeAsync();
    }

    private async Task OnIncludeDeletedChangedAsync(bool includeDeleted)
    {
        _includeDeleted = includeDeleted;
        await LoadTreeAsync();
    }

    private Task OnSelectedLocationChanged(StorageLocation? location)
    {
        _selectedLocation = location;
        _lockReason = string.Empty;
        return Task.CompletedTask;
    }

    private Task CreateRootAsync() => ShowLocationDialogAsync(null, null);
    private Task CreateChildAsync() => ShowLocationDialogAsync(null, _selectedLocation);
    private Task EditSelectedAsync() => ShowLocationDialogAsync(_selectedLocation, null);

    private async Task GenerateChildrenAsync()
    {
        if (_warehouse is null || _selectedLocation is not { IsFolder: true, DeletionMark: false } parent)
            return;
        Zone? suggestedZone = null;
        if (_childrenByParent.TryGetValue(parent.Id, out var children))
            suggestedZone = children.FirstOrDefault(x => !x.IsFolder && !x.DeletionMark)?.Zone;
        var parameters = new DialogParameters<GenerateStorageLocationsDialog>
        {
            { x => x.Warehouse, _warehouse },
            { x => x.Parent, parent },
            { x => x.SuggestedZone, suggestedZone }
        };
        var dialog = await DialogService.ShowAsync<GenerateStorageLocationsDialog>(
            "Создать диапазон ячеек", parameters);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
            await LoadTreeAsync();
    }

    private async Task ImportMapsAsync()
    {
        if (_warehouse is null)
            return;

        var parameters = new DialogParameters<StorageMapImportDialog>
        {
            { x => x.Warehouse, _warehouse }
        };
        var dialog = await DialogService.ShowAsync<StorageMapImportDialog>("Импорт карт 1С", parameters);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
            await LoadTreeAsync();
    }

    private async Task ShowLocationDialogAsync(StorageLocation? location, StorageLocation? parent)
    {
        if (_warehouse is null)
            return;

        var parameters = new DialogParameters<StorageLocationDialog>
        {
            { x => x.Warehouse, _warehouse },
            { x => x.Parent, parent },
            { x => x.StorageLocation, location }
        };
        var dialog = await DialogService.ShowAsync<StorageLocationDialog>(
            location is null ? "Создать складскую позицию" : "Изменить складскую позицию", parameters);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled)
            await LoadTreeAsync();
    }

    private async Task DeactivateSelectedAsync()
    {
        if (_selectedLocation is null)
            return;
        var result = await StorageLocationCommandService.MarkDeleteAsync(_selectedLocation.Id);
        await HandleActionResultAsync(result);
    }

    private async Task ActivateSelectedAsync()
    {
        if (_selectedLocation is null)
            return;
        var result = await StorageLocationCommandService.UnMarkDeleteAsync(_selectedLocation.Id);
        await HandleActionResultAsync(result);
    }

    private async Task LockSelectedAsync()
    {
        if (_selectedLocation is null)
            return;

        _isChangingLock = true;
        _errorMessage = null;
        try
        {
            var result = await RunAsCurrentUserAsync(userId =>
                StorageLocationLockCommandService.LockManuallyAsync(
                    _selectedLocation.Id,
                    _lockReason,
                    userId));
            await HandleActionResultAsync(result);
        }
        catch
        {
            _errorMessage = "Не удалось заблокировать ячейку.";
        }
        finally
        {
            _isChangingLock = false;
        }
    }

    private async Task UnlockSelectedAsync()
    {
        if (_selectedLocation is null)
            return;

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Разблокировать ячейку",
            $"Разблокировать ячейку {_selectedLocation.DisplayAddress}?",
            yesText: "Разблокировать",
            cancelText: "Отмена");
        if (confirmed != true)
            return;

        _isChangingLock = true;
        _errorMessage = null;
        try
        {
            var result = await RunAsCurrentUserAsync(userId =>
                StorageLocationLockCommandService.UnlockManualAsync(
                    _selectedLocation.Id,
                    userId));
            await HandleActionResultAsync(result);
        }
        catch
        {
            _errorMessage = "Не удалось разблокировать ячейку.";
        }
        finally
        {
            _isChangingLock = false;
        }
    }

    private async Task HandleActionResultAsync(OperationResult result)
    {
        if (!result.IsSuccess)
        {
            _errorMessage = result.Error?.Message ?? "Операция не выполнена.";
            return;
        }
        await LoadTreeAsync();
    }

    private async Task LoadTreeAsync()
    {
        _errorMessage = null;
        var selectedLocationId = _selectedLocation?.Id;
        if (_warehouse is null)
        {
            _locations = [];
            _rootLocations = [];
            _childrenByParent = new Dictionary<Guid, IReadOnlyList<StorageLocation>>();
            _selectedLocation = null;
            return;
        }

        _isLoading = true;
        try
        {
            _locations = await StorageLocationQueryService.GetTreeAsync(_warehouse.Id, _includeDeleted);
            _rootLocations = _locations.Where(x => x.ParentId is null).OrderBy(x => x.Code).ToArray();
            _childrenByParent = _locations.Where(x => x.ParentId is not null)
                .GroupBy(x => x.ParentId!.Value)
                .ToDictionary(x => x.Key,
                    x => (IReadOnlyList<StorageLocation>)x.OrderBy(y => y.Code).ToArray());
            _selectedLocation = selectedLocationId is Guid id
                ? _locations.SingleOrDefault(x => x.Id == id)
                : null;
            var userIds = _locations
                .Select(x => x.ActiveLock?.LockedBy)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct();
            _lockUserNames = await ApplicationUserQueryService.GetUserNamesAsync(userIds);
        }
        catch
        {
            _locations = [];
            _rootLocations = [];
            _childrenByParent = new Dictionary<Guid, IReadOnlyList<StorageLocation>>();
            _errorMessage = "Не удалось загрузить дерево складских позиций.";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static string Format(double? value) => value?.ToString("0.###") ?? "—";
    private string GetLockOwnerText(StorageLocationLock locationLock)
    {
        var userName = _lockUserNames.TryGetValue(locationLock.LockedBy, out var value)
            ? value
            : locationLock.LockedBy;
        var source = locationLock.OwnerType == StorageLocationLockOwnerType.Manual
            ? "вручную"
            : "инвентаризация";
        return $"{source} · {userName} · {locationLock.LockedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}";
    }

    private async Task<OperationResult> RunAsCurrentUserAsync(
        Func<string, Task<OperationResult>> action)
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var userId = authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null
            ? OperationError.Invalid("Не удалось определить текущего пользователя.")
            : await action(userId);
    }
}
