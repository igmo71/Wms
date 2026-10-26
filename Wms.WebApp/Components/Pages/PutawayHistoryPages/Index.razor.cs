using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Receiving;
using Wms.Application.Users;

namespace Wms.WebApp.Components.Pages.PutawayHistoryPages;

public partial class Index
{
    [Inject] private IReceivingWorkQueryStore Putaways { get; set; } = null!;
    [Inject] private ApplicationUserQueryService UserNames { get; set; } = null!;

    private MudDataGrid<ReceivingPutawayHistoryRow> _grid = null!;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private string? _lpnCode;
    private string? _skuName;
    private string? _userName;
    private string? _destinationAddress;
    private string? _error;

    private async Task<GridData<ReceivingPutawayHistoryRow>> LoadAsync(
        GridState<ReceivingPutawayHistoryRow> state, CancellationToken ct)
    {
        try
        {
            var result = await Putaways.ListPutawayHistoryAsync(
                new ReceivingPutawayHistoryQuery(
                    _dateFrom, _dateTo, _lpnCode, _skuName, _userName,
                    _destinationAddress, state.Page * state.PageSize, state.PageSize), ct);
            _userNames = await UserNames.GetUserNamesAsync(
                result.Items.Select(x => x.CreatedBy), ct);
            _error = null;
            return new GridData<ReceivingPutawayHistoryRow>
            {
                Items = result.Items,
                TotalItems = result.TotalItems
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new GridData<ReceivingPutawayHistoryRow>
            {
                Items = [],
                TotalItems = 0
            };
        }
        catch (Exception exception)
        {
            _error = exception.Message;
            return new GridData<ReceivingPutawayHistoryRow>
            {
                Items = [],
                TotalItems = 0
            };
        }
    }

    private Task OnLpnCodeChangedAsync(string? lpnCode)
    {
        _lpnCode = lpnCode;
        return ReloadFirstPageAsync();
    }

    private Task OnSkuNameChangedAsync(string? skuName)
    {
        _skuName = skuName;
        return ReloadFirstPageAsync();
    }

    private Task OnUserNameChangedAsync(string? userName)
    {
        _userName = userName;
        return ReloadFirstPageAsync();
    }

    private Task OnDestinationAddressChangedAsync(string? destinationAddress)
    {
        _destinationAddress = destinationAddress;
        return ReloadFirstPageAsync();
    }

    private Task OnDateFromChangedAsync(DateTime? dateFrom)
    {
        _dateFrom = dateFrom;
        return ReloadFirstPageAsync();
    }

    private Task OnDateToChangedAsync(DateTime? dateTo)
    {
        _dateTo = dateTo;
        return ReloadFirstPageAsync();
    }

    private Task ReloadFirstPageAsync()
    {
        _grid.CurrentPage = 0;
        return _grid.ReloadServerData();
    }

    private Task ReloadAsync() => _grid.ReloadServerData();

    private string UserName(string userId) =>
        _userNames.TryGetValue(userId, out var name) ? name : "Пользователь не найден";

    private static string SkuText(ReceivingPutawayHistoryRow row) =>
        string.IsNullOrWhiteSpace(row.SkuName)
            ? row.SkuCode ?? row.SkuId.ToString()
            : string.IsNullOrWhiteSpace(row.SkuCode)
                ? row.SkuName
                : $"{row.SkuName} ({row.SkuCode})";
}
