using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Commands;
using Wms.Application.LicensePlateNumbers;
using Wms.Application.Users;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.LicensePlateNumberPages;

public partial class Index
{
    [Inject] private LicensePlateNumberService Labels { get; set; } = null!;
    [Inject] private ApplicationUserQueryService Users { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private ILogger<Index> Logger { get; set; } = null!;
    private MudDataGrid<LicensePlateNumber> _grid = null!;
    private int _quantity = 10;
    private bool _busy;
    private bool InputsLocked => _busy || _pending is not null;
    private (int Quantity, CommandContext Context)? _pending;
    private Guid? _issuedBatch;
    private string? _error;
    private string? _search;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    private async Task<GridData<LicensePlateNumber>> LoadAsync(GridState<LicensePlateNumber> state, CancellationToken ct)
    {
        var result = await Labels.ListAsync(_search, state.Page * state.PageSize, state.PageSize, ct);
        _userNames = await Users.GetUserNamesAsync(result.Items.Select(x => x.Batch.IssuedBy), ct);
        return new() { Items = result.Items, TotalItems = result.Total };
    }

    private string UserName(string id) => _userNames.TryGetValue(id, out var name) ? name : id;
    private async Task SearchAsync(string value)
    {
        _search = value;
        _grid.NavigateTo(Page.First);
        await _grid.ReloadServerData();
    }

    private async Task IssueAsync()
    {
        if (InputsLocked) return;
        _busy = true;
        try
        {
            var user = (await Authentication.GetAuthenticationStateAsync()).User;
            _pending = (_quantity, new(Guid.NewGuid(), user.FindFirstValue(ClaimTypes.NameIdentifier) ?? ""));
        }
        finally { _busy = false; }
        await ExecuteAsync();
    }

    private async Task ExecuteAsync()
    {
        if (_busy || _pending is not { } attempt) return;
        _busy = true;
        _error = null;
        _issuedBatch = null;
        try
        {
            var result = await Labels.IssueAsync(attempt.Quantity, attempt.Context);
            _pending = null;
            if (result.IsSuccess) _issuedBatch = result.Value;
            else _error = result.Error!.Message;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "LPN label issuance failed for request {RequestId}", attempt.Context.RequestId);
            _error = "Результат выпуска неизвестен. Нажмите «Повторить выпуск»: исходное количество сохранено, второй пакет не появится.";
        }
        finally { _busy = false; }

        if (_issuedBatch is not null)
        {
            try { await _grid.ReloadServerData(); }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Failed to refresh issued LPN labels");
                _error = "Пакет выпущен, но список не обновился. Макет доступен по ссылке выше; обновите страницу для просмотра списка.";
            }
        }
    }
}
